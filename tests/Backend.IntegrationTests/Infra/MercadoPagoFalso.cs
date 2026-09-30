using System.Collections.Concurrent;
using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// O Mercado Pago de mentira dos testes de integração: autoriza qualquer código, emite pedidos em memória
/// e diz que um pedido foi pago quando o teste manda.
/// </summary>
/// <remarks>
/// A assinatura do aviso é "válida" quando vale <see cref="AssinaturaValida"/>: a conta HMAC em si é do
/// teste unitário do cliente; aqui interessa o caminho do aviso até a parcela.
/// </remarks>
public sealed class MercadoPagoFalso : IMercadoPago
{
    /// <summary>O cabeçalho <c>x-signature</c> que o falso aceita.</summary>
    public const string AssinaturaValida = "ts=1,v1=valida";

    /// <summary>O token que a autorização devolve — o teste confere que ele nunca sai pela API.</summary>
    public const string AccessToken = "APP_USR-token-secreto-da-turma";

    /// <summary>A conta do próprio Kapa no Mercado Pago (<c>MercadoPago:ContaDoKapa</c>) — a turma é a 42.</summary>
    public const long ContaDoKapa = 900;

    /// <summary>A conta que "autorizou".</summary>
    public const string Conta = "turma@mp.testes";

    /// <summary>A chave pública da conta, que o formulário do cartão usa (Sprint 39).</summary>
    public const string ChavePublica = "APP_USR-chave-publica-de-teste";

    /// <summary>O token de cartão que o falso recusa — o cartão sem limite.</summary>
    public const string CartaoRecusado = "cartao-recusado";

    /// <summary>A tarifa que o falso desconta de cada pedido pago, base 10.000 — zero por padrão (Sprint 39, P6).</summary>
    public int Tarifa { get; set; }

    private readonly ConcurrentDictionary<string, SituacaoDoPedido> _devolvidos = new();

    /// <summary>O pagador contestou no cartão, ou a turma devolveu pelo painel: o pedido pago volta (Sprint 39, P4).</summary>
    /// <param name="idExterno">O pedido.</param>
    /// <param name="situacao"><c>Contestado</c> ou <c>Devolvido</c>.</param>
    public void Devolver(string idExterno, SituacaoDoPedido situacao) => _devolvidos[idExterno] = situacao;

    private readonly ConcurrentDictionary<string, (Guid Referencia, long Valor)> _pedidos = new();
    private readonly ConcurrentDictionary<string, bool> _pagos = new();

    /// <summary>Os pedidos emitidos, pelo id.</summary>
    public IReadOnlyDictionary<string, (Guid Referencia, long Valor)> Pedidos => _pedidos;

    /// <summary>Marca o pedido como pago — o que o formando faria no app do banco.</summary>
    public void Pagar(string idExterno) => _pagos[idExterno] = true;

    private readonly ConcurrentDictionary<string, long> _pagosEmParte = new();

    /// <summary>
    /// Marca o pedido como pago por um valor diferente do cobrado — a baixa parcial pelo Mercado Pago, que deixa a parcela
    /// aberta (Sprint 42, F2 e F3).
    /// </summary>
    /// <param name="idExterno">O pedido.</param>
    /// <param name="valorEmCentavos">O que o Mercado Pago diz que entrou.</param>
    public void Pagar(string idExterno, long valorEmCentavos)
    {
        _pagosEmParte[idExterno] = valorEmCentavos;
        Pagar(idExterno);
    }

    /// <summary>Uma fábrica com o Mercado Pago ligado e este falso no lugar da rede.</summary>
    public WebApplicationFactory<Program> Na(ApiFactory fabrica) =>
        fabrica.WithWebHostBuilder(host =>
            host.UseSetting("MercadoPago:ClientId", "app-de-teste")
                .UseSetting("MercadoPago:ClientSecret", "segredo-da-aplicacao")
                .UseSetting("MercadoPago:UrlDeRetorno", "https://localhost/api/v1/mercado-pago/retorno")
                .UseSetting("MercadoPago:ContaDoKapa", ContaDoKapa.ToString(CultureInfo.InvariantCulture))
                .ConfigureTestServices(servicos => servicos.Replace(ServiceDescriptor.Singleton<IMercadoPago>(this)))
        );

    /// <inheritdoc />
    public string UrlDeAutorizacao(string state) => $"https://auth.mercadopago.testes/authorization?state={Uri.EscapeDataString(state)}";

    /// <inheritdoc />
    public Task<Result<TokensDoMercadoPago>> Autorizar(string codigo, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(new TokensDoMercadoPago(AccessToken, "renovacao", 42, DateTime.UtcNow.AddDays(180), ChavePublica)));

    /// <inheritdoc />
    public Task<Result<TokensDoMercadoPago>> Renovar(string refreshToken, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(new TokensDoMercadoPago(AccessToken, "renovacao-2", 42, DateTime.UtcNow.AddDays(180))));

    /// <inheritdoc />
    public Task<Result<ContaNoMercadoPago>> ConsultarConta(string accessToken, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(new ContaNoMercadoPago(42, Conta, true)));

    /// <inheritdoc />
    public Task<Result<DocumentoEmitido>> Emitir(string accessToken, PedidoDeCobranca pedido, CancellationToken ct = default)
    {
        var id = $"ORD{pedido.Referencia:N}".ToUpperInvariant();

        if (pedido.Cartao?.Token == CartaoRecusado)
            return Task.FromResult(Result.Falha<DocumentoEmitido>(Erro.Conflito("pagamento.cartao_recusado", "O cartão foi recusado.")));

        _pedidos[id] = (pedido.Referencia, pedido.ValorEmCentavos);

        if (pedido.Cartao is null)
            return Task.FromResult(Result.Ok(new DocumentoEmitido(id, $"00020126-{id}")));

        Pagar(id);

        return Task.FromResult(Result.Ok(new DocumentoEmitido(id, null, SituacaoDoPedido.Pago)));
    }

    /// <summary>Os pedidos consultados, pelo id — o aviso da conta do Kapa não consulta nenhum.</summary>
    public IReadOnlyCollection<string> Consultados => _consultados.Keys.ToList();

    private readonly ConcurrentDictionary<string, bool> _consultados = new();

    /// <summary>Token da conta do Kapa nos testes (<c>MercadoPago:AccessTokenDoKapa</c>).</summary>
    public const string TokenDoKapa = "APP_USR-token-da-conta-do-kapa";

    private readonly ConcurrentDictionary<string, (Guid Referencia, long Valor)> _paginas = new();
    private readonly ConcurrentDictionary<string, PagamentoNoMercadoPago> _pagamentos = new();
    private readonly ConcurrentDictionary<string, RecorrenciaNoMercadoPago> _recorrencias = new();
    private readonly ConcurrentDictionary<string, long> _valores = new();
    private readonly ConcurrentDictionary<string, DebitoDaRecorrencia> _debitos = new();
    private readonly ConcurrentQueue<(string Pagamento, long Valor)> _estornos = new();

    /// <summary>As páginas avulsas criadas, pelo id.</summary>
    public IReadOnlyDictionary<string, (Guid Referencia, long Valor)> Paginas => _paginas;

    /// <summary>As recorrências, pelo id.</summary>
    public IReadOnlyDictionary<string, RecorrenciaNoMercadoPago> Recorrencias => _recorrencias;

    /// <summary>Os estornos pedidos, na ordem.</summary>
    public IReadOnlyCollection<(string Pagamento, long Valor)> Estornos => _estornos;

    /// <summary>Uma fábrica com os planos cobrados pelo Mercado Pago (Sprint 37) e este falso no lugar da rede.</summary>
    public WebApplicationFactory<Program> CobrandoOsPlanos(ApiFactory fabrica) =>
        Na(fabrica)
            .WithWebHostBuilder(host =>
                host.UseSetting("Assinaturas:Provedor", "MercadoPago").UseSetting("MercadoPago:AccessTokenDoKapa", TokenDoKapa)
            );

    /// <summary>Paga a página avulsa da referência (o PIX do ciclo, a diferença) e devolve o id do pagamento.</summary>
    /// <param name="referencia">A cobrança.</param>
    public string PagarPagina(Guid referencia)
    {
        var (_, valor) = _paginas.Values.First(pagina => pagina.Referencia == referencia);
        var id = Proximo();
        _pagamentos[id] = new PagamentoNoMercadoPago(id, referencia.ToString("N"), SituacaoDoPagamento.Aprovado, valor, DateTime.UtcNow, false);

        return id;
    }

    /// <summary>Muda a situação da recorrência — a pessoa autorizou, ou cancelou no app do Mercado Pago.</summary>
    /// <param name="idExterno">Recorrência.</param>
    /// <param name="situacao">Situação nova.</param>
    public void MudarRecorrencia(string idExterno, SituacaoDaRecorrencia situacao) =>
        _recorrencias[idExterno] = _recorrencias[idExterno] with { Situacao = situacao };

    /// <summary>Um débito da recorrência, aprovado ou recusado. Devolve o id do débito — o <c>data.id</c> do aviso.</summary>
    /// <param name="idExterno">Recorrência.</param>
    /// <param name="aprovado">Se o cartão passou.</param>
    public string Debitar(string idExterno, bool aprovado)
    {
        var recorrencia = _recorrencias[idExterno];
        var valor = _valores[idExterno];
        var debito = Proximo();
        var pagamento = Proximo();
        var situacao = aprovado ? SituacaoDoPagamento.Aprovado : SituacaoDoPagamento.Recusado;

        _debitos[debito] = new DebitoDaRecorrencia(debito, idExterno, pagamento, situacao, valor, DateTime.UtcNow);
        _pagamentos[pagamento] = new PagamentoNoMercadoPago(
            pagamento,
            recorrencia.Referencia,
            situacao,
            valor,
            aprovado ? DateTime.UtcNow : null,
            true
        );

        return debito;
    }

    /// <summary>
    /// Ids que não se repetem entre instâncias: o banco dos testes é um só, e o id do pagamento vira o id do evento —
    /// repetido entre dois testes, o segundo cairia no "evento repetido".
    /// </summary>
    private static long _sequencia = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

    private static string Proximo() => Interlocked.Increment(ref _sequencia).ToString(CultureInfo.InvariantCulture);

    /// <summary>O valor que a recorrência cobra agora.</summary>
    /// <param name="idExterno">Recorrência.</param>
    public long ValorDaRecorrencia(string idExterno) => _valores[idExterno];

    /// <inheritdoc />
    public Task<Result<RecorrenciaNoMercadoPago>> CriarRecorrencia(string accessToken, PedidoDeRecorrencia pedido, CancellationToken ct = default)
    {
        var recorrencia = new RecorrenciaNoMercadoPago(
            $"PRE{pedido.Referencia:N}{_recorrencias.Count}",
            pedido.Referencia.ToString("N"),
            SituacaoDaRecorrencia.Pendente,
            $"https://mp.testes/preapproval/{pedido.Referencia:N}",
            pedido.ComecaEm
        );
        _recorrencias[recorrencia.IdExterno] = recorrencia;
        _valores[recorrencia.IdExterno] = pedido.ValorEmCentavos;

        return Task.FromResult(Result.Ok(recorrencia));
    }

    /// <inheritdoc />
    public Task<Result<RecorrenciaNoMercadoPago>> ConsultarRecorrencia(string accessToken, string idExterno, CancellationToken ct = default) =>
        Task.FromResult(
            Result.Ok(
                _recorrencias.GetValueOrDefault(idExterno)
                    ?? new RecorrenciaNoMercadoPago(idExterno, null, SituacaoDaRecorrencia.Pendente, null, null)
            )
        );

    /// <inheritdoc />
    public Task<Result> CancelarRecorrencia(string accessToken, string idExterno, CancellationToken ct = default)
    {
        if (_recorrencias.ContainsKey(idExterno))
            MudarRecorrencia(idExterno, SituacaoDaRecorrencia.Cancelada);

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> AtualizarValorDaRecorrencia(string accessToken, string idExterno, long valorEmCentavos, CancellationToken ct = default)
    {
        _valores[idExterno] = valorEmCentavos;

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<DebitoDaRecorrencia>> ConsultarDebitoDaRecorrencia(string accessToken, string idExterno, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(_debitos[idExterno]));

    /// <inheritdoc />
    public Task<Result<PaginaDePagamento>> CriarPagamentoAvulso(string accessToken, PedidoDePagamentoAvulso pedido, CancellationToken ct = default)
    {
        var id = $"PREF-{pedido.Referencia:N}-{_paginas.Count}";
        _paginas[id] = (pedido.Referencia, pedido.ValorEmCentavos);

        return Task.FromResult(Result.Ok(new PaginaDePagamento(id, $"https://mp.testes/checkout/{id}")));
    }

    /// <inheritdoc />
    public Task<Result<PagamentoNoMercadoPago>> ConsultarPagamento(string accessToken, string idExterno, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok(_pagamentos[idExterno]));

    /// <inheritdoc />
    public Task<Result<PagamentoNoMercadoPago?>> BuscarPagamentoAprovado(string accessToken, Guid referencia, CancellationToken ct = default) =>
        Task.FromResult(
            Result.Ok(
                _pagamentos.Values.LastOrDefault(pagamento =>
                    pagamento.Referencia == referencia.ToString("N") && pagamento.Situacao == SituacaoDoPagamento.Aprovado
                ) ?? DoPedidoPago(referencia)
            )
        );

    /// <summary>O pagamento de um pedido pago, com o líquido descontada a <see cref="Tarifa"/>.</summary>
    private PagamentoNoMercadoPago? DoPedidoPago(Guid referencia) =>
        _pedidos.FirstOrDefault(pedido => pedido.Value.Referencia == referencia && _pagos.ContainsKey(pedido.Key)) is { Key: not null } pago
            ? new PagamentoNoMercadoPago(
                pago.Key,
                referencia.ToString("N"),
                SituacaoDoPagamento.Aprovado,
                pago.Value.Valor,
                DateTime.UtcNow,
                false,
                pago.Value.Valor - pago.Value.Valor * Tarifa / 10_000
            )
            : null;

    /// <inheritdoc />
    public Task<Result> Estornar(string accessToken, string idDoPagamento, long valorEmCentavos, Guid chave, CancellationToken ct = default)
    {
        _estornos.Enqueue((idDoPagamento, valorEmCentavos));

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<PedidoConsultado>> ConsultarPedido(string accessToken, string idExterno, CancellationToken ct = default)
    {
        _consultados[idExterno] = true;
        var (referencia, valor) = _pedidos[idExterno];
        var pago = _pagos.ContainsKey(idExterno);

        if (_devolvidos.TryGetValue(idExterno, out var devolvido))
            return Task.FromResult(Result.Ok(new PedidoConsultado(idExterno, referencia.ToString("N"), devolvido, 0, null)));

        return Task.FromResult(
            Result.Ok(
                new PedidoConsultado(
                    idExterno,
                    referencia.ToString("N"),
                    pago ? SituacaoDoPedido.Pago : SituacaoDoPedido.Aberto,
                    pago ? _pagosEmParte.GetValueOrDefault(idExterno, valor) : 0,
                    pago ? DateTime.UtcNow : null
                )
            )
        );
    }

    /// <inheritdoc />
    public bool AvisoAutentico(string? assinatura, string? idDaRequisicao, string? idDoRecurso) => assinatura == AssinaturaValida;
}

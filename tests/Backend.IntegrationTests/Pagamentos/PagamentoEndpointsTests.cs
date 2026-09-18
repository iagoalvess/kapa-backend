using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Auditoria;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Backend.IntegrationTests.Infra.AdesaoDeTeste;

namespace Backend.IntegrationTests.Pagamentos;

/// <summary>
/// O caminho do dinheiro contra a API e o Postgres de verdade: quem chama cada endpoint e os critérios de
/// aceite da Sprint 9 — informe é alegação, lote é uma transação, confirmação paralela baixa uma vez,
/// estorno deixa as duas linhas, multa e juros saem do snapshot.
/// </summary>
/// <remarks>
/// A turma é montada pela API, como a comissão faz: plano com multa de 2% e juros de 1% ao mês, termo,
/// chave PIX, e o formando adere — é a adesão que gera as parcelas e grava as regras que valem para ele.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class PagamentoEndpointsTests(ApiFactory fabrica)
{
    private const long Mensalidade = 350_000;

    private const string Ip = "203.0.113.7";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Hoje => DataUtils.Hoje();

    /// <summary>Uma turma com plano vigente, termo publicado e, se pedido, a chave PIX cadastrada.</summary>
    private sealed record Turma(Guid FormaturaId, MembroDeTeste Presidente, MembroDeTeste Tesoureiro, Guid PlanoId);

    /// <summary>Um formando que aderiu, com as três parcelas por vencimento.</summary>
    private sealed record Formando(MembroDeTeste Membro, IReadOnlyList<Guid> Parcelas);

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.NotFound)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Conferir_e_da_tesouraria_e_estornar_so_do_presidente(
        string papel,
        HttpStatusCode fila,
        HttpStatusCode confirmar,
        HttpStatusCode estornar
    )
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        (await membro.Cliente.GetAsync("/api/v1/informes", Ct)).StatusCode.ShouldBe(fila);
        (await membro.Cliente.GetAsync("/api/v1/recebimentos/divergencias", Ct)).StatusCode.ShouldBe(fila);
        (await membro.Cliente.PostAsJsonAsync("/api/v1/informes/confirmar", new ConfirmarInformesRequestDTO([]), Json, Ct)).StatusCode.ShouldBe(
            confirmar
        );
        (
            await membro.Cliente.PostAsJsonAsync(
                $"/api/v1/parcelas/{Guid.CreateVersion7()}/estornar-baixa",
                new EstornarBaixaRequestDTO("Baixa em parcela errada."),
                Json,
                Ct
            )
        ).StatusCode.ShouldBe(estornar);
    }

    /// <summary>O fluxo inteiro: extrato, PIX, "já paguei", fila da tesouraria, lote confirmado.</summary>
    [Fact]
    public async Task Formando_avisa_e_o_lote_grava_parcela_recebimento_informe_e_email_juntos()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Pagadora");
        var primeira = ana.Parcelas[0];

        var extrato = await Ler<ExtratoDTO>(await ana.Membro.Cliente.GetAsync("/api/v1/extrato/eu", Ct));
        var pix = await Ler<PixDaParcelaDTO>(await ana.Membro.Cliente.GetAsync($"/api/v1/parcelas/{primeira}/pix", Ct));
        var informe = await Ler<ParcelaDTO>(
            await ana.Membro.Cliente.PostAsync($"/api/v1/parcelas/{primeira}/informes", Informe(Hoje, Mensalidade, comComprovante: true), Ct)
        );
        var repetido = await ana.Membro.Cliente.PostAsync($"/api/v1/parcelas/{primeira}/informes", Informe(Hoje, Mensalidade), Ct);
        var depoisDoAviso = await Ler<ExtratoDTO>(await ana.Membro.Cliente.GetAsync("/api/v1/extrato/eu", Ct));

        extrato.Parcelas.Count.ShouldBe(3);
        extrato.EmAbertoEmCentavos.ShouldBe(3 * Mensalidade);
        extrato.Proxima!.Id.ShouldBe(primeira);
        pix.CopiaECola.ShouldContain("52998224725");
        pix.CopiaECola.ShouldContain(pix.Identificador);
        pix.ValorEmCentavos.ShouldBe(Mensalidade);
        pix.NomeDoTitular.ShouldBe("Comissão Medicina");
        informe.EmConferencia.ShouldBeTrue();
        informe.Status.ShouldBe(StatusDaParcela.Aberta);
        (await repetido.Codigo(Ct)).ShouldBe("pagamento.informe_pendente");
        depoisDoAviso.Parcelas[0].EmConferencia.ShouldBeTrue();
        depoisDoAviso.Proxima!.Id.ShouldBe(ana.Parcelas[1]);
        (await ParcelaNoBanco(turma, primeira)).Status.ShouldBe(StatusDaParcela.Aberta);

        var fila = await Ler<PaginaDTO<InformeDTO>>(await turma.Tesoureiro.Cliente.GetAsync("/api/v1/informes", Ct));
        var pendente = fila.Itens.ShouldHaveSingleItem();
        var comprovante = await turma.Tesoureiro.Cliente.GetAsync($"/api/v1/informes/{pendente.Id}/comprovante", Ct);
        await using var comIp = ComIp();
        var antes = DateTime.UtcNow;
        var lote = await Ler<ResultadoDaConferenciaDTO>(await Confirmar(Autenticado(comIp, turma.Tesoureiro), (pendente.Id, Mensalidade)));

        pendente.TemComprovante.ShouldBeTrue();
        pendente.DevidoEmCentavos.ShouldBe(Mensalidade);
        pendente.Parcela.Id.ShouldBe(primeira);
        comprovante.StatusCode.ShouldBe(HttpStatusCode.OK);
        comprovante.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        lote.ShouldBe(new ResultadoDaConferenciaDTO(1, 0));

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var paga = await contexto.Parcelas.SingleAsync(p => p.Id == primeira, Ct);
        var recebimento = await contexto.Recebimentos.SingleAsync(r => r.ParcelaId == primeira, Ct);
        var confirmado = await contexto.Informes.SingleAsync(i => i.Id == pendente.Id, Ct);
        paga.Status.ShouldBe(StatusDaParcela.Paga);
        paga.ValorPagoEmCentavos.ShouldBe(Mensalidade);
        recebimento.InformeId.ShouldBe(pendente.Id);
        recebimento.BaixadoPorUsuarioId.ShouldBe(turma.Tesoureiro.UsuarioId);
        recebimento.EnderecoIp.ShouldBe(Ip);
        recebimento.BaixadoEm.ShouldBeInRange(antes.AddSeconds(-1), DateTime.UtcNow);
        confirmado.Status.ShouldBe(StatusDoInforme.Confirmado);
        confirmado.ConferidoPorUsuarioId.ShouldBe(turma.Tesoureiro.UsuarioId);
        (await EmailsPara(ana.Membro, "Pagamento confirmado")).ShouldBe(1);
        (await Auditoria("pagamento.baixado", turma.Tesoureiro, primeira)).ShouldBe(1);

        var trilha = await Ler<PaginaDTO<LinhaDeAuditoriaDTO>>(await turma.Tesoureiro.Cliente.GetAsync("/api/v1/auditoria", Ct));
        var naTrilha = trilha.Itens.Single(item => item.Nome == "pagamento.baixado");

        naTrilha.Pessoas[primeira.ToString()].ShouldBe(await NomeDoUsuario(ana.Membro.UsuarioId));

        var informeNaPaga = await ana.Membro.Cliente.PostAsync($"/api/v1/parcelas/{primeira}/informes", Informe(Hoje, Mensalidade), Ct);
        (await informeNaPaga.Codigo(Ct)).ShouldBe("pagamento.parcela_paga");
    }

    [Fact]
    public async Task Parcela_de_outro_formando_e_404_na_leitura_no_pix_e_no_informe()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Dona");
        var bruno = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var parcela = ana.Parcelas[0];

        (await bruno.Cliente.GetAsync($"/api/v1/parcelas/{parcela}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.Cliente.GetAsync($"/api/v1/parcelas/{parcela}/pix", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.Cliente.PostAsync($"/api/v1/parcelas/{parcela}/informes", Informe(Hoje, Mensalidade), Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
        (await turma.Tesoureiro.Cliente.GetAsync($"/api/v1/parcelas/{parcela}/pix", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>P3 de 14/09/2026: só a turma sem chave responde 409 — a não conferida mostra o PIX.</summary>
    [Fact]
    public async Task Sem_chave_cadastrada_o_pix_e_409()
    {
        var turma = await TurmaPronta(comConta: false);
        var ana = await FormandoQueAderiu(turma, "Ana Sem Chave");

        var resposta = await ana.Membro.Cliente.GetAsync($"/api/v1/parcelas/{ana.Parcelas[0]}/pix", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("pagamento.sem_conta");
    }

    [Fact]
    public async Task Confirmar_o_mesmo_informe_em_paralelo_baixa_a_parcela_uma_vez()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Paralela");
        var informeId = await Informar(turma, ana, ana.Parcelas[0]);

        var respostas = await Task.WhenAll(
            Confirmar(turma.Tesoureiro, (informeId, Mensalidade)),
            Confirmar(turma.Tesoureiro, (informeId, Mensalidade))
        );

        var resultados = await Task.WhenAll(respostas.Select(r => Ler<ResultadoDaConferenciaDTO>(r)));
        resultados.Sum(r => r.Confirmados).ShouldBe(1);
        resultados.Sum(r => r.Ignorados).ShouldBe(1);
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.Recebimentos.CountAsync(r => r.ParcelaId == ana.Parcelas[0], Ct)).ShouldBe(1);
        (await EmailsPara(ana.Membro, "Pagamento confirmado")).ShouldBe(1);
    }

    /// <summary>
    /// Recebido a menos é pagamento parcial: entra no caixa, aparece em Divergências e a parcela
    /// <b>continua aberta</b> pelo saldo (revisão de 17/09/2026).
    /// </summary>
    [Fact]
    public async Task Valor_recebido_a_menos_deixa_a_parcela_aberta_e_aparece_em_divergencias()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Divergente");
        var informeId = await Informar(turma, ana, ana.Parcelas[0]);

        (await Confirmar(turma.Tesoureiro, (informeId, 300_000))).EnsureSuccessStatusCode();

        var divergencias = await Ler<PaginaDTO<DivergenciaDTO>>(await turma.Tesoureiro.Cliente.GetAsync("/api/v1/recebimentos/divergencias", Ct));
        var divergencia = divergencias.Itens.ShouldHaveSingleItem();
        divergencia.Parcela.Id.ShouldBe(ana.Parcelas[0]);
        divergencia.Parcela.Status.ShouldBe(StatusDaParcela.Aberta);
        divergencia.DevidoEmCentavos.ShouldBe(Mensalidade);
        divergencia.RecebidoEmCentavos.ShouldBe(300_000);
        divergencia.BaixadoPor.ShouldBe("Usuário de Teste");

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var parcela = await contexto.Parcelas.SingleAsync(p => p.Id == ana.Parcelas[0], Ct);
        parcela.ValorPagoEmCentavos.ShouldBe(300_000);
    }

    /// <summary>E o resto, depois, fecha a parcela — duas entradas no caixa para a mesma parcela.</summary>
    [Fact]
    public async Task Saldo_pago_depois_fecha_a_parcela()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Parcial");
        var parcelaId = ana.Parcelas[0];
        (await Confirmar(turma.Tesoureiro, (await Informar(turma, ana, parcelaId), 300_000))).EnsureSuccessStatusCode();

        // O saldo pela baixa manual: é o caminho de quem pagou o resto em dinheiro e não avisou.
        (
            await turma.Tesoureiro.Cliente.PostAsync($"/api/v1/parcelas/{parcelaId}/baixa-manual", BaixaManual(valor: Mensalidade - 300_000), Ct)
        ).EnsureSuccessStatusCode();

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var parcela = await contexto.Parcelas.SingleAsync(p => p.Id == parcelaId, Ct);
        parcela.Status.ShouldBe(StatusDaParcela.Paga);
        parcela.ValorPagoEmCentavos.ShouldBe(Mensalidade);
        (await contexto.Recebimentos.CountAsync(r => r.ParcelaId == parcelaId, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Recusa_exige_motivo_avisa_o_formando_e_a_parcela_continua_aberta()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Recusada");
        var informeId = await Informar(turma, ana, ana.Parcelas[0]);

        var semMotivo = await turma.Tesoureiro.Cliente.PostAsJsonAsync(
            $"/api/v1/informes/{informeId}/recusar",
            new RecusarInformeRequestDTO(" "),
            Json,
            Ct
        );
        var recusa = await turma.Tesoureiro.Cliente.PostAsJsonAsync(
            $"/api/v1/informes/{informeId}/recusar",
            new RecusarInformeRequestDTO("Não encontrei no extrato."),
            Json,
            Ct
        );
        var avisoDeNovo = await ana.Membro.Cliente.PostAsync($"/api/v1/parcelas/{ana.Parcelas[0]}/informes", Informe(Hoje, Mensalidade), Ct);

        semMotivo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        recusa.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ParcelaNoBanco(turma, ana.Parcelas[0])).Status.ShouldBe(StatusDaParcela.Aberta);
        (await EmailsPara(ana.Membro, "Pagamento não confirmado")).ShouldBe(1);
        avisoDeNovo.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Baixa manual e estorno: autor, IP e hora na baixa; estorno só do Presidente, com justificativa, e as duas linhas ficam.</summary>
    [Fact]
    public async Task Estorno_exige_presidente_e_justificativa_e_mantem_as_duas_linhas_na_auditoria()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Estornada");
        var parcela = ana.Parcelas[0];

        await using var comIp = ComIp();
        var baixa = await Ler<ParcelaDTO>(
            await Autenticado(comIp, turma.Tesoureiro).Cliente.PostAsync($"/api/v1/parcelas/{parcela}/baixa-manual", BaixaManual(), Ct)
        );
        var doTesoureiro = await Estornar(turma.Tesoureiro, parcela, "Baixa na parcela errada.");
        var semJustificativa = await Estornar(turma.Presidente, parcela, "");
        var estorno = await Ler<ParcelaDTO>(await Estornar(turma.Presidente, parcela, "Baixa na parcela errada."));

        baixa.Status.ShouldBe(StatusDaParcela.Paga);
        baixa.ValorPagoEmCentavos.ShouldBe(Mensalidade);
        doTesoureiro.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        semJustificativa.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        estorno.Status.ShouldBe(StatusDaParcela.Aberta);
        estorno.ValorPagoEmCentavos.ShouldBeNull();

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var recebimento = await contexto.Recebimentos.SingleAsync(r => r.ParcelaId == parcela, Ct);
        recebimento.Forma.ShouldBe(FormaDePagamento.Dinheiro);
        recebimento.BaixadoPorUsuarioId.ShouldBe(turma.Tesoureiro.UsuarioId);
        recebimento.EnderecoIp.ShouldBe(Ip);
        recebimento.EstornadoEm.ShouldNotBeNull();
        recebimento.EstornadoPorUsuarioId.ShouldBe(turma.Presidente.UsuarioId);
        recebimento.JustificativaDoEstorno.ShouldBe("Baixa na parcela errada.");
        (await Auditoria("pagamento.baixado", turma.Tesoureiro, parcela)).ShouldBe(1);
        (await Auditoria("pagamento.estornado", turma.Presidente, parcela)).ShouldBe(1);
        (await EmailsPara(ana.Membro, "Pagamento estornado")).ShouldBe(1);
    }

    /// <summary>
    /// Multa e juros vêm do snapshot da adesão, mesmo com o plano mudado depois; a vencida aparece sem job, e
    /// a que vence hoje ainda não — no extrato, na lista e no resumo da gestão.
    /// </summary>
    [Fact]
    public async Task Vencida_usa_as_regras_aceitas_e_aparece_sem_job_no_extrato_e_no_resumo()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Atrasada");
        await MoverVencimento(turma, ana.Parcelas[0], Hoje.AddDays(-30));
        await MoverVencimento(turma, ana.Parcelas[1], Hoje);
        (
            await turma.Tesoureiro.Cliente.PutAsJsonAsync(
                $"/api/v1/cobrancas/planos/{turma.PlanoId}",
                new PlanoDeCobrancaRequestDTO("Plano 2027", 1_000, 500, 0, 0, 0),
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();

        var extrato = await Ler<ExtratoDTO>(await ana.Membro.Cliente.GetAsync("/api/v1/extrato/eu", Ct));
        var resumo = await Ler<ResumoDeParcelasDTO>(await turma.Tesoureiro.Cliente.GetAsync("/api/v1/cobrancas/parcelas/resumo", Ct));
        var lista = await Ler<PaginaDTO<ParcelaDTO>>(await turma.Tesoureiro.Cliente.GetAsync("/api/v1/cobrancas/parcelas?status=Vencida", Ct));

        var vencida = extrato.Parcelas.Single(p => p.Id == ana.Parcelas[0]);
        var venceHoje = extrato.Parcelas.Single(p => p.Id == ana.Parcelas[1]);
        vencida.Status.ShouldBe(StatusDaParcela.Vencida);
        vencida.ValorDoDia!.MultaEmCentavos.ShouldBe(7_000);
        vencida.ValorDoDia.JurosEmCentavos.ShouldBe(3_500);
        vencida.ValorDoDia.TotalEmCentavos.ShouldBe(360_500);
        venceHoje.Status.ShouldBe(StatusDaParcela.Aberta);
        venceHoje.ValorDoDia!.TotalEmCentavos.ShouldBe(Mensalidade);
        extrato.EmAbertoEmCentavos.ShouldBe(360_500 + 2 * Mensalidade);
        resumo.Vencida.ShouldBe(new SomaDeParcelasDTO(1, Mensalidade));
        resumo.Aberta.Quantidade.ShouldBe(2);
        resumo.VencidoAtualizadoEmCentavos.ShouldBe(360_500);
        lista.Itens.ShouldHaveSingleItem().ValorDoDia!.TotalEmCentavos.ShouldBe(360_500);
        (await ParcelaNoBanco(turma, ana.Parcelas[0])).Status.ShouldBe(StatusDaParcela.Aberta);
    }

    /// <summary>
    /// O selo do menu conta a vencida sem aviso — e ela sai da conta no instante em que o formando avisa,
    /// sem esperar a tesouraria conferir.
    /// </summary>
    [Fact]
    public async Task Pendencias_do_extrato_contam_a_vencida_ate_o_aviso_chegar()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Ana Sinalizada");
        await MoverVencimento(turma, ana.Parcelas[0], Hoje.AddDays(-30));

        var comVencida = await Ler<PendenciasDoExtratoDTO>(await ana.Membro.Cliente.GetAsync("/api/v1/extrato/eu/pendencias", Ct));
        (
            await ana.Membro.Cliente.PostAsync($"/api/v1/parcelas/{ana.Parcelas[0]}/informes", Informe(Hoje, Mensalidade), Ct)
        ).EnsureSuccessStatusCode();
        var depoisDoAviso = await Ler<PendenciasDoExtratoDTO>(await ana.Membro.Cliente.GetAsync("/api/v1/extrato/eu/pendencias", Ct));

        comVencida.VencidasSemAviso.ShouldBe(1);
        depoisDoAviso.VencidasSemAviso.ShouldBe(0);
    }

    [Fact]
    public async Task Busca_acha_as_parcelas_pelo_nome_civil_ou_da_conta_e_o_resumo_soma_o_recebido()
    {
        var turma = await TurmaPronta();
        var ana = await FormandoQueAderiu(turma, "Zuléica Buscada");
        // Acima do devido: a parcela fecha, e o resumo soma o que entrou de verdade, não o original.
        (
            await turma.Tesoureiro.Cliente.PostAsync($"/api/v1/parcelas/{ana.Parcelas[0]}/baixa-manual", BaixaManual(valor: 360_000), Ct)
        ).EnsureSuccessStatusCode();

        var porNome = await Ler<PaginaDTO<ParcelaDTO>>(await turma.Tesoureiro.Cliente.GetAsync("/api/v1/cobrancas/parcelas?busca=zuleica", Ct));
        var ninguem = await Ler<PaginaDTO<ParcelaDTO>>(await turma.Tesoureiro.Cliente.GetAsync("/api/v1/cobrancas/parcelas?busca=ninguem-aqui", Ct));
        var resumo = await Ler<ResumoDeParcelasDTO>(await turma.Tesoureiro.Cliente.GetAsync("/api/v1/cobrancas/parcelas/resumo?busca=zuleica", Ct));

        porNome.Total.ShouldBe(3);
        porNome.Itens.ShouldAllBe(p => p.Nome == "Zuléica Buscada");
        ninguem.Total.ShouldBe(0);
        resumo.Todas.Quantidade.ShouldBe(3);
        resumo.Paga.ShouldBe(new SomaDeParcelasDTO(1, 360_000));
    }

    /// <summary>A API com o IP remoto fixo — o TestServer não preenche, e IP é parte da trilha da baixa.</summary>
    private WebApplicationFactory<Program> ComIp() =>
        fabrica.WithWebHostBuilder(host => host.ConfigureTestServices(servicos => servicos.AddSingleton<IStartupFilter>(new IpFixo(Ip))));

    /// <summary>O mesmo membro, chamando pela API com IP fixo.</summary>
    private static MembroDeTeste Autenticado(WebApplicationFactory<Program> comIp, MembroDeTeste membro)
    {
        var cliente = comIp.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        cliente.DefaultRequestHeaders.Authorization = membro.Cliente.DefaultRequestHeaders.Authorization;

        return membro with
        {
            Cliente = cliente,
        };
    }

    private async Task<Turma> TurmaPronta(bool comConta = true)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);

        var plano = await Ler<PlanoDeCobrancaDTO>(
            await presidente.Cliente.PostAsJsonAsync(
                "/api/v1/cobrancas/planos",
                new PlanoDeCobrancaRequestDTO("Plano 2027", 200, 100, 0, 0, 0),
                Json,
                Ct
            ),
            HttpStatusCode.Created
        );
        (
            await presidente.Cliente.PostAsJsonAsync(
                $"/api/v1/cobrancas/planos/{plano.Id}/itens",
                new ItemDeCobrancaRequestDTO(TipoDeCobranca.Mensalidade, null, 3 * Mensalidade, 3, 10, new DateOnly(Hoje.Year + 1, 3, 1)),
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();
        (await presidente.Cliente.PostAsync($"/api/v1/cobrancas/planos/{plano.Id}/vigorar", null, Ct)).EnsureSuccessStatusCode();
        (
            await presidente.Cliente.PostAsJsonAsync(
                "/api/v1/adesoes/termos",
                new { conteudo = "# Termo\n\nA turma divide o custo da formatura." },
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();

        if (comConta)
            (
                await presidente.Cliente.PutAsJsonAsync(
                    "/api/v1/recebimentos/conta",
                    new ContaDeRecebimentoRequestDTO(TipoDeChavePix.Cpf, "529.982.247-25", "Comissão Medicina", "Curitiba"),
                    Json,
                    Ct
                )
            ).EnsureSuccessStatusCode();

        return new Turma(formaturaId, presidente, tesoureiro, plano.Id);
    }

    private async Task<Formando> FormandoQueAderiu(Turma turma, string nome)
    {
        var membro = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(membro.Cliente, NovoCpf(), nome: nome);
        (await Aderir(fabrica, membro.Cliente)).Resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == membro.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
        var parcelas = await contexto.Parcelas.Where(p => p.VinculoId == vinculoId).OrderBy(p => p.Vencimento).Select(p => p.Id).ToListAsync(Ct);

        return new Formando(membro, parcelas);
    }

    /// <summary>O "já paguei" do formando, devolvendo o id do informe criado.</summary>
    private async Task<Guid> Informar(Turma turma, Formando formando, Guid parcelaId)
    {
        (await formando.Membro.Cliente.PostAsync($"/api/v1/parcelas/{parcelaId}/informes", Informe(Hoje, Mensalidade), Ct)).EnsureSuccessStatusCode();

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);

        return await contexto.Informes.Where(i => i.ParcelaId == parcelaId).Select(i => i.Id).SingleAsync(Ct);
    }

    private static Task<HttpResponseMessage> Confirmar(MembroDeTeste tesouraria, params (Guid Informe, long Valor)[] itens) =>
        tesouraria.Cliente.PostAsJsonAsync(
            "/api/v1/informes/confirmar",
            new ConfirmarInformesRequestDTO([.. itens.Select(i => new ConfirmacaoDeInformeDTO(i.Informe, i.Valor))]),
            Json,
            Ct
        );

    private static Task<HttpResponseMessage> Estornar(MembroDeTeste membro, Guid parcelaId, string justificativa) =>
        membro.Cliente.PostAsJsonAsync($"/api/v1/parcelas/{parcelaId}/estornar-baixa", new EstornarBaixaRequestDTO(justificativa), Json, Ct);

    private static MultipartFormDataContent Informe(DateOnly pagoEm, long valor, bool comComprovante = false)
    {
        var formulario = new MultipartFormDataContent
        {
            { new StringContent(pagoEm.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "pagoEm" },
            { new StringContent(valor.ToString(CultureInfo.InvariantCulture)), "valorEmCentavos" },
        };

        if (comComprovante)
            formulario.Add(new ByteArrayContent("%PDF-1.4 comprovante de teste"u8.ToArray()), "comprovante", "comprovante.pdf");

        return formulario;
    }

    private static MultipartFormDataContent BaixaManual(long valor = Mensalidade)
    {
        var formulario = Informe(Hoje, valor);
        formulario.Add(new StringContent(nameof(FormaDePagamento.Dinheiro)), "forma");

        return formulario;
    }

    private async Task MoverVencimento(Turma turma, Guid parcelaId, DateOnly vencimento)
    {
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);

        await contexto.Parcelas.Where(p => p.Id == parcelaId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Vencimento, vencimento), Ct);
    }

    private async Task<Parcela> ParcelaNoBanco(Turma turma, Guid parcelaId)
    {
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);

        return await contexto.Parcelas.AsNoTracking().SingleAsync(p => p.Id == parcelaId, Ct);
    }

    private async Task<int> EmailsPara(MembroDeTeste membro, string assunto)
    {
        await using var contexto = fabrica.ContextoDe(null);
        var email = await contexto.Users.Where(u => u.Id == membro.UsuarioId).Select(u => u.Email!).SingleAsync(Ct);

        return await contexto.EmailsFila.CountAsync(e => e.Para == email && e.Assunto.StartsWith(assunto), Ct);
    }

    /// <summary>Quantos eventos de auditoria o autor deixou sobre a parcela.</summary>
    private async Task<int> Auditoria(string nome, MembroDeTeste autor, Guid parcelaId)
    {
        await using var contexto = fabrica.ContextoDe(null);
        var eventos = await contexto.Eventos.Where(e => e.Nome == nome && e.UsuarioId == autor.UsuarioId).ToListAsync(Ct);

        return eventos.Count(e => e.Dados!.Contains(parcelaId.ToString(), StringComparison.Ordinal));
    }

    /// <summary>O nome da conta, como a trilha de auditoria o resolve a partir da parcela.</summary>
    private async Task<string> NomeDoUsuario(Guid usuarioId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Nome).SingleAsync(Ct);
    }

    private static async Task<T> Ler<T>(HttpResponseMessage resposta, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        resposta.StatusCode.ShouldBe(esperado, await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }
}

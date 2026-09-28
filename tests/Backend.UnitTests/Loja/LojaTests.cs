using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Services;
using Backend.Business.Festa.Settings;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Backend.Business.Loja.Services;
using Backend.Business.Loja.Validators;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Loja;

/// <summary>
/// A loja pública (Sprint 26): o link assinado, os prazos por meio, a compra que paga tarde e a porta
/// exclusiva do item.
/// </summary>
public sealed class LojaTests
{
    private const string Segredo = "c2VncmVkby1kZS10ZXN0ZS1jb20tdHJpbnRhLWUtZG9pcy1ieXRlcw==";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>O link da compra, com o segredo de teste.</summary>
    public static LinkDaCompra Link() => new(Options.Create(new ConviteSettings { SegredoDoConvite = Segredo }));

    /// <summary>Uma compra pendente de dois convites por PIX.</summary>
    /// <param name="itemId">O item; nulo sorteia.</param>
    public static CompraDeConvite Compra(Guid? itemId = null) =>
        new(
            new DadosDaCompra(
                itemId ?? Guid.CreateVersion7(),
                2,
                "Maria Souza",
                " Maria@Teste.dev ",
                "52998224725",
                MeioDePagamento.Pix,
                Guid.CreateVersion7()
            ),
            20_000,
            DateTime.UtcNow.AddMinutes(31)
        );

    /// <summary>A confirmação da compra, com as dependências de fora substituídas.</summary>
    public static PagamentoDaCompra Pagamento(
        ICompraDeConviteRepository? compras = null,
        IOutraReceitaRepository? receitas = null,
        IConviteDoEventoRepository? convites = null,
        IEmailService? emailService = null
    ) =>
        new(
            compras ?? Substitute.For<ICompraDeConviteRepository>(),
            receitas ?? Substitute.For<IOutraReceitaRepository>(),
            new EmissaoDeConvites(
                convites ?? Substitute.For<IConviteDoEventoRepository>(),
                Substitute.For<IEventoDaTurmaRepository>(),
                Substitute.For<IFormaturaRepository>(),
                Substitute.For<IFormaturaAtual>(),
                NullLogger<EmissaoDeConvites>.Instance
            ),
            new EmailsDaLoja(
                emailService ?? Substitute.For<IEmailService>(),
                Link(),
                Options.Create(new AplicacaoSettings { Nome = "Kapa", UrlDoFrontend = "https://app.kapa.dev" }),
                Substitute.For<IFormaturaRepository>(),
                Substitute.For<IVinculoRepository>()
            ),
            Substitute.For<IUnitOfWork>(),
            NullLogger<PagamentoDaCompra>.Instance
        );

    // ---- Link de acesso (decisão 10) ----

    [Fact]
    public void Link_confere_so_na_versao_atual_e_morre_ao_girar()
    {
        // Arrange
        var link = Link();
        var compra = Compra();
        var token = link.Token(compra);

        // Act
        compra.GirarLink();

        // Assert
        LinkDaCompra.Id(token).ShouldBe(compra.Id);
        link.Confere(token, compra).ShouldBeFalse();
        link.Confere(link.Token(compra), compra).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-token")]
    [InlineData("0197a1b2c3d4e5f60718293a4b5c6d7e.")]
    public void Token_torto_nao_aponta_compra_nenhuma(string? token) => LinkDaCompra.Id(token).ShouldBeNull();

    [Fact]
    public void Link_de_uma_compra_nao_abre_outra()
    {
        var link = Link();
        var uma = Compra();
        var outra = Compra();

        link.Confere(link.Token(uma), outra).ShouldBeFalse();
    }

    // ---- Prazos da reserva (decisão 3; P1 da Sprint 35) ----

    [Fact]
    public void Pix_vence_antes_da_reserva_e_nunca_com_menos_de_30_minutos()
    {
        var agora = DateTime.UtcNow;

        var (reserva, documento) = LojaService.Prazos(agora);

        (documento - agora).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
        (documento - agora).ShouldBeLessThan(TimeSpan.FromMinutes(31));
        reserva.ShouldBeGreaterThan(documento);
    }

    // ---- A compra ----

    [Fact]
    public void Compra_normaliza_o_email_e_multiplica_o_preco()
    {
        var compra = Compra();

        compra.Email.ShouldBe("maria@teste.dev");
        compra.ValorEmCentavos.ShouldBe(40_000);
        compra.Status.ShouldBe(StatusDaCompra.Pendente);
    }

    [Fact]
    public void Pagamento_repetido_nao_muda_nada()
    {
        var compra = Compra();

        compra.Pagar(40_000, DateTime.UtcNow, null, reservouDeNovo: false).ShouldBeTrue();
        compra.Pagar(40_000, DateTime.UtcNow, null, reservouDeNovo: false).ShouldBeFalse();
        compra.Status.ShouldBe(StatusDaCompra.Paga);
    }

    [Fact]
    public void Apagar_os_dados_a_pedido_leva_o_nome_e_mata_o_link()
    {
        var link = Link();
        var compra = Compra();
        var token = link.Token(compra);

        compra.ApagarDados(DateTime.UtcNow, inclusiveONome: true);

        compra.Email.ShouldBeNull();
        compra.Cpf.ShouldBeNull();
        compra.NomeDoComprador.ShouldBeNull();
        link.Confere(token, compra).ShouldBeFalse();
    }

    // ---- Confirmação (decisão 9) ----

    [Fact]
    public async Task Pagamento_de_compra_expirada_sem_lugar_vai_para_a_devolucao_e_vira_receita()
    {
        // Arrange
        var compra = Compra();
        await ExpirarNaMao(compra);
        var compras = Substitute.For<ICompraDeConviteRepository>();
        var receitas = Substitute.For<IOutraReceitaRepository>();
        var convites = Substitute.For<IConviteDoEventoRepository>();
        compras.Travar(compra.Id, Arg.Any<CancellationToken>()).Returns(compra);
        compras.ReservarNoItem(compra.ItemDeCobrancaId, 2, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var confirmou = await Pagamento(compras, receitas, convites).Confirmar(compra.Id, Pago(40_000), Guid.CreateVersion7(), Ct);

        // Assert
        confirmou.ShouldBeTrue();
        compra.Status.ShouldBe(StatusDaCompra.ADevolver);
        compra.OutraReceitaId.ShouldNotBeNull();
        await receitas
            .Received(1)
            .Adicionar(
                Arg.Is<OutraReceita>(r =>
                    r.Categoria == CategoriaDeOutraReceita.VendaDeConvite && r.ValorEmCentavos == 40_000 && r.Status == StatusDaOutraReceita.Recebida
                ),
                Arg.Any<CancellationToken>()
            );
        await convites.DidNotReceiveWithAnyArgs().EmitirDaCompra(default, default, default, default!, Ct);
    }

    [Fact]
    public async Task Pagamento_de_compra_expirada_com_lugar_reserva_de_novo_e_fica_paga()
    {
        var compra = Compra();
        await ExpirarNaMao(compra);
        var compras = Substitute.For<ICompraDeConviteRepository>();
        compras.Travar(compra.Id, Arg.Any<CancellationToken>()).Returns(compra);
        compras.ReservarNoItem(compra.ItemDeCobrancaId, 2, Arg.Any<CancellationToken>()).Returns(true);

        await Pagamento(compras).Confirmar(compra.Id, Pago(40_000), Guid.CreateVersion7(), Ct);

        compra.Status.ShouldBe(StatusDaCompra.Paga);
    }

    [Fact]
    public async Task Confirmacao_repetida_nao_cria_segunda_receita()
    {
        var compra = Compra();
        compra.Pagar(40_000, DateTime.UtcNow, null, reservouDeNovo: false);
        var compras = Substitute.For<ICompraDeConviteRepository>();
        var receitas = Substitute.For<IOutraReceitaRepository>();
        compras.Travar(compra.Id, Arg.Any<CancellationToken>()).Returns(compra);

        var confirmou = await Pagamento(compras, receitas).Confirmar(compra.Id, Pago(40_000), Guid.CreateVersion7(), Ct);

        confirmou.ShouldBeFalse();
        await receitas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Confirmacao_grava_o_cpf_do_pagador_que_o_provedor_informa()
    {
        var compra = Compra();
        var compras = Substitute.For<ICompraDeConviteRepository>();
        compras.Travar(compra.Id, Arg.Any<CancellationToken>()).Returns(compra);

        await Pagamento(compras).Confirmar(compra.Id, Pago(40_000) with { CpfDoPagador = "11144477735" }, Guid.CreateVersion7(), Ct);

        compra.CpfDoPagador.ShouldBe("11144477735");
    }

    // ---- As duas portas (decisão 1, P8) ----

    [Fact]
    public void Item_da_loja_nao_aceita_pedido_do_formando_mas_devolve_estoque()
    {
        var item = ItemDeCobranca.NovoOpcional(
            Guid.CreateVersion7(),
            new DadosDoOpcional(Convite, Estoque: 10, ModoDeVenda: ModoDeVenda.Publica, PrecoPublicoEmCentavos: 25_000)
        );

        item.Reservar(1, 1, DateTime.UtcNow).PrimeiroErro.Codigo.ShouldBe("cobranca.item_da_loja");
        item.Reservar(-1, 0, DateTime.UtcNow).Sucesso.ShouldBeTrue();
        item.PrecoNaLoja.ShouldBe(25_000);
    }

    [Fact]
    public void Preco_publico_so_vale_no_item_da_loja()
    {
        var item = ItemDeCobranca.NovoOpcional(Guid.CreateVersion7(), new DadosDoOpcional(Convite, PrecoPublicoEmCentavos: 25_000));

        item.PrecoPublicoEmCentavos.ShouldBeNull();
        item.PrecoNaLoja.ShouldBe(Convite.ValorEmCentavos);
    }

    [Fact]
    public void Abertura_vale_pela_hora_e_nao_so_pelo_dia()
    {
        var abertura = DateTime.UtcNow.AddMinutes(10);
        var item = ItemDeCobranca.NovoOpcional(
            Guid.CreateVersion7(),
            new DadosDoOpcional(Convite, AberturaDeVendas: abertura, ModoDeVenda: ModoDeVenda.Publica)
        );

        item.Fechado(abertura.AddSeconds(-1))!.Codigo.ShouldBe("cobranca.venda_nao_aberta");
        item.Fechado(abertura).ShouldBeNull();
    }

    // ---- Forma da compra (P6, P7) ----

    [Theory]
    [InlineData("52998224725", true)]
    [InlineData("52998224724", false)]
    [InlineData("11111111111", false)]
    public void Cpf_com_digito_errado_e_recusado_antes_da_reserva(string cpf, bool valido) =>
        new DadosDaCompraValidator().Validate(Dados() with { Cpf = cpf }).Errors.Any(erro => erro.ErrorCode == "loja.cpf_invalido").ShouldBe(!valido);

    [Theory]
    [InlineData(MeioDePagamento.Pix, true)]
    [InlineData(MeioDePagamento.Cartao, false)]
    [InlineData(MeioDePagamento.PixAutomatico, false)]
    public void Compra_so_aceita_os_meios_ligados(MeioDePagamento meio, bool aceito) =>
        new DadosDaCompraValidator().Validate(Dados() with { Meio = meio }).IsValid.ShouldBe(aceito);

    [Fact]
    public void Validador_do_opcional_so_leva_convite_para_a_loja()
    {
        var resultado = new DadosDoOpcionalValidator().Validate(
            new DadosDoOpcional(Convite with { Tipo = TipoDeCobranca.Kit }, ModoDeVenda: ModoDeVenda.Publica)
        );

        resultado.Errors.ShouldContain(erro => erro.ErrorCode == "loja.so_convite");
    }

    private static DadosDoItem Convite => new(TipoDeCobranca.ConviteExtra, "Convite adulto", 20_000, 1, 10, DataUtils.Hoje().AddMonths(1));

    private static DadosDaCompra Dados() =>
        new(Guid.CreateVersion7(), 1, "Maria Souza", "maria@teste.dev", "52998224725", MeioDePagamento.Pix, Guid.CreateVersion7());

    private static PedidoConsultado Pago(long valor) => new("ORD1", null, SituacaoDoPedido.Pago, valor, DateTime.UtcNow);

    /// <summary>A expiração é SQL no repositório; aqui a compra só precisa estar no estado.</summary>
    private static Task ExpirarNaMao(CompraDeConvite compra)
    {
        typeof(CompraDeConvite).GetProperty(nameof(CompraDeConvite.Status))!.SetValue(compra, StatusDaCompra.Expirada);
        return Task.CompletedTask;
    }
}

using System.Text.Json;
using Backend.Business.Abstractions;
using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Services;
using Backend.Business.Recebimentos.Validators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Recebimentos;

/// <summary>
/// Os meios nascem não conferidos; conferir grava quem e quando; mexer no PIX volta a não conferido,
/// avisa a comissão e audita o antes e o depois — tudo antes do mesmo <c>SalvarAsync</c>. Mexer nos
/// outros meios avisa, mas não desfaz a conferência, que é do PIX e só dele.
/// </summary>
public sealed class ContaDeRecebimentoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid PresidenteId = Guid.CreateVersion7();

    private static readonly ChavePixDaConta ChaveCpf = new(TipoDeChavePix.Cpf, "529.982.247-25", "Ana Souza", "Curitiba");
    private static readonly ChavePixDaConta ChaveCelular = new(TipoDeChavePix.Telefone, "(41) 99876-5432", "Bruno Lima", "Curitiba");
    private static readonly DadosBancarios Conta = new("Banco do Brasil", "1234-5", "98765-4", "Corrente", "Comissão de Formatura");

    private static readonly MeiosDaConta SoPix = new(ChaveCpf, null, null);

    private readonly IContaDeRecebimentoRepository _contas = Substitute.For<IContaDeRecebimentoRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public ContaDeRecebimentoServiceTests()
    {
        _vinculos
            .ListarEmailsDaComissao(FormaturaId, Arg.Any<CancellationToken>())
            .Returns(["presidente@turma.com", "tesoureiro@turma.com", "comissao@turma.com"]);
    }

    private ContaDeRecebimentoService Servico =>
        new(
            _contas,
            _vinculos,
            _perfis,
            _formaturas,
            new EmailsDeRecebimento(_email, Options.Create(new AplicacaoSettings())),
            _eventos,
            new ContaDeRecebimentoValidator(),
            _unitOfWork,
            NullLogger<ContaDeRecebimentoService>.Instance
        );

    [Fact]
    public async Task Primeira_gravacao_nasce_nao_conferida_normalizada_e_sem_aviso()
    {
        var gravada = (await Servico.Gravar(FormaturaId, PresidenteId, SoPix, Ct)).Valor;

        gravada.Meios.Pix!.Chave.ShouldBe("52998224725");
        gravada.ConferidaEm.ShouldBeNull();
        await _contas.Received(1).Adicionar(Arg.Is<ContaDeRecebimento>(c => !c.Conferida), Ct);
        await _eventos.Received(1).Adicionar(Arg.Is<Evento>(e => e.Nome == ContaDeRecebimentoService.EventoDeCadastro), Ct);
        await _email.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    /// <summary>Decisão de 14/09/2026 (P2): o aviso vai para a comissão — os três e-mails que o repositório devolve.</summary>
    [Fact]
    public async Task Trocar_a_chave_volta_a_nao_conferida_avisa_a_comissao_e_audita_antes_e_depois_na_mesma_transacao()
    {
        var conta = ContaConferida();
        Evento? auditado = null;
        await _eventos.Adicionar(Arg.Do<Evento>(e => auditado = e), Arg.Any<CancellationToken>());

        var gravada = (await Servico.Gravar(FormaturaId, PresidenteId, SoPix with { Pix = ChaveCelular }, Ct)).Valor;

        gravada.ConferidaEm.ShouldBeNull();
        conta.Conferida.ShouldBeFalse();
        conta.ConferidaPorUsuarioId.ShouldBeNull();
        auditado.ShouldNotBeNull();
        auditado.Nome.ShouldBe(ContaDeRecebimentoService.EventoDeTroca);
        auditado.UsuarioId.ShouldBe(PresidenteId);
        var dados = JsonDocument.Parse(auditado.Dados!).RootElement;
        dados.GetProperty("formaturaId").GetGuid().ShouldBe(FormaturaId);
        dados.GetProperty("antes").GetProperty("pix").GetProperty("tipoDeChave").GetString().ShouldBe("Cpf");
        dados.GetProperty("antes").GetProperty("pix").GetProperty("chave").GetString().ShouldBe("52998224725");
        dados.GetProperty("depois").GetProperty("pix").GetProperty("tipoDeChave").GetString().ShouldBe("Telefone");
        dados.GetProperty("depois").GetProperty("pix").GetProperty("chave").GetString().ShouldBe("+5541998765432");
        dados.GetProperty("depois").GetProperty("pix").GetProperty("nomeDoTitular").GetString().ShouldBe("Bruno Lima");
        Received.InOrder(() =>
        {
            _email.Enfileirar(Arg.Is<NovoEmail>(e => e.Para == "presidente@turma.com"), Ct);
            _email.Enfileirar(Arg.Is<NovoEmail>(e => e.Para == "tesoureiro@turma.com"), Ct);
            _email.Enfileirar(Arg.Is<NovoEmail>(e => e.Para == "comissao@turma.com" && e.CorpoHtml.Contains("+5541998765432")), Ct);
            _eventos.Adicionar(Arg.Any<Evento>(), Ct);
            _unitOfWork.SalvarAsync(Ct);
        });
    }

    /// <summary>Mudar só o titular também vale como troca: o teste conferiu a combinação chave e titular.</summary>
    [Fact]
    public async Task Mudar_so_o_titular_tambem_desfaz_a_conferencia()
    {
        var conta = ContaConferida();

        await Servico.Gravar(FormaturaId, PresidenteId, SoPix with { Pix = ChaveCpf with { NomeDoTitular = "Ana Souza Lima" } }, Ct);

        conta.Conferida.ShouldBeFalse();
        await _email.ReceivedWithAnyArgs(3).Enfileirar(default!, Ct);
    }

    /// <summary>
    /// O e-mail existe para flagrar a fraude desta sprint: desligar o PIX da comissão e ligar
    /// "dinheiro com fulano". Mostrando só o depois, a lista nova parece plausível e a chave que
    /// sumiu passa despercebida — então o que saiu abre a mensagem.
    /// </summary>
    [Fact]
    public async Task Aviso_da_troca_diz_o_que_deixou_de_valer_antes_do_que_passou_a_valer()
    {
        ContaConferida();
        NovoEmail? aviso = null;
        await _email.Enfileirar(Arg.Do<NovoEmail>(e => aviso ??= e), Arg.Any<CancellationToken>());

        await Servico.Gravar(FormaturaId, PresidenteId, new MeiosDaConta(null, null, new DinheiroComAlguem("Lucas", null)), Ct);

        aviso.ShouldNotBeNull();
        aviso.CorpoHtml.ShouldContain("Deixou de valer");
        aviso.CorpoHtml.ShouldContain("52998224725");
        aviso.CorpoHtml.ShouldContain("Lucas");
        aviso
            .CorpoHtml.IndexOf("Deixou de valer", StringComparison.Ordinal)
            .ShouldBeLessThan(aviso.CorpoHtml.IndexOf("recebe assim", StringComparison.Ordinal));
    }

    /// <summary>Só acrescentar não tem o que anunciar como perdido: o bloco não nasce.</summary>
    [Fact]
    public async Task Aviso_de_meio_acrescentado_nao_traz_o_bloco_do_que_saiu()
    {
        ContaConferida();
        NovoEmail? aviso = null;
        await _email.Enfileirar(Arg.Do<NovoEmail>(e => aviso ??= e), Arg.Any<CancellationToken>());

        await Servico.Gravar(FormaturaId, PresidenteId, SoPix with { Transferencia = Conta }, Ct);

        aviso.ShouldNotBeNull();
        aviso.CorpoHtml.ShouldNotContain("Deixou de valer");
        aviso.CorpoHtml.ShouldContain("Banco do Brasil");
    }

    /// <summary>A conferência é do PIX: o TED que entra ao lado dele não tem o que desfazer.</summary>
    [Fact]
    public async Task Habilitar_outro_meio_avisa_a_comissao_mas_nao_desfaz_a_conferencia_do_pix()
    {
        var conta = ContaConferida();

        var gravada = (await Servico.Gravar(FormaturaId, PresidenteId, SoPix with { Transferencia = Conta }, Ct)).Valor;

        conta.Conferida.ShouldBeTrue();
        gravada.ConferidaEm.ShouldNotBeNull();
        gravada.Meios.Habilitados.ShouldBe([MeioDeRecebimento.Pix, MeioDeRecebimento.Transferencia]);
        await _email.ReceivedWithAnyArgs(3).Enfileirar(default!, Ct);
    }

    /// <summary>P4 de 21/09/2026: a chave virou opcional, e a turma que só recebe em espécie funciona.</summary>
    [Fact]
    public async Task Turma_sem_pix_grava_e_o_pix_de_teste_nao_existe()
    {
        var meios = new MeiosDaConta(null, null, new DinheiroComAlguem("Ana Souza", "nas reuniões de quinta"));

        var gravada = (await Servico.Gravar(FormaturaId, PresidenteId, meios, Ct)).Valor;

        gravada.Meios.Habilitados.ShouldBe([MeioDeRecebimento.Dinheiro]);
        gravada.Meios.Pix.ShouldBeNull();

        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(new ContaDeRecebimentoDetalhe(gravada.Meios, DateTime.UtcNow, null, null));

        (await Servico.GerarPixDeTeste(Ct)).PrimeiroErro.Codigo.ShouldBe("recebimento.sem_chave_pix");
    }

    [Fact]
    public async Task Sem_meio_nenhum_devolve_validacao_sem_consultar_nem_gravar()
    {
        var resultado = await Servico.Gravar(FormaturaId, PresidenteId, new MeiosDaConta(null, null, null), Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("recebimento.sem_meio");
        await _contas.DidNotReceiveWithAnyArgs().ObterParaEdicao(Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    /// <summary>Meio habilitado com campo em branco não passa: senão a entidade o apararia até sumir.</summary>
    [Fact]
    public async Task Transferencia_sem_banco_devolve_validacao_no_caminho_do_campo()
    {
        var resultado = await Servico.Gravar(FormaturaId, PresidenteId, SoPix with { Transferencia = Conta with { Banco = "  " } }, Ct);

        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);
        resultado.PrimeiroErro.Campo.ShouldBe("transferencia.banco");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Gravar_os_mesmos_dados_devolve_conflito_e_nao_desfaz_a_conferencia()
    {
        var conta = ContaConferida();

        var resultado = await Servico.Gravar(FormaturaId, PresidenteId, SoPix with { Pix = ChaveCpf with { Chave = "52998224725" } }, Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("recebimento.conta_sem_mudanca");
        conta.Conferida.ShouldBeTrue();
        await _email.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Chave_invalida_devolve_validacao_sem_consultar_nem_gravar()
    {
        var resultado = await Servico.Gravar(FormaturaId, PresidenteId, SoPix with { Pix = ChaveCpf with { Chave = "529.982.247-24" } }, Ct);

        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);
        await _contas.DidNotReceiveWithAnyArgs().ObterParaEdicao(Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Conferir_grava_quem_e_quando()
    {
        var conta = new ContaDeRecebimento();
        conta.Aplicar(SoPix);
        _contas.ObterParaEdicao(Arg.Any<CancellationToken>()).Returns(conta);
        _contas
            .ObterDetalhe(Arg.Any<CancellationToken>())
            .Returns(new ContaDeRecebimentoDetalhe(conta.ParaMeios(), DateTime.UtcNow, DateTime.UtcNow, "Ana"));
        var antes = DateTime.UtcNow;

        var conferida = (await Servico.Conferir(PresidenteId, Ct)).Valor;

        conferida.ConferidaPor.ShouldBe("Ana");
        conta.ConferidaPorUsuarioId.ShouldBe(PresidenteId);
        conta.ConferidaEm!.Value.ShouldBeInRange(antes, DateTime.UtcNow);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    [Fact]
    public async Task Conferir_de_novo_devolve_conflito()
    {
        ContaConferida();

        (await Servico.Conferir(PresidenteId, Ct)).PrimeiroErro.Codigo.ShouldBe("recebimento.conta_ja_conferida");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Sem_conta_nao_ha_pix_de_teste_nem_conferencia()
    {
        (await Servico.GerarPixDeTeste(Ct)).PrimeiroErro.Codigo.ShouldBe("recebimento.sem_conta");
        (await Servico.Conferir(PresidenteId, Ct)).PrimeiroErro.Codigo.ShouldBe("recebimento.sem_conta");
    }

    [Fact]
    public async Task Pix_de_teste_e_de_um_real_para_a_chave_gravada()
    {
        _contas
            .ObterDetalhe(Arg.Any<CancellationToken>())
            .Returns(
                new ContaDeRecebimentoDetalhe(
                    new MeiosDaConta(new ChavePixDaConta(TipoDeChavePix.Cpf, "52998224725", "Ana Souza", "Curitiba"), null, null),
                    DateTime.UtcNow,
                    null,
                    null
                )
            );

        var pix = (await Servico.GerarPixDeTeste(Ct)).Valor;

        pix.ValorEmCentavos.ShouldBe(100);
        pix.CopiaECola.ShouldBe(BrCode.Montar("52998224725", "Ana Souza", "Curitiba", 100, ContaDeRecebimentoService.IdentificadorDoTeste));
        pix.CopiaECola.ShouldContain("011152998224725");
        pix.CopiaECola.ShouldContain("54041.00");
    }

    /// <summary>A conta com a chave de CPF, conferida pelo Presidente, devolvida para edição.</summary>
    private ContaDeRecebimento ContaConferida()
    {
        var conta = new ContaDeRecebimento();
        conta.Aplicar(SoPix);
        conta.Conferir(PresidenteId, DateTime.UtcNow).Sucesso.ShouldBeTrue();
        _contas.ObterParaEdicao(Arg.Any<CancellationToken>()).Returns(conta);

        return conta;
    }
}

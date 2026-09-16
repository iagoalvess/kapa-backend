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
/// A chave nasce não conferida; conferir grava quem e quando; trocar volta a não conferida, avisa a
/// comissão e audita o antes e o depois — tudo antes do mesmo <c>SalvarAsync</c>.
/// </summary>
public sealed class ContaDeRecebimentoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid PresidenteId = Guid.CreateVersion7();

    private static readonly DadosDaConta ChaveCpf = new(TipoDeChavePix.Cpf, "529.982.247-25", "Ana Souza", "Curitiba");
    private static readonly DadosDaConta ChaveCelular = new(TipoDeChavePix.Telefone, "(41) 99876-5432", "Bruno Lima", "Curitiba");

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
    public async Task Primeira_chave_nasce_nao_conferida_normalizada_e_sem_aviso()
    {
        var gravada = (await Servico.Gravar(FormaturaId, PresidenteId, ChaveCpf, Ct)).Valor;

        gravada.Chave.ShouldBe("52998224725");
        gravada.ConferidaEm.ShouldBeNull();
        await _contas.Received(1).Adicionar(Arg.Is<ContaDeRecebimento>(c => !c.Conferida), Ct);
        await _eventos.Received(1).Adicionar(Arg.Is<Evento>(e => e.Nome == ContaDeRecebimentoService.EventoDeCadastro), Ct);
        await _email.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    /// <summary>Decisão de 14/09/2026 (P2): o aviso vai para a comissão — os três e-mails que o repositório devolve.</summary>
    [Fact]
    public async Task Troca_volta_a_nao_conferida_avisa_a_comissao_e_audita_antes_e_depois_na_mesma_transacao()
    {
        var conta = ContaConferida();
        Evento? auditado = null;
        await _eventos.Adicionar(Arg.Do<Evento>(e => auditado = e), Arg.Any<CancellationToken>());

        var gravada = (await Servico.Gravar(FormaturaId, PresidenteId, ChaveCelular, Ct)).Valor;

        gravada.ConferidaEm.ShouldBeNull();
        conta.Conferida.ShouldBeFalse();
        conta.ConferidaPorUsuarioId.ShouldBeNull();
        auditado.ShouldNotBeNull();
        auditado.Nome.ShouldBe(ContaDeRecebimentoService.EventoDeTroca);
        auditado.UsuarioId.ShouldBe(PresidenteId);
        var dados = JsonDocument.Parse(auditado.Dados!).RootElement;
        dados.GetProperty("formaturaId").GetGuid().ShouldBe(FormaturaId);
        dados.GetProperty("antes").GetProperty("tipoDeChave").GetString().ShouldBe("Cpf");
        dados.GetProperty("antes").GetProperty("chave").GetString().ShouldBe("52998224725");
        dados.GetProperty("depois").GetProperty("tipoDeChave").GetString().ShouldBe("Telefone");
        dados.GetProperty("depois").GetProperty("chave").GetString().ShouldBe("+5541998765432");
        dados.GetProperty("depois").GetProperty("nomeDoTitular").GetString().ShouldBe("Bruno Lima");
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

        await Servico.Gravar(FormaturaId, PresidenteId, ChaveCpf with { NomeDoTitular = "Ana Souza Lima" }, Ct);

        conta.Conferida.ShouldBeFalse();
        await _email.ReceivedWithAnyArgs(3).Enfileirar(default!, Ct);
    }

    [Fact]
    public async Task Gravar_os_mesmos_dados_devolve_conflito_e_nao_desfaz_a_conferencia()
    {
        var conta = ContaConferida();

        var resultado = await Servico.Gravar(FormaturaId, PresidenteId, ChaveCpf with { Chave = "52998224725" }, Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("recebimento.conta_sem_mudanca");
        conta.Conferida.ShouldBeTrue();
        await _email.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Chave_invalida_devolve_validacao_sem_consultar_nem_gravar()
    {
        var resultado = await Servico.Gravar(FormaturaId, PresidenteId, ChaveCpf with { Chave = "529.982.247-24" }, Ct);

        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);
        await _contas.DidNotReceiveWithAnyArgs().ObterParaEdicao(Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Conferir_grava_quem_e_quando()
    {
        var conta = new ContaDeRecebimento();
        conta.Aplicar(ChaveCpf);
        _contas.ObterParaEdicao(Arg.Any<CancellationToken>()).Returns(conta);
        _contas
            .ObterDetalhe(Arg.Any<CancellationToken>())
            .Returns(
                new ContaDeRecebimentoDetalhe(TipoDeChavePix.Cpf, "52998224725", "Ana Souza", "Curitiba", DateTime.UtcNow, DateTime.UtcNow, "Ana")
            );
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
            .Returns(new ContaDeRecebimentoDetalhe(TipoDeChavePix.Cpf, "52998224725", "Ana Souza", "Curitiba", DateTime.UtcNow, null, null));

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
        conta.Aplicar(ChaveCpf);
        conta.Conferir(PresidenteId, DateTime.UtcNow).Sucesso.ShouldBeTrue();
        _contas.ObterParaEdicao(Arg.Any<CancellationToken>()).Returns(conta);

        return conta;
    }
}

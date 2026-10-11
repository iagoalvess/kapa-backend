using Backend.Business.Abstractions;
using Backend.Business.Enquetes.Interfaces;
using Backend.Business.Enquetes.Models;
using Backend.Business.Enquetes.Services;
using Backend.Business.Enquetes.Validators;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Enquetes;

/// <summary>Regras que permanecem verdadeiras mesmo sem contadores e sem job de encerramento.</summary>
public sealed class EnqueteServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly IEnqueteRepository _repo = Substitute.For<IEnqueteRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly MembroParaEnquete _membro = new(Guid.NewGuid(), true, true, false);
    private readonly Enquete _enquete = Enquete.Nova(Dados(), Guid.NewGuid(), 10);
    private EnqueteService Servico => new(_repo, new DadosDaEnqueteValidator(), _uow);

    private static DadosDaEnquete Dados() =>
        new("Qual tema?", null, TipoDeEnquete.UnicaEscolha, null, 50, DateTime.UtcNow.AddDays(1), ["Clássico", "Tropical"]);

    public EnqueteServiceTests()
    {
        _repo.Travar(_enquete.Id, Arg.Any<CancellationToken>()).Returns(_enquete);
        _repo.ObterMembro(_usuario, Arg.Any<CancellationToken>()).Returns(_membro);
        _repo.Obter(_enquete.Id, _membro.VinculoId, Arg.Any<CancellationToken>()).Returns(new EnqueteComContagem(_enquete, 0, null));
        _repo.Resultados(_enquete.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _uow.EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<Result>>>()(Ct));
    }

    [Theory]
    [InlineData(false, true, "enquete.email_nao_confirmado")]
    [InlineData(true, false, "adesao.obrigatoria")]
    public async Task Gestao_tambem_precisa_confirmar_email_e_aderir(bool email, bool adesao, string codigo)
    {
        _repo.ObterMembro(_usuario, Arg.Any<CancellationToken>()).Returns(_membro with { Gestao = true, EmailConfirmado = email, Aderiu = adesao });
        var resultado = await Servico.Responder(_enquete.Id, _usuario, [_enquete.Opcoes[0].Id], Ct);
        resultado.PrimeiroErro.Codigo.ShouldBe(codigo);
        await _repo.DidNotReceive().AdicionarResposta(Arg.Any<RespostaDaEnquete>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resposta_exige_opcoes_proprias_distintas_e_dentro_do_limite()
    {
        (await Servico.Responder(_enquete.Id, _usuario, [], Ct)).PrimeiroErro.Codigo.ShouldBe("enquete.resposta_invalida");
        (await Servico.Responder(_enquete.Id, _usuario, [Guid.NewGuid()], Ct)).PrimeiroErro.Codigo.ShouldBe("enquete.opcao_de_outra_enquete");
        (await Servico.Responder(_enquete.Id, _usuario, [_enquete.Opcoes[0].Id, _enquete.Opcoes[0].Id], Ct)).PrimeiroErro.Codigo.ShouldBe(
            "enquete.resposta_invalida"
        );
        (await Servico.Responder(_enquete.Id, _usuario, _enquete.Opcoes.Select(o => o.Id).ToArray(), Ct)).PrimeiroErro.Codigo.ShouldBe(
            "enquete.escolhas_demais"
        );
    }

    [Fact]
    public async Task Substituir_resposta_remove_escolhas_anteriores_sem_criar_outro_participante()
    {
        var anterior = new RespostaDaEnquete { EnqueteId = _enquete.Id, VinculoId = _membro.VinculoId };
        anterior.Escolhas.Add(new EscolhaDaResposta { RespostaId = anterior.Id, OpcaoId = _enquete.Opcoes[0].Id });
        _repo.ObterResposta(_enquete.Id, _membro.VinculoId, Arg.Any<CancellationToken>()).Returns(anterior);
        (await Servico.Responder(_enquete.Id, _usuario, [_enquete.Opcoes[1].Id], Ct)).Sucesso.ShouldBeTrue();
        anterior.Escolhas.Single().OpcaoId.ShouldBe(_enquete.Opcoes[1].Id);
        _repo.Received(1).RemoverEscolhas(Arg.Is<IReadOnlyList<EscolhaDaResposta>>(x => x[0].OpcaoId == _enquete.Opcoes[0].Id));
        await _repo.DidNotReceive().AdicionarResposta(Arg.Any<RespostaDaEnquete>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retirar_todas_as_respostas_nao_destrava_as_regras()
    {
        (await Servico.Responder(_enquete.Id, _usuario, [_enquete.Opcoes[0].Id], Ct)).Sucesso.ShouldBeTrue();
        (await Servico.Retirar(_enquete.Id, _usuario, Ct)).Sucesso.ShouldBeTrue();
        var dados = Dados() with { Pergunta = "Outra pergunta?" };
        (await Servico.Atualizar(_enquete.Id, dados, _usuario, Ct)).PrimeiroErro.Codigo.ShouldBe("enquete.opcoes_travadas");
    }

    [Fact]
    public async Task Depois_do_voto_somente_descricao_e_adiamento_podem_mudar()
    {
        _enquete.RegistrarResposta(DateTime.UtcNow);
        var dados = Dados() with { EncerraEm = _enquete.EncerraEm.AddHours(-1) };
        (await Servico.Atualizar(_enquete.Id, dados, _usuario, Ct)).PrimeiroErro.Codigo.ShouldBe("enquete.opcoes_travadas");
        dados = dados with { Descricao = "Mais contexto", EncerraEm = _enquete.EncerraEm.AddHours(1) };
        (await Servico.Atualizar(_enquete.Id, dados, _usuario, Ct)).Sucesso.ShouldBeTrue();
        _enquete.Descricao.ShouldBe("Mais contexto");
    }

    [Fact]
    public async Task Prazo_vencido_recusa_voto_retirada_e_edicao_sem_job()
    {
        _enquete.Aplicar(Dados() with { EncerraEm = DateTime.UtcNow.AddSeconds(-1) });
        (await Servico.Responder(_enquete.Id, _usuario, [_enquete.Opcoes[0].Id], Ct)).PrimeiroErro.Codigo.ShouldBe("enquete.encerrada");
        (await Servico.Retirar(_enquete.Id, _usuario, Ct)).PrimeiroErro.Codigo.ShouldBe("enquete.encerrada");
        (await Servico.Atualizar(_enquete.Id, Dados(), _usuario, Ct)).PrimeiroErro.Codigo.ShouldBe("enquete.encerrada");
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public async Task Quorum_conta_pessoas_e_permanece_visivel_ao_encerrar(int respostas, bool atingido)
    {
        _enquete.Encerrar(DateTime.UtcNow);
        _repo.Obter(_enquete.Id, _membro.VinculoId, Arg.Any<CancellationToken>()).Returns(new EnqueteComContagem(_enquete, respostas, null));
        var resultado = await Servico.Obter(_enquete.Id, _usuario, Ct);
        resultado.Valor.Enquete.TotalRespostas.ShouldBe(respostas);
        resultado.Valor.Enquete.QuorumAtingido.ShouldBe(atingido);
        resultado.Valor.Participantes.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task Publicacao_fora_da_janela_nao_grava(int dias)
    {
        var resultado = await Servico.Criar(Dados() with { EncerraEm = DateTime.UtcNow.AddDays(dias) }, _usuario, Ct);
        resultado.PrimeiroErro.Codigo.ShouldBe("enquete.prazo_invalido");
        await _uow.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validador_recusa_opcoes_repetidas_quorum_e_limite_invalidos()
    {
        var validator = new DadosDaEnqueteValidator();
        validator.Validate(Dados() with { Opcoes = ["A", " a "] }).IsValid.ShouldBeFalse();
        validator.Validate(Dados() with { QuorumMinimoPercentual = 0 }).IsValid.ShouldBeFalse();
        validator.Validate(Dados() with { Tipo = TipoDeEnquete.MultiplaEscolha, MaximoDeEscolhas = null }).IsValid.ShouldBeFalse();
        validator.Validate(Dados() with { Tipo = TipoDeEnquete.MultiplaEscolha, MaximoDeEscolhas = 3 }).IsValid.ShouldBeFalse();
        validator.Validate(Dados() with { Tipo = TipoDeEnquete.MultiplaEscolha, MaximoDeEscolhas = 2 }).IsValid.ShouldBeTrue();
    }
}

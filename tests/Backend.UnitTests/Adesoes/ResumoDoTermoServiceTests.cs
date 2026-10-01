using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Adesoes.Services;
using Backend.Business.Adesoes.Settings;
using Backend.Business.IA.Interfaces;
using Backend.Business.IA.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Adesoes;

/// <summary>A regra do resumo do termo (Sprint 24): desligamento, janela, teto por rodada, descarte e gravação.</summary>
public sealed class ResumoDoTermoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly VersaoDoTermo Termo = new(Guid.CreateVersion7(), 2, "# Termo\n\nMulta de 2%.", DateTime.UtcNow);

    private readonly IAdesaoRepository _adesoes = Substitute.For<IAdesaoRepository>();
    private readonly IModeloDeLinguagem _modelo = Substitute.For<IModeloDeLinguagem>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public ResumoDoTermoServiceTests()
    {
        _modelo.Ligado.Returns(true);
        _adesoes.ObterTermo(Termo.Id, Arg.Any<CancellationToken>()).Returns(Termo);
    }

    private ResumoDoTermoService Servico =>
        new(_adesoes, _modelo, Options.Create(new ResumoSettings { Modelos = ["m1", "m2"], PorRodada = 3 }), _unitOfWork);

    [Fact]
    public async Task Ia_desligada_nao_consulta_o_banco()
    {
        _modelo.Ligado.Returns(false);

        (await Servico.ListarPendentes(DateTime.UtcNow, Ct)).ShouldBeEmpty();

        await _adesoes.DidNotReceiveWithAnyArgs().ListarTermosSemResumoDeTodasAsFormaturas(default, default, Ct);
    }

    [Fact]
    public async Task A_rodada_olha_sete_dias_para_tras_e_respeita_o_teto()
    {
        var agora = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

        await Servico.ListarPendentes(agora, Ct);

        await _adesoes.Received(1).ListarTermosSemResumoDeTodasAsFormaturas(agora.AddDays(-7), 3, Ct);
    }

    [Fact]
    public async Task Grava_o_resumo_com_o_modelo_que_respondeu_e_manda_so_o_texto_do_termo()
    {
        _modelo.Completar(Arg.Any<PedidoAoModelo>(), Arg.Any<CancellationToken>()).Returns(new RespostaDoModelo("Você paga 12 parcelas.", "m2"));

        var resultado = await Servico.Gerar(Termo.Id, Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _modelo
            .Received(1)
            .Completar(
                Arg.Is<PedidoAoModelo>(p =>
                    p.Texto == Termo.Conteudo && p.Instrucao == ResumoDoTermoService.Instrucao && p.Modelos.SequenceEqual(new[] { "m1", "m2" })
                ),
                Ct
            );
        await _adesoes.Received(1).AdicionarResumo(Arg.Is<ResumoDoTermo>(r => r.TermoId == Termo.Id && r.Texto == "Você paga 12 parcelas."), Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    [Fact]
    public async Task Resposta_acima_do_teto_e_descartada()
    {
        _modelo
            .Completar(Arg.Any<PedidoAoModelo>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaDoModelo(new string('a', ResumoDoTermoService.TetoDeCaracteres + 1), "m1"));

        (await Servico.Gerar(Termo.Id, Ct)).PrimeiroErro.Codigo.ShouldBe("adesao.resumo_descartado");

        await _adesoes.DidNotReceiveWithAnyArgs().AdicionarResumo(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Nenhum_modelo_respondeu_nao_grava_e_o_termo_segue_na_fila()
    {
        _modelo
            .Completar(Arg.Any<PedidoAoModelo>(), Arg.Any<CancellationToken>())
            .Returns(Erro.Indisponivel("ia.indisponivel", "Nenhum modelo devolveu resposta."));

        (await Servico.Gerar(Termo.Id, Ct)).PrimeiroErro.Codigo.ShouldBe("ia.indisponivel");

        await _adesoes.DidNotReceiveWithAnyArgs().AdicionarResumo(default!, Ct);
    }
}

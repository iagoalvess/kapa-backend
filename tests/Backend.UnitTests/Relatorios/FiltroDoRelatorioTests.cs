using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Relatorios.Models;
using Shouldly;

namespace Backend.UnitTests.Relatorios;

/// <summary>
/// O recorte de um relatório: o que ele guarda, o que o balancete descarta e o que a fila usa para
/// não confundir dois pedidos.
/// </summary>
public sealed class FiltroDoRelatorioTests
{
    private static readonly PeriodoDoRelatorio Periodo = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 15));

    /// <summary>Só o período não é recorte: é o padrão de todo relatório.</summary>
    [Fact]
    public void Periodo_sozinho_nao_conta_como_recorte() => new FiltroDoRelatorio(Periodo).TemRecorte.ShouldBeFalse();

    /// <summary>Qualquer um dos seis campos liga o recorte — é o que faz o subtítulo dizê-lo.</summary>
    [Theory]
    [MemberData(nameof(UmCampoDeCadaVez))]
    public void Qualquer_campo_liga_o_recorte(FiltroDoRelatorio filtro) => filtro.TemRecorte.ShouldBeTrue();

    /// <summary>Um filtro por campo, para cobrir os seis.</summary>
    public static TheoryData<FiltroDoRelatorio> UmCampoDeCadaVez() =>
        [
            new FiltroDoRelatorio(Periodo, FornecedorId: Guid.NewGuid()),
            new FiltroDoRelatorio(Periodo, Categoria: CategoriaDeDespesa.Buffet),
            new FiltroDoRelatorio(Periodo, SituacaoDaDespesa: StatusDaDespesa.Paga),
            new FiltroDoRelatorio(Periodo, FormandoId: Guid.NewGuid()),
            new FiltroDoRelatorio(Periodo, ItemDeCobrancaId: Guid.NewGuid()),
            new FiltroDoRelatorio(Periodo, SituacaoDaParcela: StatusDaParcela.Vencida),
        ];

    /// <summary>
    /// <c>SomentePeriodo</c> descarta tudo menos o intervalo — é o que o balancete recebe.
    /// </summary>
    /// <remarks>
    /// Recortado por um fornecedor, o balancete deixa de fechar: as entradas continuam as da turma e
    /// as saídas viram as de um contrato só. O documento sairia com a cara do que fecha.
    /// </remarks>
    [Fact]
    public void Somente_periodo_descarta_todos_os_recortes()
    {
        // Arrange
        var cheio = new FiltroDoRelatorio(
            Periodo,
            Guid.NewGuid(),
            CategoriaDeDespesa.Buffet,
            StatusDaDespesa.Paga,
            Guid.NewGuid(),
            Guid.NewGuid(),
            StatusDaParcela.Vencida
        );

        // Act
        var limpo = cheio.SomentePeriodo();

        // Assert
        limpo.Periodo.ShouldBe(Periodo);
        limpo.TemRecorte.ShouldBeFalse();
    }

    /// <summary>
    /// Dois recortes diferentes são dois pedidos diferentes.
    /// </summary>
    /// <remarks>
    /// A fila deduplica por igualdade do filtro. Se dois recortes distintos se igualassem, quem
    /// pedisse as despesas de um fornecedor logo depois de alguém ter pedido as de outro receberia o
    /// PDF do outro — e o registro em `SolicitacaoDeRelatorio.Filtro` tem de voltar igual ao que
    /// entrou, porque é dele que o worker refaz o arquivo.
    /// </remarks>
    [Fact]
    public void Recortes_diferentes_nao_sao_o_mesmo_pedido()
    {
        var buffet = new FiltroDoRelatorio(Periodo, FornecedorId: Guid.NewGuid());
        var outro = buffet with { FornecedorId = Guid.NewGuid() };

        buffet.ShouldNotBe(outro);
        buffet.ShouldBe(buffet with { });
    }

    /// <summary>O que a entidade grava volta igual: é com isso que o worker monta o relatório depois.</summary>
    [Fact]
    public void A_solicitacao_devolve_o_recorte_que_recebeu()
    {
        // Arrange
        var filtro = new FiltroDoRelatorio(
            Periodo,
            Guid.NewGuid(),
            CategoriaDeDespesa.Espaco,
            StatusDaDespesa.Prevista,
            Guid.NewGuid(),
            Guid.NewGuid(),
            StatusDaParcela.Aberta
        );

        // Act
        var solicitacao = SolicitacaoDeRelatorio.Nova(TipoDeRelatorio.Despesas, filtro, Guid.NewGuid());

        // Assert
        solicitacao.Filtro.ShouldBe(filtro);
    }
}

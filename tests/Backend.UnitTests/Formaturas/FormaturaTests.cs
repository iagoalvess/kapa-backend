using Backend.Business.Formaturas.Models;
using Shouldly;

namespace Backend.UnitTests.Formaturas;

/// <summary>
/// O ciclo de vida inteiro, par a par: toda combinação (origem, destino) passa por
/// <see cref="Formatura.Transicionar"/>, válida ou não.
/// </summary>
/// <remarks>
/// <see cref="Validas"/> é o diagrama da sprint transcrito. Mudou o diagrama, muda aqui — e o teste
/// diz se o código concorda.
/// </remarks>
public sealed class FormaturaTests
{
    private static readonly (StatusDaFormatura De, StatusDaFormatura Para)[] Validas =
    [
        (StatusDaFormatura.Rascunho, StatusDaFormatura.AguardandoPagamento),
        (StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa),
        (StatusDaFormatura.Ativa, StatusDaFormatura.Suspensa),
        (StatusDaFormatura.Ativa, StatusDaFormatura.Encerrada),
        (StatusDaFormatura.Suspensa, StatusDaFormatura.Ativa),
        (StatusDaFormatura.Suspensa, StatusDaFormatura.Encerrada),
        (StatusDaFormatura.Rascunho, StatusDaFormatura.Descartada),
        (StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Descartada),
    ];

    /// <summary>Os 25 pares possíveis.</summary>
    public static TheoryData<StatusDaFormatura, StatusDaFormatura> TodosOsPares
    {
        get
        {
            var pares = new TheoryData<StatusDaFormatura, StatusDaFormatura>();

            foreach (var de in Enum.GetValues<StatusDaFormatura>())
            foreach (var para in Enum.GetValues<StatusDaFormatura>())
                pares.Add(de, para);

            return pares;
        }
    }

    [Theory]
    [MemberData(nameof(TodosOsPares))]
    public void Cada_par_segue_o_diagrama(StatusDaFormatura de, StatusDaFormatura para)
    {
        // Arrange
        var formatura = Em(de);
        var valida = Validas.Contains((de, para));

        // Act
        var resultado = formatura.Transicionar(para);

        // Assert
        resultado.Sucesso.ShouldBe(valida, $"{de} → {para}");
        formatura.Status.ShouldBe(valida ? para : de);

        if (!valida)
            resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.transicao_invalida");
    }

    [Fact]
    public void Nasce_em_rascunho()
    {
        new Formatura().Status.ShouldBe(StatusDaFormatura.Rascunho);
    }

    /// <summary>Regularizar uma suspensão não reescreve quando a turma começou.</summary>
    [Fact]
    public void Reativar_preserva_a_primeira_ativacao()
    {
        var formatura = Em(StatusDaFormatura.Ativa);
        var primeira = formatura.AtivadaEm;

        formatura.Transicionar(StatusDaFormatura.Suspensa);
        formatura.Transicionar(StatusDaFormatura.Ativa);

        primeira.ShouldNotBeNull();
        formatura.AtivadaEm.ShouldBe(primeira);
    }

    [Fact]
    public void Encerrar_carimba_a_data()
    {
        var formatura = Em(StatusDaFormatura.Ativa);

        formatura.Transicionar(StatusDaFormatura.Encerrada);

        formatura.EncerradaEm.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(StatusDaFormatura.Rascunho, true)]
    [InlineData(StatusDaFormatura.AguardandoPagamento, true)]
    [InlineData(StatusDaFormatura.Ativa, true)]
    [InlineData(StatusDaFormatura.Suspensa, false)]
    [InlineData(StatusDaFormatura.Encerrada, false)]
    [InlineData(StatusDaFormatura.Descartada, false)]
    public void So_rascunho_aguardando_e_ativa_aceitam_edicao(StatusDaFormatura status, bool aceita)
    {
        Em(status).AceitaEdicao.ShouldBe(aceita);
    }

    /// <summary>Leva uma formatura nova até o status pedido pelo caminho do diagrama.</summary>
    /// <param name="status">Status final.</param>
    private static Formatura Em(StatusDaFormatura status)
    {
        StatusDaFormatura[] caminho = status switch
        {
            StatusDaFormatura.Rascunho => [],
            StatusDaFormatura.AguardandoPagamento => [StatusDaFormatura.AguardandoPagamento],
            StatusDaFormatura.Ativa => [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa],
            StatusDaFormatura.Suspensa => [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa, StatusDaFormatura.Suspensa],
            StatusDaFormatura.Descartada => [StatusDaFormatura.Descartada],
            _ => [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa, StatusDaFormatura.Encerrada],
        };

        var formatura = new Formatura();

        foreach (var passo in caminho)
            formatura.Transicionar(passo).Sucesso.ShouldBeTrue();

        return formatura;
    }
}

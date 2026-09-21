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
        (StatusDaFormatura.Ativa, StatusDaFormatura.Suspensa),
        (StatusDaFormatura.Ativa, StatusDaFormatura.Encerrada),
        (StatusDaFormatura.Ativa, StatusDaFormatura.Descartada),
        (StatusDaFormatura.Suspensa, StatusDaFormatura.Ativa),
        (StatusDaFormatura.Suspensa, StatusDaFormatura.Encerrada),
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

    /// <summary>Nasce ativa, no gratuito: não há mais estado de espera antes de contratar.</summary>
    [Fact]
    public void Nasce_ativa()
    {
        new Formatura().Status.ShouldBe(StatusDaFormatura.Ativa);
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
    [InlineData(StatusDaFormatura.Ativa, true)]
    [InlineData(StatusDaFormatura.Suspensa, false)]
    [InlineData(StatusDaFormatura.Encerrada, false)]
    [InlineData(StatusDaFormatura.Descartada, false)]
    public void So_a_turma_ativa_aceita_edicao(StatusDaFormatura status, bool aceita)
    {
        Em(status).AceitaEdicao.ShouldBe(aceita);
    }

    /// <summary>Leva uma formatura nova até o status pedido pelo caminho do diagrama.</summary>
    /// <param name="status">Status final.</param>
    private static Formatura Em(StatusDaFormatura status)
    {
        // A turma nasce Ativa, então o caminho é no máximo um passo.
        StatusDaFormatura[] caminho = status == StatusDaFormatura.Ativa ? [] : [status];

        var formatura = new Formatura();
        formatura.NascerNoGratuito();

        foreach (var passo in caminho)
            formatura.Transicionar(passo).Sucesso.ShouldBeTrue();

        return formatura;
    }
}

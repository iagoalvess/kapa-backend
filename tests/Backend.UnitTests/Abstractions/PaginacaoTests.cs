using Backend.Business.Abstractions;
using Shouldly;

namespace Backend.UnitTests.Abstractions;

/// <summary>
/// A paginação vem crua da query string e não pode virar consulta inválida.
/// </summary>
public sealed class PaginacaoTests
{
    /// <summary><c>?pagina=21474838&amp;tamanho=100</c> estourava o deslocamento e o banco devolvia 503.</summary>
    [Theory]
    [InlineData(21_474_838)]
    [InlineData(int.MaxValue)]
    public void Pagina_gigante_nao_estoura_o_deslocamento(int pagina)
    {
        var paginacao = new PaginacaoRequest { Pagina = pagina, Tamanho = 100 }.Normalizar();

        paginacao.Pular.ShouldBeGreaterThanOrEqualTo(0);
        paginacao.Pagina.ShouldBe(PaginacaoRequest.PaginaMaxima);
    }
}

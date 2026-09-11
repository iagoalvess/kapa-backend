using Backend.Business.Abstractions;
using Shouldly;

namespace Backend.UnitTests.Abstractions;

/// <summary>
/// Garante o contrato do <see cref="Result"/>, do qual todo service depende.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void Ok_carrega_o_valor_e_nao_tem_erros()
    {
        var resultado = Result.Ok(42);

        resultado.Sucesso.ShouldBeTrue();
        resultado.Falhou.ShouldBeFalse();
        resultado.Valor.ShouldBe(42);
        resultado.Erros.ShouldBeEmpty();
    }

    [Fact]
    public void Acessar_o_valor_de_uma_falha_lanca()
    {
        Result<int> resultado = Erro.NaoEncontrado("x.nao_encontrado", "Não achei.");

        resultado.Falhou.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => resultado.Valor);
    }

    [Fact]
    public void Um_valor_vira_sucesso_por_conversao_implicita()
    {
        Result<string> resultado = "pronto";

        resultado.Sucesso.ShouldBeTrue();
        resultado.Valor.ShouldBe("pronto");
    }

    [Fact]
    public void Um_erro_vira_falha_por_conversao_implicita()
    {
        Result<string> resultado = Erro.Conflito("x.conflito", "Já existe.");

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("x.conflito");
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Conflito);
    }

    [Fact]
    public void Falha_preserva_todos_os_erros_de_validacao()
    {
        Erro[] erros = [Erro.Validacao("v.nome", "Nome obrigatório.", "nome"), Erro.Validacao("v.email", "E-mail inválido.", "email")];

        var resultado = Result.Falha<int>(erros);

        resultado.Erros.Count.ShouldBe(2);
        resultado.Erros.Select(e => e.Campo).ShouldBe(["nome", "email"]);
    }

    [Fact]
    public void Map_transforma_o_sucesso()
    {
        var resultado = Result.Ok(21).Map(valor => valor * 2);

        resultado.Valor.ShouldBe(42);
    }

    [Fact]
    public void Map_nao_executa_a_projecao_quando_ja_falhou()
    {
        var executou = false;

        var resultado = Result
            .Falha<int>(Erro.Validacao("v", "ruim"))
            .Map(valor =>
            {
                executou = true;
                return valor;
            });

        executou.ShouldBeFalse();
        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("v");
    }

    [Fact]
    public void Bind_encadeia_e_propaga_a_primeira_falha()
    {
        var resultado = Result.Ok(10).Bind(valor => valor > 5 ? Result.Falha<string>(Erro.Conflito("c", "grande demais")) : Result.Ok("ok"));

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("c");
    }

    [Fact]
    public void Um_sucesso_nao_pode_carregar_erro()
    {
        Should.Throw<ArgumentException>(() => Result.Falha([]));
    }
}

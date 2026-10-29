using Backend.Business.Auth.Services;
using Shouldly;

namespace Backend.UnitTests.Auth;

public sealed class TentativasDeSenhaTests
{
    /// <summary>O bloqueio é auditado uma vez: só a falha que o fecha avisa, e as seguintes não repetem o evento.</summary>
    [Fact]
    public void So_a_falha_que_bloqueia_avisa()
    {
        var tentativas = new TentativasDeSenha();

        var avisos = Enumerable.Range(0, TentativasDeSenha.Maximo + 2).Select(_ => tentativas.RegistrarFalha("ana@turma.dev", "10.0.0.1")).ToList();

        avisos.Count(bloqueou => bloqueou).ShouldBe(1);
        avisos[TentativasDeSenha.Maximo - 1].ShouldBeTrue();
        tentativas.Bloqueada("ana@turma.dev", "10.0.0.1").ShouldBeTrue();
        tentativas.Bloqueada("ana@turma.dev", "10.0.0.2").ShouldBeFalse();
    }

    /// <summary>O desbloqueio do suporte limpa todas as origens da conta, e só dela.</summary>
    [Fact]
    public void Desbloqueio_limpa_todas_as_origens_da_conta()
    {
        var tentativas = new TentativasDeSenha();

        for (var i = 0; i < TentativasDeSenha.Maximo; i++)
        {
            tentativas.RegistrarFalha("ana@turma.dev", "10.0.0.1");
            tentativas.RegistrarFalha("ana@turma.dev", "10.0.0.2");
            tentativas.RegistrarFalha("bia@turma.dev", "10.0.0.1");
        }

        tentativas.LimparConta("Ana@Turma.dev");

        tentativas.Bloqueada("ana@turma.dev", "10.0.0.1").ShouldBeFalse();
        tentativas.Bloqueada("ana@turma.dev", "10.0.0.2").ShouldBeFalse();
        tentativas.Bloqueada("bia@turma.dev", "10.0.0.1").ShouldBeTrue();
    }
}

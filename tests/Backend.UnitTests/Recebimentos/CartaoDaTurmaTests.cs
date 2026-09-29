using Backend.Business.Recebimentos.Models;
using Shouldly;

namespace Backend.UnitTests.Recebimentos;

/// <summary>O cartão da turma (Sprint 39): o acréscimo da taxa repassada e o que a troca de conta faz com ele.</summary>
public sealed class CartaoDaTurmaTests
{
    private static CredencialDeProvedor Conectada(long conta = 1, string? chavePublica = "APP_USR-publica")
    {
        var credencial = new CredencialDeProvedor();
        credencial.Conectar("token", "renovacao", DateTime.UtcNow.AddDays(100), conta, "turma@mp.dev", Guid.CreateVersion7(), chavePublica);

        return credencial;
    }

    /// <summary>P2: com a taxa repassada, a turma recebe o valor do PIX inteiro depois da tarifa.</summary>
    [Theory]
    [InlineData(null, 30_000, 0)]
    [InlineData(498, 30_000, 1_573)]
    [InlineData(398, 10_000, 415)]
    [InlineData(1500, 1, 1)]
    public void Acrescimo_faz_a_turma_receber_o_valor_cheio(int? taxa, long valor, long acrescimo)
    {
        var credencial = Conectada();
        credencial.LigarCartao(taxa, Guid.CreateVersion7(), DateTime.UtcNow);

        credencial.AcrescimoDoCartao(valor).ShouldBe(acrescimo);
        (valor + acrescimo - Math.Round((valor + acrescimo) * (taxa ?? 0) / 10_000m)).ShouldBeGreaterThanOrEqualTo(valor);
    }

    /// <summary>Sem a chave pública (conexão anterior à sprint), o cartão não aparece mesmo ligado.</summary>
    [Fact]
    public void Sem_chave_publica_o_cartao_nao_aparece()
    {
        var credencial = Conectada(chavePublica: null);
        credencial.LigarCartao(null, Guid.CreateVersion7(), DateTime.UtcNow);

        credencial.CartaoLigado.ShouldBeFalse();
        credencial.CartaoPara(10_000).ShouldBeNull();
    }

    /// <summary>Outra conta desliga o cartão: a taxa e o risco aceitos eram os da anterior. A mesma conta mantém.</summary>
    [Fact]
    public void Trocar_de_conta_desliga_o_cartao_e_reconectar_a_mesma_mantem()
    {
        var credencial = Conectada();
        credencial.LigarCartao(498, Guid.CreateVersion7(), DateTime.UtcNow);

        credencial.Conectar("token2", "renovacao2", DateTime.UtcNow.AddDays(100), 1, "turma@mp.dev", Guid.CreateVersion7());
        credencial.CartaoLigado.ShouldBeTrue();

        credencial.Conectar("token3", "renovacao3", DateTime.UtcNow.AddDays(100), 2, "outra@mp.dev", Guid.CreateVersion7(), "APP_USR-outra");
        credencial.CartaoLigado.ShouldBeFalse();
        credencial.TaxaDoCartaoRepassada.ShouldBeNull();
    }
}

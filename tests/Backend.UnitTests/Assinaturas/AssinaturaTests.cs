using Backend.Business.Assinaturas.Models;
using Shouldly;

namespace Backend.UnitTests.Assinaturas;

/// <summary>
/// As regras de tempo e de status da assinatura, sem banco, sem provedor e sem worker.
/// </summary>
public sealed class AssinaturaTests
{
    private static readonly DateTime Agora = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Carencia = TimeSpan.FromDays(7);

    private static Assinatura Ativa(DateTime pagaEm)
    {
        var assinatura = new Assinatura();
        assinatura.ConfirmarPagamento(pagaEm, CicloDeCobranca.Mensal).Sucesso.ShouldBeTrue();

        return assinatura;
    }

    [Fact]
    public void Confirmar_pagamento_ativa_por_um_ciclo_a_partir_de_agora()
    {
        // Arrange
        var assinatura = new Assinatura();

        // Act
        var resultado = assinatura.ConfirmarPagamento(Agora, CicloDeCobranca.Mensal);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        assinatura.VigenteAte.ShouldBe(Agora.AddMonths(1));
    }

    /// <summary>A conciliação achou o pagamento e depois o webhook atrasado chegou: não soma outro ciclo.</summary>
    [Fact]
    public void Segunda_confirmacao_nao_soma_outro_ciclo()
    {
        var assinatura = Ativa(Agora);

        var resultado = assinatura.ConfirmarPagamento(Agora.AddMinutes(5), CicloDeCobranca.Mensal);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.transicao_invalida");
        assinatura.VigenteAte.ShouldBe(Agora.AddMonths(1));
    }

    [Fact]
    public void Renovar_antes_do_vencimento_soma_a_partir_do_fim_da_vigencia()
    {
        var assinatura = Ativa(Agora);

        assinatura.Renovar(Agora.AddDays(25), CicloDeCobranca.Mensal).Sucesso.ShouldBeTrue();

        assinatura.VigenteAte.ShouldBe(Agora.AddMonths(2));
    }

    [Fact]
    public void Cancelar_mantem_a_vigencia()
    {
        var assinatura = Ativa(Agora);

        assinatura.Cancelar(Agora.AddDays(3)).Sucesso.ShouldBeTrue();

        assinatura.Status.ShouldBe(StatusDaAssinatura.Cancelada);
        assinatura.VigenteAte.ShouldBe(Agora.AddMonths(1));
        assinatura.CanceladaEm.ShouldBe(Agora.AddDays(3));
    }

    [Fact]
    public void Cancelar_pendente_devolve_nao_ativa()
    {
        new Assinatura().Cancelar(Agora).Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.nao_ativa");
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public void Ativa_vence_so_depois_da_carencia(int diasDepoisDoVencimento, bool vence)
    {
        var assinatura = Ativa(Agora);
        var fim = assinatura.VigenteAte!.Value;

        assinatura.DeveVencer(fim.AddDays(diasDepoisDoVencimento), Carencia).ShouldBe(vence);
    }

    /// <summary>Quem cancelou não tem cobrança por vir: vira leitura no fim da vigência, sem carência.</summary>
    [Fact]
    public void Cancelada_vence_no_fim_da_vigencia()
    {
        var assinatura = Ativa(Agora);
        assinatura.Cancelar(Agora);
        var fim = assinatura.VigenteAte!.Value;

        assinatura.DeveVencer(fim.AddMinutes(-1), Carencia).ShouldBeFalse();
        assinatura.DeveVencer(fim, Carencia).ShouldBeTrue();
    }

    [Fact]
    public void Pendente_nunca_vence()
    {
        new Assinatura().DeveVencer(Agora.AddYears(1), Carencia).ShouldBeFalse();
    }

    [Theory]
    [InlineData(-8, null)]
    [InlineData(-7, 7)]
    [InlineData(-4, 7)]
    [InlineData(-3, 3)]
    [InlineData(0, 3)]
    [InlineData(1, -1)]
    public void Aviso_devido_segue_os_marcos(int diasAteOVencimento, int? marco)
    {
        var assinatura = Ativa(Agora);
        var fim = assinatura.VigenteAte!.Value;

        assinatura.AvisoDeVencimentoDevido(fim.AddDays(diasAteOVencimento)).ShouldBe(marco);
    }

    [Fact]
    public void Aviso_enviado_nao_se_repete_e_o_proximo_marco_vem_na_hora_dele()
    {
        var assinatura = Ativa(Agora);
        var fim = assinatura.VigenteAte!.Value;

        assinatura.RegistrarAviso(7);

        assinatura.AvisoDeVencimentoDevido(fim.AddDays(-5)).ShouldBeNull();
        assinatura.AvisoDeVencimentoDevido(fim.AddDays(-2)).ShouldBe(3);
    }

    /// <summary>Worker parado dois dias: sai o aviso atual, não os atrasados.</summary>
    [Fact]
    public void Worker_parado_manda_so_o_marco_mais_avancado()
    {
        var assinatura = Ativa(Agora);

        assinatura.AvisoDeVencimentoDevido(assinatura.VigenteAte!.Value.AddDays(2)).ShouldBe(-1);
    }

    [Fact]
    public void Renovar_zera_os_avisos()
    {
        var assinatura = Ativa(Agora);
        assinatura.RegistrarAviso(3);

        assinatura.Renovar(Agora.AddDays(29), CicloDeCobranca.Mensal);

        assinatura.UltimoAvisoDeVencimento.ShouldBeNull();
    }
}

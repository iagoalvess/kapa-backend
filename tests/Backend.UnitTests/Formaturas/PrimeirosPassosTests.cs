using Backend.Business.Formaturas.Models;
using Shouldly;

namespace Backend.UnitTests.Formaturas;

/// <summary>Quando o bloco "Primeiros passos" some do Início: todos os passos obrigatórios feitos.</summary>
public sealed class PrimeirosPassosTests
{
    private static readonly PrimeirosPassos Todos = new(true, true, true, true, true, true);

    [Fact]
    public void Todos_feitos_conclui() => Todos.Concluidos.ShouldBeTrue();

    /// <summary>Montar a comissão é opcional: convidar alguém não termina nem impede a configuração.</summary>
    [Fact]
    public void Sem_a_comissao_montada_conclui_mesmo_assim() => (Todos with { ComissaoMontada = false }).Concluidos.ShouldBeTrue();

    public static TheoryData<PrimeirosPassos> UmObrigatorioFaltando =>
        [
            Todos with
            {
                PlanoDeCobrancaEmVigor = false,
            },
            Todos with
            {
                TermoPublicado = false,
            },
            Todos with
            {
                RecebimentosConfigurados = false,
            },
            Todos with
            {
                PlanoContratado = false,
            },
            Todos with
            {
                FormandosNaTurma = false,
            },
        ];

    [Theory]
    [MemberData(nameof(UmObrigatorioFaltando))]
    public void Faltando_um_obrigatorio_nao_conclui(PrimeirosPassos passos) => passos.Concluidos.ShouldBeFalse();
}

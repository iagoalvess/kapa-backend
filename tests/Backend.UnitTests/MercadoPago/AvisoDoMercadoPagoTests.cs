using Backend.Business.MercadoPago.Services;
using Shouldly;

namespace Backend.UnitTests.MercadoPago;

/// <summary>
/// Sprint 35: o <c>user_id</c> do corpo é o que separa o aviso da conta do Kapa do de uma turma. O Mercado Pago
/// o escreve como número ou texto; o que não dá para ler segue o caminho da turma.
/// </summary>
public sealed class AvisoDoMercadoPagoTests
{
    [Theory]
    [InlineData("""{"type":"order","user_id":900}""", 900L)]
    [InlineData("""{"type":"subscription_preapproval","user_id":"900"}""", 900L)]
    [InlineData("""{"type":"order"}""", null)]
    [InlineData("""{"user_id":"abc"}""", null)]
    [InlineData("não é json", null)]
    [InlineData("[1,2]", null)]
    [InlineData("", null)]
    public void Conta_do_aviso_sai_do_user_id(string corpo, long? conta) => AvisoDoMercadoPago.ContaDoAviso(corpo).ShouldBe(conta);
}

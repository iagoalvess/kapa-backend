using System.Security.Claims;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Services;

namespace Backend.Api.Extensions;

/// <summary>
/// Lê a formatura da sessão a partir da claim <c>formatura_id</c> do access token.
/// </summary>
/// <remarks>
/// A formatura vem do token, e nunca de cabeçalho ou de rota: cabeçalho é escolhido pelo
/// cliente, e "o cliente diz de qual turma são os dados" é exatamente o buraco que o isolamento
/// existe para fechar. O token é assinado pela API, que só põe ali uma formatura cujo vínculo
/// acabou de ser conferido.
/// <para>
/// Requisição sem a claim — login, listagem de formaturas, cadastro — resolve para
/// <c>Id = null</c>, e nesse estado o filtro global não casa com linha nenhuma.
/// </para>
/// <para>
/// A exceção é a requisição que chega <b>sem sessão</b> e já prova de qual turma é — o aviso do Mercado
/// Pago, pelo pedido assinado, o retorno do OAuth, pelo <c>state</c> assinado (Sprint 25), e a loja pública,
/// pela rota ou pelo link assinado da compra (Sprint 26). Quem provou aponta o
/// <see cref="FormaturaDoProcessamento"/>, como no worker.
/// </para>
/// <para>
/// O apontado ganha da claim: só as rotas anônimas apontam, e o formando logado numa turma que abre o link
/// da loja de outra está comprando <b>desta</b> — com a claim na frente, ele veria a loja vazia.
/// </para>
/// </remarks>
/// <param name="accessor">Acesso ao contexto da requisição.</param>
/// <param name="processamento">A turma apontada por quem chegou sem sessão.</param>
public sealed class FormaturaAtual(IHttpContextAccessor accessor, FormaturaDoProcessamento processamento) : IFormaturaAtual
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    /// <inheritdoc />
    public Guid? Id => processamento.Id ?? (Guid.TryParse(Principal?.FindFirstValue(TokenService.ClaimDeFormatura), out var id) ? id : null);
}

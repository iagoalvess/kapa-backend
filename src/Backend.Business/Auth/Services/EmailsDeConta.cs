using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Settings;
using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Auth.Services;

/// <summary>
/// Monta e enfileira os e-mails do ciclo de vida da conta.
/// </summary>
/// <remarks>
/// HTML montado em código, sem motor de template. Para três mensagens curtas, um motor custaria
/// mais dependência e mais arquivos do que economiza. Quando as mensagens virarem dez, ou quando
/// alguém de fora do time precisar editá-las, aí entra um template de verdade — e só esta classe
/// muda.
/// <para>
/// Tudo o que vem do usuário passa por <see cref="ModeloDeEmail.Texto"/> antes de entrar
/// no corpo: nome de usuário é texto controlado por terceiro, e e-mail é HTML.
/// </para>
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
/// <param name="conta">Regras do ciclo de vida da conta.</param>
public sealed class EmailsDeConta(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao, IOptions<ContaSettings> conta) : IEmailsDeConta
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;
    private readonly ContaSettings _conta = conta.Value;

    /// <inheritdoc />
    public async Task EnfileirarConfirmacao(Usuario usuario, string token, CancellationToken ct = default)
    {
        var link = MontarLink(_conta.CaminhoDeConfirmacao, usuario.Email!, token);

        var corpo = Modelo(
            $"Bem-vindo ao {Texto(_aplicacao.Nome)}",
            $"Olá, {Texto(usuario.Nome)}. Confirme seu e-mail para ativar o acesso.",
            "Confirmar e-mail",
            link,
            Mascote.Acenando
        );

        await emailService.Enfileirar(new NovoEmail(usuario.Email!, $"Confirme seu e-mail — {_aplicacao.Nome}", corpo, EEmailPrioridade.Alta), ct);
    }

    /// <inheritdoc />
    public async Task EnfileirarRedefinicaoDeSenha(Usuario usuario, string token, CancellationToken ct = default)
    {
        var link = MontarLink(_conta.CaminhoDeRedefinicao, usuario.Email!, token);

        var corpo = Modelo(
            "Redefinição de senha",
            $"Olá, {Texto(usuario.Nome)}. Recebemos um pedido para redefinir sua senha. "
                + $"O link vale por {_conta.HorasDeValidadeDoLink} horas. "
                + "Se não foi você quem pediu, ignore este e-mail — sua senha continua a mesma.",
            "Redefinir senha",
            link,
            Mascote.Cadeado
        );

        await emailService.Enfileirar(new NovoEmail(usuario.Email!, $"Redefinição de senha — {_aplicacao.Nome}", corpo, EEmailPrioridade.Alta), ct);
    }

    /// <inheritdoc />
    public async Task EnfileirarAvisoDeSenhaAlterada(Usuario usuario, CancellationToken ct = default)
    {
        var corpo = Modelo(
            "Sua senha foi alterada",
            $"Olá, {Texto(usuario.Nome)}. A senha da sua conta foi alterada agora há pouco e todas as sessões abertas foram encerradas. "
                + "<strong>Se não foi você, procure o administrador imediatamente</strong> — alguém pode ter acesso à sua conta.",
            botao: null,
            link: null,
            Mascote.Cadeado
        );

        await emailService.Enfileirar(new NovoEmail(usuario.Email!, $"Sua senha foi alterada — {_aplicacao.Nome}", corpo, EEmailPrioridade.Alta), ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O código vai no assunto, como nos apps de banco: dá para ler na notificação sem abrir o e-mail. Prioridade
    /// alta, porque a pessoa está esperando na tela.
    /// </remarks>
    public async Task EnfileirarCodigoDeEntrada(Usuario usuario, string codigo, int minutos, CancellationToken ct = default)
    {
        var corpo = Modelo(
            "Seu código de acesso",
            $"Olá, {Texto(usuario.Nome)}. Use o código <strong style=\"font-size:22px;letter-spacing:4px\">{Texto(codigo)}</strong> "
                + $"para entrar no {Texto(_aplicacao.Nome)}. Ele vale por poucos minutos — conte com {minutos}.<br><br>"
                + "<strong>Se não foi você que tentou entrar, troque sua senha agora</strong>: alguém acertou a sua senha, e só "
                + "este código impediu a entrada.",
            botao: null,
            link: null,
            Mascote.Cadeado
        );

        await emailService.Enfileirar(
            new NovoEmail(usuario.Email!, $"{codigo} é o seu código de acesso — {_aplicacao.Nome}", corpo, EEmailPrioridade.Alta),
            ct
        );
    }

    private string MontarLink(string caminho, string email, string token) =>
        _aplicacao.MontarUrl(caminho, [new("email", email), new("token", CodificadorDeToken.Codificar(token))]);

    private static string Texto(string? valor) => ModeloDeEmail.Texto(valor);

    private string Modelo(string titulo, string mensagem, string? botao, string? link, Mascote mascote) =>
        ModeloDeEmail.Montar(_aplicacao, titulo, mensagem, botao, link, mascote);
}

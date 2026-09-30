namespace Backend.Business.Emails.Models;

/// <summary>O rodapé do e-mail de marketing: por que a pessoa recebe e por onde sai.</summary>
/// <param name="MotivoHtml">"Você recebe este e-mail porque…", já em HTML — quem monta codifica o nome da turma.</param>
/// <param name="LinkDeDescadastro">A página de descadastro no app.</param>
public sealed record RodapeDeMarketing(string MotivoHtml, string LinkDeDescadastro);

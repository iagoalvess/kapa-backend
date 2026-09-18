using Backend.Business.Leads.Models;
using FluentValidation;

namespace Backend.Business.Leads.Validators;

/// <summary>
/// Forma do formulário de contato da página institucional.
/// </summary>
/// <remarks>
/// O honeypot <b>não</b> é validado aqui: campo escondido preenchido não é erro de forma, é robô —
/// e a resposta certa é sucesso silencioso, no service. Um 400 ensinaria o robô qual campo deixar
/// em branco na próxima tentativa.
/// <para>
/// Formulário público é a porta mais aberta do produto: todo campo tem teto de tamanho, senão o
/// primeiro robô grava um megabyte de texto por linha.
/// </para>
/// </remarks>
public sealed class NovoLeadValidator : AbstractValidator<NovoLead>
{
    /// <summary>Registra as regras de validação.</summary>
    public NovoLeadValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().WithMessage("Informe seu nome.").MaximumLength(120);

        RuleFor(x => x.Email).NotEmpty().WithMessage("Informe seu e-mail.").EmailAddress().WithMessage("E-mail inválido.").MaximumLength(256);

        RuleFor(x => x.Telefone).MaximumLength(32).When(x => !string.IsNullOrWhiteSpace(x.Telefone));

        RuleFor(x => x.Instituicao).NotEmpty().WithMessage("Informe a instituição.").MaximumLength(160);

        RuleFor(x => x.Curso).NotEmpty().WithMessage("Informe o curso.").MaximumLength(160);

        RuleFor(x => x.TamanhoDaTurma).InclusiveBetween(1, 2000).WithMessage("Informe quantos formandos a turma tem — entre 1 e 2000.");

        RuleFor(x => x.PrevisaoDeColacao)
            .Matches("^[0-9]{4}-(0[1-9]|1[0-2])$")
            .WithMessage("Use o formato aaaa-mm.")
            .When(x => !string.IsNullOrWhiteSpace(x.PrevisaoDeColacao));

        RuleFor(x => x.Mensagem).MaximumLength(1000).When(x => !string.IsNullOrWhiteSpace(x.Mensagem));

        RuleFor(x => x.AceitaPrivacidade)
            .Equal(true)
            .WithErrorCode("lead.privacidade_nao_aceita")
            .WithMessage("Marque que você leu a Política de Privacidade para enviarmos seu contato.");

        RuleFor(x => x.Origem).MaximumLength(120);
        RuleFor(x => x.Meio).MaximumLength(120);
        RuleFor(x => x.Campanha).MaximumLength(120);
    }
}

using Backend.Business.Common.Texto;
using Backend.Business.Formandos.Models;
using FluentValidation;

namespace Backend.Business.Formandos.Validators;

/// <summary>
/// Forma do cadastro do formando.
/// </summary>
/// <remarks>
/// Nenhum campo é obrigatório — cadastro incompleto não bloqueia nada. O que se confere é a forma
/// do que veio: CPF pelo dígito verificador e telefone reconhecível. Seção ausente não é validada,
/// porque não vai ser tocada.
/// <para>
/// O campo do erro sai com a seção na frente (<c>pessoais.cpf</c>), que é como o cliente sabe em
/// qual formulário acender a mensagem.
/// </para>
/// </remarks>
public sealed class AtualizarPerfilValidator : AbstractValidator<AtualizarPerfil>
{
    private const string MensagemDeTelefone = "Telefone inválido. Use DDD e número, como (41) 99876-5432, ou o formato internacional +55….";

    /// <summary>Registra as regras de validação.</summary>
    public AtualizarPerfilValidator()
    {
        When(
            x => x.Pessoais is not null,
            () =>
            {
                RuleFor(x => x.Pessoais!.NomeCompleto).MaximumLength(200).WithMessage("O nome completo deve ter no máximo 200 caracteres.");

                RuleFor(x => x.Pessoais!.Cpf)
                    .Must(FormatosBrasileiros.CpfValido)
                    .When(x => !string.IsNullOrWhiteSpace(x.Pessoais!.Cpf))
                    .WithErrorCode("perfil.cpf_invalido")
                    .WithMessage("CPF inválido. Confira os 11 dígitos.");

                RuleFor(x => x.Pessoais!.Telefone)
                    .Must(telefone => FormatosBrasileiros.TelefoneE164(telefone) is not null)
                    .When(x => !string.IsNullOrWhiteSpace(x.Pessoais!.Telefone))
                    .WithErrorCode("perfil.telefone_invalido")
                    .WithMessage(MensagemDeTelefone);
            }
        );

        When(
            x => x.ContatoDeEmergencia is not null,
            () =>
            {
                RuleFor(x => x.ContatoDeEmergencia!.Nome).MaximumLength(200).WithMessage("O nome deve ter no máximo 200 caracteres.");

                RuleFor(x => x.ContatoDeEmergencia!.Telefone)
                    .Must(telefone => FormatosBrasileiros.TelefoneE164(telefone) is not null)
                    .When(x => !string.IsNullOrWhiteSpace(x.ContatoDeEmergencia!.Telefone))
                    .WithErrorCode("perfil.telefone_invalido")
                    .WithMessage(MensagemDeTelefone);

                RuleFor(x => x.ContatoDeEmergencia!.Parentesco).MaximumLength(50).WithMessage("O parentesco deve ter no máximo 50 caracteres.");
            }
        );
    }
}

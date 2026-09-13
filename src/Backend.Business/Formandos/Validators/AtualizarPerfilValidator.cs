using Backend.Business.Common.Texto;
using Backend.Business.Formandos.Models;
using FluentValidation;

namespace Backend.Business.Formandos.Validators;

/// <summary>
/// Forma do cadastro do formando.
/// </summary>
/// <remarks>
/// Nenhum campo é obrigatório — cadastro incompleto não bloqueia nada. O que se confere é a forma
/// do que veio: CPF pelo dígito verificador, CEP com 8 dígitos, telefone reconhecível, idade
/// plausível. Seção ausente não é validada, porque não vai ser tocada.
/// <para>
/// O campo do erro sai com a seção na frente (<c>pessoais.cpf</c>), que é como o cliente sabe em
/// qual formulário acender a mensagem.
/// </para>
/// <para>
/// A data de hoje é lida <b>a cada validação</b>, dentro do <c>Must</c>: o validator é singleton,
/// e um limite calculado no construtor congelaria o dia em que o processo subiu.
/// </para>
/// </remarks>
public sealed class AtualizarPerfilValidator : AbstractValidator<AtualizarPerfil>
{
    /// <summary>Idade mínima aceita na data de nascimento.</summary>
    public const int IdadeMinima = 16;

    /// <summary>Idade máxima aceita na data de nascimento.</summary>
    public const int IdadeMaxima = 90;

    private const string MensagemDeTelefone = "Telefone inválido. Use DDD e número, como (41) 99876-5432, ou o formato internacional +55….";

    /// <summary>Registra as regras de validação.</summary>
    public AtualizarPerfilValidator()
    {
        When(
            x => x.Pessoais is not null,
            () =>
            {
                RuleFor(x => x.Pessoais!.NomeCompleto).MaximumLength(200).WithMessage("O nome completo deve ter no máximo 200 caracteres.");
                RuleFor(x => x.Pessoais!.NomeNoDiploma).MaximumLength(200).WithMessage("O nome no diploma deve ter no máximo 200 caracteres.");

                RuleFor(x => x.Pessoais!.Cpf)
                    .Must(FormatosBrasileiros.CpfValido)
                    .When(x => !string.IsNullOrWhiteSpace(x.Pessoais!.Cpf))
                    .WithErrorCode("perfil.cpf_invalido")
                    .WithMessage("CPF inválido. Confira os 11 dígitos.");

                RuleFor(x => x.Pessoais!.Rg).MaximumLength(20).WithMessage("O RG deve ter no máximo 20 caracteres.");
                RuleFor(x => x.Pessoais!.Matricula).MaximumLength(30).WithMessage("A matrícula deve ter no máximo 30 caracteres.");

                RuleFor(x => x.Pessoais!.Telefone)
                    .Must(telefone => FormatosBrasileiros.TelefoneE164(telefone) is not null)
                    .When(x => !string.IsNullOrWhiteSpace(x.Pessoais!.Telefone))
                    .WithErrorCode("perfil.telefone_invalido")
                    .WithMessage(MensagemDeTelefone);

                RuleFor(x => x.Pessoais!.DataDeNascimento)
                    .Must(data => IdadePlausivel(data!.Value))
                    .When(x => x.Pessoais!.DataDeNascimento is not null)
                    .WithErrorCode("perfil.data_de_nascimento_invalida")
                    .WithMessage($"A data de nascimento precisa ser de {IdadeMinima} a {IdadeMaxima} anos atrás.");

                RuleFor(x => x.Pessoais!.Observacoes).MaximumLength(1000).WithMessage("As observações devem ter no máximo 1000 caracteres.");
            }
        );

        When(
            x => x.Endereco is not null,
            () =>
            {
                RuleFor(x => x.Endereco!.Cep)
                    .Must(FormatosBrasileiros.CepValido)
                    .When(x => !string.IsNullOrWhiteSpace(x.Endereco!.Cep))
                    .WithErrorCode("perfil.cep_invalido")
                    .WithMessage("CEP inválido. Use os 8 dígitos, como 80000-000.");

                RuleFor(x => x.Endereco!.Logradouro).MaximumLength(200).WithMessage("O logradouro deve ter no máximo 200 caracteres.");
                RuleFor(x => x.Endereco!.Numero).MaximumLength(20).WithMessage("O número deve ter no máximo 20 caracteres.");
                RuleFor(x => x.Endereco!.Complemento).MaximumLength(100).WithMessage("O complemento deve ter no máximo 100 caracteres.");
                RuleFor(x => x.Endereco!.Bairro).MaximumLength(100).WithMessage("O bairro deve ter no máximo 100 caracteres.");
                RuleFor(x => x.Endereco!.Cidade).MaximumLength(100).WithMessage("A cidade deve ter no máximo 100 caracteres.");

                RuleFor(x => x.Endereco!.Uf)
                    .Must(FormatosBrasileiros.UfValida)
                    .When(x => !string.IsNullOrWhiteSpace(x.Endereco!.Uf))
                    .WithErrorCode("perfil.uf_invalida")
                    .WithMessage("UF inválida. Use a sigla, como PR.");
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

    private static bool IdadePlausivel(DateOnly nascimento)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        return nascimento <= hoje.AddYears(-IdadeMinima) && nascimento >= hoje.AddYears(-IdadeMaxima);
    }
}

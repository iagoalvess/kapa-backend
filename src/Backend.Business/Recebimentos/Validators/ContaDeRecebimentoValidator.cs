using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Services;
using FluentValidation;

namespace Backend.Business.Recebimentos.Validators;

/// <summary>
/// Forma dos meios de recebimento: cada grupo só é conferido quando a turma o habilita, e ao menos
/// um precisa existir.
/// </summary>
/// <remarks>
/// A chave é conferida pelo tipo, com a mensagem que diz o que corrigir. Nome e cidade precisam
/// sobrar no BR Code depois de perder o acento e o que não é ASCII — um nome só de ideogramas
/// viraria um campo vazio, e o banco recusaria o QR.
/// <para>
/// Dado bancário não tem dígito conferido (P5 de 21/09/2026): só presença e tamanho. O Kapa não
/// transfere nada, e travar um dado que o banco aceitaria seria pior do que mostrá-lo errado — o
/// erro aparece no primeiro pagamento, como já acontece com a chave PIX.
/// </para>
/// <para>
/// Campo em branco é recusado, e não aparado até sumir: a entidade apara o texto antes de gravar, e
/// um grupo que virasse nulo ali deixaria a turma com menos meios do que o validator aprovou.
/// </para>
/// </remarks>
public sealed class ContaDeRecebimentoValidator : AbstractValidator<MeiosDaConta>
{
    /// <summary>Tamanho de cada campo bancário e da instrução de onde encontrar quem recebe.</summary>
    public const int TamanhoDoCampoBancario = 100;

    private static readonly Dictionary<TipoDeChavePix, string> MotivoPorTipo = new()
    {
        [TipoDeChavePix.Cpf] = "CPF inválido: confira os 11 dígitos.",
        [TipoDeChavePix.Cnpj] = "CNPJ inválido: confira os 14 caracteres.",
        [TipoDeChavePix.Email] = "E-mail inválido.",
        [TipoDeChavePix.Telefone] = "Informe o celular com DDD, como (41) 99876-5432.",
        [TipoDeChavePix.Aleatoria] = "A chave aleatória tem o formato 123e4567-e89b-12d3-a456-426614174000.",
    };

    /// <summary>Registra as regras de validação.</summary>
    public ContaDeRecebimentoValidator()
    {
        RuleFor(x => x)
            .Must(meios => meios.Habilitados.Count > 0)
            .WithErrorCode("recebimento.sem_meio")
            .WithMessage("Escolha ao menos um meio de recebimento para a turma.");

        When(
            x => x.Pix is not null,
            () =>
            {
                RuleFor(x => x.Pix!.TipoDeChave).IsInEnum().WithMessage("Tipo de chave inválido. Use Cpf, Cnpj, Email, Telefone ou Aleatoria.");

                RuleFor(x => x.Pix!.Chave)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .WithMessage("Informe a chave PIX.")
                    .Must((meios, chave) => ChavePix.Normalizar(meios.Pix!.TipoDeChave, chave) is not null)
                    .WithMessage(meios => MotivoPorTipo.GetValueOrDefault(meios.Pix!.TipoDeChave, "Chave inválida."))
                    .When(x => Enum.IsDefined(x.Pix!.TipoDeChave));

                RuleFor(x => x.Pix!.NomeDoTitular)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .WithMessage("Informe o nome do titular, como o banco mostra.")
                    .MaximumLength(200)
                    .WithMessage("O nome deve ter no máximo 200 caracteres.")
                    .Must(nome => BrCode.Texto(nome, BrCode.TamanhoMaximoDoNome).Length > 0)
                    .WithMessage("Escreva o nome com letras do alfabeto latino.");

                RuleFor(x => x.Pix!.Cidade)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .WithMessage("Informe a cidade do titular.")
                    .MaximumLength(100)
                    .WithMessage("A cidade deve ter no máximo 100 caracteres.")
                    .Must(cidade => BrCode.Texto(cidade, BrCode.TamanhoMaximoDaCidade).Length > 0)
                    .WithMessage("Escreva a cidade com letras do alfabeto latino.");

                RuleFor(x => x.Pix!.Banco)
                    .MaximumLength(TamanhoDoCampoBancario)
                    .WithMessage($"O banco deve ter no máximo {TamanhoDoCampoBancario} caracteres.");
            }
        );

        When(
            x => x.Transferencia is not null,
            () =>
            {
                RuleFor(x => x.Transferencia!.Banco).CampoBancario("Informe o banco.", "O banco");
                RuleFor(x => x.Transferencia!.Agencia).CampoBancario("Informe a agência.", "A agência");
                RuleFor(x => x.Transferencia!.Conta).CampoBancario("Informe a conta, com o dígito.", "A conta");
                RuleFor(x => x.Transferencia!.TipoDeConta).CampoBancario("Informe o tipo da conta.", "O tipo da conta");
                RuleFor(x => x.Transferencia!.Titular).CampoBancario("Informe o titular da conta.", "O titular");
            }
        );

        When(
            x => x.Dinheiro is not null,
            () =>
            {
                RuleFor(x => x.Dinheiro!.Nome).CampoBancario("Informe com quem o formando fala para pagar em dinheiro.", "O nome");

                RuleFor(x => x.Dinheiro!.Onde)
                    .MaximumLength(TamanhoDoCampoBancario)
                    .WithMessage($"O local deve ter no máximo {TamanhoDoCampoBancario} caracteres.");
            }
        );
    }
}

/// <summary>Presença e tamanho de um campo de instrução — é tudo o que se confere sem o banco.</summary>
internal static class RegrasDoCampoBancario
{
    /// <summary>Obrigatório e dentro do limite.</summary>
    /// <typeparam name="T">Tipo validado.</typeparam>
    /// <param name="regra">Regra em construção.</param>
    /// <param name="ausente">Mensagem de campo vazio.</param>
    /// <param name="rotulo">Como o campo é chamado na mensagem de tamanho.</param>
    public static IRuleBuilderOptions<T, string> CampoBancario<T>(this IRuleBuilderInitial<T, string> regra, string ausente, string rotulo) =>
        regra
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ausente)
            .MaximumLength(ContaDeRecebimentoValidator.TamanhoDoCampoBancario)
            .WithMessage($"{rotulo} deve ter no máximo {ContaDeRecebimentoValidator.TamanhoDoCampoBancario} caracteres.");
}

/// <summary>Forma da configuração do cartão (Sprint 39, P2).</summary>
/// <remarks>
/// A taxa repassada vai até 15%: acima disso não é a tarifa do Mercado Pago, é engano de digitação — e um formando
/// pagando 30% a mais sem ninguém perceber. Nula é a turma absorvendo, o padrão.
/// </remarks>
public sealed class ConfiguracaoDoCartaoValidator : AbstractValidator<ConfiguracaoDoCartao>
{
    /// <summary>Taxa máxima repassável, base 10.000.</summary>
    public const int TaxaMaxima = 1500;

    /// <summary>Monta as regras.</summary>
    public ConfiguracaoDoCartaoValidator()
    {
        RuleFor(c => c.TaxaRepassada)
            .InclusiveBetween(1, TaxaMaxima)
            .When(c => c.TaxaRepassada is not null)
            .WithMessage("A taxa repassada deve ficar entre 0,01% e 15%.");
    }
}

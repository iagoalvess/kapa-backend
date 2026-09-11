using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Settings;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Backend.Business.Arquivos.Validators;

/// <summary>
/// Valida um pedido de envio de arquivo.
/// </summary>
/// <remarks>
/// É uma fronteira de confiança: nome, tamanho e categoria vêm do cliente e nenhum deles pode ser
/// aceito como verdade. As regras aqui são de segurança, não de conveniência.
/// <para>
/// O tipo do conteúdo não é validado porque não é aceito: o service o deriva da extensão, que a
/// regra de <c>Nome</c> já conferiu contra a lista de permissão.
/// </para>
/// </remarks>
public sealed class NovoArquivoValidator : AbstractValidator<NovoArquivo>
{
    /// <summary>Registra as regras de validação.</summary>
    /// <param name="options">Limites configurados.</param>
    public NovoArquivoValidator(IOptions<ArmazenamentoSettings> options)
    {
        var settings = options.Value;

        RuleFor(x => x.Nome)
            .NotEmpty()
            .WithMessage("O nome do arquivo é obrigatório.")
            .MaximumLength(255)
            .WithMessage("O nome do arquivo deve ter no máximo 255 caracteres.")
            .Must(nome => settings.ExtensoesPermitidas.Contains(Path.GetExtension(nome), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Extensão não permitida. Aceitas: {string.Join(", ", settings.ExtensoesPermitidas)}.");

        RuleFor(x => x.Tamanho)
            .GreaterThan(0)
            .WithMessage("O arquivo está vazio.")
            .LessThanOrEqualTo(settings.TamanhoMaximoEmBytes)
            .WithMessage($"O arquivo excede o limite de {settings.TamanhoMaximoEmMB} MB.");

        RuleFor(x => x.Categoria)
            .NotEmpty()
            .WithMessage("A categoria é obrigatória.")
            .MaximumLength(60)
            .Matches("^[a-z0-9-]+$")
            .WithMessage("A categoria aceita apenas letras minúsculas, números e hífen.");
    }
}

using Backend.Business.Festa.Models;
using FluentValidation;

namespace Backend.Business.Festa.Validators;

/// <summary>Forma do cadastro de uma mesa.</summary>
public sealed class DadosDaMesaValidator : AbstractValidator<DadosDaMesa>
{
    /// <summary>Tamanho da identificação.</summary>
    public const int IdentificacaoMaxima = 60;

    /// <summary>Tamanho da observação.</summary>
    public const int ObservacaoMaxima = 200;

    /// <summary>Teto de lugares: mesa de 40 já é banquete; dedo trocado não vira mesa de 400.</summary>
    public const int LugaresMaximo = 40;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDaMesaValidator()
    {
        RuleFor(x => x.Identificacao)
            .NotEmpty()
            .WithMessage("Informe como a mesa se chama.")
            .MaximumLength(IdentificacaoMaxima)
            .WithMessage($"A identificação deve ter no máximo {IdentificacaoMaxima} caracteres.");

        RuleFor(x => x.Lugares).InclusiveBetween(1, LugaresMaximo).WithMessage($"Informe de 1 a {LugaresMaximo} lugares.");

        RuleFor(x => x.Observacao).MaximumLength(ObservacaoMaxima).WithMessage($"A observação deve ter no máximo {ObservacaoMaxima} caracteres.");

        RuleFor(x => x.Formato).IsInEnum().WithMessage("Escolha o formato da mesa.");
    }
}

/// <summary>
/// Forma do mapa salvo: o salão num tamanho de salão, e tudo dentro dele.
/// </summary>
/// <remarks>
/// Os limites não são arquitetura, são sanidade: um salão de 6 m a 100 m de lado, um elemento de ao
/// menos 20 cm e no máximo 150 deles. O que está fora do retângulo do salão é recusado, e não
/// recortado — o editor não deixa arrastar para fora, então quem manda isso não é a tela.
/// </remarks>
public sealed class DesenhoDoSalaoValidator : AbstractValidator<DesenhoDoSalao>
{
    /// <summary>Menor lado do salão, em centímetros.</summary>
    public const int LadoMinimo = 600;

    /// <summary>Maior lado do salão, em centímetros.</summary>
    public const int LadoMaximo = 10_000;

    /// <summary>Menor lado de um elemento, em centímetros.</summary>
    public const int ElementoMinimo = 20;

    /// <summary>Quantos elementos um salão pode ter.</summary>
    public const int ElementosMaximo = 150;

    /// <summary>Tamanho do nome escrito no mapa.</summary>
    public const int RotuloMaximo = 40;

    /// <summary>Registra as regras de validação.</summary>
    public DesenhoDoSalaoValidator()
    {
        RuleFor(x => x.Planta.Largura)
            .InclusiveBetween(LadoMinimo, LadoMaximo)
            .WithMessage($"A largura do salão vai de {LadoMinimo / 100} m a {LadoMaximo / 100} m.");

        RuleFor(x => x.Planta.Altura)
            .InclusiveBetween(LadoMinimo, LadoMaximo)
            .WithMessage($"A profundidade do salão vai de {LadoMinimo / 100} m a {LadoMaximo / 100} m.");

        RuleFor(x => x.Planta.Elementos.Count)
            .LessThanOrEqualTo(ElementosMaximo)
            .WithMessage($"O salão comporta no máximo {ElementosMaximo} elementos.");

        RuleForEach(x => x.Planta.Elementos)
            .Must(elemento => Enum.IsDefined(elemento.Tipo) && (elemento.Cor is null || Enum.IsDefined(elemento.Cor.Value)))
            .WithMessage("Elemento de tipo ou cor desconhecidos.")
            .Must(elemento => !string.IsNullOrWhiteSpace(elemento.Rotulo) && elemento.Rotulo.Trim().Length <= RotuloMaximo)
            .WithMessage($"Todo elemento tem nome, de até {RotuloMaximo} caracteres.")
            .Must(
                (desenho, elemento) =>
                    elemento.Largura >= ElementoMinimo
                    && elemento.Altura >= ElementoMinimo
                    && elemento.X >= 0
                    && elemento.Y >= 0
                    && elemento.X + elemento.Largura <= desenho.Planta.Largura
                    && elemento.Y + elemento.Altura <= desenho.Planta.Altura
            )
            .WithMessage("Há elemento fora do salão. Diminua o elemento ou aumente o salão.");

        RuleForEach(x => x.Posicoes)
            .Must(posicao => (posicao.X is null) == (posicao.Y is null))
            .WithMessage("A mesa está no mapa ou fora dele: informe as duas coordenadas, ou nenhuma.")
            .Must(
                (desenho, posicao) =>
                    posicao.X is null
                    || (posicao.X >= 0 && posicao.Y >= 0 && posicao.X <= desenho.Planta.Largura && posicao.Y <= desenho.Planta.Altura)
            )
            .WithMessage("Há mesa fora do salão. Aproxime a mesa ou aumente o salão.");
    }
}

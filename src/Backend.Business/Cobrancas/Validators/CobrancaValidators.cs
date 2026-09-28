using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using FluentValidation;

namespace Backend.Business.Cobrancas.Validators;

/// <summary>Forma do nome e das regras de atraso do plano.</summary>
/// <remarks>
/// Percentuais de 0 a 100% (base 10.000), sem teto de mercado: a decisão de 14/09/2026 foi deixar
/// livre e só avisar na tela acima de 2% de multa ou 1% de juros ao mês.
/// </remarks>
public sealed class DadosDoPlanoValidator : AbstractValidator<DadosDoPlano>
{
    /// <summary>100%, em base 10.000.</summary>
    public const int PercentualMaximo = 10_000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDoPlanoValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty()
            .WithMessage("Dê um nome ao plano.")
            .MaximumLength(120)
            .WithMessage("O nome deve ter no máximo 120 caracteres.");

        RuleFor(x => x.PercentualDeMulta).InclusiveBetween(0, PercentualMaximo).WithMessage("A multa deve ficar entre 0% e 100%.");
        RuleFor(x => x.PercentualDeJurosAoMes).InclusiveBetween(0, PercentualMaximo).WithMessage("Os juros devem ficar entre 0% e 100% ao mês.");
        RuleFor(x => x.CarenciaEmDias).InclusiveBetween(0, 60).WithMessage("A carência deve ficar entre 0 e 60 dias.");
        RuleFor(x => x.PercentualDeDescontoPorAntecipacao)
            .InclusiveBetween(0, PercentualMaximo)
            .WithMessage("O desconto deve ficar entre 0% e 100%.");

        RuleFor(x => x.DiasMinimosParaDesconto).InclusiveBetween(0, 365).WithMessage("A antecedência do desconto deve ficar entre 0 e 365 dias.");

        RuleFor(x => x.DiasMinimosParaDesconto)
            .GreaterThan(0)
            .When(x => x.PercentualDeDescontoPorAntecipacao > 0)
            .WithErrorCode("cobranca.antecedencia_obrigatoria")
            .WithMessage("Diga com quantos dias de antecedência o desconto vale — senão ele sai para quem pagar um dia antes.");
    }
}

/// <summary>Forma de um item do plano.</summary>
/// <remarks>
/// O dia de vencimento vai de 1 a 31 (decisão de 14/09/2026); o mês que não tem o dia usa o último
/// (<see cref="GradeDeParcelas.Vencimento"/>).
/// </remarks>
public sealed class DadosDoItemValidator : AbstractValidator<DadosDoItem>
{
    /// <summary>Teto de parcelas: seis anos de mensalidade, a turma de medicina.</summary>
    public const int ParcelasMaximas = 120;

    /// <summary>Teto do valor de um item, em centavos: R$ 1.000.000,00.</summary>
    public const long ValorMaximo = 100_000_000;

    /// <summary>Registra as regras de validação.</summary>
    /// <param name="tipos">Os tipos aceitos; sem ele, os do plano (<see cref="TiposDeCobranca.DoPlano"/>).</param>
    public DadosDoItemValidator(IReadOnlySet<TipoDeCobranca>? tipos = null)
    {
        var aceitos = tipos ?? TiposDeCobranca.DoPlano;

        RuleFor(x => x.Tipo).Must(aceitos.Contains).WithErrorCode("cobranca.tipo_invalido").WithMessage("Este tipo de cobrança não cabe aqui.");

        RuleFor(x => x.Descricao).MaximumLength(120).WithMessage("A descrição deve ter no máximo 120 caracteres.");

        RuleFor(x => x.ValorEmCentavos)
            .GreaterThan(0)
            .When(x => x.Tipo != TipoDeCobranca.Avulsa)
            .WithErrorCode("cobranca.valor_invalido")
            .WithMessage("Informe um valor maior que zero.");

        RuleFor(x => x.ValorEmCentavos)
            .NotEqual(0)
            .When(x => x.Tipo == TipoDeCobranca.Avulsa)
            .WithErrorCode("cobranca.valor_invalido")
            .WithMessage("Informe um valor diferente de zero.");

        RuleFor(x => x.ValorEmCentavos)
            .InclusiveBetween(-ValorMaximo, ValorMaximo)
            .WithErrorCode("cobranca.valor_invalido")
            .WithMessage("O valor deve ser de no máximo R$ 1.000.000,00.");

        RuleFor(x => x.NumeroDeParcelas).InclusiveBetween(1, ParcelasMaximas).WithMessage($"O número de parcelas vai de 1 a {ParcelasMaximas}.");

        RuleFor(x => x.DiaDeVencimento)
            .InclusiveBetween(1, 31)
            .WithErrorCode("cobranca.dia_invalido")
            .WithMessage("O vencimento vai do dia 1 ao 31.");

        RuleFor(x => x.PrimeiroMes.Year)
            .InclusiveBetween(2000, 2100)
            .OverridePropertyName("primeiro_mes")
            .WithMessage("Informe o mês do primeiro vencimento.");
    }
}

/// <summary>
/// Forma do rateio extraordinário: sem a origem da decisão, ele não existe.
/// </summary>
/// <remarks>
/// A origem é obrigatória porque é a única prova da cobrança — quem já aderiu passa a dever por
/// um item que o termo aceito não cita, e "quem mandou" não pode ficar na memória da tesouraria.
/// </remarks>
public sealed class RateioExtraordinarioValidator : AbstractValidator<RateioExtraordinario>
{
    /// <summary>Tamanho da origem da decisão.</summary>
    public const int TamanhoDaOrigem = 200;

    /// <summary>Registra as regras de validação.</summary>
    public RateioExtraordinarioValidator()
    {
        RuleFor(x => x.OrigemDaDecisao)
            .NotEmpty()
            .WithErrorCode("cobranca.origem_obrigatoria")
            .WithMessage("Informe onde a turma decidiu esta cobrança — a assembleia e a data.")
            .MaximumLength(TamanhoDaOrigem)
            .WithMessage($"A origem deve ter no máximo {TamanhoDaOrigem} caracteres.");
    }
}

/// <summary>
/// Forma de um item opcional: o item de sempre, mais estoque, cota, prazo e abertura.
/// </summary>
/// <remarks>
/// O preço aqui é <b>unitário</b> (decisão 2), então o negativo do <c>Avulsa</c> não faz sentido:
/// vender uma unidade por menos de nada é o crédito da P5, que a tesouraria lança como parcela
/// negativa e não como item opcional.
/// </remarks>
public sealed class DadosDoOpcionalValidator : AbstractValidator<DadosDoOpcional>
{
    /// <summary>Teto de unidades de um item: cabe o salão mais otimista.</summary>
    public const int EstoqueMaximo = 10_000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDoOpcionalValidator()
    {
        RuleFor(x => x.Item).SetValidator(new DadosDoItemValidator(TiposDeCobranca.DosOpcionais));

        RuleFor(x => x.Item.ValorEmCentavos)
            .GreaterThan(0)
            .OverridePropertyName("valor_em_centavos")
            .WithErrorCode("cobranca.valor_invalido")
            .WithMessage("Informe o preço de uma unidade, maior que zero.");

        RuleFor(x => x.LimitePorFormando)
            .InclusiveBetween(1, EstoqueMaximo)
            .When(x => x.LimitePorFormando is not null)
            .OverridePropertyName("limite_por_formando")
            .WithMessage($"A cota por formando vai de 1 a {EstoqueMaximo}.");

        RuleFor(x => x.Estoque)
            .InclusiveBetween(0, EstoqueMaximo)
            .When(x => x.Estoque is not null)
            .OverridePropertyName("estoque")
            .WithMessage($"O estoque vai de 0 a {EstoqueMaximo}.");

        RuleFor(x => x.ModoDeVenda)
            .IsInEnum()
            .Must((dados, modo) => modo != ModoDeVenda.Publica || dados.Item.Tipo == TipoDeCobranca.ConviteExtra)
            .OverridePropertyName("modo_de_venda")
            .WithErrorCode("loja.so_convite")
            .WithMessage("A loja pública vende só o convite da festa.");

        RuleFor(x => x.PrecoPublicoEmCentavos)
            .GreaterThan(0)
            .When(x => x.PrecoPublicoEmCentavos is not null)
            .OverridePropertyName("preco_publico_em_centavos")
            .WithErrorCode("cobranca.valor_invalido")
            .WithMessage("O preço na loja precisa ser maior que zero.");

        RuleFor(x => x.PedidosAteDia)
            .Must((dados, prazo) => prazo >= DateOnly.FromDateTime(DataUtils.ParaExibicao(dados.AberturaDeVendas!.Value)))
            .When(x => x.PedidosAteDia is not null && x.AberturaDeVendas is not null)
            .OverridePropertyName("pedidos_ate_dia")
            .WithErrorCode("cobranca.prazo_antes_da_abertura")
            .WithMessage("O prazo para pedir não pode ser anterior à abertura das vendas.");
    }
}

/// <summary>Forma de um pedido: um item e uma quantidade absoluta.</summary>
public sealed class DadosDoPedidoValidator : AbstractValidator<DadosDoPedido>
{
    /// <summary>Registra as regras de validação.</summary>
    /// <remarks>O teto de parcelas é do item, e só o service o conhece: aqui é só a forma.</remarks>
    public DadosDoPedidoValidator()
    {
        RuleFor(x => x.ItemDeCobrancaId).NotEmpty().OverridePropertyName("item_de_cobranca_id").WithMessage("Escolha o item.");

        RuleFor(x => x.Quantidade)
            .InclusiveBetween(1, DadosDoOpcionalValidator.EstoqueMaximo)
            .OverridePropertyName("quantidade")
            .WithMessage("Informe uma quantidade de ao menos 1.");

        RuleFor(x => x.Parcelas)
            .GreaterThanOrEqualTo(1)
            .When(x => x.Parcelas is not null)
            .OverridePropertyName("parcelas")
            .WithMessage("Escolha ao menos 1 parcela.");
    }
}

/// <summary>Forma do pedido de simulação: cada item como no cadastro.</summary>
public sealed class SimularPlanoValidator : AbstractValidator<SimularPlano>
{
    /// <summary>Teto de itens simulados de uma vez.</summary>
    public const int ItensMaximos = 20;

    /// <summary>Registra as regras de validação.</summary>
    public SimularPlanoValidator()
    {
        When(
            x => x.Itens is not null,
            () =>
            {
                RuleFor(x => x.Itens!.Count)
                    .LessThanOrEqualTo(ItensMaximos)
                    .OverridePropertyName("itens")
                    .WithMessage($"Simule até {ItensMaximos} itens.");
                RuleForEach(x => x.Itens).SetValidator(new DadosDoItemValidator());
            }
        );
    }
}

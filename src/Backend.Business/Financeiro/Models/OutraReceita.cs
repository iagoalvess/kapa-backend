using Backend.Business.Abstractions;

namespace Backend.Business.Financeiro.Models;

/// <summary>
/// Dinheiro que entra na conta da turma sem ser parcela de formando: patrocínio, evento, doação,
/// rendimento, venda de convite (Sprint 28).
/// </summary>
/// <remarks>
/// O espelho de <see cref="Despesa"/> (decisão 1), e nada mais: sem plano de contas, sem centro de
/// custo, sem parcelamento. Não é <c>Parcela</c> e não tem dono (decisão 2) — patrocinador não deve,
/// paga —, então nenhum extrato de formando e nenhuma régua a enxergam.
/// <para>
/// Uma data só: enquanto <see cref="StatusDaOutraReceita.Prevista"/>, é o dia em que se espera o dinheiro;
/// ao receber, vira o dia em que ele entrou. É a data que põe a receita num mês do caixa, e a
/// despesa só precisa de duas porque competência e vencimento respondem perguntas diferentes.
/// </para>
/// <para>
/// ponytail: <see cref="Origem"/> é texto livre, não entidade de patrocinador (decisão 2). Cadastro
/// de patrocinador quando uma turma tiver o segundo.
/// </para>
/// </remarks>
public class OutraReceita : EntidadeDaFormatura
{
    /// <summary>O que é ("Cota ouro do palco", "Rendimento de agosto").</summary>
    public string Descricao { get; private set; } = string.Empty;

    /// <summary>De quem veio, como a tesouraria escreveu ("Clínica Sorriso"). Nulo: não se aplica.</summary>
    public string? Origem { get; private set; }

    /// <summary>De onde vem o dinheiro.</summary>
    public CategoriaDeOutraReceita Categoria { get; private set; }

    /// <summary>Valor, em centavos.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>Dia previsto enquanto prevista; dia em que entrou depois de recebida.</summary>
    public DateOnly Data { get; private set; }

    /// <summary>Situação. Muda só por <see cref="Receber"/> e <see cref="Cancelar"/>.</summary>
    public StatusDaOutraReceita Status { get; private set; } = StatusDaOutraReceita.Prevista;

    /// <summary>Comprovante no acervo (Sprint 11), visível para a turma. Opcional: rendimento raramente tem.</summary>
    public Guid? DocumentoId { get; private set; }

    /// <summary>
    /// A receita que este lançamento estorna (Sprint 38, P5). Nulo na receita comum.
    /// </summary>
    /// <remarks>
    /// O estorno é recebido e <b>negativo</b>, na data da devolução: o arrecadado, a meta e o balancete somam os
    /// dois e voltam ao valor sem a venda, e a receita original — de um mês que pode já estar fechado — não muda.
    /// </remarks>
    public Guid? EstornoDeId { get; private set; }

    /// <summary>Se é o estorno de outra receita.</summary>
    public bool EhEstorno => EstornoDeId is not null;

    /// <summary>Uma receita nova — já recebida, quando o dinheiro já caiu na conta.</summary>
    /// <param name="dados">Dados já validados.</param>
    public static OutraReceita Nova(NovaOutraReceita dados)
    {
        var outraReceita = new OutraReceita { Status = dados.Recebida ? StatusDaOutraReceita.Recebida : StatusDaOutraReceita.Prevista };
        outraReceita.Preencher(
            new DadosDaOutraReceita(dados.Descricao, dados.Origem, dados.Categoria, dados.ValorEmCentavos, dados.Data, dados.DocumentoId)
        );

        return outraReceita;
    }

    /// <summary>
    /// O estorno de uma receita recebida: outra receita, recebida, com o valor negativo e a mesma categoria (P5).
    /// </summary>
    /// <remarks>
    /// Não passa pelo validator da receita comum, que recusa valor negativo: é um tipo de lançamento, não uma
    /// exceção à regra da receita (P5). Quem cria é o sistema — nenhuma tela lança estorno à mão.
    /// </remarks>
    /// <param name="original">A receita estornada.</param>
    /// <param name="descricao">O que o extrato mostra.</param>
    /// <param name="valorEmCentavos">Quanto volta — positivo; gravado negativo.</param>
    /// <param name="data">A data da devolução.</param>
    /// <param name="categoria">A categoria da original — a venda de convite, ou a taxa do cartão repassada (Sprint 39).</param>
    public static OutraReceita Estorno(
        Guid original,
        string descricao,
        long valorEmCentavos,
        DateOnly data,
        CategoriaDeOutraReceita categoria = CategoriaDeOutraReceita.VendaDeConvite
    ) =>
        new()
        {
            Status = StatusDaOutraReceita.Recebida,
            Descricao = descricao,
            Categoria = categoria,
            ValorEmCentavos = -valorEmCentavos,
            Data = data,
            EstornoDeId = original,
        };

    /// <summary>
    /// Corrige o que a tesouraria digitou errado — prevista ou recebida, como a despesa paga.
    /// </summary>
    /// <param name="dados">Dados já validados.</param>
    public Result Aplicar(DadosDaOutraReceita dados)
    {
        if (Status == StatusDaOutraReceita.Cancelada)
            return Result.Falha(ErroCancelada);

        if (EhEstorno)
            return Result.Falha(Erro.Conflito("financeiro.outra_receita_estorno", "Este lançamento é o estorno de uma venda e não se edita."));

        Preencher(dados);

        return Result.Ok();
    }

    /// <summary>Registra a entrada do dinheiro. A partir daqui a receita conta no arrecadado e na meta.</summary>
    /// <param name="recebidaEm">Dia em que o dinheiro entrou — passa a ser a <see cref="Data"/>.</param>
    public Result Receber(DateOnly recebidaEm)
    {
        if (Status != StatusDaOutraReceita.Prevista)
            return Result.Falha(Status == StatusDaOutraReceita.Recebida ? ErroJaRecebida : ErroCancelada);

        Status = StatusDaOutraReceita.Recebida;
        Data = recebidaEm;

        return Result.Ok();
    }

    /// <summary>Deixa de ser esperada. Só a prevista: a recebida se corrige, não se cancela.</summary>
    public Result Cancelar()
    {
        if (Status != StatusDaOutraReceita.Prevista)
            return Result.Falha(Status == StatusDaOutraReceita.Recebida ? ErroJaRecebida : ErroCancelada);

        Status = StatusDaOutraReceita.Cancelada;

        return Result.Ok();
    }

    private static Erro ErroJaRecebida =>
        Erro.Conflito("financeiro.outra_receita_ja_recebida", "Esta receita já foi recebida. Para acertar algum dado, edite-a.");

    private static Erro ErroCancelada => Erro.Conflito("financeiro.outra_receita_cancelada", "Esta receita foi cancelada. Lance uma nova.");

    private void Preencher(DadosDaOutraReceita dados)
    {
        Descricao = dados.Descricao.Trim();
        Origem = string.IsNullOrWhiteSpace(dados.Origem) ? null : dados.Origem.Trim();
        Categoria = dados.Categoria;
        ValorEmCentavos = dados.ValorEmCentavos;
        Data = dados.Data;
        DocumentoId = dados.DocumentoId;
    }
}

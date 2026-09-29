using Backend.Business.Abstractions;

namespace Backend.Business.Financeiro.Models;

/// <summary>
/// O que a turma deve ou já pagou a alguém: uma linha por vencimento.
/// </summary>
/// <remarks>
/// Duas datas (decisão 2): <see cref="Competencia"/> é quando o gasto aconteceu, <see cref="Vencimento"/>
/// é quando ele é pago. A primeira responde "quanto custou a festa"; a segunda, "sobra dinheiro em
/// novembro?".
/// <para>
/// Despesa parcelada gera N linhas, uma por vencimento — o mesmo raciocínio de snapshot da Sprint 6:
/// grade calculada a cada consulta faria mudar o passado quando o combinado mudasse.
/// </para>
/// <para>
/// Não existe coluna de saldo em lugar nenhum (decisão 1). O caixa é a soma dos recebimentos menos a
/// soma das despesas <see cref="StatusDaDespesa.Paga"/>, sempre.
/// </para>
/// </remarks>
public class Despesa : EntidadeDaFormatura
{
    /// <summary>
    /// O lançamento que criou esta linha: as N parcelas de uma parcelada compartilham o mesmo valor.
    /// </summary>
    /// <remarks>
    /// É o que amarra as irmãs. Sem ele, "2 de 3" não tem como chegar às outras duas: fornecedor e
    /// descrição não servem, porque a correção (<see cref="Aplicar"/>) muda os dois numa linha só.
    /// Despesa à vista também tem o seu — um lançamento de uma linha continua sendo um lançamento.
    /// </remarks>
    public Guid LancamentoId { get; private set; }

    /// <summary>A quem se paga. Nulo: gasto sem fornecedor cadastrado (uma taxa bancária, um reembolso).</summary>
    public Guid? FornecedorId { get; private set; }

    /// <summary>
    /// O item da festa que esta despesa paga (Sprint 17). Nulo: gasto que não é da festa.
    /// </summary>
    /// <remarks>
    /// É o que faz o cartão do item mudar de "a contratar" para "contratado" sem ninguém editar o
    /// item (Sprint 17, decisão 2). Opcional de propósito: a taxa bancária e o reembolso não são
    /// item nenhum, e obrigar um vínculo transformaria a lista da festa num plano de contas.
    /// </remarks>
    public Guid? ItemDaFestaId { get; private set; }

    /// <summary>O que é ("Buffet — entrada", "Aluguel do salão").</summary>
    public string Descricao { get; private set; } = string.Empty;

    /// <summary>Em que a turma gastou.</summary>
    public CategoriaDeDespesa Categoria { get; private set; }

    /// <summary>Valor desta linha, em centavos. Na parcelada, o valor da parcela, não o do contrato.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>Mês em que o gasto aconteceu. Gravado no dia 1: competência é mês, não dia.</summary>
    public DateOnly Competencia { get; private set; }

    /// <summary>Dia em que esta linha é paga.</summary>
    public DateOnly Vencimento { get; private set; }

    /// <summary>Posição na parcelada, a partir de 1. Despesa à vista é 1.</summary>
    public int Numero { get; private set; } = 1;

    /// <summary>Total de parcelas do lançamento — o "3" de "2/3". À vista é 1.</summary>
    public int TotalDeParcelas { get; private set; } = 1;

    /// <summary>Situação. Muda só por <see cref="Pagar"/> e <see cref="Cancelar"/>.</summary>
    public StatusDaDespesa Status { get; private set; } = StatusDaDespesa.Prevista;

    /// <summary>Dia em que o dinheiro saiu. Só na paga.</summary>
    public DateOnly? PagoEm { get; private set; }

    /// <summary>Comprovante do pagamento. Obrigatório na paga (decisão 3).</summary>
    public Guid? ComprovanteArquivoId { get; private set; }

    /// <summary>Se ainda vai sair dinheiro por esta linha.</summary>
    public bool EmAberto => Status == StatusDaDespesa.Prevista;

    /// <summary>Uma linha da parcelada — ou a despesa inteira, quando é à vista.</summary>
    /// <param name="dados">Dados já validados do lançamento.</param>
    /// <param name="lancamentoId">O lançamento, o mesmo para todas as linhas desta chamada.</param>
    /// <param name="numero">Posição na parcelada, a partir de 1.</param>
    /// <param name="vencimento">Vencimento desta linha.</param>
    /// <param name="valorEmCentavos">Valor desta linha.</param>
    public static Despesa Nova(NovaDespesa dados, Guid lancamentoId, int numero, DateOnly vencimento, long valorEmCentavos) =>
        new()
        {
            LancamentoId = lancamentoId,
            FornecedorId = dados.FornecedorId,
            ItemDaFestaId = dados.ItemDaFestaId,
            Descricao = dados.Descricao.Trim(),
            Categoria = dados.Categoria,
            ValorEmCentavos = valorEmCentavos,
            Competencia = PrimeiroDoMes(dados.Competencia),
            Vencimento = vencimento,
            Numero = numero,
            TotalDeParcelas = dados.NumeroDeParcelas,
        };

    /// <summary>
    /// Corrige o que a tesouraria digitou errado.
    /// </summary>
    /// <remarks>
    /// Vale para a prevista e para a paga (decisão 5, de 14/09/2026): a despesa não tem contraparte do
    /// outro lado, e prestação de contas com número errado é pior que número corrigido. Cancelada não
    /// aceita: relance.
    /// </remarks>
    /// <param name="dados">Dados já validados.</param>
    public Result Aplicar(DadosDaDespesa dados)
    {
        if (Status == StatusDaDespesa.Cancelada)
            return Result.Falha(Erro.Conflito("financeiro.despesa_cancelada", "Esta despesa foi cancelada. Lance uma nova."));

        FornecedorId = dados.FornecedorId;
        ItemDaFestaId = dados.ItemDaFestaId;
        Descricao = dados.Descricao.Trim();
        Categoria = dados.Categoria;
        ValorEmCentavos = dados.ValorEmCentavos;
        Competencia = PrimeiroDoMes(dados.Competencia);
        Vencimento = dados.Vencimento;

        return Result.Ok();
    }

    /// <summary>
    /// A tarifa que o Mercado Pago descontou de um pagamento (Sprint 39, P6): já paga, na categoria Taxas, sem
    /// fornecedor e sem comprovante — a prova é o extrato do Mercado Pago, e é com ele que o balancete fecha.
    /// </summary>
    /// <param name="descricao">O que é, com o pagamento que a gerou.</param>
    /// <param name="valorEmCentavos">A tarifa.</param>
    /// <param name="pagoEm">Dia do pagamento que a gerou.</param>
    public static Despesa TarifaDoMercadoPago(string descricao, long valorEmCentavos, DateOnly pagoEm) =>
        new()
        {
            LancamentoId = Guid.CreateVersion7(),
            Descricao = descricao,
            Categoria = CategoriaDeDespesa.Taxas,
            ValorEmCentavos = valorEmCentavos,
            Competencia = PrimeiroDoMes(pagoEm),
            Vencimento = pagoEm,
            Status = StatusDaDespesa.Paga,
            PagoEm = pagoEm,
        };

    /// <summary>Registra a saída do dinheiro, com o comprovante.</summary>
    /// <remarks>Sem comprovante não paga (decisão 3): quem barra o nulo é o service, antes de chegar aqui.</remarks>
    /// <param name="pagoEm">Dia em que o dinheiro saiu.</param>
    /// <param name="comprovanteArquivoId">Comprovante gravado no módulo de arquivos.</param>
    public Result Pagar(DateOnly pagoEm, Guid comprovanteArquivoId)
    {
        if (Status != StatusDaDespesa.Prevista)
            return Result.Falha(Erro.Conflito("financeiro.despesa_nao_prevista", "Esta despesa não está prevista para pagamento."));

        Status = StatusDaDespesa.Paga;
        PagoEm = pagoEm;
        ComprovanteArquivoId = comprovanteArquivoId;

        return Result.Ok();
    }

    /// <summary>Deixa de ser devida. Só a prevista: a paga se corrige, não se cancela (decisão 5).</summary>
    public Result Cancelar()
    {
        if (Status != StatusDaDespesa.Prevista)
            return Result.Falha(Erro.Conflito("financeiro.despesa_nao_prevista", "Só uma despesa prevista pode ser cancelada."));

        Status = StatusDaDespesa.Cancelada;

        return Result.Ok();
    }

    /// <summary>O dia 1 do mês da data — a competência é gravada assim.</summary>
    /// <param name="data">Qualquer dia do mês.</param>
    public static DateOnly PrimeiroDoMes(DateOnly data) => new(data.Year, data.Month, 1);
}

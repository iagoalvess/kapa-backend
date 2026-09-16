using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Models;

namespace Backend.Business.Relatorios.Models;

/// <summary>
/// O que o usuário pediu, para o worker gerar.
/// </summary>
/// <remarks>
/// O PDF do balancete não sai na requisição (decisão 2): relatório síncrono que demora quarenta
/// segundos estoura o timeout do proxy e é reenviado pelo usuário — três vezes, em paralelo. O
/// <c>POST</c> grava esta linha e volta; o <c>GeracaoDeRelatoriosJob</c> a encontra, gera, guarda o
/// arquivo e enfileira o e-mail.
/// <para>
/// O arquivo expira em <see cref="DiasDeValidade"/> dias. Relatório é fotografia de um dia — guardar
/// para sempre o PDF de toda semana de toda turma é encher o bucket com o que ninguém vai reabrir, e
/// manter dados financeiros por aí muito além do necessário.
/// </para>
/// </remarks>
public class SolicitacaoDeRelatorio : EntidadeDaFormatura
{
    /// <summary>Por quantos dias o arquivo fica disponível para download.</summary>
    public const int DiasDeValidade = 7;

    /// <summary>Quantas vezes o job insiste antes de desistir de uma solicitação.</summary>
    public const int MaximoDeTentativas = 3;

    /// <summary>O que gerar.</summary>
    public TipoDeRelatorio Tipo { get; private set; }

    /// <summary>Primeiro dia do período pedido.</summary>
    public DateOnly De { get; private set; }

    /// <summary>Último dia do período pedido.</summary>
    public DateOnly Ate { get; private set; }

    /// <summary>Despesas: fornecedor do recorte, quando houver.</summary>
    public Guid? FornecedorId { get; private set; }

    /// <summary>Despesas: categoria do recorte, quando houver.</summary>
    public CategoriaDeDespesa? Categoria { get; private set; }

    /// <summary>Despesas: situação do recorte, quando houver.</summary>
    public StatusDaDespesa? SituacaoDaDespesa { get; private set; }

    /// <summary>Parcelas: formando do recorte, quando houver.</summary>
    public Guid? FormandoId { get; private set; }

    /// <summary>Parcelas: item de cobrança do recorte, quando houver.</summary>
    public Guid? ItemDeCobrancaId { get; private set; }

    /// <summary>Parcelas: situação do recorte, quando houver.</summary>
    public StatusDaParcela? SituacaoDaParcela { get; private set; }

    /// <summary>Quem pediu — é quem recebe o e-mail e quem pode baixar.</summary>
    public Guid SolicitadaPorUsuarioId { get; private set; }

    /// <summary>Situação. Muda só por <see cref="Concluir"/> e <see cref="Falhar"/>.</summary>
    public StatusDaSolicitacao Status { get; private set; } = StatusDaSolicitacao.NaFila;

    /// <summary>Tentativas de geração já feitas.</summary>
    public int Tentativas { get; private set; }

    /// <summary>Arquivo gerado, no módulo de arquivos. Só na concluída.</summary>
    public Guid? ArquivoId { get; private set; }

    /// <summary>Quando o arquivo deixa de estar disponível, em UTC. Só na concluída.</summary>
    public DateTime? ExpiraEm { get; private set; }

    /// <summary>Por que falhou, para a tela dizer algo melhor que "erro".</summary>
    public string? Motivo { get; private set; }

    /// <summary>O período pedido, como o resto do código o lê.</summary>
    public PeriodoDoRelatorio Periodo => new(De, Ate);

    /// <summary>
    /// O recorte pedido, remontado para o worker gerar o mesmo arquivo depois.
    /// </summary>
    /// <remarks>
    /// É por isso que o filtro é <b>coluna</b>, e não parâmetro de requisição: quem gera é o worker,
    /// minutos depois, sem a requisição que pediu. O que não estiver gravado aqui não existe para ele.
    /// </remarks>
    public FiltroDoRelatorio Filtro => new(Periodo, FornecedorId, Categoria, SituacaoDaDespesa, FormandoId, ItemDeCobrancaId, SituacaoDaParcela);

    /// <summary>Um pedido novo, ainda na fila.</summary>
    /// <param name="tipo">O que gerar.</param>
    /// <param name="filtro">Recorte pedido, com o período já normalizado.</param>
    /// <param name="solicitadaPorUsuarioId">Quem pediu.</param>
    public static SolicitacaoDeRelatorio Nova(TipoDeRelatorio tipo, FiltroDoRelatorio filtro, Guid solicitadaPorUsuarioId) =>
        new()
        {
            Tipo = tipo,
            De = filtro.Periodo.De,
            Ate = filtro.Periodo.Ate,
            FornecedorId = filtro.FornecedorId,
            Categoria = filtro.Categoria,
            SituacaoDaDespesa = filtro.SituacaoDaDespesa,
            FormandoId = filtro.FormandoId,
            ItemDeCobrancaId = filtro.ItemDeCobrancaId,
            SituacaoDaParcela = filtro.SituacaoDaParcela,
            SolicitadaPorUsuarioId = solicitadaPorUsuarioId,
        };

    /// <summary>Marca mais uma tentativa — chamado pelo job antes de gerar.</summary>
    public void Tentar() => Tentativas++;

    /// <summary>O arquivo ficou pronto e vale por <see cref="DiasDeValidade"/> dias.</summary>
    /// <param name="arquivoId">Arquivo gravado.</param>
    /// <param name="agora">Momento da conclusão, em UTC.</param>
    public void Concluir(Guid arquivoId, DateTime agora)
    {
        Status = StatusDaSolicitacao.Pronta;
        ArquivoId = arquivoId;
        ExpiraEm = agora.AddDays(DiasDeValidade);
        Motivo = null;
    }

    /// <summary>
    /// A geração não deu certo.
    /// </summary>
    /// <remarks>
    /// Volta para a fila enquanto houver tentativa: falha de rede no provedor de arquivos é a causa
    /// mais provável, e ela passa. Esgotadas, a linha fica <see cref="StatusDaSolicitacao.Falhou"/> e
    /// a tela mostra o motivo, em vez de um "na fila" que nunca sai de lá.
    /// </remarks>
    /// <param name="motivo">O que aconteceu, em uma linha.</param>
    public void Falhar(string motivo)
    {
        Motivo = motivo;

        if (Tentativas >= MaximoDeTentativas)
            Status = StatusDaSolicitacao.Falhou;
    }

    /// <summary>
    /// O prazo acabou: o arquivo foi apagado.
    /// </summary>
    /// <remarks>
    /// A linha continua, sem o arquivo. Ela é o registro de que o balancete foi pedido, e a tela
    /// precisa poder dizer "expirou" — item que some sem explicação vira "o sistema perdeu".
    /// </remarks>
    public void Expirar() => ArquivoId = null;

    /// <summary>Se o arquivo ainda pode ser baixado no momento informado.</summary>
    /// <param name="agora">Momento da consulta, em UTC.</param>
    public bool Disponivel(DateTime agora) => Status == StatusDaSolicitacao.Pronta && ArquivoId is not null && ExpiraEm > agora;
}

/// <summary>
/// Qual relatório — o mesmo enum para a planilha e para o PDF.
/// </summary>
/// <remarks>
/// Um só, e não um por formato: eram dois enums com os mesmos quatro nomes, e o segundo existia
/// apenas porque só o balancete tinha PDF. Vira segmento de rota em minúsculas na planilha e valor
/// gravado na fila do PDF.
/// </remarks>
public enum TipoDeRelatorio
{
    /// <summary>Entradas e saídas do período, consolidadas — o documento da assembleia.</summary>
    Balancete,

    /// <summary>Uma linha por despesa: o que o contador confere nota a nota.</summary>
    Despesas,

    /// <summary>Uma linha por parcela. Nomeia quem deve.</summary>
    Parcelas,

    /// <summary>Uma linha por fornecedor, com o pago e o previsto.</summary>
    Fornecedores,
}

/// <summary>Em que pé está a solicitação.</summary>
public enum StatusDaSolicitacao
{
    /// <summary>Esperando o worker.</summary>
    NaFila,

    /// <summary>Gerada; o arquivo pode ser baixado até expirar.</summary>
    Pronta,

    /// <summary>Não deu certo depois de todas as tentativas.</summary>
    Falhou,
}

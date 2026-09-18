using Backend.Business.Abstractions;

namespace Backend.Business.Privacidade.Models;

/// <summary>
/// Um pedido do titular sobre os próprios dados: exportar tudo, ou ser eliminado.
/// </summary>
/// <remarks>
/// <b>Não herda de <c>EntidadeDaFormatura</c></b>, e é a única entidade de domínio que não herda
/// (decisão 2 da Sprint 14). Titular, na LGPD, é a pessoa — não o vínculo dela com uma turma. Quem
/// está em duas formaturas pede uma vez e o pedido alcança as duas; o contrário seria a pessoa
/// pedir exclusão e continuar cadastrada na turma ao lado.
/// <para>
/// O <see cref="PrazoEm"/> é o que o worker lê: ele processa o que está <c>Pendente</c> e já
/// venceu. Exportação nasce vencida (sai na próxima passada); exclusão nasce com quinze dias pela
/// frente (art. 18, §3º), que é a janela em que o titular confirma — <see cref="Confirmar"/> —, a
/// comissão conversa com ele, ou nada acontece e a eliminação sai sozinha no fim.
/// </para>
/// </remarks>
public class SolicitacaoDePrivacidade : Entity
{
    /// <summary>Prazo legal de resposta ao titular, em dias (LGPD, art. 18, §3º).</summary>
    public const int DiasDePrazo = 15;

    /// <summary>Por quantos dias o arquivo da exportação fica disponível.</summary>
    /// <remarks>
    /// Mesmo prazo dos relatórios da Sprint 12, pelo mesmo motivo: o pacote tem CPF, endereço e o
    /// financeiro inteiro de uma pessoa. Guardá-lo para sempre é manter uma cópia concentrada do que
    /// o resto do sistema guarda espalhado.
    /// </remarks>
    public const int DiasDeValidadeDoArquivo = 7;

    /// <summary>Quantas vezes o worker insiste antes de desistir.</summary>
    public const int MaximoDeTentativas = 3;

    /// <summary>O que o titular pediu.</summary>
    public TipoDeSolicitacao Tipo { get; private set; }

    /// <summary>A pessoa. É quem pede, quem é avisada e a única que pode baixar.</summary>
    public Guid TitularUsuarioId { get; private set; }

    /// <summary>Em que pé está. Muda só pelos métodos desta classe.</summary>
    public StatusDaSolicitacaoDePrivacidade Status { get; private set; } = StatusDaSolicitacaoDePrivacidade.Pendente;

    /// <summary>A partir de quando o worker executa, em UTC.</summary>
    /// <remarks>
    /// É prazo e agenda ao mesmo tempo, numa coluna só. Na exportação é o instante do pedido; na
    /// exclusão, quinze dias depois dele — ou o instante da confirmação, se ela vier antes.
    /// </remarks>
    public DateTime PrazoEm { get; private set; }

    /// <summary>Quando o titular confirmou, antecipando o prazo. Nulo enquanto não confirmou.</summary>
    public DateTime? ConfirmadaEm { get; private set; }

    /// <summary>Quando a solicitação foi atendida, em UTC.</summary>
    public DateTime? ConcluidaEm { get; private set; }

    /// <summary>Pacote gerado, no módulo de arquivos. Só na exportação concluída.</summary>
    public Guid? ArquivoId { get; private set; }

    /// <summary>Quando o pacote deixa de estar disponível, em UTC.</summary>
    public DateTime? ExpiraEm { get; private set; }

    /// <summary>Por que falhou, para a tela dizer algo melhor que "erro".</summary>
    public string? Motivo { get; private set; }

    /// <summary>Tentativas de processamento já feitas.</summary>
    public int Tentativas { get; private set; }

    /// <summary>Um pedido novo.</summary>
    /// <param name="tipo">Exportação ou exclusão.</param>
    /// <param name="titularUsuarioId">Quem pediu.</param>
    /// <param name="agora">Momento do pedido, em UTC.</param>
    public static SolicitacaoDePrivacidade Nova(TipoDeSolicitacao tipo, Guid titularUsuarioId, DateTime agora) =>
        new()
        {
            Tipo = tipo,
            TitularUsuarioId = titularUsuarioId,
            PrazoEm = tipo == TipoDeSolicitacao.Exclusao ? agora.AddDays(DiasDePrazo) : agora,
        };

    /// <summary>Marca mais uma tentativa — chamado antes de processar.</summary>
    public void Tentar() => Tentativas++;

    /// <summary>
    /// O titular confirmou: a eliminação deixa de esperar o prazo.
    /// </summary>
    /// <remarks>
    /// Só a exclusão tem o que confirmar, e só enquanto pendente. Confirmar de novo não adianta o
    /// relógio duas vezes — o clique duplo no e-mail não é um segundo pedido.
    /// </remarks>
    /// <param name="agora">Momento da confirmação, em UTC.</param>
    public Result Confirmar(DateTime agora)
    {
        if (Tipo != TipoDeSolicitacao.Exclusao)
            return Result.Falha(Erro.Conflito("privacidade.nada_a_confirmar", "Só a solicitação de exclusão precisa de confirmação."));

        if (Status != StatusDaSolicitacaoDePrivacidade.Pendente)
            return Result.Falha(Erro.Conflito("privacidade.solicitacao_encerrada", "Esta solicitação já foi atendida ou cancelada."));

        if (ConfirmadaEm is null)
        {
            ConfirmadaEm = agora;
            PrazoEm = agora;
        }

        return Result.Ok();
    }

    /// <summary>
    /// O titular desistiu do pedido.
    /// </summary>
    /// <remarks>
    /// Só enquanto pendente: depois de anonimizar não há o que desfazer. É por existir esta porta
    /// que o prazo de quinze dias não é só burocracia — ele é a janela em que um clique errado ainda
    /// tem conserto.
    /// </remarks>
    /// <param name="agora">Momento do cancelamento, em UTC.</param>
    public Result Cancelar(DateTime agora)
    {
        if (Status != StatusDaSolicitacaoDePrivacidade.Pendente)
            return Result.Falha(Erro.Conflito("privacidade.solicitacao_encerrada", "Esta solicitação já foi atendida ou cancelada."));

        Status = StatusDaSolicitacaoDePrivacidade.Cancelada;
        ConcluidaEm = agora;

        return Result.Ok();
    }

    /// <summary>O pacote ficou pronto e vale por <see cref="DiasDeValidadeDoArquivo"/> dias.</summary>
    /// <param name="arquivoId">Arquivo gravado.</param>
    /// <param name="agora">Momento da conclusão, em UTC.</param>
    public void ConcluirExportacao(Guid arquivoId, DateTime agora)
    {
        Status = StatusDaSolicitacaoDePrivacidade.Concluida;
        ArquivoId = arquivoId;
        ConcluidaEm = agora;
        ExpiraEm = agora.AddDays(DiasDeValidadeDoArquivo);
        Motivo = null;
    }

    /// <summary>A anonimização aconteceu.</summary>
    /// <param name="agora">Momento da conclusão, em UTC.</param>
    public void ConcluirExclusao(DateTime agora)
    {
        Status = StatusDaSolicitacaoDePrivacidade.Concluida;
        ConcluidaEm = agora;
        Motivo = null;
    }

    /// <summary>
    /// O processamento não deu certo.
    /// </summary>
    /// <remarks>
    /// Volta para a fila enquanto houver tentativa. Esgotadas, a linha fica
    /// <see cref="StatusDaSolicitacaoDePrivacidade.Falhou"/> com o motivo à vista — pedido de titular
    /// que fica "pendente" para sempre é o que vira reclamação na ANPD.
    /// </remarks>
    /// <param name="motivo">O que aconteceu, em uma linha.</param>
    public void Falhar(string motivo)
    {
        Motivo = motivo;

        if (Tentativas >= MaximoDeTentativas)
            Status = StatusDaSolicitacaoDePrivacidade.Falhou;
    }

    /// <summary>O prazo do arquivo acabou: os bytes foram apagados, a linha fica.</summary>
    public void Expirar() => ArquivoId = null;

    /// <summary>Se o pacote ainda pode ser baixado no momento informado.</summary>
    /// <param name="agora">Momento da consulta, em UTC.</param>
    public bool Disponivel(DateTime agora) =>
        Tipo == TipoDeSolicitacao.Exportacao && Status == StatusDaSolicitacaoDePrivacidade.Concluida && ArquivoId is not null && ExpiraEm > agora;
}

/// <summary>O que o titular pediu.</summary>
public enum TipoDeSolicitacao
{
    /// <summary>Portabilidade: o pacote com tudo o que a Kapa guarda sobre a pessoa.</summary>
    Exportacao,

    /// <summary>Eliminação: anonimização do que identifica, preservando o registro financeiro.</summary>
    Exclusao,
}

/// <summary>Em que pé está a solicitação.</summary>
public enum StatusDaSolicitacaoDePrivacidade
{
    /// <summary>Esperando o worker — na exclusão, esperando também o prazo.</summary>
    Pendente,

    /// <summary>Atendida.</summary>
    Concluida,

    /// <summary>O titular desistiu antes de ser atendida.</summary>
    Cancelada,

    /// <summary>Não deu certo depois de todas as tentativas.</summary>
    Falhou,
}

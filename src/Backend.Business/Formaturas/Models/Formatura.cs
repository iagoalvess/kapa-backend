using Backend.Business.Abstractions;

namespace Backend.Business.Formaturas.Models;

/// <summary>
/// A turma de formatura — a unidade de isolamento do produto.
/// </summary>
/// <remarks>
/// Herda de <see cref="Entity"/>, e não de <see cref="EntidadeDaFormatura"/>: é a raiz do
/// isolamento, então não pertence a si mesma. Quem filtra o acesso a ela é o vínculo.
/// <para>
/// <b>Sem as datas de colação e de festa</b> desde a Sprint 19: elas eram dois campos de cadastro
/// que quatro telas liam como calendário, e viraram eventos da agenda (decisão 1). A
/// <c>FormaturaDetalhe</c> continua devolvendo as duas — por projeção, não por coluna.
/// </para>
/// </remarks>
public class Formatura : Entity
{
    private static readonly Dictionary<StatusDaFormatura, StatusDaFormatura[]> Transicoes = new()
    {
        [StatusDaFormatura.Ativa] = [StatusDaFormatura.Suspensa, StatusDaFormatura.Encerrada, StatusDaFormatura.Descartada],
        [StatusDaFormatura.Suspensa] = [StatusDaFormatura.Ativa, StatusDaFormatura.Encerrada],
        [StatusDaFormatura.Encerrada] = [],
        [StatusDaFormatura.Descartada] = [],
    };

    /// <summary>Nome pelo qual a turma se identifica. Ex.: "Medicina 2027".</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>Instituição de ensino.</summary>
    public string Instituicao { get; set; } = string.Empty;

    /// <summary>Curso da turma.</summary>
    public string Curso { get; set; } = string.Empty;

    /// <summary>Ano de conclusão.</summary>
    public int Ano { get; set; }

    /// <summary>Semestre de conclusão: 1 ou 2.</summary>
    public int Semestre { get; set; }

    /// <summary>Quantos formandos a comissão espera.</summary>
    public int QuantidadeEstimadaDeFormandos { get; set; }

    /// <summary>Situação no ciclo de vida. Muda só por <see cref="Transicionar"/>.</summary>
    public StatusDaFormatura Status { get; private set; } = StatusDaFormatura.Ativa;

    /// <summary>Quem criou a turma.</summary>
    public Guid CriadoPorUsuarioId { get; set; }

    /// <summary>Primeira ativação, em UTC.</summary>
    public DateTime? AtivadaEm { get; private set; }

    /// <summary>Encerramento, em UTC.</summary>
    public DateTime? EncerradaEm { get; private set; }

    /// <summary>Se os dados cadastrais ainda podem ser editados.</summary>
    /// <remarks>Suspensa é leitura, Encerrada é arquivo e Descartada foi abandonada: nenhuma aceita escrita.</remarks>
    public bool AceitaEdicao => Status is StatusDaFormatura.Ativa;

    /// <summary>Aplica uma transição de status, rejeitando as inválidas.</summary>
    /// <remarks>
    /// As transições vivem num único lugar de propósito. Espalhar <c>Status = X</c> pelos services
    /// é como se descobre, meses depois, uma formatura Encerrada recebendo cobrança nova — por isso
    /// o setter de <see cref="Status"/> é privado.
    /// <para>
    /// <see cref="AtivadaEm"/> guarda a <b>primeira</b> ativação: regularizar uma suspensão não
    /// reescreve quando a turma começou.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Traz a turma de volta a <c>Ativa</c> porque o pagamento entrou.
    /// </summary>
    /// <remarks>
    /// Existe para os dois caminhos que regularizam uma turma dizerem a mesma coisa: o webhook do
    /// PSP e o botão do painel de suporte (Sprint 16). Uma cópia da sequência em cada um é tudo o
    /// que separa "turma ativada pelo suporte" de "turma ativada pelo suporte, sem <c>AtivadaEm</c>".
    /// <para>
    /// Hoje só há de onde vir: <c>Suspensa</c> — a turma que deixou a assinatura vencer. Antes de
    /// 18/09/2026 vinha também de <c>Rascunho</c>, quando contratar era a porta de entrada.
    /// </para>
    /// <para>
    /// Já ativa responde sucesso: a ação é idempotente de propósito, porque quem clica no painel
    /// está justamente em dúvida sobre o estado da turma.
    /// </para>
    /// </remarks>
    public Result AtivarPorPagamento() => Status == StatusDaFormatura.Ativa ? Result.Ok() : Transicionar(StatusDaFormatura.Ativa);

    /// <summary>Marca o começo da turma recém-criada, que já nasce <c>Ativa</c> no gratuito.</summary>
    /// <remarks>
    /// Não é transição: <see cref="Status"/> já vale <c>Ativa</c> desde a construção. O que falta é
    /// <see cref="AtivadaEm"/>, que só <see cref="Transicionar"/> carimbava — e sem ele a turma não
    /// teria data de início nenhuma.
    /// <para>
    /// Ela nasce ativa porque o gratuito trava por <b>capacidade</b>, não por status: quem segura o
    /// formando é o <c>LimiteDeFormandos = 0</c> do plano, e quem segura as áreas não contratadas é
    /// a política de módulo. Um estado de espera travaria também o que o gratuito dá.
    /// </para>
    /// </remarks>
    public void NascerNoGratuito() => AtivadaEm ??= DateTime.UtcNow;

    /// <param name="destino">Status pretendido.</param>
    public Result Transicionar(StatusDaFormatura destino)
    {
        if (!Transicoes[Status].Contains(destino))
            return Result.Falha(Erro.Conflito("formatura.transicao_invalida", $"Uma formatura {Status} não pode passar para {destino}."));

        Status = destino;

        if (destino == StatusDaFormatura.Ativa)
            AtivadaEm ??= DateTime.UtcNow;

        if (destino == StatusDaFormatura.Encerrada)
            EncerradaEm = DateTime.UtcNow;

        return Result.Ok();
    }
}

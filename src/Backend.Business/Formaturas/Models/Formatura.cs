using Backend.Business.Abstractions;

namespace Backend.Business.Formaturas.Models;

/// <summary>
/// A turma de formatura — a unidade de isolamento do produto.
/// </summary>
/// <remarks>
/// Herda de <see cref="Entity"/>, e não de <see cref="EntidadeDaFormatura"/>: é a raiz do
/// isolamento, então não pertence a si mesma. Quem filtra o acesso a ela é o vínculo.
/// </remarks>
public class Formatura : Entity
{
    private static readonly Dictionary<StatusDaFormatura, StatusDaFormatura[]> Transicoes = new()
    {
        [StatusDaFormatura.Rascunho] = [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Descartada],
        [StatusDaFormatura.AguardandoPagamento] = [StatusDaFormatura.Ativa, StatusDaFormatura.Descartada],
        [StatusDaFormatura.Ativa] = [StatusDaFormatura.Suspensa, StatusDaFormatura.Encerrada],
        [StatusDaFormatura.Suspensa] = [StatusDaFormatura.Ativa, StatusDaFormatura.Encerrada],
        [StatusDaFormatura.Encerrada] = [],
        [StatusDaFormatura.Descartada] = [],
    };

    /// <summary>Nome pelo qual a turma se identifica. Ex.: "Medicina 2027.1 — UFPR".</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>Instituição de ensino.</summary>
    public string Instituicao { get; set; } = string.Empty;

    /// <summary>Curso da turma.</summary>
    public string Curso { get; set; } = string.Empty;

    /// <summary>Ano de conclusão.</summary>
    public int Ano { get; set; }

    /// <summary>Semestre de conclusão: 1 ou 2.</summary>
    public int Semestre { get; set; }

    /// <summary>Data prevista da colação de grau, se já houver.</summary>
    public DateOnly? PrevisaoDeColacao { get; set; }

    /// <summary>Quantos formandos a comissão espera.</summary>
    public int QuantidadeEstimadaDeFormandos { get; set; }

    /// <summary>Situação no ciclo de vida. Muda só por <see cref="Transicionar"/>.</summary>
    public StatusDaFormatura Status { get; private set; } = StatusDaFormatura.Rascunho;

    /// <summary>Quem criou a turma.</summary>
    public Guid CriadoPorUsuarioId { get; set; }

    /// <summary>Primeira ativação, em UTC.</summary>
    public DateTime? AtivadaEm { get; private set; }

    /// <summary>Encerramento, em UTC.</summary>
    public DateTime? EncerradaEm { get; private set; }

    /// <summary>Se os dados cadastrais ainda podem ser editados.</summary>
    /// <remarks>Suspensa é leitura, Encerrada é arquivo e Descartada foi abandonada: nenhuma aceita escrita.</remarks>
    public bool AceitaEdicao => Status is StatusDaFormatura.Rascunho or StatusDaFormatura.AguardandoPagamento or StatusDaFormatura.Ativa;

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

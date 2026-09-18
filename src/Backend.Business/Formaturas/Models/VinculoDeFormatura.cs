using Backend.Business.Abstractions;

namespace Backend.Business.Formaturas.Models;

/// <summary>
/// Liga um usuário a uma formatura, com o papel que ele exerce nela.
/// </summary>
/// <remarks>
/// O usuário vive **fora** do isolamento: um aluno pode estar em duas turmas e um presidente de
/// comissão pode gerir a formatura de dois cursos. Amarrar a identidade a uma formatura
/// obrigaria a duplicar conta, senha e e-mail — e a explicar isso ao usuário.
/// <para>
/// Herda de <see cref="Entity"/> e carrega <see cref="FormaturaId"/> explícito: listar e
/// selecionar formaturas precisa funcionar **antes** de existir a claim <c>formatura_id</c> no
/// token, o que o filtro global de <see cref="EntidadeDaFormatura"/> impediria.
/// </para>
/// </remarks>
public class VinculoDeFormatura : Entity
{
    /// <summary>Usuário vinculado.</summary>
    public Guid UsuarioId { get; set; }

    /// <summary>Formatura à qual ele pertence.</summary>
    public Guid FormaturaId { get; set; }

    /// <summary>Papel exercido. Ver <see cref="PapelNaFormatura"/>.</summary>
    public string Papel { get; set; } = PapelNaFormatura.Formando;

    /// <summary>Se o vínculo ainda vale. Desligar tira o acesso sem apagar o histórico.</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>
    /// Quando a pessoa foi desligada da turma, em UTC. Nulo em quem está na turma e em quem foi
    /// apenas <b>removido</b>.
    /// </summary>
    /// <remarks>
    /// É o que distingue os dois motivos de um vínculo inativo (Sprint 15). Remover é o erro de
    /// cadastro — convidei a pessoa errada, ela nunca aderiu, nunca deveu nada. Desligar é o fato
    /// financeiro: ela aderiu, deve, pagou parte, e agora sai.
    /// </remarks>
    public DateTime? DesligadoEm { get; private set; }

    /// <summary>Por que saiu. Ver <see cref="MotivoDeSaida"/>. Obrigatório no desligamento.</summary>
    public string? MotivoDoDesligamento { get; private set; }

    /// <summary>A justificativa por escrito, exigida só quando o motivo é <see cref="MotivoDeSaida.Outro"/>.</summary>
    public string? DetalheDoDesligamento { get; private set; }

    /// <summary>Se este vínculo foi desligado — e não apenas removido.</summary>
    public bool Desligado => DesligadoEm is not null;

    /// <summary>
    /// Até quando esta pessoa já viu o mural, em UTC. Nulo em quem nunca o abriu.
    /// </summary>
    /// <remarks>
    /// Uma data por pessoa, e não uma linha por aviso lido: o sino responde "o que foi publicado
    /// desde a última vez que você olhou", que é a pergunta que ele existe para responder. Uma
    /// tabela de leituras cresceria com turma × avisos para dar uma resposta mais fina que ninguém
    /// pediu — e, se um dia pedirem "marcar este como não lido", é ela que entra.
    /// </remarks>
    public DateTime? MuralVistoEm { get; private set; }

    /// <summary>Marca o mural como visto agora — o sino zera.</summary>
    /// <param name="agoraUtc">Momento da visita.</param>
    public void VerMural(DateTime agoraUtc) => MuralVistoEm = agoraUtc;

    /// <summary>
    /// Desliga da turma: inativa o vínculo e grava o porquê.
    /// </summary>
    /// <remarks>
    /// <b>Desligar implica inativar</b>, e é por isso que a regra mora aqui e não no service: toda
    /// consulta que já filtrava <see cref="Ativo"/> continua valendo sem ser tocada, e nenhuma
    /// esquecida volta a contar quem saiu.
    /// </remarks>
    /// <param name="motivo">Motivo da lista de <see cref="MotivoDeSaida"/>.</param>
    /// <param name="detalhe">Justificativa, quando o motivo é <see cref="MotivoDeSaida.Outro"/>.</param>
    /// <param name="agoraUtc">Instante do desligamento.</param>
    public void Desligar(string motivo, string? detalhe, DateTime agoraUtc)
    {
        Ativo = false;
        DesligadoEm = agoraUtc;
        MotivoDoDesligamento = motivo;
        DetalheDoDesligamento = motivo == MotivoDeSaida.Outro ? detalhe : null;
    }

    /// <summary>
    /// Desfaz o desligamento: o acesso volta e o vínculo deixa de estar desligado.
    /// </summary>
    /// <remarks>
    /// Não ressuscita parcela cancelada, e nem poderia daqui — a cobrança volta por lançamento
    /// novo, não por desfazer (decisão da Sprint 15).
    /// </remarks>
    public void Religar()
    {
        Ativo = true;
        DesligadoEm = null;
        MotivoDoDesligamento = null;
        DetalheDoDesligamento = null;
    }
}

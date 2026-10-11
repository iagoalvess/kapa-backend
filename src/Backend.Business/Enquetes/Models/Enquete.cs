using Backend.Business.Abstractions;

namespace Backend.Business.Enquetes.Models;

/// <summary>Os dois tipos de consulta da turma; toda escolha permanece secreta.</summary>
public enum TipoDeEnquete
{
    UnicaEscolha,
    MultiplaEscolha,
}

/// <summary>Uma consulta à turma, com prazo e base de participação congelada na publicação.</summary>
public sealed class Enquete : EntidadeDaFormatura
{
    /// <summary>A pergunta publicada.</summary>
    public string Pergunta { get; private set; } = string.Empty;

    /// <summary>Contexto em Markdown.</summary>
    public string? Descricao { get; private set; }

    /// <summary>Uma escolha ou várias.</summary>
    public TipoDeEnquete Tipo { get; private set; }

    /// <summary>Limite obrigatório na múltipla escolha.</summary>
    public int? MaximoDeEscolhas { get; private set; }

    /// <summary>Mínimo de participação escolhido pela comissão.</summary>
    public int QuorumMinimoPercentual { get; private set; }

    /// <summary>Membros aptos na publicação; sair ou anonimizar não muda o histórico.</summary>
    public int TotalAptosNaPublicacao { get; private set; }

    /// <summary>Instante do primeiro voto, preservado mesmo se todos retirarem suas respostas.</summary>
    public DateTime? PrimeiraRespostaEm { get; private set; }

    /// <summary>Prazo automático em UTC.</summary>
    public DateTime EncerraEm { get; private set; }

    /// <summary>Encerramento antecipado em UTC.</summary>
    public DateTime? EncerradaEm { get; private set; }

    /// <summary>Cancelamento em UTC.</summary>
    public DateTime? CanceladaEm { get; private set; }

    /// <summary>Autor da pergunta, sem relação com a escolha de qualquer participante.</summary>
    public Guid CriadaPorUsuarioId { get; private set; }

    /// <summary>Alternativas ordenadas da pergunta.</summary>
    public List<OpcaoDaEnquete> Opcoes { get; private set; } = [];

    /// <summary>Publica uma consulta nova.</summary>
    public static Enquete Nova(DadosDaEnquete dados, Guid autor, int aptos)
    {
        var enquete = new Enquete { CriadaPorUsuarioId = autor, TotalAptosNaPublicacao = aptos };
        enquete.Aplicar(dados);
        return enquete;
    }

    /// <summary>Estado derivado exclusivamente das datas.</summary>
    public string Situacao(DateTime agora) =>
        CanceladaEm is not null ? "Cancelada"
        : EncerradaEm is not null || EncerraEm <= agora ? "Encerrada"
        : "Aberta";

    /// <summary>Corrige o cadastro, preservando os ids das opções quando elas não mudam.</summary>
    public void Aplicar(DadosDaEnquete dados)
    {
        Pergunta = dados.Pergunta.Trim();
        Descricao = string.IsNullOrWhiteSpace(dados.Descricao) ? null : dados.Descricao.Trim();
        Tipo = dados.Tipo;
        MaximoDeEscolhas = dados.MaximoDeEscolhas;
        QuorumMinimoPercentual = dados.QuorumMinimoPercentual;
        EncerraEm = dados.EncerraEm;
        if (!Opcoes.OrderBy(o => o.Ordem).Select(o => o.Texto).SequenceEqual(dados.Opcoes.Select(o => o.Trim())))
            Opcoes = dados
                .Opcoes.Select(
                    (texto, ordem) =>
                        new OpcaoDaEnquete
                        {
                            EnqueteId = Id,
                            Texto = texto.Trim(),
                            Ordem = ordem,
                        }
                )
                .ToList();
    }

    /// <summary>Trava as regras ao receber a primeira resposta.</summary>
    public void RegistrarResposta(DateTime agora) => PrimeiraRespostaEm ??= agora;

    /// <summary>Encerra antes do prazo.</summary>
    public void Encerrar(DateTime agora) => EncerradaEm = agora;

    /// <summary>Cancela sem apagar respostas.</summary>
    public void Cancelar(DateTime agora) => CanceladaEm = agora;
}

/// <summary>Uma alternativa da pergunta, sem contador de votos.</summary>
public sealed class OpcaoDaEnquete : EntidadeDaFormatura
{
    /// <summary>Consulta dona.</summary>
    public Guid EnqueteId { get; set; }

    /// <summary>Texto visível.</summary>
    public string Texto { get; set; } = string.Empty;

    /// <summary>Posição na pergunta.</summary>
    public int Ordem { get; set; }
}

/// <summary>Uma resposta por vínculo; anonimizar remove a identificação e preserva as escolhas.</summary>
public sealed class RespostaDaEnquete : EntidadeDaFormatura
{
    /// <summary>Identificador aleatório, sem o instante do voto embutido.</summary>
    public RespostaDaEnquete() => Id = Guid.NewGuid();

    /// <summary>Remove vínculo e datas originais que poderiam correlacionar o voto à participação.</summary>
    public void Anonimizar()
    {
        VinculoId = null;
        RespondidaEm = DateTime.UnixEpoch;
        CriadoEm = DateTime.UnixEpoch;
        foreach (var escolha in Escolhas)
            escolha.AnonimizarMetadados();
    }

    /// <summary>Consulta respondida.</summary>
    public Guid EnqueteId { get; set; }

    /// <summary>Participante; nulo somente após anonimização.</summary>
    public Guid? VinculoId { get; set; }

    /// <summary>Último envio em UTC.</summary>
    public DateTime RespondidaEm { get; set; }

    /// <summary>Opções escolhidas pelo próprio titular.</summary>
    public List<EscolhaDaResposta> Escolhas { get; set; } = [];
}

/// <summary>Uma alternativa marcada numa resposta.</summary>
public sealed class EscolhaDaResposta : EntidadeDaFormatura
{
    /// <summary>Identificador aleatório, sem instante embutido.</summary>
    public EscolhaDaResposta() => Id = Guid.NewGuid();

    /// <summary>Apaga a data original da escolha ao anonimizar seu titular.</summary>
    public void AnonimizarMetadados() => CriadoEm = DateTime.UnixEpoch;

    /// <summary>Resposta dona.</summary>
    public Guid RespostaId { get; set; }

    /// <summary>Alternativa escolhida.</summary>
    public Guid OpcaoId { get; set; }
}

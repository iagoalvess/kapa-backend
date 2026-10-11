using Backend.Business.Abstractions;
using Backend.Business.Enquetes.Models;

namespace Backend.Business.Enquetes.Interfaces;

/// <summary>Consultas e respostas da turma, sem e-mail e com sigilo fixo.</summary>
public interface IEnqueteService
{
    /// <summary>Lista com estado e a resposta do próprio solicitante.</summary>
    Task<Result<IReadOnlyList<EnqueteResumo>>> Listar(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Abre a pergunta e seu resultado agregado.</summary>
    Task<Result<EnqueteDetalhe>> Obter(Guid id, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Publica uma pergunta com prazo.</summary>
    Task<Result<EnqueteDetalhe>> Criar(DadosDaEnquete dados, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Corrige dentro das regras travadas no primeiro voto.</summary>
    Task<Result<EnqueteDetalhe>> Atualizar(Guid id, DadosDaEnquete dados, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Encerra antecipadamente.</summary>
    Task<Result> Encerrar(Guid id, CancellationToken ct = default);

    /// <summary>Cancela preservando as respostas.</summary>
    Task<Result> Cancelar(Guid id, CancellationToken ct = default);

    /// <summary>Envia ou substitui a resposta do titular.</summary>
    Task<Result> Responder(Guid id, Guid usuarioId, IReadOnlyList<Guid> opcoes, CancellationToken ct = default);

    /// <summary>Retira a resposta enquanto aberta.</summary>
    Task<Result> Retirar(Guid id, Guid usuarioId, CancellationToken ct = default);
}

/// <summary>Persistência e resultados da consulta, isolados pelo filtro da formatura.</summary>
public interface IEnqueteRepository
{
    /// <summary>Vínculo ativo e requisitos do próprio usuário.</summary>
    Task<MembroParaEnquete?> ObterMembro(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Quantidade apta no instante de publicação.</summary>
    Task<int> ContarAptos(CancellationToken ct = default);

    /// <summary>Lista e contagens em consultas em lote, sem N+1.</summary>
    Task<IReadOnlyList<EnqueteComContagem>> Listar(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Uma pergunta, resultado agregado e resposta do titular.</summary>
    Task<EnqueteComContagem?> Obter(Guid id, Guid vinculoId, CancellationToken ct = default);

    /// <summary>Alternativas com contagem, nunca as respostas identificadas.</summary>
    Task<IReadOnlyList<OpcaoComResultado>> Resultados(Guid id, int respondentes, CancellationToken ct = default);

    /// <summary>Nomes de quem participou, sem id da resposta ou data que permita associar à escolha.</summary>
    Task<IReadOnlyList<ParticipanteDaEnquete>> Participantes(Guid id, CancellationToken ct = default);

    /// <summary>Trava a consulta para serializar respostas, correções e encerramento.</summary>
    Task<Enquete?> Travar(Guid id, CancellationToken ct = default);

    /// <summary>Resposta rastreada do próprio vínculo.</summary>
    Task<RespostaDaEnquete?> ObterResposta(Guid id, Guid vinculoId, CancellationToken ct = default);

    /// <summary>Marca a nova pergunta para inclusão.</summary>
    Task Adicionar(Enquete enquete, CancellationToken ct = default);

    /// <summary>Remove as alternativas antigas antes da correção sem votos.</summary>
    void RemoverOpcoes(IReadOnlyList<OpcaoDaEnquete> opcoes);

    /// <summary>Marca alternativas corrigidas para inclusão explícita.</summary>
    void AdicionarOpcoes(IReadOnlyList<OpcaoDaEnquete> opcoes);

    /// <summary>Marca uma resposta nova para inclusão.</summary>
    Task AdicionarResposta(RespostaDaEnquete resposta, CancellationToken ct = default);

    /// <summary>Retira as escolhas substituídas.</summary>
    void RemoverEscolhas(IReadOnlyList<EscolhaDaResposta> escolhas);

    /// <summary>Marca as escolhas novas para inclusão, inclusive na substituição.</summary>
    void AdicionarEscolhas(IReadOnlyList<EscolhaDaResposta> escolhas);

    /// <summary>Retira a resposta e suas escolhas.</summary>
    void RemoverResposta(RespostaDaEnquete resposta);
}

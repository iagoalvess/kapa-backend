using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;

namespace Backend.Business.Adesoes.Interfaces;

/// <summary>
/// Termos e adesões da formatura selecionada.
/// </summary>
/// <remarks>
/// Não há método para alterar nem remover: termo e adesão são append-only, e a ausência do método é
/// a primeira barreira — o gatilho no banco é a segunda. Isolados pelo filtro global; o que parte do
/// vínculo (o painel) leva a formatura explícita.
/// </remarks>
public interface IAdesaoRepository
{
    /// <summary>A versão mais recente do termo; nula se a turma ainda não publicou.</summary>
    Task<VersaoDoTermo?> ObterTermoVigente(CancellationToken ct = default);

    /// <summary>As versões publicadas, da mais nova para a mais antiga, com quantos aceitaram cada uma.</summary>
    Task<IReadOnlyList<TermoPublicado>> ListarTermos(CancellationToken ct = default);

    /// <summary>Marca uma versão nova do termo para inclusão.</summary>
    /// <param name="termo">Versão.</param>
    Task AdicionarTermo(TermoDaFormatura termo, CancellationToken ct = default);

    /// <summary>Uma versão do termo da turma, com o texto; nula se não existir aqui.</summary>
    /// <param name="termoId">Versão.</param>
    Task<VersaoDoTermo?> ObterTermo(Guid termoId, CancellationToken ct = default);

    /// <summary>O resumo gerado por IA de uma versão (Sprint 24); nulo enquanto não houver.</summary>
    /// <param name="termoId">Versão — já lida pelo filtro da turma.</param>
    Task<string?> ObterResumo(Guid termoId, CancellationToken ct = default);

    /// <summary>
    /// Versões vigentes, publicadas a partir de <paramref name="publicadosDesde"/>, sem resumo — de
    /// todas as turmas ativas, as mais antigas primeiro.
    /// </summary>
    /// <remarks>
    /// Atravessa formaturas porque quem pergunta é o job, antes de apontar escopo para qualquer uma.
    /// Devolve só ids: a geração acontece depois, num escopo apontado para a turma do termo. Só a
    /// versão mais recente de cada turma entra — resumir a v1 já substituída pela v2 gastaria cota
    /// num texto que ninguém mais vai ler.
    /// </remarks>
    /// <param name="publicadosDesde">Janela: o que ficou de fora dela o job não tenta mais (decisão 5).</param>
    /// <param name="limite">Teto de termos devolvidos.</param>
    Task<IReadOnlyList<TermoSemResumo>> ListarTermosSemResumoDeTodasAsFormaturas(
        DateTime publicadosDesde,
        int limite,
        CancellationToken ct = default
    );

    /// <summary>Marca um resumo para inclusão.</summary>
    /// <param name="resumo">Resumo de uma versão.</param>
    Task AdicionarResumo(ResumoDoTermo resumo, CancellationToken ct = default);

    /// <summary>Se o vínculo já aceitou esta versão.</summary>
    /// <param name="vinculoId">Vínculo.</param>
    /// <param name="termoId">Versão do termo.</param>
    Task<bool> JaAderiu(Guid vinculoId, Guid termoId, CancellationToken ct = default);

    /// <summary>Se o vínculo aderiu a alguma versão do termo.</summary>
    /// <remarks>
    /// É o que separa Desligar de Remover (decisão 1 da Sprint 15): quem aderiu deve, e sai pela
    /// porta que cancela dívida; quem nunca aderiu é erro de cadastro, e sai pela que não mexe em
    /// dinheiro nenhum. Qualquer versão serve — a pessoa pode ter aderido à v1 e não à v2 vigente,
    /// e a dívida da v1 continua sendo dela.
    /// </remarks>
    /// <param name="vinculoId">Vínculo.</param>
    Task<bool> JaAderiuAlgumaVez(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Se outro vínculo da turma já aderiu com este CPF.</summary>
    /// <remarks>Compara pelo HMAC: o CPF gravado é cifrado com nonce aleatório e não admite busca.</remarks>
    /// <param name="cpf">CPF, só os dígitos.</param>
    /// <param name="vinculoId">Quem está aderindo agora, que não conflita consigo mesmo.</param>
    Task<bool> CpfEmUsoPorOutro(string cpf, Guid vinculoId, CancellationToken ct = default);

    /// <summary>Marca uma adesão para inclusão, com o HMAC do CPF.</summary>
    /// <param name="adesao">Adesão.</param>
    Task Adicionar(AdesaoDoFormando adesao, CancellationToken ct = default);

    /// <summary>A adesão mais recente do vínculo, com o texto aceito; nula se ele nunca aderiu.</summary>
    /// <param name="vinculoId">Vínculo.</param>
    Task<AdesaoComTermo?> ObterUltimaDoVinculo(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Uma adesão da turma, com o texto aceito; nula se não existir aqui.</summary>
    /// <param name="adesaoId">Adesão.</param>
    Task<AdesaoComTermo?> Obter(Guid adesaoId, CancellationToken ct = default);

    /// <summary>Uma página dos membros ativos com a adesão mais recente de cada um: quem aderiu e quem falta.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Situação e busca.</param>
    Task<PaginaDe<SituacaoDeAdesao>> ListarSituacoes(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeAdesoes filtro,
        CancellationToken ct = default
    );

    /// <summary>Membros ativos e quantos deles aderiram.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    Task<(int Membros, int Aderiram)> Contar(Guid formaturaId, CancellationToken ct = default);
}

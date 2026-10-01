using Backend.Business.Abstractions;

namespace Backend.Business.Adesoes.Models;

/// <summary>
/// Uma versão publicada do termo de adesão da turma, escrito pela comissão.
/// </summary>
/// <remarks>
/// A mesma máquina dos documentos legais da Sprint 1 (<c>DocumentoLegal</c>): não existe "editar o
/// termo", existe publicar a versão seguinte, e o banco recusa <c>UPDATE</c> e <c>DELETE</c>. A
/// diferença é o dono — o termo pertence à formatura, e por isso herda de
/// <see cref="EntidadeDaFormatura"/> (o <c>AtualizadoEm</c> herdado nunca muda).
/// <para>
/// Versões coexistem: cada adesão aponta para a sua, e publicar a v2 não mexe em quem aceitou a v1.
/// </para>
/// </remarks>
public class TermoDaFormatura : EntidadeDaFormatura
{
    /// <summary>Número da versão na turma, a partir de 1.</summary>
    public int Versao { get; init; }

    /// <summary>Texto integral, em markdown.</summary>
    public string Conteudo { get; init; } = string.Empty;

    /// <summary>A partir de quando vale, em UTC. É o instante da publicação.</summary>
    public DateTime VigenteDesde { get; init; }
}

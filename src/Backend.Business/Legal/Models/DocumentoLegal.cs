namespace Backend.Business.Legal.Models;

/// <summary>
/// Uma versão publicada de um documento legal da plataforma.
/// </summary>
/// <remarks>
/// Não existe "editar os termos": existe publicar a versão seguinte. Por isso as propriedades
/// são <c>init</c> e a classe não herda de <c>Entity</c> — um <c>AtualizadoEm</c> aqui seria a
/// promessa de uma alteração que não pode acontecer. O banco reforça: um gatilho recusa
/// <c>UPDATE</c> e <c>DELETE</c> na tabela.
/// <para>
/// Pertence à <b>plataforma</b>, não à turma — não herda de <c>EntidadeDaFormatura</c>. O termo
/// de adesão da turma, escrito pela comissão, é outra coisa (Sprint 7).
/// </para>
/// </remarks>
public class DocumentoLegal
{
    /// <summary>Identificador da versão.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Qual documento. Ver <see cref="TipoDeDocumento"/>.</summary>
    public string Tipo { get; init; } = string.Empty;

    /// <summary>Rótulo da versão, único por tipo (<c>1</c>, <c>2</c>…).</summary>
    public string Versao { get; init; } = string.Empty;

    /// <summary>Texto integral, em markdown.</summary>
    public string Conteudo { get; init; } = string.Empty;

    /// <summary>
    /// A partir de quando esta versão vale, em UTC.
    /// </summary>
    /// <remarks>
    /// Pode estar no futuro: publicar com antecedência dá tempo de avisar antes de a versão nova
    /// passar a ser exigida.
    /// </remarks>
    public DateTime VigenteDesde { get; init; }
}

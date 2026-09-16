using Backend.Business.Abstractions;

namespace Backend.Business.Comunicacao.Models;

/// <summary>
/// Arquivo do acervo: ata, contrato, orçamento — o que não pode se perder na rolagem do grupo.
/// </summary>
/// <remarks>
/// Aviso se lê, documento se baixa (decisão 3). Os bytes, o nome e o tamanho do arquivo moram no
/// módulo de arquivos; aqui fica o que a turma precisa para achar e autorizar: título, categoria e
/// visibilidade.
/// <para>
/// ponytail: substituir troca o arquivo e soma um em <see cref="Versao"/>, com o antes e o depois na
/// auditoria — sem histórico navegável (fora de escopo). Se pedirem, o caminho é uma tabela de
/// versões apontando para os arquivos antigos, que hoje são apagados.
/// </para>
/// </remarks>
public class Documento : EntidadeDaFormatura
{
    /// <summary>Teto do arquivo, em megabytes — abaixo do teto geral de arquivos: o acervo não é backup.</summary>
    public const int TamanhoMaximoEmMB = 20;

    /// <summary>Teto em bytes.</summary>
    public const long TamanhoMaximoEmBytes = TamanhoMaximoEmMB * 1024L * 1024L;

    /// <summary>Como a turma chama o documento ("Contrato do buffet").</summary>
    public string Titulo { get; private set; } = string.Empty;

    /// <summary>Gaveta do acervo.</summary>
    public CategoriaDeDocumento Categoria { get; private set; }

    /// <summary>O arquivo atual, no módulo de arquivos.</summary>
    public Guid ArquivoId { get; private set; }

    /// <summary>Para quem aparece.</summary>
    public Visibilidade Visibilidade { get; private set; } = Visibilidade.Turma;

    /// <summary>Quantas vezes o arquivo já foi enviado: 1 no primeiro, mais um a cada substituição.</summary>
    public int Versao { get; private set; } = 1;

    /// <summary>Um documento novo, na versão 1.</summary>
    /// <param name="dados">Dados já validados.</param>
    /// <param name="arquivoId">Arquivo já gravado.</param>
    public static Documento Novo(DadosDoDocumento dados, Guid arquivoId)
    {
        var documento = new Documento { ArquivoId = arquivoId };

        documento.Aplicar(dados);

        return documento;
    }

    /// <summary>Corrige título, categoria e visibilidade.</summary>
    /// <param name="dados">Dados já validados.</param>
    public void Aplicar(DadosDoDocumento dados)
    {
        Titulo = dados.Titulo.Trim();
        Categoria = dados.Categoria!.Value;
        Visibilidade = dados.Visibilidade!.Value;
    }

    /// <summary>Troca o arquivo pela versão seguinte.</summary>
    /// <param name="arquivoId">Arquivo novo, já gravado.</param>
    public void Substituir(Guid arquivoId)
    {
        ArquivoId = arquivoId;
        Versao++;
    }
}

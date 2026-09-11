namespace Backend.Business.Arquivos.Settings;

/// <summary>Onde os bytes dos arquivos ficam guardados.</summary>
public enum EProvedorDeArmazenamento
{
    /// <summary>Disco do processo. Para desenvolvimento.</summary>
    Local = 0,

    /// <summary>Amazon S3 ou qualquer serviço compatível (MinIO, Cloudflare R2, Backblaze B2).</summary>
    S3 = 1,
}

/// <summary>
/// Configuração de armazenamento de arquivos.
/// </summary>
/// <remarks>
/// <b>O provedor Local não serve para produção.</b> Disco de container é efêmero: o arquivo some
/// no próximo deploy, e com mais de uma réplica cada uma enxerga um conjunto diferente de
/// arquivos. Ele existe para desenvolvimento e testes.
/// </remarks>
public sealed class ArmazenamentoSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Armazenamento";

    /// <summary>Provedor em uso.</summary>
    public EProvedorDeArmazenamento Provedor { get; init; } = EProvedorDeArmazenamento.Local;

    /// <summary>Diretório raiz do provedor local, relativo ao diretório da aplicação.</summary>
    public string CaminhoLocal { get; init; } = "arquivos";

    /// <summary>Tamanho máximo aceito por arquivo, em megabytes.</summary>
    public int TamanhoMaximoEmMB { get; init; } = 25;

    /// <summary>
    /// Extensões aceitas, em minúsculas e com ponto.
    /// </summary>
    /// <remarks>
    /// É uma **lista de permissão**, e não de bloqueio, de propósito: lista de bloqueio sempre
    /// esquece alguma extensão executável, e basta uma para transformar o upload em execução
    /// remota de código.
    /// </remarks>
    public string[] ExtensoesPermitidas { get; init; } = [".pdf", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".csv", ".txt", ".xlsx", ".docx"];

    /// <summary>
    /// Espaço total que um usuário pode ocupar, em megabytes. <c>0</c> desliga a cota.
    /// </summary>
    /// <remarks>
    /// Limitar o tamanho de **cada** arquivo não limita coisa nenhuma: com 25 MB por envio e 120
    /// requisições por minuto, um único usuário autenticado escreve gigabytes por minuto até
    /// acabar o disco — ou a fatura do bucket. O teto por arquivo protege a memória do processo;
    /// quem protege o armazenamento é esta cota.
    /// </remarks>
    public int CotaPorUsuarioEmMB { get; init; } = 500;

    /// <summary>
    /// Quantidade máxima de arquivos por usuário. <c>0</c> desliga o limite.
    /// </summary>
    /// <remarks>
    /// Existe junto da cota de espaço porque os dois abusos são diferentes: milhões de arquivos
    /// de 1 KB cabem folgados em 500 MB e ainda assim inutilizam a listagem e a fatura de
    /// requisições do provedor.
    /// </remarks>
    public int MaximoDeArquivosPorUsuario { get; init; } = 200;

    /// <summary>Configuração do provedor S3.</summary>
    public S3Settings S3 { get; init; } = new();

    /// <summary>Tamanho máximo em bytes.</summary>
    public long TamanhoMaximoEmBytes => TamanhoMaximoEmMB * 1024L * 1024L;

    /// <summary>Cota por usuário em bytes.</summary>
    public long CotaPorUsuarioEmBytes => CotaPorUsuarioEmMB * 1024L * 1024L;
}

/// <summary>
/// Configuração do provedor S3.
/// </summary>
/// <remarks>
/// Não há campo de chave de acesso aqui de propósito. As credenciais vêm da cadeia padrão do SDK
/// da AWS — perfil de instância, role do IRSA no Kubernetes, ou as variáveis
/// <c>AWS_ACCESS_KEY_ID</c> e <c>AWS_SECRET_ACCESS_KEY</c>. Credencial em arquivo de configuração
/// da aplicação é credencial que acaba versionada.
/// </remarks>
public sealed class S3Settings
{
    /// <summary>Nome do bucket.</summary>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>Região, por exemplo <c>sa-east-1</c>.</summary>
    public string Regiao { get; init; } = string.Empty;

    /// <summary>Prefixo aplicado a todas as chaves, para dividir um bucket entre ambientes.</summary>
    public string Prefixo { get; init; } = string.Empty;

    /// <summary>
    /// Endpoint alternativo, para serviços compatíveis com S3 (MinIO, R2, B2). Vazio usa a AWS.
    /// </summary>
    public string ServiceUrl { get; init; } = string.Empty;
}

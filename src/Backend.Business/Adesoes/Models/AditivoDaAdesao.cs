using Backend.Business.Abstractions;

namespace Backend.Business.Adesoes.Models;

/// <summary>
/// Prova de que um formando aceitou mudar a cesta depois da adesão — o aditivo contratual (Sprint 48, D7/D38).
/// </summary>
/// <remarks>
/// Uma adesão nova que só acrescenta a diferença: o contrato passa a ser o original mais os aditivos, cada um com o
/// próprio snapshot e o próprio hash. É tabela própria, e não uma linha a mais em <see cref="AdesaoDoFormando"/>,
/// porque a adesão é uma por versão do termo — e o aditivo acontece sobre a mesma versão.
/// <para>
/// O rito é o da adesão (D38): código no e-mail, hash sobre o termo e o snapshot, IP e User-Agent. Imutável como ela:
/// as propriedades são <c>init</c>, e o banco recusa <c>UPDATE</c> e <c>DELETE</c> na tabela.
/// </para>
/// </remarks>
public class AditivoDaAdesao : EntidadeDaFormatura
{
    /// <summary>Vínculo de quem aceitou.</summary>
    public Guid VinculoId { get; init; }

    /// <summary>A adesão que o aditivo emenda — a mais recente do vínculo no dia.</summary>
    public Guid AdesaoId { get; init; }

    /// <summary>SHA-256 do termo e do snapshot do aditivo, em hexadecimal minúsculo (<see cref="AdesaoDoFormando.CalcularHash"/>).</summary>
    public string HashDoConteudo { get; init; } = string.Empty;

    /// <summary>Momento do aceite, em UTC.</summary>
    public DateTime AceitoEm { get; init; }

    /// <summary>IP de onde veio o aceite.</summary>
    public string EnderecoIp { get; init; } = string.Empty;

    /// <summary>Navegador que enviou o aceite.</summary>
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>E-mail para onde foi o código conferido neste aceite.</summary>
    public string EmailDoAceite { get; init; } = string.Empty;

    /// <summary>JSON do <see cref="SnapshotDoAditivo"/> aceito, exatamente como entrou no hash.</summary>
    public string Conteudo { get; init; } = string.Empty;

    /// <summary>O aditivo aceito, lido do JSON gravado.</summary>
    public SnapshotDoAditivo Ler() => SnapshotDoAditivo.Ler(Conteudo);
}

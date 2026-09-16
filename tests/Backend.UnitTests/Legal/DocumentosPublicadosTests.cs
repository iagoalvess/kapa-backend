using System.Security.Cryptography;
using System.Text;
using Backend.Data.Seed;
using Shouldly;

namespace Backend.UnitTests.Legal;

/// <summary>
/// Trava o texto de cada versão já publicada.
/// </summary>
/// <remarks>
/// O banco recusa alterar um documento publicado, mas não enxerga o repositório: editar
/// <c>TermosDeUso.1.md</c> faria todo banco criado depois disso publicar um texto diferente do
/// que as pessoas aceitaram, sob o mesmo rótulo de versão. Mudou o texto? É versão nova — arquivo
/// novo, migration nova, e uma linha nova aqui.
/// <para>
/// Enquanto a v1 não estiver em produção (os marcadores <c>[RAZÃO SOCIAL]</c> ainda estão lá),
/// editar a v1 é legítimo: atualize o hash abaixo e recrie os bancos de desenvolvimento.
/// </para>
/// </remarks>
public sealed class DocumentosPublicadosTests
{
    [Theory]
    [InlineData("TermosDeUso", "1", "63dc86e734c17602cc908048c90c5534a940c8454ce14da5dcfade477ec06fb4")]
    [InlineData("PoliticaDePrivacidade", "1", "3c19d02f6c293fb4ec3d78462a88a9287e6e169d9f8ae3545c7abc4d6b65a7f6")]
    public void Texto_publicado_nao_muda(string tipo, string versao, string hashEsperado)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DocumentosLegais.Ler(tipo, versao))));

        hash.ShouldBe(hashEsperado, $"{tipo}.{versao}.md mudou depois de publicado. Publique uma versão nova.");
    }
}

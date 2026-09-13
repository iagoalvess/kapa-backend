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
    [InlineData("TermosDeUso", "1", "b37049b9962c2b925431ae69b36d2f922ee1ac143010f8d0ab50bd2090c1fe5a")]
    [InlineData("PoliticaDePrivacidade", "1", "d9d8d23eaabc07aa4d7f3bff73c9ee0d295a078a7ebc7feca494c14cecd918a7")]
    public void Texto_publicado_nao_muda(string tipo, string versao, string hashEsperado)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DocumentosLegais.Ler(tipo, versao))));

        hash.ShouldBe(hashEsperado, $"{tipo}.{versao}.md mudou depois de publicado. Publique uma versão nova.");
    }
}

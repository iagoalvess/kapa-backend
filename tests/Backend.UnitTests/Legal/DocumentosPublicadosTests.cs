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
/// Enquanto a v1 não estiver em produção,
/// editar a v1 é legítimo: atualize o hash abaixo e recrie os bancos de desenvolvimento.
/// </para>
/// </remarks>
public sealed class DocumentosPublicadosTests
{
    [Theory]
    [InlineData("TermosDeUso", "1", "fe4fdc8e5016a996532067c4ec1beac14d29f016c682a7bfba5897f558f7c93c")]
    [InlineData("PoliticaDePrivacidade", "1", "cad80aab7806093e7619ba3ee6cd938fce906d2e70e82af864d0e5a8a236690c")]
    public void Texto_publicado_nao_muda(string tipo, string versao, string hashEsperado)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DocumentosLegais.Ler(tipo, versao))));

        hash.ShouldBe(hashEsperado, $"{tipo}.{versao}.md mudou depois de publicado. Publique uma versão nova.");
    }
}

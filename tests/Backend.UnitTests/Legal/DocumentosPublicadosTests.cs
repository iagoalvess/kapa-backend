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
    [InlineData("TermosDeUso", "1", "89651043dd0c7483145c43113aaa1803dee8ca2587700dfea8c73a148c31e92c")]
    [InlineData("PoliticaDePrivacidade", "1", "4155626537e5d207c0a6caed1436f14b87d7718f347c2195047930ff2e28cc21")]
    public void Texto_publicado_nao_muda(string tipo, string versao, string hashEsperado)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DocumentosLegais.Ler(tipo, versao))));

        hash.ShouldBe(hashEsperado, $"{tipo}.{versao}.md mudou depois de publicado. Publique uma versão nova.");
    }
}

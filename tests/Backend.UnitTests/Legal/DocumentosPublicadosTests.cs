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
    [InlineData("TermosDeUso", "1", "ca211e91e7d3d4c6db3e0ed9e5d436e1bcfd6586782dbe2085c190cdae626cc1")]
    [InlineData("PoliticaDePrivacidade", "1", "c2bde3dc7ef549129ad40e288f1fa46791db9f0f0ef4220e214c66bea5bb72e6")]
    public void Texto_publicado_nao_muda(string tipo, string versao, string hashEsperado)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DocumentosLegais.Ler(tipo, versao))));

        hash.ShouldBe(hashEsperado, $"{tipo}.{versao}.md mudou depois de publicado. Publique uma versão nova.");
    }
}

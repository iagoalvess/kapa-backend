using System.Globalization;
using System.Text;
using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Services;
using Shouldly;

namespace Backend.UnitTests.Arquivos;

/// <summary>
/// A URL temporária do provedor local vale até o prazo e só com a assinatura intacta; e os primeiros
/// bytes de um arquivo têm de bater com a extensão.
/// </summary>
public sealed class UrlTemporariaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly UrlTemporariaLocal _urls = new();

    /// <summary>Lê a query string como o model binding faria.</summary>
    private static ObjetoTemporario Ler(string url)
    {
        var campos = url[(url.IndexOf('?') + 1)..]
            .Split('&')
            .Select(par => par.Split('=', 2))
            .ToDictionary(par => par[0], par => Uri.UnescapeDataString(par[1]));

        return new ObjetoTemporario(
            campos["chave"],
            campos["nome"],
            campos["tipo"],
            long.Parse(campos["expira"], CultureInfo.InvariantCulture),
            campos["assinatura"]
        );
    }

    private string Gerar(DateTimeOffset expiraEm) => _urls.Gerar("documentos/2026/09/abc.pdf", "Contrato & anexo.pdf", "application/pdf", expiraEm);

    [Fact]
    public void Aponta_para_o_endpoint_da_api_e_confere_dentro_do_prazo()
    {
        var url = Gerar(Agora.AddMinutes(5));

        url.ShouldStartWith(UrlTemporariaLocal.Caminho + "?");
        _urls.Conferir(Ler(url), Agora).ShouldBeTrue();
        Ler(url).Nome.ShouldBe("Contrato & anexo.pdf");
    }

    /// <summary>Critério de aceite: expira em minutos e não é reutilizável depois disso.</summary>
    [Fact]
    public void Depois_do_prazo_nao_serve_mais()
    {
        var objeto = Ler(Gerar(Agora.AddMinutes(5)));

        _urls.Conferir(objeto, Agora.AddMinutes(5)).ShouldBeFalse();
        _urls.Conferir(objeto, Agora.AddHours(1)).ShouldBeFalse();
    }

    [Fact]
    public void Trocar_a_chave_ou_esticar_o_prazo_quebra_a_assinatura()
    {
        var objeto = Ler(Gerar(Agora.AddMinutes(5)));

        _urls.Conferir(objeto with { Chave = "documentos/2026/09/outro.pdf" }, Agora).ShouldBeFalse();
        _urls.Conferir(objeto with { Expira = Agora.AddDays(30).ToUnixTimeSeconds() }, Agora).ShouldBeFalse();
        _urls.Conferir(objeto with { Assinatura = null }, Agora).ShouldBeFalse();
    }

    /// <summary>O segredo é do processo: a URL de outra instância (outro processo, outro reinício) não confere.</summary>
    [Fact]
    public void Url_de_outra_instancia_nao_confere()
    {
        var deOutra = Ler(new UrlTemporariaLocal().Gerar("a.pdf", "a.pdf", "application/pdf", Agora.AddMinutes(5)));

        _urls.Conferir(deOutra, Agora).ShouldBeFalse();
    }

    [Theory]
    [InlineData(".pdf", "%PDF-1.4")]
    [InlineData(".PDF", "%PDF-1.7")]
    [InlineData(".gif", "GIF89a")]
    public void Conteudo_de_texto_que_bate_com_a_extensao_confere(string extensao, string inicio) =>
        ConteudoDeArquivo.Confere(extensao, Encoding.ASCII.GetBytes(inicio)).ShouldBeTrue();

    [Fact]
    public void Assinaturas_binarias_conferem_com_a_propria_extensao()
    {
        ConteudoDeArquivo.Confere(".png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]).ShouldBeTrue();
        ConteudoDeArquivo.Confere(".jpeg", [0xFF, 0xD8, 0xFF, 0xE0]).ShouldBeTrue();
        ConteudoDeArquivo.Confere(".docx", [0x50, 0x4B, 0x03, 0x04, 0x14]).ShouldBeTrue();
        ConteudoDeArquivo.Confere(".webp", "RIFF\0\0\0\0WEBPVP8 "u8).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".png")]
    [InlineData(".xlsx")]
    public void Executavel_renomeado_nao_confere(string extensao) =>
        ConteudoDeArquivo.Confere(extensao, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]).ShouldBeFalse();

    /// <summary>
    /// Texto puro não tem assinatura, então não há o que contradizer: qualquer conteúdo confere.
    /// </summary>
    /// <remarks>
    /// Quem decide que <c>.txt</c> pode entrar é a lista de permissão; aqui só se responde se o
    /// conteúdo desmente a extensão. E o download devolve <c>text/plain</c> como anexo, não como
    /// página.
    /// </remarks>
    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    public void Texto_puro_confere_com_qualquer_conteudo(string extensao) =>
        ConteudoDeArquivo.Confere(extensao, [0x4D, 0x5A, 0x90, 0x00]).ShouldBeTrue();

    /// <summary>Extensão que este arquivo não conhece é recusada: acrescentar uma deve falhar alto.</summary>
    [Fact]
    public void Extensao_sem_assinatura_mapeada_nao_confere() => ConteudoDeArquivo.Confere(".xyz", "qualquer coisa"u8).ShouldBeFalse();

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".txt")]
    public void Arquivo_vazio_nao_confere(string extensao) => ConteudoDeArquivo.Confere(extensao, []).ShouldBeFalse();

    [Fact]
    public async Task Conferir_o_fluxo_devolve_a_leitura_ao_comeco()
    {
        using var fluxo = new MemoryStream("%PDF-1.4 resto do arquivo"u8.ToArray());

        (await ConteudoDeArquivo.ConfereAsync("ata.pdf", fluxo, TestContext.Current.CancellationToken)).ShouldBeTrue();

        fluxo.Position.ShouldBe(0);
    }
}

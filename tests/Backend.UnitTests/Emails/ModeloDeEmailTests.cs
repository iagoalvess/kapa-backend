using Backend.Business.Emails.Services;
using Shouldly;

namespace Backend.UnitTests.Emails;

/// <summary>
/// Cobre o elo entre o corpo e o anexo: o <c>cid</c> que o HTML cita e o PNG que o assembly carrega.
/// </summary>
/// <remarks>
/// É a única parte do modelo que quebra em silêncio. Renomear o arquivo do mascote, mudar o
/// <c>RootNamespace</c> ou tirar o <c>EmbeddedResource</c> do csproj não quebra compilação nenhuma —
/// quebra a imagem no topo de toda mensagem, e isso só aparece na caixa de quem recebeu.
/// </remarks>
public sealed class ModeloDeEmailTests
{
    [Theory]
    [InlineData(Mascote.Feliz)]
    [InlineData(Mascote.Acenando)]
    [InlineData(Mascote.Alerta)]
    [InlineData(Mascote.Binoculo)]
    [InlineData(Mascote.Canudo)]
    [InlineData(Mascote.Celular)]
    [InlineData(Mascote.Checklist)]
    [InlineData(Mascote.Cofrinho)]
    [InlineData(Mascote.Documento)]
    [InlineData(Mascote.Erro)]
    [InlineData(Mascote.Foguete)]
    [InlineData(Mascote.Lendo)]
    [InlineData(Mascote.Lupa)]
    public void Todo_mascote_tem_um_png_embutido(Mascote mascote)
    {
        var corpo = ModeloDeEmail.Montar("Kapa", "Título", "Mensagem", "Botão", "https://kapa.dev", mascote);

        var imagens = ModeloDeEmail.ImagensDe(corpo).ToList();

        imagens.Select(imagem => imagem.Cid).ShouldBe(["kapa-logo", $"kapa-{mascote.ToString().ToLowerInvariant()}"], ignoreOrder: true);
        imagens.ShouldAllBe(imagem => imagem.Conteudo.Length > 0);
    }

    [Fact]
    public void O_corpo_nao_manda_ninguem_colar_endereco_no_navegador()
    {
        var corpo = ModeloDeEmail.Montar("Kapa", "Título", "Mensagem", "Botão", "https://kapa.dev/confirmar?token=abc");

        corpo.ShouldContain("""<a href="https://kapa.dev/confirmar?token=abc" style=""");
        corpo.ShouldNotContain("copie e cole");
    }

    [Fact]
    public void Sem_botao_o_corpo_sai_sem_link()
    {
        var corpo = ModeloDeEmail.Montar("Kapa", "Sua senha foi alterada", "Mensagem", botao: null, link: null, Mascote.Alerta);

        corpo.ShouldNotContain("<a href");
        corpo.ShouldContain("cid:kapa-alerta");
    }
}

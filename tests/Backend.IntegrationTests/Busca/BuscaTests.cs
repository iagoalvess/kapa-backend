using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Busca;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Shouldly;

namespace Backend.IntegrationTests.Busca;

/// <summary>
/// A busca do topo: quem enxerga o quê.
/// </summary>
/// <remarks>
/// O recorte por papel acontece <b>dentro</b> da consulta, e não em cinco endpoints com cinco
/// políticas — é o tipo de decisão que nenhum compilador confere. Daí a fronteira estar coberta
/// aqui: um formando que recebesse a lista de membros pela busca teria a lista de membros.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class BuscaTests(ApiFactory fabrica)
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj <<>> endobj\ntrailer <<>>\n%%EOF"u8.ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Busca_exige_login()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/api/v1/busca?termo=ana", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A Gestão procura gente: a lista de membros é dela.
    /// </summary>
    /// <remarks>
    /// O termo vai <b>sem acento</b> de propósito: todo usuário de teste se chama "Usuário de Teste",
    /// e achá-lo por "usuario" é o que prova que a comparação passa pelo <c>unaccent</c>.
    /// </remarks>
    [Fact]
    public async Task Gestao_acha_membro_pelo_nome()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var busca = await Buscar(presidente, "usuario");

        busca.Membros.ShouldContain(resultado => resultado.Id == presidente.UsuarioId);
    }

    /// <summary>
    /// Formando não recebe membro nem fornecedor — nem para ver o nome de quem está na turma.
    /// </summary>
    /// <remarks>
    /// A busca não pode virar a porta dos fundos de duas telas que ele não abre: a de membros é da
    /// Gestão, a de fornecedores é da Tesouraria.
    /// </remarks>
    [Fact]
    public async Task Formando_nao_recebe_membro_nem_fornecedor()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var busca = await Buscar(formando, "usuario");

        busca.Membros.ShouldBeEmpty();
        busca.Fornecedores.ShouldBeEmpty();
    }

    /// <summary>
    /// A busca recorta também pelo plano: aviso e documento são do mural, e a turma que perdeu o mural (assinatura
    /// vencida, de volta ao gratuito) não os acha pela busca — a mesma recusa do gate de módulo.
    /// </summary>
    [Fact]
    public async Task Turma_sem_mural_nao_acha_aviso_nem_documento()
    {
        // Arrange
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var titulo = $"Rifa {Guid.NewGuid():N}";
        (
            await presidente.Cliente.PostAsJsonAsync(
                "/api/v1/comunicacao/avisos",
                new
                {
                    titulo,
                    conteudo = "Texto.",
                    visibilidade = "Turma",
                    fixado = false,
                    destaque = false,
                },
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();
        var documento = new MultipartFormDataContent
        {
            { new StringContent(titulo), "titulo" },
            { new StringContent("Ata"), "categoria" },
            { new StringContent("Turma"), "visibilidade" },
        };
        var pdf = new ByteArrayContent(Pdf);
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        documento.Add(pdf, "arquivo", "ata.pdf");
        (await presidente.Cliente.PostAsync("/api/v1/comunicacao/documentos", documento, Ct)).EnsureSuccessStatusCode();
        var comMural = await Buscar(presidente, titulo);

        // Act
        await fabrica.VencerAssinatura(formaturaId, Ct);
        var semMural = await Buscar(presidente, titulo);

        // Assert
        comMural.Avisos.ShouldHaveSingleItem();
        comMural.Documentos.ShouldHaveSingleItem();
        semMural.Avisos.ShouldBeEmpty();
        semMural.Documentos.ShouldBeEmpty();
    }

    /// <summary>Termo curto não é erro: quem está digitando ainda não terminou.</summary>
    [Fact]
    public async Task Termo_curto_volta_vazio_e_sem_erro()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var busca = await Buscar(presidente, "an");

        busca.Membros.ShouldBeEmpty();
        busca.Despesas.ShouldBeEmpty();
        busca.Avisos.ShouldBeEmpty();
        busca.Documentos.ShouldBeEmpty();
    }

    private static async Task<BuscaNaTurmaDTO> Buscar(MembroDeTeste membro, string termo)
    {
        var resposta = await membro.Cliente.GetAsync($"/api/v1/busca?termo={Uri.EscapeDataString(termo)}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<BuscaNaTurmaDTO>(Json, Ct))!;
    }
}

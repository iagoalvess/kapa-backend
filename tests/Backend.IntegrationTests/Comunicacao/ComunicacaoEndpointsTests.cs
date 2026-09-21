using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Comunicacao;
using Backend.Business.Common.Datas;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Comunicacao.Services;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Comunicacao;

/// <summary>
/// Mural e acervo contra a API e o Postgres de verdade — os critérios de aceite da Sprint 11 que são
/// de endpoint: o formando não recebe o interno na resposta, publicar é da Gestão, o quarto fixado é
/// 409, o download confere formatura e visibilidade antes de redirecionar, a URL assinada não aceita
/// adulteração, o upload confere os primeiros bytes e a exclusão deixa rastro.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ComunicacaoEndpointsTests(ApiFactory fabrica)
{
    private const string Avisos = "/api/v1/comunicacao/avisos";
    private const string Documentos = "/api/v1/comunicacao/documentos";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj <<>> endobj\ntrailer <<>>\n%%EOF"u8.ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Pagina<T>(List<T> Itens, long Total);

    private static AvisoRequestDTO Pedido(
        string titulo,
        Visibilidade visibilidade = Visibilidade.Turma,
        bool fixado = false,
        bool destaque = false
    ) => new(titulo, $"# {titulo}\n\nTexto do aviso.", visibilidade, fixado, destaque);

    private static MultipartFormDataContent Documento(string titulo, Visibilidade visibilidade, byte[]? bytes = null, string nome = "ata.pdf")
    {
        var corpo = new MultipartFormDataContent
        {
            { new StringContent(titulo), "titulo" },
            { new StringContent(nameof(CategoriaDeDocumento.Ata)), "categoria" },
            { new StringContent(visibilidade.ToString()), "visibilidade" },
        };

        var arquivo = new ByteArrayContent(bytes ?? Pdf);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        corpo.Add(arquivo, "arquivo", nome);

        return corpo;
    }

    private static async Task<AvisoDTO> Publicar(MembroDeTeste membro, AvisoRequestDTO aviso)
    {
        var resposta = await membro.Cliente.PostAsJsonAsync(Avisos, aviso, Json, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await resposta.Content.ReadFromJsonAsync<AvisoDTO>(Json, Ct))!;
    }

    private static async Task<DocumentoDTO> Enviar(MembroDeTeste membro, string titulo, Visibilidade visibilidade)
    {
        var resposta = await membro.Cliente.PostAsync(Documentos, Documento(titulo, visibilidade), Ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<DocumentoDTO>(Json, Ct))!;
    }

    /// <summary>Um cliente com o token do membro que <b>não</b> segue redirecionamento — para ver o 302.</summary>
    private HttpClient SemSeguir(MembroDeTeste membro)
    {
        var cliente = fabrica.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }
        );
        cliente.DefaultRequestHeaders.Authorization = membro.Cliente.DefaultRequestHeaders.Authorization;

        return cliente;
    }

    /// <summary>Critério de aceite: teste de endpoint, não de tela — o interno não está na resposta.</summary>
    [Fact]
    public async Task Formando_nao_recebe_aviso_nem_documento_somente_da_comissao()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var publico = await Publicar(comissao, Pedido("Festa confirmada"));
        var interno = await Publicar(comissao, Pedido("Ata da reunião interna", Visibilidade.SomenteComissao));
        var contrato = await Enviar(comissao, "Contrato do buffet", Visibilidade.Turma);
        var ata = await Enviar(comissao, "Ata interna", Visibilidade.SomenteComissao);

        var avisosDoFormando = await formando.Cliente.GetFromJsonAsync<Pagina<AvisoDTO>>(Avisos, Json, Ct);
        avisosDoFormando!.Itens.Select(a => a.Id).ShouldBe([publico.Id]);
        avisosDoFormando.Total.ShouldBe(1);
        (await formando.Cliente.GetAsync($"{Avisos}/{interno.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var bruto = await formando.Cliente.GetStringAsync(Documentos, Ct);
        bruto.ShouldNotContain("Ata interna");
        var documentosDoFormando = JsonSerializer.Deserialize<Pagina<DocumentoDTO>>(bruto, Json);
        documentosDoFormando!.Itens.Select(d => d.Id).ShouldBe([contrato.Id]);
        (await formando.Cliente.GetFromJsonAsync<ResumoDoAcervoDTO>($"{Documentos}/resumo", Json, Ct))!.Quantidade.ShouldBe(1);
        (await SemSeguir(formando).GetAsync($"{Documentos}/{ata.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var avisosDaComissao = await comissao.Cliente.GetFromJsonAsync<Pagina<AvisoDTO>>(Avisos, Json, Ct);
        avisosDaComissao!.Total.ShouldBe(2);
        (await comissao.Cliente.GetFromJsonAsync<Pagina<DocumentoDTO>>(Documentos, Json, Ct))!.Total.ShouldBe(2);
    }

    /// <summary>
    /// O filtro de visibilidade estreita o acervo, e nunca o alarga.
    /// </summary>
    /// <remarks>
    /// Ele existe para a tela que precisa escolher um documento que a turma inteira abre — o contrato
    /// de um item da festa (Sprint 17). O risco de um filtro assim é ser lido como "mostre-me os
    /// internos": para quem não os vê, a resposta é lista vazia, e não o acervo da comissão.
    /// </remarks>
    [Fact]
    public async Task O_filtro_de_visibilidade_estreita_o_acervo_e_nunca_o_alarga()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var contrato = await Enviar(comissao, "Contrato do buffet", Visibilidade.Turma);
        await Enviar(comissao, "Ata da negociação", Visibilidade.SomenteComissao);

        var daTurma = await comissao.Cliente.GetFromJsonAsync<Pagina<DocumentoDTO>>($"{Documentos}?visibilidade=Turma", Json, Ct);
        daTurma!.Itens.Select(d => d.Id).ShouldBe([contrato.Id]);
        daTurma.Total.ShouldBe(1);

        var internos = await comissao.Cliente.GetFromJsonAsync<Pagina<DocumentoDTO>>($"{Documentos}?visibilidade=SomenteComissao", Json, Ct);
        internos!.Total.ShouldBe(1);

        // O formando pedindo os internos recebe vazio: o recorte do papel vem antes do filtro.
        var tentativa = await formando.Cliente.GetFromJsonAsync<Pagina<DocumentoDTO>>($"{Documentos}?visibilidade=SomenteComissao", Json, Ct);
        tentativa!.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Publicar_e_da_gestao_e_o_formando_recebe_403()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var aviso = await Publicar(comissao, Pedido("Reunião"));
        var documento = await Enviar(comissao, "Regulamento", Visibilidade.Turma);

        (await formando.Cliente.PostAsJsonAsync(Avisos, Pedido("Quero publicar"), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.PutAsJsonAsync($"{Avisos}/{aviso.Id}", Pedido("Mudei"), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.DeleteAsync($"{Avisos}/{aviso.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.PostAsync(Documentos, Documento("Meu", Visibilidade.Turma), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.PutAsync($"{Documentos}/{documento.Id}", Documento("Meu", Visibilidade.Turma), Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Forbidden
        );
        (await formando.Cliente.DeleteAsync($"{Documentos}/{documento.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        foreach (var papel in new[] { PapelNaFormatura.Presidente, PapelNaFormatura.Tesoureiro })
        {
            var gestao = await fabrica.NovoMembro(formaturaId, papel, Ct);
            (await gestao.Cliente.PostAsJsonAsync(Avisos, Pedido($"Aviso de {papel}"), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    /// <summary>Suspender não é sequestrar dado: a turma lê o mural, mas ninguém publica.</summary>
    [Fact]
    public async Task Turma_suspensa_le_mas_nao_publica()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Suspensa, Ct), PapelNaFormatura.Comissao, Ct);

        (await comissao.Cliente.GetAsync(Avisos, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await comissao.Cliente.GetAsync(Documentos, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var publicar = await comissao.Cliente.PostAsJsonAsync(Avisos, Pedido("Aviso"), Json, Ct);
        publicar.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await publicar.Codigo(Ct)).ShouldBe("formatura.inativa");
    }

    [Fact]
    public async Task O_quarto_aviso_fixado_devolve_409_e_os_fixados_vem_primeiro()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);

        for (var i = 1; i <= Aviso.LimiteDeFixados; i++)
            await Publicar(comissao, Pedido($"Fixado {i}", fixado: true));
        var solto = await Publicar(comissao, Pedido("Mais novo, sem fixar"));

        var quarto = await comissao.Cliente.PostAsJsonAsync(Avisos, Pedido("Fixado 4", fixado: true), Json, Ct);
        quarto.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await quarto.Codigo(Ct)).ShouldBe("comunicacao.limite_de_fixados");

        var corrigindo = await comissao.Cliente.PutAsJsonAsync($"{Avisos}/{solto.Id}", Pedido("Mais novo, sem fixar", fixado: true), Json, Ct);
        corrigindo.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var mural = await comissao.Cliente.GetFromJsonAsync<Pagina<AvisoDTO>>(Avisos, Json, Ct);
        mural!.Itens.Select(a => a.Fixado).ShouldBe([true, true, true, false]);
        mural.Itens[0].Titulo.ShouldBe("Fixado 3");
    }

    /// <summary>Critério de aceite: autor e data de publicação ficam registrados e visíveis.</summary>
    [Fact]
    public async Task O_aviso_traz_autor_e_data_de_publicacao()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);
        var antes = DateTime.UtcNow.AddSeconds(-5);

        var aviso = await Publicar(comissao, Pedido("Reunião"));

        aviso.PublicadoPorUsuarioId.ShouldBe(comissao.UsuarioId);
        aviso.Autor.ShouldBe("Usuário de Teste");
        aviso.PublicadoEm.ShouldBeGreaterThan(antes);
    }

    [Fact]
    public async Task Excluir_aviso_registra_quem_excluiu_na_auditoria()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var autora = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var aviso = await Publicar(autora, Pedido("Aviso errado"));

        (await presidente.Cliente.DeleteAsync($"{Avisos}/{aviso.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await autora.Cliente.GetAsync($"{Avisos}/{aviso.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var contexto = fabrica.ContextoDe(null);
        var evento = (await contexto.Eventos.Where(e => e.Nome == AvisoService.EventoDeExclusao).ToListAsync(Ct)).Single(e =>
            e.Dados!.Contains(aviso.Id.ToString())
        );
        evento.UsuarioId.ShouldBe(presidente.UsuarioId);
        evento.Dados!.ShouldContain("Aviso errado");
    }

    /// <summary>
    /// Critério de aceite: o download passa pela API, confere formatura e visibilidade e só então
    /// redireciona; a URL assinada serve o arquivo e não aceita adulteração.
    /// </summary>
    [Fact]
    public async Task Download_confere_e_redireciona_para_url_assinada_de_minutos()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var deOutraTurma = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var documento = await Enviar(comissao, "Contrato do buffet", Visibilidade.Turma);

        var redirecionamento = await SemSeguir(formando).GetAsync($"{Documentos}/{documento.Id}/download", Ct);

        redirecionamento.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var destino = redirecionamento.Headers.Location!.OriginalString;
        destino.ShouldStartWith("/api/v1/arquivos/temporario?");
        destino.ShouldNotContain(documento.Id.ToString());

        var anonimo = fabrica.CreateClient();
        var arquivo = await anonimo.GetAsync(destino, Ct);
        arquivo.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await arquivo.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Pdf);
        arquivo.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        (await anonimo.GetAsync(destino.Replace("expira=", "expira=9"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonimo.GetAsync(destino.Replace("ata.pdf", "outra.pdf"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await SemSeguir(deOutraTurma).GetAsync($"{Documentos}/{documento.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_confere_o_tipo_pelos_primeiros_bytes()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);
        byte[] executavel = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];

        var resposta = await comissao.Cliente.PostAsync(Documentos, Documento("Contrato", Visibilidade.Turma, executavel, "contrato.pdf"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await comissao.Cliente.GetFromJsonAsync<Pagina<DocumentoDTO>>(Documentos, Json, Ct))!.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Substituir_soma_a_versao_e_a_auditoria_guarda_os_dois_arquivos()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);
        var documento = await Enviar(comissao, "Orçamento da banda", Visibilidade.Turma);

        var resposta = await comissao.Cliente.PutAsync(
            $"{Documentos}/{documento.Id}",
            Documento("Orçamento da banda", Visibilidade.SomenteComissao, nome: "orcamento-v2.pdf"),
            Ct
        );

        resposta.EnsureSuccessStatusCode();
        var substituido = (await resposta.Content.ReadFromJsonAsync<DocumentoDTO>(Json, Ct))!;
        substituido.Versao.ShouldBe(2);
        substituido.NomeDoArquivo.ShouldBe("orcamento-v2.pdf");
        substituido.Visibilidade.ShouldBe(Visibilidade.SomenteComissao);

        await using var contexto = fabrica.ContextoDe(null);
        var evento = (await contexto.Eventos.Where(e => e.Nome == DocumentoService.EventoDeSubstituicao).ToListAsync(Ct)).Single(e =>
            e.Dados!.Contains(documento.Id.ToString())
        );
        evento.Dados!.ShouldContain("ata.pdf");
        evento.Dados!.ShouldContain("orcamento-v2.pdf");
        (await contexto.Arquivos.CountAsync(a => a.EnviadoPorId == comissao.UsuarioId, Ct)).ShouldBe(1);
    }

    /// <summary>
    /// Os filtros e as contagens do mural saem do mesmo recorte da lista: o formando não conta o
    /// aviso interno, nem descobre que ele existe pelo número da pílula.
    /// </summary>
    [Fact]
    public async Task Os_filtros_e_o_resumo_do_mural_andam_com_o_papel()
    {
        var formatura = await fabrica.CriarFormatura(Ct);
        var comissao = await fabrica.NovoMembro(formatura, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formatura, PapelNaFormatura.Formando, Ct);

        await Publicar(comissao, Pedido("Reunião de sábado", fixado: true));
        await Publicar(comissao, Pedido("Contrato do bufê", destaque: true));
        await Publicar(comissao, Pedido("Ata da comissão", Visibilidade.SomenteComissao));

        var fixados = await comissao.Cliente.GetFromJsonAsync<Pagina<AvisoDTO>>($"{Avisos}?fixado=true", Json, Ct);
        fixados!.Itens.ShouldHaveSingleItem().Titulo.ShouldBe("Reunião de sábado");

        var importantes = await comissao.Cliente.GetFromJsonAsync<Pagina<AvisoDTO>>($"{Avisos}?destaque=true", Json, Ct);
        importantes!.Itens.ShouldHaveSingleItem().Titulo.ShouldBe("Contrato do bufê");

        // A busca varre título e texto sem acento, como a do acervo.
        var achados = await formando.Cliente.GetFromJsonAsync<Pagina<AvisoDTO>>($"{Avisos}?busca=reuniao", Json, Ct);
        achados!.Itens.ShouldHaveSingleItem().Titulo.ShouldBe("Reunião de sábado");

        // O período é o dia de Brasília: publicado agora, o aviso cai dentro de "hoje até hoje".
        var hoje = DataUtils.Hoje();
        var doDia = await comissao.Cliente.GetFromJsonAsync<Pagina<AvisoDTO>>($"{Avisos}?de={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}", Json, Ct);
        doDia!.Total.ShouldBe(3);

        var ontem = hoje.AddDays(-1);
        var deOntem = await comissao.Cliente.GetFromJsonAsync<Pagina<AvisoDTO>>($"{Avisos}?ate={ontem:yyyy-MM-dd}", Json, Ct);
        deOntem!.Total.ShouldBe(0);

        var daComissao = await comissao.Cliente.GetFromJsonAsync<ResumoDoMuralDTO>($"{Avisos}/resumo", Json, Ct);
        daComissao!.Quantidade.ShouldBe(3);
        daComissao.Fixados.ShouldBe(1);
        daComissao.Importantes.ShouldBe(1);
        daComissao.Internos.ShouldBe(1);
        daComissao.UltimaPublicacao.ShouldNotBeNull();

        var doFormando = await formando.Cliente.GetFromJsonAsync<ResumoDoMuralDTO>($"{Avisos}/resumo", Json, Ct);
        doFormando!.Quantidade.ShouldBe(2);
        doFormando.Internos.ShouldBe(0);
    }

    [Fact]
    public async Task A_busca_do_acervo_ignora_acento()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);
        await Enviar(comissao, "Orçamento da decoração", Visibilidade.Turma);
        await Enviar(comissao, "Contrato do fotógrafo", Visibilidade.Turma);

        var achados = await comissao.Cliente.GetFromJsonAsync<Pagina<DocumentoDTO>>($"{Documentos}?busca=decoracao", Json, Ct);

        achados!.Itens.ShouldHaveSingleItem().Titulo.ShouldBe("Orçamento da decoração");
    }

    /// <summary>
    /// O sino conta o que entrou desde a última visita — e nada do que é interno para quem não o lê.
    /// </summary>
    /// <remarks>
    /// É a mesma fronteira do resto do mural, vista por outro endpoint: um selo com "3" para um
    /// formando que só tem dois avisos a ler conta a ele que existe um terceiro, interno.
    /// </remarks>
    [Fact]
    public async Task O_sino_nao_conta_aviso_interno_para_o_formando()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        await Publicar(comissao, Pedido("Assembleia geral"));
        await Publicar(comissao, Pedido("Ata da reunião", Visibilidade.SomenteComissao));

        var daComissao = await comissao.Cliente.GetFromJsonAsync<NovidadesDoMuralDTO>($"{Avisos}/novidades", Json, Ct);
        var doFormando = await formando.Cliente.GetFromJsonAsync<NovidadesDoMuralDTO>($"{Avisos}/novidades", Json, Ct);

        daComissao!.Quantidade.ShouldBe(2);
        doFormando!.Quantidade.ShouldBe(1);
        doFormando.Itens.ShouldHaveSingleItem().Titulo.ShouldBe("Assembleia geral");
    }

    /// <summary>Abrir o mural zera o sino; o que for publicado depois volta a acendê-lo.</summary>
    [Fact]
    public async Task Marcar_visto_zera_o_sino_e_o_aviso_seguinte_o_acende()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        await Publicar(comissao, Pedido("Assembleia geral"));

        var visto = await formando.Cliente.PostAsync($"{Avisos}/novidades/visto", null, Ct);
        visto.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var zerado = await formando.Cliente.GetFromJsonAsync<NovidadesDoMuralDTO>($"{Avisos}/novidades", Json, Ct);
        zerado!.Quantidade.ShouldBe(0);
        zerado.Itens.ShouldBeEmpty();

        await Publicar(comissao, Pedido("Rifa do jantar"));

        var depois = await formando.Cliente.GetFromJsonAsync<NovidadesDoMuralDTO>($"{Avisos}/novidades", Json, Ct);
        depois!.Quantidade.ShouldBe(1);
        depois.Itens.ShouldHaveSingleItem().Titulo.ShouldBe("Rifa do jantar");
    }
}

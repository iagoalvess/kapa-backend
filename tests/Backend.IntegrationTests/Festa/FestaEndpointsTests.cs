using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.Api.DTOs.Comunicacao;
using Backend.Api.DTOs.Festa;
using Backend.Api.DTOs.Financeiro;
using Backend.Business.Common.Datas;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace Backend.IntegrationTests.Festa;

/// <summary>
/// A festa contra a API e o Postgres de verdade: quem pode chamar cada endpoint e os critérios de
/// aceite da Sprint 17 — a turma nasce com os seis sugeridos, o estado do item sai das despesas,
/// contratar por menos derruba o custo, item com despesa não se exclui e o cancelado sai da meta sem
/// levar o caixa junto.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class FestaEndpointsTests(ApiFactory fabrica)
{
    private const string Itens = "/api/v1/festa/itens";
    private const string Meta = "/api/v1/festa/meta";
    private const string Despesas = "/api/v1/financeiro/despesas";
    private const string Caixa = "/api/v1/financeiro/caixa";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Hoje => DataUtils.Hoje();

    /// <summary>
    /// Ler é de todo membro; escrever é da Gestão.
    /// </summary>
    /// <remarks>
    /// A tela responde "pelo que eu estou pagando?", e quem paga tem direito de ler. Escrever é da
    /// Gestão inteira, e não só da Tesouraria: a descrição é da comissão, o preço é da tesouraria, e
    /// os dois moram no mesmo registro.
    /// </remarks>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Todo_membro_le_a_festa_mas_so_a_gestao_escreve(string papel, HttpStatusCode escrita)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        (await membro.Cliente.GetAsync(Itens, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.GetAsync(Meta, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await membro.Cliente.PostAsync(Itens, ItemMultipart(Novo("Decoração")), Ct)).StatusCode.ShouldBe(escrita);
    }

    [Fact]
    public async Task O_formando_nao_altera_cancela_nem_exclui_item()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var item = await Criar(presidente, Novo("Banda"), Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        (await formando.Cliente.PutAsync($"{Itens}/{item.Id}", ItemMultipart(Novo("Outra banda")), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.PostAsync($"{Itens}/{item.Id}/cancelamento", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.DeleteAsync($"{Itens}/{item.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await formando.Cliente.GetAsync($"{Itens}/{item.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Item_de_outra_turma_devolve_404_e_nao_403()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var item = await Criar(presidente, Novo("Buffet da outra turma"), Ct);

        var vizinha = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        (await vizinha.Cliente.GetAsync($"{Itens}/{item.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vizinha.Cliente.DeleteAsync($"{Itens}/{item.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>O detalhe do item traz as propostas da mais barata para a mais cara, contra o Postgres de verdade.</summary>
    [Fact]
    public async Task O_detalhe_traz_as_propostas_da_mais_barata_para_a_mais_cara()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var item = await Criar(presidente, Novo("Banda"), Ct);

        await Propor(presidente, item.Id, "Banda X", 8_000_00, Ct);
        await Propor(presidente, item.Id, "Banda Y", 6_500_00, Ct);

        (await ObterDetalhe(presidente, item.Id, Ct)).Propostas.Select(p => p.Titulo).ShouldBe(["Banda Y", "Banda X"]);
    }

    /// <summary>Contratado o item, a escolha aconteceu: proposta para de aceitar escrita, e continua legível.</summary>
    [Fact]
    public async Task Item_com_despesa_nao_aceita_proposta()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var item = await Criar(tesoureiro, Novo("Banda"), Ct);
        await Propor(tesoureiro, item.Id, "Banda X", 8_000_00, Ct);

        await Lancar(tesoureiro, item.Id, 8_000_00, Ct);

        var nova = await tesoureiro.Cliente.PostAsJsonAsync(
            $"{Itens}/{item.Id}/propostas",
            new PropostaRequestDTO("Banda Z", 7_000_00, null),
            Json,
            Ct
        );
        nova.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await ObterDetalhe(tesoureiro, item.Id, Ct)).Propostas.Count().ShouldBe(1);
    }

    /// <summary>O formando lê as propostas, mas não as cadastra — escrever é da Gestão.</summary>
    [Fact]
    public async Task O_formando_le_mas_nao_cadastra_proposta()
    {
        var formatura = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formatura, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formatura, PapelNaFormatura.Formando, Ct);
        var item = await Criar(presidente, Novo("Banda"), Ct);
        await Propor(presidente, item.Id, "Banda X", 8_000_00, Ct);

        var tentativa = await formando.Cliente.PostAsJsonAsync(
            $"{Itens}/{item.Id}/propostas",
            new PropostaRequestDTO("Banda Z", 7_000_00, null),
            Json,
            Ct
        );
        tentativa.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ObterDetalhe(formando, item.Id, Ct)).Propostas.Single().Titulo.ShouldBe("Banda X");
    }

    [Fact]
    public async Task A_turma_nasce_com_os_seis_sugeridos_a_contratar_e_sem_custo()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var itens = await Listar(presidente, Ct);

        itens.Count.ShouldBe(6);
        itens.Select(item => item.Titulo).ShouldBe(["Buffet", "Espaço", "Fotografia", "Banda", "Convites", "Beca"]);
        itens.ShouldAllBe(item => item.Estado == EstadoDoItem.AContratar);

        var meta = await ObterMeta(presidente, Ct);
        meta.CustoEmCentavos.ShouldBe(0);
        meta.AContratar.ShouldBe(6);
    }

    [Fact]
    public async Task Lancar_e_pagar_a_despesa_mudam_o_selo_do_item_sem_ninguem_edita_lo()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var item = await Criar(tesoureiro, Novo("Buffet") with { ValorPrevistoEmCentavos = 60_000_00 }, Ct);

        item.Estado.ShouldBe(EstadoDoItem.AContratar);
        item.CustoEmCentavos.ShouldBe(60_000_00);

        var despesa = (await Lancar(tesoureiro, item.Id, 54_000_00, Ct))[0];

        var contratado = await Obter(tesoureiro, item.Id, Ct);
        contratado.Estado.ShouldBe(EstadoDoItem.Contratado);
        // O contratado toma o lugar do previsto: fechar por menos faz o custo da festa cair.
        contratado.CustoEmCentavos.ShouldBe(54_000_00);
        contratado.PagoEmCentavos.ShouldBe(0);

        (await tesoureiro.Cliente.PostAsync($"{Despesas}/{despesa.Id}/pagar", Pagamento(), Ct)).EnsureSuccessStatusCode();

        var pago = await Obter(tesoureiro, item.Id, Ct);
        pago.Estado.ShouldBe(EstadoDoItem.Pago);
        pago.PagoEmCentavos.ShouldBe(54_000_00);
    }

    [Fact]
    public async Task Item_com_despesa_nao_e_excluido_e_o_cancelado_sai_da_meta_sem_levar_o_caixa()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var item = await Criar(tesoureiro, Novo("Banda") with { ValorPrevistoEmCentavos = 9_000_00 }, Ct);
        var despesa = (await Lancar(tesoureiro, item.Id, 9_000_00, Ct))[0];
        (await tesoureiro.Cliente.PostAsync($"{Despesas}/{despesa.Id}/pagar", Pagamento(), Ct)).EnsureSuccessStatusCode();

        var exclusao = await tesoureiro.Cliente.DeleteAsync($"{Itens}/{item.Id}", Ct);

        exclusao.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await exclusao.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Extensions["codigo"]!.ToString().ShouldBe("festa.item_em_uso");

        var cancelamento = await tesoureiro.Cliente.PostAsync($"{Itens}/{item.Id}/cancelamento", null, Ct);
        cancelamento.EnsureSuccessStatusCode();
        (await cancelamento.Content.ReadFromJsonAsync<ItemDaFestaDTO>(Json, Ct))!.Estado.ShouldBe(EstadoDoItem.Cancelado);

        (await ObterMeta(tesoureiro, Ct)).CustoEmCentavos.ShouldBe(0);

        // O sinal da banda saiu do caixa e continua lá: a turma desistiu do item, não do pagamento.
        var caixa = await tesoureiro.Cliente.GetFromJsonAsync<CaixaDTO>(Caixa, Json, Ct);
        caixa!.GastoEmCentavos.ShouldBe(9_000_00);
    }

    [Fact]
    public async Task Item_sem_despesa_e_excluido_e_some_da_lista()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var item = await Criar(presidente, Novo("Decoração"), Ct);

        (await presidente.Cliente.DeleteAsync($"{Itens}/{item.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Listar(presidente, Ct)).ShouldNotContain(outro => outro.Id == item.Id);
    }

    [Fact]
    public async Task Item_por_formando_entra_na_meta_pelo_preco_vezes_a_expectativa()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var item = await Criar(
            presidente,
            Novo("Álbum de fotos") with
            {
                Rateio = TipoDeRateio.PorFormando,
                ValorPrevistoEmCentavos = 350_00,
                QuantidadeEstimada = 40,
            },
            Ct
        );

        item.CustoPrevistoEmCentavos.ShouldBe(14_000_00);
        item.CustoEmCentavos.ShouldBe(14_000_00);
        (await ObterMeta(presidente, Ct)).CustoEmCentavos.ShouldBe(14_000_00);
    }

    [Fact]
    public async Task O_item_rateado_ignora_a_quantidade_informada()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var item = await Criar(presidente, Novo("Espaço") with { ValorPrevistoEmCentavos = 40_000_00, QuantidadeEstimada = 80 }, Ct);

        item.QuantidadeEstimada.ShouldBe(1);
        item.CustoEmCentavos.ShouldBe(40_000_00);
    }

    [Fact]
    public async Task Item_cancelado_nao_aceita_correcao_e_reativar_o_traz_de_volta()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var item = await Criar(presidente, Novo("Banda") with { ValorPrevistoEmCentavos = 9_000_00 }, Ct);

        (await presidente.Cliente.PostAsync($"{Itens}/{item.Id}/cancelamento", null, Ct)).EnsureSuccessStatusCode();

        var correcao = await presidente.Cliente.PutAsync($"{Itens}/{item.Id}", ItemMultipart(Novo("Banda nova")), Ct);
        correcao.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await correcao.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Extensions["codigo"]!.ToString().ShouldBe("festa.item_cancelado");

        (await presidente.Cliente.DeleteAsync($"{Itens}/{item.Id}/cancelamento", Ct)).EnsureSuccessStatusCode();

        (await Obter(presidente, item.Id, Ct)).Estado.ShouldBe(EstadoDoItem.AContratar);
        (await ObterMeta(presidente, Ct)).CustoEmCentavos.ShouldBe(9_000_00);
    }

    [Fact]
    public async Task A_despesa_sem_item_continua_possivel_e_nao_aparece_na_festa()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var item = await Criar(tesoureiro, Novo("Buffet") with { ValorPrevistoEmCentavos = 60_000_00 }, Ct);

        await Lancar(tesoureiro, null, 1_200_00, Ct, "Taxa bancária");

        var semVinculo = await Obter(tesoureiro, item.Id, Ct);
        semVinculo.Estado.ShouldBe(EstadoDoItem.AContratar);
        semVinculo.QuantidadeDeDespesas.ShouldBe(0);
        (await ObterMeta(tesoureiro, Ct)).CustoEmCentavos.ShouldBe(60_000_00);
    }

    [Fact]
    public async Task O_contrato_do_item_so_aceita_documento_que_a_turma_inteira_abre()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var daTurma = await EnviarDocumento(presidente, "Contrato do buffet", Visibilidade.Turma, Ct);
        var daComissao = await EnviarDocumento(presidente, "Ata da reunião interna", Visibilidade.SomenteComissao, Ct);

        var recusa = await presidente.Cliente.PostAsync(Itens, ItemMultipart(Novo("Buffet") with { DocumentoId = daComissao }), Ct);
        recusa.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await recusa.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct))!.Extensions["codigo"]!
            .ToString()
            .ShouldBe("festa.documento_nao_encontrado");

        var item = await Criar(presidente, Novo("Buffet") with { DocumentoId = daTurma }, Ct);
        item.Documento!.Titulo.ShouldBe("Contrato do buffet");
        item.Documento.ContentType.ShouldBe("application/pdf");

        // O formando enxerga o contrato: é dele que a decisão 7 fala — a prova do que a turma comprou.
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var comOFormando = await Obter(formando, item.Id, Ct);
        comOFormando.Documento!.Id.ShouldBe(daTurma);
    }

    /// <summary>O contrato anexado no item nasce no acervo, visível para a turma, e liga ao item.</summary>
    [Fact]
    public async Task Contrato_anexado_no_item_nasce_no_acervo()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PostAsync(
            Itens,
            ItemMultipart(Novo("Buffet"), Encoding.UTF8.GetBytes("%PDF-1.4 contrato de teste")),
            Ct
        );
        resposta.EnsureSuccessStatusCode();

        var item = (await resposta.Content.ReadFromJsonAsync<ItemDaFestaDTO>(Json, Ct))!;
        item.Documento.ShouldNotBeNull();
        item.Documento!.Titulo.ShouldBe("Buffet");

        // O formando abre o contrato: nasceu visível para a turma.
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        (await formando.Cliente.GetAsync($"/api/v1/comunicacao/documentos/{item.Documento.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Despesa_apontando_para_item_inexistente_e_recusada_com_400()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        var resposta = await tesoureiro.Cliente.PostAsync(Despesas, Multipart(Guid.CreateVersion7(), 1_000_00, "Buffet"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct))!.Extensions["codigo"]!
            .ToString()
            .ShouldBe("financeiro.item_da_festa_nao_encontrado");
    }

    private static ItemDaFestaRequestDTO Novo(string titulo) => new(titulo, CategoriaDeDespesa.Outros, null, null, TipoDeRateio.Turma, 0, 1);

    /// <summary>Sobe um documento ao acervo da Sprint 11 e devolve o id dele.</summary>
    /// <param name="membro">Quem envia — precisa ser da Gestão.</param>
    /// <param name="titulo">Como a turma o chama.</param>
    /// <param name="visibilidade">Turma inteira, ou só a comissão.</param>
    private static async Task<Guid> EnviarDocumento(MembroDeTeste membro, string titulo, Visibilidade visibilidade, CancellationToken ct)
    {
        var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 contrato de teste"));
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        var corpo = new MultipartFormDataContent
        {
            { new StringContent(titulo), "titulo" },
            { new StringContent(nameof(CategoriaDeDocumento.Contrato)), "categoria" },
            { new StringContent(visibilidade.ToString()), "visibilidade" },
        };
        corpo.Add(arquivo, "arquivo", "contrato.pdf");

        var resposta = await membro.Cliente.PostAsync("/api/v1/comunicacao/documentos", corpo, ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<DocumentoDTO>(Json, ct))!.Id;
    }

    private static async Task<ItemDaFestaDTO> Criar(MembroDeTeste membro, ItemDaFestaRequestDTO dados, CancellationToken ct)
    {
        var resposta = await membro.Cliente.PostAsync(Itens, ItemMultipart(dados), ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<ItemDaFestaDTO>(Json, ct))!;
    }

    /// <summary>O item como multipart, como a tela o envia; o contrato é opcional e nasce no acervo.</summary>
    private static MultipartFormDataContent ItemMultipart(ItemDaFestaRequestDTO dados, byte[]? contrato = null)
    {
        var conteudo = new MultipartFormDataContent
        {
            { new StringContent(dados.Titulo ?? string.Empty), "titulo" },
            { new StringContent(dados.Categoria.ToString()), "categoria" },
            { new StringContent((dados.Rateio ?? TipoDeRateio.Turma).ToString()), "rateio" },
            { new StringContent(dados.ValorPrevistoEmCentavos.ToString(CultureInfo.InvariantCulture)), "valorPrevistoEmCentavos" },
            { new StringContent((dados.QuantidadeEstimada ?? 1).ToString(CultureInfo.InvariantCulture)), "quantidadeEstimada" },
        };

        if (dados.OQueInclui is not null)
            conteudo.Add(new StringContent(dados.OQueInclui), "oQueInclui");

        if (dados.DocumentoId is { } documentoId)
            conteudo.Add(new StringContent(documentoId.ToString()), "documentoId");

        if (contrato is not null)
        {
            var arquivo = new ByteArrayContent(contrato);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            conteudo.Add(arquivo, "contrato", "contrato.pdf");
        }

        return conteudo;
    }

    private static async Task<ItemDaFestaDetalheDTO> ObterDetalhe(MembroDeTeste membro, Guid id, CancellationToken ct) =>
        (await membro.Cliente.GetFromJsonAsync<ItemDaFestaDetalheDTO>($"{Itens}/{id}/detalhe", Json, ct))!;

    private static async Task<PropostaDTO> Propor(MembroDeTeste membro, Guid itemId, string titulo, long valor, CancellationToken ct)
    {
        var resposta = await membro.Cliente.PostAsJsonAsync($"{Itens}/{itemId}/propostas", new PropostaRequestDTO(titulo, valor, null), Json, ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<PropostaDTO>(Json, ct))!;
    }

    private static async Task<IReadOnlyList<ItemDaFestaDTO>> Listar(MembroDeTeste membro, CancellationToken ct) =>
        (await membro.Cliente.GetFromJsonAsync<List<ItemDaFestaDTO>>(Itens, Json, ct))!;

    private static async Task<ItemDaFestaDTO> Obter(MembroDeTeste membro, Guid id, CancellationToken ct) =>
        (await membro.Cliente.GetFromJsonAsync<ItemDaFestaDTO>($"{Itens}/{id}", Json, ct))!;

    private static async Task<MetaDaFestaDTO> ObterMeta(MembroDeTeste membro, CancellationToken ct) =>
        (await membro.Cliente.GetFromJsonAsync<MetaDaFestaDTO>(Meta, Json, ct))!;

    private static async Task<IReadOnlyList<DespesaDTO>> Lancar(
        MembroDeTeste membro,
        Guid? itemId,
        long valorEmCentavos,
        CancellationToken ct,
        string descricao = "Contrato"
    )
    {
        var resposta = await membro.Cliente.PostAsync(Despesas, Multipart(itemId, valorEmCentavos, descricao), ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<List<DespesaDTO>>(Json, ct))!;
    }

    /// <summary>O lançamento como multipart — é assim que o formulário da tela o envia.</summary>
    private static MultipartFormDataContent Multipart(Guid? itemId, long valorEmCentavos, string descricao)
    {
        var conteudo = new MultipartFormDataContent
        {
            { new StringContent(descricao), "descricao" },
            { new StringContent(nameof(CategoriaDeDespesa.Outros)), "categoria" },
            { new StringContent(valorEmCentavos.ToString(CultureInfo.InvariantCulture)), "valorEmCentavos" },
            { new StringContent("1"), "numeroDeParcelas" },
            { new StringContent(Hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "competencia" },
            { new StringContent(Hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "vencimento" },
        };

        if (itemId is { } vinculo)
            conteudo.Add(new StringContent(vinculo.ToString()), "itemDaFestaId");

        return conteudo;
    }

    private static MultipartFormDataContent Pagamento()
    {
        var comprovante = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 comprovante de teste"));
        comprovante.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        return new MultipartFormDataContent
        {
            { new StringContent(Hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "pagoEm" },
            { comprovante, "comprovante", "comprovante.pdf" },
        };
    }
}

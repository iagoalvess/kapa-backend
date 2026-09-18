using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Auditoria;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Privacidade;

/// <summary>
/// A trilha de auditoria: quem a lê, o que entra nela e o que ela recusa.
/// </summary>
/// <remarks>
/// As fronteiras de autorização vivem aqui porque política mal declarada não quebra o build — e esta
/// é a tela que nomeia quem mexeu no dinheiro da turma.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class AuditoriaTests(ApiFactory fabrica)
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Formando recebe 403. O que é público é o dashboard da Sprint 12, onde tudo é soma; aqui há
    /// nome de gente ao lado de cada operação de caixa.
    /// </summary>
    [Fact]
    public async Task Formando_nao_le_a_auditoria()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await formando.Cliente.GetAsync("/api/v1/auditoria", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auditoria_exige_login()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/api/v1/auditoria", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente)]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Comissao)]
    public async Task Gestao_le_a_auditoria(string papel)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var membro = await fabrica.NovoMembro(formaturaId, papel, Ct);

        var resposta = await membro.Cliente.GetAsync("/api/v1/auditoria", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// O caminho inteiro: uma troca de papel de verdade vira uma linha na tela, com o autor resolvido
    /// em nome e o antes/depois no corpo.
    /// </summary>
    [Fact]
    public async Task Troca_de_papel_aparece_na_trilha_com_antes_e_depois()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var troca = await presidente.Cliente.PutAsJsonAsync(
            $"/api/v1/formaturas/atual/membros/{formando.UsuarioId}/papel",
            new AlterarPapelRequestDTO(PapelNaFormatura.Tesoureiro),
            Json,
            Ct
        );
        troca.EnsureSuccessStatusCode();

        var pagina = await Trilha(presidente);
        var linha = pagina.Itens.Single(item => item.Nome == "membro.papel_alterado");

        linha.AutorUsuarioId.ShouldBe(presidente.UsuarioId);
        linha.Autor.ShouldNotBeNullOrWhiteSpace();
        linha.Dados.ShouldNotBeNull();
        linha.Dados!.ShouldContain("Formando");
        linha.Dados.ShouldContain("Tesoureiro");
    }

    /// <summary>
    /// O isolamento não some porque a tabela de eventos é da plataforma: a turma vizinha não pode
    /// aparecer na trilha desta.
    /// </summary>
    [Fact]
    public async Task Trilha_nao_mostra_evento_de_outra_turma()
    {
        var daVizinha = await fabrica.CriarFormatura(Ct);
        var presidenteVizinho = await fabrica.NovoMembro(daVizinha, PapelNaFormatura.Presidente, Ct);
        var formandoVizinho = await fabrica.NovoMembro(daVizinha, PapelNaFormatura.Formando, Ct);

        var troca = await presidenteVizinho.Cliente.PutAsJsonAsync(
            $"/api/v1/formaturas/atual/membros/{formandoVizinho.UsuarioId}/papel",
            new AlterarPapelRequestDTO(PapelNaFormatura.Comissao),
            Json,
            Ct
        );
        troca.EnsureSuccessStatusCode();

        var minhaFormatura = await fabrica.CriarFormatura(Ct);
        var euPresidente = await fabrica.NovoMembro(minhaFormatura, PapelNaFormatura.Presidente, Ct);

        var pagina = await Trilha(euPresidente);

        pagina.Itens.ShouldNotContain(item => item.Dados != null && item.Dados.Contains(formandoVizinho.UsuarioId.ToString()));
    }

    [Fact]
    public async Task Opcoes_de_filtro_trazem_os_autores_da_turma()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var troca = await presidente.Cliente.PutAsJsonAsync(
            $"/api/v1/formaturas/atual/membros/{formando.UsuarioId}/papel",
            new AlterarPapelRequestDTO(PapelNaFormatura.Comissao),
            Json,
            Ct
        );
        troca.EnsureSuccessStatusCode();

        var opcoes = await presidente.Cliente.GetFromJsonAsync<OpcoesDeAuditoriaDTO>("/api/v1/auditoria/opcoes-de-filtro", Json, Ct);

        opcoes.ShouldNotBeNull();
        opcoes.Autores.ShouldContain(autor => autor.UsuarioId == presidente.UsuarioId);
        opcoes.Nomes.ShouldContain("membro.papel_alterado");
    }

    /// <summary>Não existe rota de escrita: auditoria que a aplicação sabe escrever é auditoria forjável.</summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Auditoria_nao_aceita_escrita(string metodo)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.SendAsync(new HttpRequestMessage(new HttpMethod(metodo), "/api/v1/auditoria"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    /// <summary>
    /// O corpo do evento guarda o id de quem sofreu a operação, e a tela mostrava
    /// "Pessoa 01a09d04-…". O nome é resolvido na leitura, e a mesma busca o acha — ela procura em
    /// quem fez, em quem sofreu e no texto do corpo.
    /// </summary>
    [Fact]
    public async Task Trilha_nomeia_quem_sofreu_a_operacao_e_a_busca_o_acha_pelo_nome()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        await Renomear(formando.UsuarioId, "Juliana Gonçalves");

        var troca = await presidente.Cliente.PutAsJsonAsync(
            $"/api/v1/formaturas/atual/membros/{formando.UsuarioId}/papel",
            new AlterarPapelRequestDTO(PapelNaFormatura.Tesoureiro),
            Json,
            Ct
        );
        troca.EnsureSuccessStatusCode();

        var linha = (await Trilha(presidente)).Itens.Single(item => item.Nome == "membro.papel_alterado");

        linha.Pessoas[formando.UsuarioId.ToString()].ShouldBe("Juliana Gonçalves");

        var semAcento = await Trilha(presidente, "juliana goncalves");

        semAcento.Itens.ShouldContain(item => item.Id == linha.Id);

        var outroNome = await Trilha(presidente, "ninguem com este nome");

        outroNome.Itens.ShouldBeEmpty();
    }

    /// <summary>Troca o nome da conta direto no banco, para o teste ter um nome que só ele usa.</summary>
    private async Task Renomear(Guid usuarioId, string nome)
    {
        await using var contexto = fabrica.ContextoDe(null);

        await contexto.Users.Where(u => u.Id == usuarioId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Nome, nome), Ct);
    }

    private static async Task<PaginaDTO<LinhaDeAuditoriaDTO>> Trilha(MembroDeTeste membro, string? busca = null) =>
        (
            await membro.Cliente.GetFromJsonAsync<PaginaDTO<LinhaDeAuditoriaDTO>>(
                busca is null ? "/api/v1/auditoria" : $"/api/v1/auditoria?busca={Uri.EscapeDataString(busca)}",
                Json,
                Ct
            )
        )!;
}

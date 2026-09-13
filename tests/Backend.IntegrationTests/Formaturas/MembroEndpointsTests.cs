using System.Net;
using System.Net.Http.Json;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Formaturas;

/// <summary>
/// Gestão de membros: quem pode chamar cada endpoint, e as regras que dependem do estado da turma.
/// </summary>
/// <remarks>
/// A matriz papel × endpoint roda contra a API de verdade porque política mal declarada não
/// quebra o build — só aparece como endpoint aberto em produção.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class MembroEndpointsTests(ApiFactory fabrica)
{
    private const string Rota = "/api/v1/formaturas/atual/membros";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Listar_membros_segue_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var membro = await fabrica.NovoMembro(formaturaId, papel, Ct);

        var resposta = await membro.Cliente.GetAsync(Rota, Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Resumo_segue_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var membro = await fabrica.NovoMembro(formaturaId, papel, Ct);

        var resposta = await membro.Cliente.GetAsync($"{Rota}/resumo", Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    /// <summary>Conta por papel e situação, e só da formatura da sessão.</summary>
    [Fact]
    public async Task Resumo_conta_por_papel_e_situacao_so_da_formatura_da_sessao()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var removido = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);
        (await presidente.Cliente.DeleteAsync($"{Rota}/{removido.UsuarioId}", Ct)).EnsureSuccessStatusCode();

        var contagens = await presidente.Cliente.GetFromJsonAsync<List<ContagemDeMembrosDTO>>($"{Rota}/resumo", Ct);

        contagens.ShouldNotBeNull();
        contagens.ShouldBe(
            [
                new ContagemDeMembrosDTO(PapelNaFormatura.Formando, false, 1),
                new ContagemDeMembrosDTO(PapelNaFormatura.Formando, true, 1),
                new ContagemDeMembrosDTO(PapelNaFormatura.Presidente, true, 1),
            ],
            ignoreOrder: true
        );
    }

    [Fact]
    public async Task Filtro_por_papel_traz_so_o_papel_pedido()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var membros = (await Pagina(presidente, $"{Rota}?papel={PapelNaFormatura.Tesoureiro}")).Itens;

        membros.ShouldHaveSingleItem().UsuarioId.ShouldBe(tesoureiro.UsuarioId);
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.NoContent)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Alterar_papel_e_so_do_presidente(string papel, HttpStatusCode esperado)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var ator = await fabrica.NovoMembro(formaturaId, papel, Ct);
        var alvo = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await ator.Cliente.PutAsJsonAsync($"{Rota}/{alvo.UsuarioId}/papel", new AlterarPapelRequestDTO(PapelNaFormatura.Comissao), Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.NoContent)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Remover_membro_e_so_do_presidente(string papel, HttpStatusCode esperado)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var ator = await fabrica.NovoMembro(formaturaId, papel, Ct);
        var alvo = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await ator.Cliente.DeleteAsync($"{Rota}/{alvo.UsuarioId}", Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    /// <summary>Papel sem formatura não significa nada: o 403 manda para a seleção.</summary>
    [Fact]
    public async Task Sem_formatura_selecionada_responde_403_formatura_nao_selecionada()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).GetAsync(Rota, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.nao_selecionada");
    }

    [Fact]
    public async Task Listar_traz_papel_e_status_dos_membros_da_formatura()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        var membros = (await Pagina(presidente, Rota)).Itens;

        membros.Count.ShouldBe(2);
        membros.Single(m => m.UsuarioId == tesoureiro.UsuarioId).Papel.ShouldBe(PapelNaFormatura.Tesoureiro);
        membros.ShouldAllBe(m => m.Ativo);
    }

    [Fact]
    public async Task Remover_o_ultimo_presidente_devolve_409()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.DeleteAsync($"{Rota}/{presidente.UsuarioId}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.ultimo_presidente");
    }

    [Fact]
    public async Task Rebaixar_o_ultimo_presidente_devolve_409()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PutAsJsonAsync(
            $"{Rota}/{presidente.UsuarioId}/papel",
            new AlterarPapelRequestDTO(PapelNaFormatura.Formando),
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.ultimo_presidente");
    }

    [Fact]
    public async Task Com_outro_presidente_ativo_o_presidente_pode_sair()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var primeiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await primeiro.Cliente.DeleteAsync($"{Rota}/{primeiro.UsuarioId}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Desativar é <c>Ativo = false</c>, nunca <c>DELETE</c>: parcelas, pagamentos e adesão
    /// continuam apontando para o vínculo.
    /// </summary>
    [Fact]
    public async Task Remover_desativa_o_vinculo_sem_apagar_a_linha()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        (await presidente.Cliente.DeleteAsync($"{Rota}/{formando.UsuarioId}", Ct)).EnsureSuccessStatusCode();

        await using var contexto = fabrica.ContextoDe(null);
        var vinculo = await contexto.Vinculos.SingleAsync(v => v.UsuarioId == formando.UsuarioId && v.FormaturaId == formaturaId, Ct);
        vinculo.Ativo.ShouldBeFalse();

        var membros = (await Pagina(presidente, Rota)).Itens;
        membros.Single(m => m.UsuarioId == formando.UsuarioId).Ativo.ShouldBeFalse();
    }

    [Fact]
    public async Task Alterar_papel_grava_o_papel_novo()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await presidente.Cliente.PutAsJsonAsync(
            $"{Rota}/{formando.UsuarioId}/papel",
            new AlterarPapelRequestDTO(PapelNaFormatura.Tesoureiro),
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var membros = (await Pagina(presidente, Rota)).Itens;
        membros.Single(m => m.UsuarioId == formando.UsuarioId).Papel.ShouldBe(PapelNaFormatura.Tesoureiro);
    }

    [Fact]
    public async Task Papel_inexistente_devolve_400()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await presidente.Cliente.PutAsJsonAsync($"{Rota}/{formando.UsuarioId}/papel", new AlterarPapelRequestDTO("Rei"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Codigo(Ct)).ShouldBe("membro.papel_invalido");
    }

    /// <summary>O id vem da rota, a formatura vem do token: membro de outra turma não existe aqui.</summary>
    [Fact]
    public async Task Membro_de_outra_formatura_responde_404()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var deOutra = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        var resposta = await presidente.Cliente.DeleteAsync($"{Rota}/{deOutra.UsuarioId}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await resposta.Codigo(Ct)).ShouldBe("membro.nao_encontrado");
    }

    /// <summary>Páginas sem sobreposição e metadados coerentes: nenhum membro some nem repete entre páginas.</summary>
    [Fact]
    public async Task Listar_pagina_com_total_e_sem_repetir_entre_paginas()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        for (var i = 0; i < 4; i++)
            await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var primeira = await Pagina(presidente, $"{Rota}?pagina=1&tamanho=2");
        var segunda = await Pagina(presidente, $"{Rota}?pagina=2&tamanho=2");
        var terceira = await Pagina(presidente, $"{Rota}?pagina=3&tamanho=2");

        primeira.Total.ShouldBe(5);
        primeira.TotalPaginas.ShouldBe(3);
        primeira.TemProxima.ShouldBeTrue();
        terceira.TemProxima.ShouldBeFalse();
        terceira.Itens.Count.ShouldBe(1);
        primeira.Itens.Concat(segunda.Itens).Concat(terceira.Itens).Select(m => m.UsuarioId).Distinct().Count().ShouldBe(5);
    }

    /// <summary>O teto do servidor vale mesmo quando o cliente pede mais.</summary>
    [Fact]
    public async Task Tamanho_acima_do_teto_e_limitado_pelo_servidor()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var pagina = await Pagina(presidente, $"{Rota}?tamanho=100000");

        pagina.Tamanho.ShouldBe(100);
    }

    [Fact]
    public async Task Busca_por_nome_ou_email_e_filtro_de_situacao()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var removido = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        (await presidente.Cliente.DeleteAsync($"{Rota}/{removido.UsuarioId}", Ct)).EnsureSuccessStatusCode();

        var email = await EmailDe(removido.UsuarioId);
        var porEmail = await Pagina(presidente, $"{Rota}?busca={email.Split('@')[0][^10..].ToUpperInvariant()}");
        var soRemovidos = await Pagina(presidente, $"{Rota}?ativo=false");
        var soAtivos = await Pagina(presidente, $"{Rota}?ativo=true");

        porEmail.Itens.Select(m => m.UsuarioId).ShouldBe([removido.UsuarioId]);
        soRemovidos.Itens.Select(m => m.UsuarioId).ShouldBe([removido.UsuarioId]);
        soAtivos.Total.ShouldBe(2);
        soAtivos.Itens.ShouldAllBe(m => m.Ativo);
    }

    /// <summary>
    /// O papel é conferido no vínculo, não na claim: rebaixado, o tesoureiro perde a gestão na
    /// requisição seguinte, com o mesmo access token ainda válido.
    /// </summary>
    [Fact]
    public async Task Rebaixar_corta_o_acesso_na_hora_mesmo_com_o_token_antigo()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        (await tesoureiro.Cliente.GetAsync(Rota, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await presidente.Cliente.PutAsJsonAsync($"{Rota}/{tesoureiro.UsuarioId}/papel", new AlterarPapelRequestDTO(PapelNaFormatura.Formando), Ct);

        var depois = await tesoureiro.Cliente.GetAsync(Rota, Ct);
        depois.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await depois.Codigo(Ct)).ShouldBe("auth.sem_permissao");
    }

    [Fact]
    public async Task Remover_corta_o_acesso_na_hora_mesmo_com_o_token_antigo()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);

        (await presidente.Cliente.DeleteAsync($"{Rota}/{comissao.UsuarioId}", Ct)).EnsureSuccessStatusCode();

        (await comissao.Cliente.GetAsync(Rota, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Dois presidentes rebaixando um ao outro no mesmo instante: sem a trava, os dois contariam
    /// dois presidentes e a turma ficaria sem nenhum. Repetido em várias turmas para a corrida
    /// acontecer de verdade.
    /// <para>
    /// O perdedor recebe 409 quando as duas chegam juntas à trava, ou 403 quando a outra já
    /// terminou antes de ele ser autorizado — aí ele já não é Presidente. Os dois são recusas
    /// corretas; o que não pode é haver dois 204.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Rebaixamentos_simultaneos_nunca_deixam_a_turma_sem_presidente()
    {
        for (var rodada = 0; rodada < 5; rodada++)
        {
            var formaturaId = await fabrica.CriarFormatura(Ct);
            var a = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
            var b = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
            var rebaixar = new AlterarPapelRequestDTO(PapelNaFormatura.Formando);

            var respostas = await Task.WhenAll(
                a.Cliente.PutAsJsonAsync($"{Rota}/{b.UsuarioId}/papel", rebaixar, Ct),
                b.Cliente.PutAsJsonAsync($"{Rota}/{a.UsuarioId}/papel", rebaixar, Ct)
            );

            respostas.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
            respostas.ShouldContain(r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.Forbidden);

            await using var contexto = fabrica.ContextoDe(null);
            (await contexto.Vinculos.CountAsync(v => v.FormaturaId == formaturaId && v.Ativo && v.Papel == PapelNaFormatura.Presidente, Ct)).ShouldBe(
                1
            );
        }
    }

    private static async Task<PaginaDTO<MembroDaFormaturaDTO>> Pagina(MembroDeTeste membro, string rota) =>
        (await membro.Cliente.GetFromJsonAsync<PaginaDTO<MembroDaFormaturaDTO>>(rota, Ct))!;

    private async Task<string> EmailDe(Guid usuarioId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Users.Where(u => u.Id == usuarioId).Select(u => u.Email!).SingleAsync(Ct);
    }
}

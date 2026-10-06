using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Formaturas.Models;
using Backend.Business.Recebimentos.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Recebimentos;

/// <summary>
/// A conta de recebimento contra a API e o Postgres de verdade: quem pode chamar cada endpoint e os
/// critérios de aceite da Sprint 8 — nasce não conferida, conferir grava quem e quando, trocar volta
/// a não conferida, avisa a comissão e deixa o antes e o depois na auditoria.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class RecebimentoEndpointsTests(ApiFactory fabrica)
{
    private const string Rota = "/api/v1/recebimentos/conta";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static readonly MeiosDaContaDTO ChaveCpf = new(
        new ChavePixDTO(TipoDeChavePix.Cpf, "529.982.247-25", "Ana Souza", "Curitiba"),
        null,
        null
    );

    private static readonly MeiosDaContaDTO ChaveCelular = new(
        new ChavePixDTO(TipoDeChavePix.Telefone, "(41) 99876-5432", "Bruno Lima", "São José dos Pinhais"),
        null,
        null
    );

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Só o Presidente grava, troca, gera o PIX de teste e confere; o Tesoureiro só vê.</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Tesouraria_ve_e_so_o_presidente_escreve(string papel, HttpStatusCode leitura, HttpStatusCode escrita)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        (await presidente.Cliente.GravarMeios(fabrica, ChaveCpf, Ct)).EnsureSuccessStatusCode();
        var membro = await fabrica.NovoMembro(formaturaId, papel, Ct);

        (await membro.Cliente.GetAsync(Rota, Ct)).StatusCode.ShouldBe(leitura);
        (await membro.Cliente.GetAsync($"{Rota}/pix-de-teste", Ct)).StatusCode.ShouldBe(escrita);
        (await membro.Cliente.PostAsync($"{Rota}/conferir", null, Ct)).StatusCode.ShouldBe(escrita);
        (await membro.Cliente.PutAsJsonAsync(Rota, ChaveCelular, Json, Ct)).StatusCode.ShouldBe(escrita);
    }

    [Theory]
    [InlineData(TipoDeChavePix.Cpf, "529.982.247-24", "CPF inválido")]
    [InlineData(TipoDeChavePix.Telefone, "99876-5432", "celular com DDD")]
    [InlineData(TipoDeChavePix.Email, "tesouraria@turma", "E-mail inválido")]
    public async Task Chave_invalida_para_o_tipo_devolve_400_com_o_motivo(TipoDeChavePix tipo, string chave, string motivo)
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PutAsJsonAsync(
            Rota,
            new MeiosDaContaDTO(new ChavePixDTO(tipo, chave, "Ana Souza", "Curitiba"), null, null),
            Json,
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problema = await resposta.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct);
        problema!.Errors["pix.chave"].ShouldHaveSingleItem().ShouldContain(motivo);
    }

    [Fact]
    public async Task Sem_conta_a_leitura_vem_vazia_e_o_pix_de_teste_e_404()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var daTurma = await presidente.Cliente.GetFromJsonAsync<ContaDeRecebimentoDaTurmaDTO>(Rota, Json, Ct);
        var pix = await presidente.Cliente.GetAsync($"{Rota}/pix-de-teste", Ct);

        daTurma!.Conta.ShouldBeNull();
        pix.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await pix.Codigo(Ct)).ShouldBe("recebimento.sem_conta");
    }

    /// <summary>O fluxo inteiro da tela: cadastrar, pagar o teste, conferir, trocar.</summary>
    [Fact]
    public async Task Nasce_nao_conferida_conferir_grava_quem_e_quando_e_trocar_volta_a_nao_conferida()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var gravada = await Ler<ContaDeRecebimentoDTO>(await presidente.Cliente.GravarMeios(fabrica, ChaveCpf, Ct));
        var pix = await presidente.Cliente.GetFromJsonAsync<PixDeTesteDTO>($"{Rota}/pix-de-teste", Json, Ct);
        var antes = DateTime.UtcNow;
        var conferida = await Ler<ContaDeRecebimentoDTO>(await presidente.Cliente.PostAsync($"{Rota}/conferir", null, Ct));
        var deNovo = await presidente.Cliente.PostAsync($"{Rota}/conferir", null, Ct);
        var trocada = await Ler<ContaDeRecebimentoDTO>(await presidente.Cliente.GravarMeios(fabrica, ChaveCelular, Ct));
        var repetida = await presidente.Cliente.PutAsJsonAsync(Rota, ChaveCelular, Json, Ct);

        gravada.Meios.Pix!.Chave.ShouldBe("52998224725");
        gravada.ConferidaEm.ShouldBeNull();
        pix!.ValorEmCentavos.ShouldBe(100);
        pix.CopiaECola.ShouldContain("011152998224725");
        pix.CopiaECola.ShouldContain("54041.00");
        conferida.ConferidaEm!.Value.ShouldBeInRange(antes.AddSeconds(-1), DateTime.UtcNow);
        conferida.ConferidaPor.ShouldBe("Usuário de Teste");
        (await deNovo.Codigo(Ct)).ShouldBe("recebimento.conta_ja_conferida");
        trocada.Meios.Pix!.Chave.ShouldBe("+5541998765432");
        trocada.Meios.Pix.Cidade.ShouldBe("São José dos Pinhais");
        trocada.ConferidaEm.ShouldBeNull();
        (await repetida.Codigo(Ct)).ShouldBe("recebimento.conta_sem_mudanca");
        await using var contexto = fabrica.ContextoDe(formaturaId);
        (await contexto.ContasDeRecebimento.CountAsync(Ct)).ShouldBe(1);
    }

    /// <summary>
    /// Decisão de 14/09/2026 (P2): a troca avisa a comissão — Presidente, Tesoureiro e Comissão —, e o
    /// formando não. O evento sai com o antes e o depois; a primeira chave não avisa ninguém.
    /// </summary>
    [Fact]
    public async Task Troca_avisa_so_a_comissao_e_grava_o_antes_e_o_depois()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        (await presidente.Cliente.GravarMeios(fabrica, ChaveCpf, Ct)).EnsureSuccessStatusCode();
        var avisosDoCadastro = await AvisosPara([presidente, tesoureiro, comissao, formando]);

        (await presidente.Cliente.GravarMeios(fabrica, ChaveCelular, Ct)).EnsureSuccessStatusCode();

        avisosDoCadastro.ShouldBeEmpty();
        var avisos = await AvisosPara([presidente, tesoureiro, comissao, formando]);
        avisos.Select(a => a.Para).ShouldBe(await EmailsDe([presidente, tesoureiro, comissao]), ignoreOrder: true);
        avisos.ShouldAllBe(a => a.CorpoHtml.Contains("+5541998765432") && a.CorpoHtml.Contains("Bruno Lima"));

        await using var contexto = fabrica.ContextoDe(null);
        var troca = await contexto.Eventos.Where(e => e.Nome == "recebimento.conta_alterada" && e.UsuarioId == presidente.UsuarioId).SingleAsync(Ct);
        var dados = JsonDocument.Parse(troca.Dados!).RootElement;
        dados.GetProperty("formaturaId").GetGuid().ShouldBe(formaturaId);
        dados.GetProperty("antes").GetProperty("pix").GetProperty("chave").GetString().ShouldBe("52998224725");
        dados.GetProperty("depois").GetProperty("pix").GetProperty("chave").GetString().ShouldBe("+5541998765432");
    }

    /// <summary>
    /// P4 de 21/09/2026: a turma que não aceita PIX grava os meios que aceita, e o PIX de teste some.
    /// Nenhum meio é 400 — a conta existe para o formando ter para onde pagar.
    /// </summary>
    [Fact]
    public async Task Turma_sem_pix_grava_os_outros_meios_e_sem_meio_nenhum_e_400()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var soDinheiro = new MeiosDaContaDTO(null, null, new DinheiroDTO("Ana Souza", "nas reuniões de quinta"));

        var gravada = (await Ler<GravacaoDaContaDTO>(await presidente.Cliente.PutAsJsonAsync(Rota, soDinheiro, Json, Ct))).Conta!;
        var teste = await presidente.Cliente.GetAsync($"{Rota}/pix-de-teste", Ct);
        var vazia = await presidente.Cliente.PutAsJsonAsync(Rota, new MeiosDaContaDTO(null, null, null), Json, Ct);

        gravada.Meios.Pix.ShouldBeNull();
        gravada.Meios.Dinheiro!.Nome.ShouldBe("Ana Souza");
        teste.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await teste.Codigo(Ct)).ShouldBe("recebimento.sem_chave_pix");
        vazia.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await vazia.Codigo(Ct)).ShouldBe("recebimento.sem_meio");
    }

    /// <summary>A conferência é do PIX: habilitar TED ao lado dele avisa a comissão, mas não a desfaz.</summary>
    [Fact]
    public async Task Habilitar_outro_meio_mantem_a_conferencia_do_pix()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        (await presidente.Cliente.GravarMeios(fabrica, ChaveCpf, Ct)).EnsureSuccessStatusCode();
        (await presidente.Cliente.PostAsync($"{Rota}/conferir", null, Ct)).EnsureSuccessStatusCode();

        var comTed = ChaveCpf with { Transferencia = new DadosBancariosDTO("Banco do Brasil", "1234-5", "98765-4", "Corrente", "Comissão") };
        var gravada = await Ler<ContaDeRecebimentoDTO>(await presidente.Cliente.GravarMeios(fabrica, comTed, Ct));

        gravada.ConferidaEm.ShouldNotBeNull();
        gravada.Meios.Transferencia!.Banco.ShouldBe("Banco do Brasil");
    }

    /// <summary>
    /// Revisão de segurança de 05/10/2026: a sessão do presidente sozinha não muda para onde a turma paga. O pedido
    /// deixa a conta como estava — o formando segue vendo a chave antiga —, o link só vale para quem pediu, e uma vez — nem na volta
    /// à chave do pedido.
    /// </summary>
    [Fact]
    public async Task Troca_so_vale_pelo_link_do_email_de_quem_pediu_e_uma_vez()
    {
        // Arrange
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var outroPresidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        (await presidente.Cliente.GravarMeios(fabrica, ChaveCpf, Ct)).EnsureSuccessStatusCode();

        // Act
        var pedido = await Ler<GravacaoDaContaDTO>(await presidente.Cliente.PutAsJsonAsync(Rota, ChaveCelular, Json, Ct));
        var token = await fabrica.TokenDoUltimoEmail(Ct);
        var lidaNoMeio = await presidente.Cliente.GetFromJsonAsync<ContaDeRecebimentoDaTurmaDTO>(Rota, Json, Ct);
        var peloOutro = await outroPresidente.Cliente.PostAsJsonAsync($"{Rota}/confirmar", new ConfirmacaoPorEmailDTO(token), Json, Ct);
        var peloTesoureiro = await tesoureiro.Cliente.PostAsJsonAsync($"{Rota}/confirmar", new ConfirmacaoPorEmailDTO(token), Json, Ct);
        var adulterado = await presidente.Cliente.PostAsJsonAsync($"{Rota}/confirmar", new ConfirmacaoPorEmailDTO(token + "x"), Json, Ct);
        var confirmada = await Ler<ContaDeRecebimentoDTO>(
            await presidente.Cliente.PostAsJsonAsync($"{Rota}/confirmar", new ConfirmacaoPorEmailDTO(token), Json, Ct)
        );
        var deNovo = await presidente.Cliente.PostAsJsonAsync($"{Rota}/confirmar", new ConfirmacaoPorEmailDTO(token), Json, Ct);
        (await presidente.Cliente.GravarMeios(fabrica, ChaveCpf, Ct)).EnsureSuccessStatusCode();
        var naVolta = await presidente.Cliente.PostAsJsonAsync($"{Rota}/confirmar", new ConfirmacaoPorEmailDTO(token), Json, Ct);

        // Assert
        pedido.ConfirmacaoEnviadaPara.ShouldNotBeNull();
        pedido.Conta!.Meios.Pix!.Chave.ShouldBe("52998224725");
        lidaNoMeio!.Conta!.Meios.Pix!.Chave.ShouldBe("52998224725");
        (await peloOutro.Codigo(Ct)).ShouldBe("recebimento.confirmacao_invalida");
        peloTesoureiro.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await adulterado.Codigo(Ct)).ShouldBe("recebimento.confirmacao_invalida");
        confirmada.Meios.Pix!.Chave.ShouldBe("+5541998765432");
        (await deNovo.Codigo(Ct)).ShouldBe("recebimento.confirmacao_invalida");
        (await naVolta.Codigo(Ct)).ShouldBe("recebimento.confirmacao_invalida");
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Eventos.CountAsync(e => e.Nome == "recebimento.troca_pedida" && e.UsuarioId == presidente.UsuarioId, Ct)).ShouldBe(3);
    }

    private static async Task<T> Ler<T>(HttpResponseMessage resposta)
    {
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }

    private async Task<List<string>> EmailsDe(MembroDeTeste[] membros)
    {
        var ids = membros.Select(m => m.UsuarioId).ToList();
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Users.Where(u => ids.Contains(u.Id)).Select(u => u.Email!).ToListAsync(Ct);
    }

    private async Task<List<(string Para, string CorpoHtml)>> AvisosPara(MembroDeTeste[] membros)
    {
        var emails = await EmailsDe(membros);
        await using var contexto = fabrica.ContextoDe(null);

        return
        [
            .. (
                await contexto
                    .EmailsFila.Where(e => emails.Contains(e.Para) && e.Assunto.StartsWith("Conta de recebimento alterada"))
                    .Select(e => new { e.Para, e.CorpoHtml })
                    .ToListAsync(Ct)
            ).Select(e => (e.Para, e.CorpoHtml)),
        ];
    }
}

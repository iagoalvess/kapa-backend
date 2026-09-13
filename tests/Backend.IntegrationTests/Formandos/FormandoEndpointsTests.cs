using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Formandos;
using Backend.Business.Formaturas.Models;
using Backend.Data.Criptografia;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using SkiaSharp;

namespace Backend.IntegrationTests.Formandos;

/// <summary>
/// Cadastro do formando: quem pode chamar cada endpoint, a cifra no banco e o upload da foto.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class FormandoEndpointsTests(ApiFactory fabrica)
{
    private const string Rota = "/api/v1/formandos";

    private const string CpfValido = "529.982.247-25";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AtualizarPerfilRequestDTO Pessoais(string? cpf = CpfValido, string? telefone = "(41) 99876-5432") =>
        new(new DadosPessoaisDTO("Ana Souza", null, cpf, null, null, telefone, null, null), null, null);

    /// <summary>A coluna guarda cifra; decifrada com a chave, volta o CPF.</summary>
    [Fact]
    public async Task Cpf_sai_cifrado_na_coluna()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        (await formando.Cliente.PutAsJsonAsync($"{Rota}/eu", Pessoais(), Ct)).EnsureSuccessStatusCode();

        await using var contexto = fabrica.ContextoDe(null);
        var bruto = await contexto
            .Database.SqlQuery<string>(
                $"SELECT p.cpf AS \"Value\" FROM perfis_de_formandos p JOIN vinculos_de_formatura v ON v.id = p.vinculo_id WHERE v.usuario_id = {formando.UsuarioId}"
            )
            .SingleAsync(Ct);

        bruto.ShouldNotContain("52998224725");
        new CifraDeCampo(Options.Create(new CriptografiaSettings { ChaveDeDados = ApiFactory.ChaveDeDados })).Decifrar(bruto).ShouldBe("52998224725");
    }

    /// <summary>
    /// O cadastro é do titular: o Presidente preenche o dele antes de pagar, e o formando de turma
    /// suspensa ainda corrige o próprio dado. Só a turma encerrada é arquivo.
    /// </summary>
    [Theory]
    [InlineData(StatusDaFormatura.Rascunho, HttpStatusCode.OK)]
    [InlineData(StatusDaFormatura.AguardandoPagamento, HttpStatusCode.OK)]
    [InlineData(StatusDaFormatura.Suspensa, HttpStatusCode.OK)]
    [InlineData(StatusDaFormatura.Encerrada, HttpStatusCode.Forbidden)]
    public async Task Proprio_cadastro_grava_em_qualquer_status_menos_encerrada(StatusDaFormatura status, HttpStatusCode esperado)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(status, Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await membro.Cliente.PutAsJsonAsync($"{Rota}/eu", Pessoais(), Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    [Fact]
    public async Task Cpf_com_digito_verificador_errado_devolve_400_apontando_o_campo()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        var resposta = await formando.Cliente.PutAsJsonAsync($"{Rota}/eu", Pessoais(cpf: "529.982.247-24"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problema = await resposta.Content.ReadFromJsonAsync<ProblemaDeValidacao>(Ct);
        problema!.Codigo.ShouldBe("perfil.cpf_invalido");
        problema.Errors.Keys.ShouldBe(["pessoais.cpf"]);
    }

    /// <summary>Cada um lê e grava o próprio: o id vem do token, não há onde trocar.</summary>
    [Fact]
    public async Task Eu_opera_sempre_sobre_o_usuario_do_token()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var ana = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var bruno = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        (await ana.Cliente.PutAsJsonAsync($"{Rota}/eu", Pessoais(), Ct)).EnsureSuccessStatusCode();
        var deBruno = await bruno.Cliente.GetFromJsonAsync<PerfilDoFormandoDTO>($"{Rota}/eu", Ct);
        var deAna = await ana.Cliente.GetFromJsonAsync<PerfilDoFormandoDTO>($"{Rota}/eu", Ct);

        deAna!.UsuarioId.ShouldBe(ana.UsuarioId);
        deAna.Pessoais.Cpf.ShouldBe("52998224725");
        deBruno!.UsuarioId.ShouldBe(bruno.UsuarioId);
        deBruno.Pessoais.Cpf.ShouldBeNull();
        deBruno.Completude.ShouldBe(0);
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Lista_e_detalhe_seguem_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var ator = await fabrica.NovoMembro(formaturaId, papel, Ct);
        var outro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        (await ator.Cliente.GetAsync(Rota, Ct)).StatusCode.ShouldBe(esperado);
        (await ator.Cliente.GetAsync($"{Rota}/{outro.UsuarioId}", Ct)).StatusCode.ShouldBe(esperado);
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Correcao_e_so_do_presidente(string papel, HttpStatusCode esperado)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var ator = await fabrica.NovoMembro(formaturaId, papel, Ct);
        var alvo = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await ator.Cliente.PutAsJsonAsync($"{Rota}/{alvo.UsuarioId}", Pessoais(), Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    [Fact]
    public async Task Correcao_fica_registrada_com_o_autor()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var alvo = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        (await presidente.Cliente.PutAsJsonAsync($"{Rota}/{alvo.UsuarioId}", Pessoais(), Ct)).EnsureSuccessStatusCode();

        await using var contexto = fabrica.ContextoDe(formaturaId);
        var perfil = await contexto.PerfisDeFormandos.SingleAsync(p => p.NomeCompleto == "Ana Souza", Ct);
        var correcao = await contexto.CorrecoesDePerfil.SingleAsync(c => c.PerfilId == perfil.Id, Ct);
        correcao.AutorUsuarioId.ShouldBe(presidente.UsuarioId);
        correcao.Secoes.ShouldBe("pessoais");
    }

    /// <summary>A formatura vem do token: formando de outra turma não existe aqui.</summary>
    [Fact]
    public async Task Formando_de_outra_formatura_responde_404()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var deOutra = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        var resposta = await presidente.Cliente.GetAsync($"{Rota}/{deOutra.UsuarioId}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await resposta.Codigo(Ct)).ShouldBe("formando.nao_encontrado");
    }

    /// <summary>Quem nunca abriu o cadastro aparece como pendente — é quem a comissão quer achar.</summary>
    [Fact]
    public async Task Lista_traz_quem_nao_preencheu_e_filtra_por_situacao()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var preencheu = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        (await preencheu.Cliente.PutAsJsonAsync($"{Rota}/eu", Pessoais(), Ct)).EnsureSuccessStatusCode();

        var todos = await Pagina(presidente, Rota);
        var pendentes = await Pagina(presidente, $"{Rota}?situacao=Pendente");
        var porNomeCivil = await Pagina(presidente, $"{Rota}?busca=souza");

        todos.Total.ShouldBe(2);
        todos.Itens.Single(f => f.UsuarioId == preencheu.UsuarioId).Completude.ShouldBe(30);
        pendentes.Itens.Select(f => f.UsuarioId).ShouldBe([presidente.UsuarioId]);
        porNomeCivil.Itens.Select(f => f.UsuarioId).ShouldBe([preencheu.UsuarioId]);
    }

    [Fact]
    public async Task Cadastro_incompleto_nao_impede_o_resto_da_turma()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        var resposta = await formando.Cliente.GetAsync("/api/v1/formaturas/atual", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Foto_e_redimensionada_no_servidor_para_ate_512()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        var resposta = await EnviarFoto(formando, Png(1600, 1200), "foto.png");

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var perfil = await resposta.Content.ReadFromJsonAsync<PerfilDoFormandoDTO>(Ct);
        var bytes = await formando.Cliente.GetByteArrayAsync($"/api/v1/arquivos/{perfil!.FotoArquivoId}/conteudo", Ct);
        using var imagem = SKBitmap.Decode(bytes);
        (imagem.Width, imagem.Height).ShouldBe((512, 384));
        perfil.Faltando.ShouldNotContain("foto");
    }

    [Fact]
    public async Task Trocar_a_foto_nao_acumula_arquivos()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        for (var i = 0; i < ApiFactory.LimiteDeArquivos + 1; i++)
            (await EnviarFoto(formando, Png(64, 64), "foto.png")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Arquivos.CountAsync(a => a.EnviadoPorId == formando.UsuarioId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Executavel_renomeado_para_jpg_e_recusado()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);
        byte[] exe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF];

        var resposta = await EnviarFoto(formando, exe, "foto.jpg");

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Codigo(Ct)).ShouldBe("perfil.foto_tipo_invalido");
    }

    [Fact]
    public async Task Foto_acima_de_5_MB_devolve_400_sem_gravar()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);
        var grande = new byte[(5 * 1024 * 1024) + 1];
        grande[0] = 0xFF;
        grande[1] = 0xD8;
        grande[2] = 0xFF;

        var resposta = await EnviarFoto(formando, grande, "foto.jpg");

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Codigo(Ct)).ShouldBe("perfil.foto_grande");
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Arquivos.AnyAsync(a => a.EnviadoPorId == formando.UsuarioId, Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Foto_do_formando_segue_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var ator = await fabrica.NovoMembro(formaturaId, papel, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        (await EnviarFoto(formando, Png(800, 600), "foto.png")).EnsureSuccessStatusCode();

        var resposta = await ator.Cliente.GetAsync($"{Rota}/{formando.UsuarioId}/foto", Ct);

        resposta.StatusCode.ShouldBe(esperado);
        if (esperado == HttpStatusCode.OK)
        {
            resposta.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
            using var imagem = SKBitmap.Decode(await resposta.Content.ReadAsByteArrayAsync(Ct));
            (imagem.Width, imagem.Height).ShouldBe((512, 384));
        }
    }

    [Fact]
    public async Task Formando_sem_foto_responde_404_perfil_sem_foto()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await comissao.Cliente.GetAsync($"{Rota}/{formando.UsuarioId}/foto", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await resposta.Codigo(Ct)).ShouldBe("perfil.sem_foto");
    }

    /// <summary>A comissão da turma A não chega à foto de quem só é da turma B.</summary>
    [Fact]
    public async Task Foto_de_formando_de_outra_formatura_responde_404()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);
        var deOutra = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);
        (await EnviarFoto(deOutra, Png(64, 64), "foto.png")).EnsureSuccessStatusCode();

        var resposta = await comissao.Cliente.GetAsync($"{Rota}/{deOutra.UsuarioId}/foto", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await resposta.Codigo(Ct)).ShouldBe("formando.nao_encontrado");
    }

    private static byte[] Png(int largura, int altura)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(SKColors.Orange);
        using var imagem = SKImage.FromBitmap(bitmap);

        return imagem.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static Task<HttpResponseMessage> EnviarFoto(MembroDeTeste membro, byte[] bytes, string nome)
    {
        var arquivo = new ByteArrayContent(bytes);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        return membro.Cliente.PostAsync($"{Rota}/eu/foto", new MultipartFormDataContent { { arquivo, "foto", nome } }, Ct);
    }

    private static async Task<PaginaDTO<FormandoResumoDTO>> Pagina(MembroDeTeste membro, string rota) =>
        (await membro.Cliente.GetFromJsonAsync<PaginaDTO<FormandoResumoDTO>>(rota, Ct))!;

    private sealed record ProblemaDeValidacao(string Codigo, Dictionary<string, string[]> Errors);
}

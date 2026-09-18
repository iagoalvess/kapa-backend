using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Privacidade;
using Backend.Business.Formaturas.Models;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Models;
using Backend.Business.Privacidade.Services;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Privacidade;

/// <summary>
/// O portal do titular de ponta a ponta: ver, exportar, revogar e ser eliminado.
/// </summary>
/// <remarks>
/// É onde as promessas da Sprint 14 param de ser texto: a exportação precisa trazer tudo, a
/// anonimização precisa apagar o que identifica <b>e</b> deixar o extrato da turma de pé, e o pacote
/// de um titular não pode abrir para outro.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class PortalDoTitularTests(ApiFactory fabrica)
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A lista de operadores abre antes do cadastro — é o art. 18, VII.</summary>
    [Fact]
    public async Task Operadores_respondem_sem_login()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/api/v1/privacidade/operadores", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var operadores = await resposta.Content.ReadFromJsonAsync<List<OperadorDTO>>(Json, Ct);

        operadores.ShouldNotBeNull();
        operadores.ShouldNotBeEmpty();
        operadores.ShouldContain(operador => operador.Nome.Contains("e-mail", StringComparison.OrdinalIgnoreCase));
        operadores.ShouldContain(operador => operador.Nome.Contains("armazenamento", StringComparison.OrdinalIgnoreCase));
        operadores.ShouldContain(operador => operador.Nome.Contains("pagamento", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Meus_dados_trazem_conta_turma_e_consentimentos()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var dados = await formando.Cliente.GetFromJsonAsync<MeusDadosDTO>("/api/v1/privacidade/meus-dados", Json, Ct);

        dados.ShouldNotBeNull();
        dados.Conta.Id.ShouldBe(formando.UsuarioId);
        dados.Turmas.ShouldHaveSingleItem().FormaturaId.ShouldBe(formaturaId);
        dados.Consentimentos.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Titular é a pessoa, não o vínculo (decisão 2): quem está em duas turmas vê as duas, sem trocar
    /// a formatura da sessão.
    /// </summary>
    [Fact]
    public async Task Meus_dados_atravessam_as_turmas_do_titular()
    {
        var primeira = await fabrica.CriarFormatura(Ct);
        var segunda = await fabrica.CriarFormatura(Ct);
        var membro = await fabrica.NovoMembro(primeira, PapelNaFormatura.Formando, Ct);

        await using (var contexto = fabrica.ContextoDe(null))
        {
            contexto.Vinculos.Add(
                new VinculoDeFormatura
                {
                    UsuarioId = membro.UsuarioId,
                    FormaturaId = segunda,
                    Papel = PapelNaFormatura.Presidente,
                }
            );

            await contexto.SaveChangesAsync(Ct);
        }

        var dados = await membro.Cliente.GetFromJsonAsync<MeusDadosDTO>("/api/v1/privacidade/meus-dados", Json, Ct);

        dados!.Turmas.Select(turma => turma.FormaturaId).ShouldBe([primeira, segunda], ignoreOrder: true);
    }

    [Fact]
    public async Task Meus_dados_exigem_login()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/api/v1/privacidade/meus-dados", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Exportacao_gera_o_pacote_e_ele_baixa()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var pedida = await Solicitar(formando, TipoDeSolicitacao.Exportacao);

        pedida.Tipo.ShouldBe(TipoDeSolicitacao.Exportacao);
        pedida.Status.ShouldBe(StatusDaSolicitacaoDePrivacidade.Pendente);

        (await Processar(pedida.Id)).ShouldBeTrue();

        var pronta = (await Listar(formando)).Single(s => s.Id == pedida.Id);

        pronta.Status.ShouldBe(StatusDaSolicitacaoDePrivacidade.Concluida);
        pronta.Disponivel.ShouldBeTrue();

        var pacote = await formando.Cliente.GetByteArrayAsync($"/api/v1/privacidade/solicitacoes/{pedida.Id}/arquivo", Ct);

        using var memoria = new MemoryStream(pacote);
        using var zip = new ZipArchive(memoria, ZipArchiveMode.Read);

        zip.Entries.Select(e => e.FullName).ShouldContain("dados.json");
        zip.Entries.Select(e => e.FullName).ShouldContain("cadastro.csv");
    }

    /// <summary>
    /// Pacote de outro titular responde 404, e não 403: distinguir os casos transformaria o endpoint
    /// num verificador de quem pediu exportação.
    /// </summary>
    [Fact]
    public async Task Pacote_de_outro_titular_responde_404()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var dono = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var curioso = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var pedida = await Solicitar(dono, TipoDeSolicitacao.Exportacao);
        await Processar(pedida.Id);

        var resposta = await curioso.Cliente.GetAsync($"/api/v1/privacidade/solicitacoes/{pedida.Id}/arquivo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>Clique duplo no botão não vira dois pacotes.</summary>
    [Fact]
    public async Task Pedido_repetido_devolve_o_mesmo()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var primeira = await Solicitar(formando, TipoDeSolicitacao.Exportacao);
        var segunda = await Solicitar(formando, TipoDeSolicitacao.Exportacao);

        segunda.Id.ShouldBe(primeira.Id);
    }

    /// <summary>Eliminação é irreversível: a senha é a diferença entre o titular e a aba esquecida aberta.</summary>
    [Fact]
    public async Task Exclusao_sem_senha_e_recusada()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await formando.Cliente.PostAsJsonAsync(
            "/api/v1/privacidade/solicitacoes",
            new SolicitarPrivacidadeDTO(TipoDeSolicitacao.Exclusao, null),
            Json,
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Exclusao_com_senha_errada_e_recusada()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await formando.Cliente.PostAsJsonAsync(
            "/api/v1/privacidade/solicitacoes",
            new SolicitarPrivacidadeDTO(TipoDeSolicitacao.Exclusao, "Nao@Ehessa123"),
            Json,
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await resposta.Codigo(Ct)).ShouldBe("privacidade.senha_invalida");
    }

    /// <summary>
    /// O critério de aceite inteiro numa prova só: depois da anonimização, nenhuma consulta recupera
    /// nome, CPF, e-mail ou telefone — e a parcela, o recebimento e a adesão continuam de pé.
    /// </summary>
    [Fact]
    public async Task Anonimizacao_apaga_o_que_identifica_e_preserva_o_lancamento()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        await PreencherCadastro(formando);

        var vinculoId = await VinculoDe(formando.UsuarioId, formaturaId);

        using (var escopo = fabrica.Services.CreateScope())
        {
            var anonimizacao = escopo.ServiceProvider.GetRequiredService<IAnonimizacaoDeTitular>();

            (await anonimizacao.Anonimizar(formando.UsuarioId, Ct)).Sucesso.ShouldBeTrue();
        }

        await using var contexto = fabrica.ContextoDe(null);

        var usuario = await contexto.Users.SingleAsync(u => u.Id == formando.UsuarioId, Ct);

        usuario.Nome.ShouldBe(AnonimizacaoDeTitular.Marcador(formando.UsuarioId));
        usuario.Email.ShouldEndWith("@anonimizado.invalid");
        usuario.PhoneNumber.ShouldBeNull();
        usuario.Ativo.ShouldBeFalse();
        usuario.AnonimizadoEm.ShouldNotBeNull();

        var perfil = await contexto.PerfisDeFormandos.IgnoreQueryFilters().SingleAsync(p => p.VinculoId == vinculoId, Ct);

        perfil.NomeCompleto.ShouldBeNull();
        perfil.Cpf.ShouldBeNull();
        perfil.Telefone.ShouldBeNull();
        perfil.Endereco.Cep.ShouldBeNull();
        perfil.ContatoDeEmergencia.Nome.ShouldBeNull();

        // A linha continua amarrada ao vínculo: é o que mantém parcela e recebimento apontando para
        // alguém, sem que esse alguém seja identificável.
        perfil.VinculoId.ShouldBe(vinculoId);
        (await contexto.Vinculos.IgnoreQueryFilters().AnyAsync(v => v.Id == vinculoId, Ct)).ShouldBeTrue();
    }

    /// <summary>Idempotente: o worker pode repetir uma tentativa que caiu no meio.</summary>
    [Fact]
    public async Task Anonimizar_duas_vezes_nao_falha()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        using var escopo = fabrica.Services.CreateScope();
        var anonimizacao = escopo.ServiceProvider.GetRequiredService<IAnonimizacaoDeTitular>();

        var primeira = await anonimizacao.Anonimizar(formando.UsuarioId, Ct);
        var segunda = await anonimizacao.Anonimizar(formando.UsuarioId, Ct);

        primeira.Sucesso.ShouldBeTrue();
        segunda.Sucesso.ShouldBeTrue();
        segunda.Valor.ShouldBe(primeira.Valor);
    }

    /// <summary>A anonimização é a prova de que o direito foi atendido — ela mesma é auditada.</summary>
    [Fact]
    public async Task Anonimizacao_grava_evento_de_auditoria()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        using (var escopo = fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<IAnonimizacaoDeTitular>().Anonimizar(formando.UsuarioId, Ct);

        await using var contexto = fabrica.ContextoDe(null);

        var evento = await contexto.Eventos.SingleOrDefaultAsync(
            e => e.Nome == "privacidade.titular_anonimizado" && e.UsuarioId == formando.UsuarioId,
            Ct
        );

        evento.ShouldNotBeNull();
        evento.Dados.ShouldNotBeNull();
        evento.Dados!.ShouldContain("marcador");
    }

    private static async Task PreencherCadastro(MembroDeTeste membro)
    {
        var resposta = await membro.Cliente.PutAsJsonAsync(
            "/api/v1/formandos/eu",
            new
            {
                pessoais = new
                {
                    nome_completo = "Ana Souza",
                    cpf = "39053344705",
                    telefone = "+5541999990000",
                },
                endereco = new
                {
                    cep = "80000000",
                    logradouro = "Rua das Flores",
                    numero = "100",
                    bairro = "Centro",
                    cidade = "Curitiba",
                    uf = "PR",
                },
                contato_de_emergencia = new
                {
                    nome = "Marta Souza",
                    telefone = "+5541988880000",
                    parentesco = "mãe",
                },
            },
            Json,
            Ct
        );

        resposta.EnsureSuccessStatusCode();
    }

    private async Task<Guid> VinculoDe(Guid usuarioId, Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto
            .Vinculos.IgnoreQueryFilters()
            .Where(v => v.UsuarioId == usuarioId && v.FormaturaId == formaturaId)
            .Select(v => v.Id)
            .SingleAsync(Ct);
    }

    private static async Task<SolicitacaoDePrivacidadeDTO> Solicitar(MembroDeTeste membro, TipoDeSolicitacao tipo)
    {
        var resposta = await membro.Cliente.PostAsJsonAsync("/api/v1/privacidade/solicitacoes", new SolicitarPrivacidadeDTO(tipo, null), Json, Ct);

        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<SolicitacaoDePrivacidadeDTO>(Json, Ct))!;
    }

    private static async Task<List<SolicitacaoDePrivacidadeDTO>> Listar(MembroDeTeste membro) =>
        (await membro.Cliente.GetFromJsonAsync<List<SolicitacaoDePrivacidadeDTO>>("/api/v1/privacidade/solicitacoes", Json, Ct))!;

    /// <summary>Roda o que o worker rodaria, sem subir o worker.</summary>
    private async Task<bool> Processar(Guid solicitacaoId)
    {
        using var escopo = fabrica.Services.CreateScope();

        return await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePrivacidadeService>().Processar(solicitacaoId, Ct);
    }
}

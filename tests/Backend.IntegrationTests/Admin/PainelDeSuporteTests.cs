using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Admin;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Eventos.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Admin;

/// <summary>
/// O painel de suporte: a fronteira que o protege, o CPF mascarado e o rastro de cada ação.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class PainelDeSuporteTests(ApiFactory fabrica)
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>As três telas e as quatro ações, para o teste de fronteira não esquecer nenhuma.</summary>
    public static TheoryData<string, string> Rotas =>
        new()
        {
            { "GET", "/api/v1/admin/suporte/busca?termo=medicina" },
            { "GET", $"/api/v1/admin/suporte/formaturas/{Guid.Empty}" },
            { "GET", $"/api/v1/admin/suporte/usuarios/{Guid.Empty}" },
            { "POST", $"/api/v1/admin/suporte/formaturas/{Guid.Empty}/ativar-assinatura" },
            { "POST", $"/api/v1/admin/suporte/usuarios/{Guid.Empty}/reenviar-confirmacao" },
            { "POST", $"/api/v1/admin/suporte/usuarios/{Guid.Empty}/redefinir-senha" },
            { "POST", $"/api/v1/admin/suporte/usuarios/{Guid.Empty}/desbloquear" },
            { "POST", $"/api/v1/admin/suporte/formaturas/{Guid.Empty}/pagamentos/{Guid.Empty}/estornar" },
            { "GET", "/api/v1/admin/suporte/pagamentos?ano=2026&mes=9" },
        };

    /// <summary>
    /// Critério de aceite da Sprint 16: quem não é <c>Administrador</c> recebe 403 em todas.
    /// </summary>
    /// <remarks>
    /// Inclui o Presidente de uma turma de propósito: papel de formatura é papel de turma, e não
    /// perfil de plataforma. O painel enxerga <b>todas</b> as turmas — quem entrasse nele com o
    /// papel da própria turma passaria a ler as outras.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Quem_nao_e_administrador_recebe_403_em_todas_as_rotas_do_painel(string metodo, string rota)
    {
        // Arrange
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        // Act
        var resposta = await presidente.Cliente.SendAsync(new HttpRequestMessage(new HttpMethod(metodo), rota), Ct);

        // Assert
        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Sem_token_o_painel_responde_401(string metodo, string rota)
    {
        var resposta = await fabrica.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(metodo), rota), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>A busca acha a turma pelo nome e a conta pelo e-mail, na mesma caixa.</summary>
    [Fact]
    public async Task Busca_acha_turma_e_conta_pelo_mesmo_termo()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var membro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var nomeDaTurma = await NomeDaTurma(formaturaId);
        var suporte = await Suporte();

        var resultado = await suporte.GetFromJsonAsync<ResultadoDaBuscaDTO>($"/api/v1/admin/suporte/busca?termo={nomeDaTurma}", Json, Ct);

        resultado.ShouldNotBeNull();
        resultado.Turmas.ShouldContain(t => t.Id == formaturaId);
        resultado.Turmas.First(t => t.Id == formaturaId).Membros.ShouldBe(1);
        _ = membro;
    }

    /// <summary>Termo curto não varre o banco: as duas listas voltam vazias.</summary>
    [Fact]
    public async Task Busca_com_menos_de_tres_letras_volta_vazia()
    {
        var suporte = await Suporte();

        var resultado = await suporte.GetFromJsonAsync<ResultadoDaBuscaDTO>("/api/v1/admin/suporte/busca?termo=ab", Json, Ct);

        resultado.ShouldNotBeNull();
        resultado.Turmas.ShouldBeEmpty();
        resultado.Usuarios.ShouldBeEmpty();
    }

    /// <summary>
    /// Critério de aceite: o CPF sai mascarado em todas as telas do painel.
    /// </summary>
    [Fact]
    public async Task Tela_da_turma_mostra_o_cpf_mascarado()
    {
        // Arrange — um formando com cadastro preenchido.
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCpf(formaturaId, formando.UsuarioId, "52998224725");
        var suporte = await Suporte();

        // Act
        var turma = await suporte.GetFromJsonAsync<TurmaNoSuporteDTO>($"/api/v1/admin/suporte/formaturas/{formaturaId}", Json, Ct);

        // Assert
        turma.ShouldNotBeNull();
        var linha = turma.Membros.ShouldHaveSingleItem();
        linha.Cpf.ShouldBe("***.982.247-**");
        linha.Cpf!.ShouldNotContain("52998224725");
    }

    /// <summary>A tela da conta mostra os vínculos da pessoa, inclusive em turma que não é a da sessão.</summary>
    [Fact]
    public async Task Tela_da_conta_mostra_as_turmas_da_pessoa()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var membro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        var suporte = await Suporte();

        var conta = await suporte.GetFromJsonAsync<UsuarioNoSuporteDTO>($"/api/v1/admin/suporte/usuarios/{membro.UsuarioId}", Json, Ct);

        conta.ShouldNotBeNull();
        conta.Id.ShouldBe(membro.UsuarioId);
        conta.Vinculos.ShouldHaveSingleItem().Papel.ShouldBe(PapelNaFormatura.Tesoureiro);
        conta.TentativasFalhas.ShouldBe(0);
    }

    /// <summary>
    /// Critério de aceite: ativar assinatura pelo painel funciona e grava evento com autor.
    /// </summary>
    /// <remarks>
    /// É a razão de o painel existir — "paguei e a turma não ativou" —, e o evento com a formatura
    /// no corpo é o que faz a comissão ver na trilha dela que quem ativou foi o suporte.
    /// </remarks>
    [Fact]
    public async Task Ativar_assinatura_pelo_painel_ativa_a_turma_e_grava_evento_com_autor()
    {
        // Arrange — turma aguardando pagamento, com assinatura pendente: o webhook que se perdeu.
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        await CriarAssinaturaPendente(formaturaId);
        var (suporte, suporteId) = await SuporteComId();

        // Act
        var resposta = await suporte.PostAsync($"/api/v1/admin/suporte/formaturas/{formaturaId}/ativar-assinatura", null, Ct);

        // Assert
        resposta.EnsureSuccessStatusCode();
        var turma = (await resposta.Content.ReadFromJsonAsync<TurmaNoSuporteDTO>(Json, Ct))!;
        turma.Status.ShouldBe(nameof(StatusDaFormatura.Ativa));
        turma.Assinatura!.Status.ShouldBe(nameof(StatusDaAssinatura.Ativa));
        turma.Assinatura.VigenteAte.ShouldNotBeNull();

        await using var contexto = fabrica.ContextoDe(null);
        var evento = await contexto
            .Eventos.Where(e => e.Nome == NomesDeAuditoria.SuporteAssinaturaAtivada && e.FormaturaId == formaturaId)
            .SingleOrDefaultAsync(Ct);

        evento.ShouldNotBeNull();
        evento.UsuarioId.ShouldBe(suporteId);
        evento.Dados.ShouldNotBeNull();
        evento.Dados.ShouldContain("antes");
        evento.Dados.ShouldContain("depois");
    }

    /// <summary>Segunda vez responde 200 sem gravar um segundo evento: a ação é idempotente.</summary>
    [Fact]
    public async Task Ativar_assinatura_duas_vezes_grava_um_evento_so()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        await CriarAssinaturaPendente(formaturaId);
        var suporte = await Suporte();

        (await suporte.PostAsync($"/api/v1/admin/suporte/formaturas/{formaturaId}/ativar-assinatura", null, Ct)).EnsureSuccessStatusCode();
        (await suporte.PostAsync($"/api/v1/admin/suporte/formaturas/{formaturaId}/ativar-assinatura", null, Ct)).EnsureSuccessStatusCode();

        await using var contexto = fabrica.ContextoDe(null);
        var eventos = await contexto.Eventos.CountAsync(e => e.Nome == NomesDeAuditoria.SuporteAssinaturaAtivada && e.FormaturaId == formaturaId, Ct);

        eventos.ShouldBe(1);
    }

    /// <summary>Turma que nunca contratou não é ativada pelo painel: escolher plano é da comissão.</summary>
    [Fact]
    public async Task Ativar_turma_sem_assinatura_devolve_409()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false);
        var suporte = await Suporte();

        var resposta = await suporte.PostAsync($"/api/v1/admin/suporte/formaturas/{formaturaId}/ativar-assinatura", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("suporte.assinatura_inexistente");
    }

    /// <summary>As três ações de conta gravam evento sem formatura — elas são da conta, não de turma nenhuma.</summary>
    [Theory]
    [InlineData("reenviar-confirmacao", NomesDeAuditoria.SuporteConfirmacaoReenviada)]
    [InlineData("redefinir-senha", NomesDeAuditoria.SuporteRedefinicaoDisparada)]
    [InlineData("desbloquear", NomesDeAuditoria.SuporteContaDesbloqueada)]
    public async Task Acao_de_conta_grava_evento_sem_formatura(string acao, string nomeDoEvento)
    {
        // Arrange
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var alvo = FormaturaDeTeste.IdDoUsuario(tokens.AccessToken);
        var (suporte, suporteId) = await SuporteComId();

        // Act
        var resposta = await suporte.PostAsync($"/api/v1/admin/suporte/usuarios/{alvo}/{acao}", null, Ct);

        // Assert
        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var evento = await EventoDoAlvo(nomeDoEvento, suporteId, alvo);

        evento.ShouldNotBeNull();
        evento.FormaturaId.ShouldBeNull();
    }

    /// <summary>O e-mail vai mascarado no corpo do evento: a trilha não é uma segunda lista de e-mails.</summary>
    [Fact]
    public async Task Evento_de_acao_de_conta_guarda_o_email_mascarado()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var alvo = FormaturaDeTeste.IdDoUsuario(tokens.AccessToken);
        var (suporte, suporteId) = await SuporteComId();

        await suporte.PostAsync($"/api/v1/admin/suporte/usuarios/{alvo}/desbloquear", null, Ct);

        var evento = await EventoDoAlvo(NomesDeAuditoria.SuporteContaDesbloqueada, suporteId, alvo);

        evento.ShouldNotBeNull();
        evento.Dados.ShouldNotBeNull();
        evento.Dados.ShouldContain("*");
    }

    [Fact]
    public async Task Acao_sobre_conta_inexistente_devolve_404()
    {
        var suporte = await Suporte();

        var resposta = await suporte.PostAsync($"/api/v1/admin/suporte/usuarios/{Guid.CreateVersion7()}/desbloquear", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await resposta.Codigo(Ct)).ShouldBe("suporte.conta_nao_encontrada");
    }

    /// <summary>
    /// O evento de auditoria daquele nome, autor e alvo.
    /// </summary>
    /// <remarks>
    /// O alvo é filtrado <b>em memória</b>: a conta de administrador é a mesma em toda a suíte, e
    /// filtrar só por nome e autor acharia os eventos dos testes vizinhos. <c>Dados</c> é uma coluna
    /// <c>jsonb</c>, e um <c>Contains</c> sobre ela não traduz para a busca em texto que se espera.
    /// </remarks>
    /// <param name="nome">Nome do evento.</param>
    /// <param name="autorId">Quem do suporte executou.</param>
    /// <param name="alvoId">Conta sobre a qual a ação foi feita.</param>
    private async Task<Evento?> EventoDoAlvo(string nome, Guid autorId, Guid alvoId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        var candidatos = await contexto.Eventos.AsNoTracking().Where(e => e.Nome == nome && e.UsuarioId == autorId).ToListAsync(Ct);

        return candidatos.SingleOrDefault(e => e.Dados?.Contains(alvoId.ToString(), StringComparison.OrdinalIgnoreCase) == true);
    }

    private async Task<HttpClient> Suporte() => (await SuporteComId()).Cliente;

    private async Task<(HttpClient Cliente, Guid Id)> SuporteComId()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);

        return (cliente.ComToken(tokens.AccessToken), FormaturaDeTeste.IdDoUsuario(tokens.AccessToken));
    }

    private async Task<string> NomeDaTurma(Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Formaturas.Where(f => f.Id == formaturaId).Select(f => f.Nome).SingleAsync(Ct);
    }

    private async Task CriarAssinaturaPendente(Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        var plano = await contexto.Planos.FirstAsync(Ct);
        contexto.Assinaturas.Add(new Assinatura { PlanoId = plano.Id });

        await contexto.SaveChangesAsync(Ct);
    }

    private async Task PreencherCpf(Guid formaturaId, Guid usuarioId, string cpf)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == usuarioId).Select(v => v.Id).SingleAsync(Ct);
        var perfil = new PerfilDoFormando { VinculoId = vinculoId };
        perfil.Aplicar(new AtualizarPerfil(new DadosPessoais("Ana Souza", null, cpf, null, null, null, new DateOnly(2000, 5, 20), null), null, null));

        contexto.PerfisDeFormandos.Add(perfil);

        await contexto.SaveChangesAsync(Ct);
    }
}

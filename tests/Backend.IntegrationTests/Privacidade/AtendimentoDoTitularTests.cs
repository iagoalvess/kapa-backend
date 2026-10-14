using System.Net.Http.Json;
using System.Text.Json;
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
/// Consultas internas e limpeza cadastral usadas no atendimento pelo suporte.
/// </summary>
/// <remarks>
/// É onde as promessas da Sprint 14 param de ser texto: a exportação precisa trazer tudo, a
/// anonimização precisa apagar o que identifica <b>e</b> deixar o extrato da turma de pé, e o pacote
/// de um titular não pode abrir para outro.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class AtendimentoDoTitularTests(ApiFactory fabrica)
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Meus_dados_trazem_conta_turma_e_consentimentos()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var dados = await Ler(formando.UsuarioId);

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

        var dados = await Ler(membro.UsuarioId);

        dados!.Turmas.Select(turma => turma.FormaturaId).ShouldBe([primeira, segunda], ignoreOrder: true);
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
        usuario.ReceberComunicacaoDoKapa.ShouldBeFalse();
        usuario.PasswordHash.ShouldBeNull();
        usuario.AnonimizadoEm.ShouldNotBeNull();

        var perfil = await contexto.PerfisDeFormandos.IgnoreQueryFilters().SingleAsync(p => p.VinculoId == vinculoId, Ct);

        perfil.NomeCompleto.ShouldBeNull();
        perfil.Cpf.ShouldBeNull();
        perfil.Telefone.ShouldBeNull();
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

    private async Task<MeusDados?> Ler(Guid titular)
    {
        using var escopo = fabrica.Services.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<IPrivacidadeRepository>().ObterMeusDadosDeTodasAsFormaturas(titular, Ct);
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
}

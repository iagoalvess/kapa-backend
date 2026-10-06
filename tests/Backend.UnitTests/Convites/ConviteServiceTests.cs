using System.Security.Cryptography;
using System.Text;
using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Common;
using Backend.Business.Convites.Interfaces;
using Backend.Business.Convites.Models;
using Backend.Business.Convites.Services;
using Backend.Business.Convites.Validators;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Convites;

/// <summary>
/// Regras do convite: quem oferece qual papel, o que o banco guarda e o que o aceite confere.
/// </summary>
public sealed class ConviteServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid UsuarioId = Guid.CreateVersion7();
    private static readonly Guid FormaturaId = Guid.CreateVersion7();

    private readonly IConviteRepository _convites = Substitute.For<IConviteRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IEmailService _emails = Substitute.For<IEmailService>();
    private readonly IAssinaturaRepository _assinaturas = Substitute.For<IAssinaturaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IAdesaoRepository _adesoes = Substitute.For<IAdesaoRepository>();
    private readonly IPlanoDeCobrancaRepository _planos = Substitute.For<IPlanoDeCobrancaRepository>();

    public ConviteServiceTests()
    {
        _tokens.CalcularHash(Arg.Any<string>()).Returns(chamada => Hash(chamada.Arg<string>()));
        _adesoes.ObterTermoVigente(Arg.Any<CancellationToken>()).Returns(new VersaoDoTermo(Guid.CreateVersion7(), 1, "Termo", DateTime.UtcNow));
        _planos.ExisteVigente(Arg.Any<CancellationToken>()).Returns(true);
        _formaturas.ObterDetalheDeTodasAsFormaturas(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Formatura(StatusDaFormatura.Ativa));
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<ParDeTokens>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<ParDeTokens>>>>()(CancellationToken.None));
        _convites.ConsumirUsoDeTodasAsFormaturas(default, default, default).ReturnsForAnyArgs(true);
        _vinculos.ObterPapelAtivo(default, default, default).ReturnsForAnyArgs((string?)null);
        _auth
            .EmitirSessaoDeFormatura(default, default, default!, default!, default, default)
            .ReturnsForAnyArgs(new ParDeTokens("acesso", DateTime.UtcNow, "refresh"));
    }

    private ConviteService Servico =>
        new(
            _convites,
            _vinculos,
            _formaturas,
            new VagasDoPlano(_assinaturas, _vinculos),
            _adesoes,
            _planos,
            _usuarios,
            _auth,
            _tokens,
            _emails,
            new CriarConviteValidator(),
            Options.Create(new AplicacaoSettings { Nome = "Kapa", UrlDoFrontend = "https://app.kapa" }),
            _unitOfWork
        );

    /// <summary>Sem termo e catálogo, o formando não teria a que aderir — e o gate o prenderia (Sprint 47, D34).</summary>
    [Fact]
    public async Task Convite_de_formando_sem_termo_publicado_e_recusado()
    {
        // Arrange
        _adesoes.ObterTermoVigente(Arg.Any<CancellationToken>()).Returns((VersaoDoTermo?)null);
        var presidente = Guid.CreateVersion7();
        _vinculos.ObterPapelAtivo(presidente, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(PapelNaFormatura.Presidente);

        // Act
        var resultado = await Servico.Criar(Guid.CreateVersion7(), presidente, new CriarConvite(null, null), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("convite.sem_termo_ou_plano");
        await _convites.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Comissao)]
    public async Task Comissao_convidando_para_papel_de_gestao_devolve_403_sem_gravar(string papel)
    {
        // Arrange
        AutorCom(PapelNaFormatura.Comissao);

        // Act
        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", papel), Ct);

        // Assert
        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.papel_restrito");
        await _convites.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Presidente_convida_para_tesoureiro()
    {
        AutorCom(PapelNaFormatura.Presidente);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", PapelNaFormatura.Tesoureiro), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _convites.Received(1).Adicionar(Arg.Is<Convite>(c => c.Papel == PapelNaFormatura.Tesoureiro), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Revisão de segurança de 05/10/2026: Presidente não entra por convite — senão a senha do presidente bastava para
    /// criar outro e confirmar a troca da chave no e-mail dele. Entra como comissão e é promovido, com confirmação.
    /// </summary>
    [Fact]
    public async Task Nem_o_presidente_convida_para_presidente()
    {
        AutorCom(PapelNaFormatura.Presidente);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", PapelNaFormatura.Presidente), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.presidente_por_promocao");
        await _convites.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>Link da turma circula em grupo: nem o Presidente abre um link de Tesoureiro.</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Presidente)]
    public async Task Link_da_turma_so_convida_formando(string papel)
    {
        AutorCom(PapelNaFormatura.Presidente);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite(null, papel), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.link_so_para_formando");
        await _convites.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>O token só existe no link devolvido; o banco recebe o SHA-256 dele.</summary>
    [Fact]
    public async Task Nominal_grava_so_o_hash_vale_sete_dias_um_uso_e_enfileira_o_email()
    {
        Convite? gravado = null;
        await _convites.Adicionar(Arg.Do<Convite>(c => gravado = c), Arg.Any<CancellationToken>());

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite(" ana@exemplo.com ", null), Ct);

        resultado.Sucesso.ShouldBeTrue();
        var token = resultado.Valor.Link.Split('/').Last();
        resultado.Valor.Link.ShouldStartWith("https://app.kapa/convite/");
        gravado.ShouldNotBeNull();
        gravado.TokenHash.ShouldBe(Hash(token));
        gravado.TokenHash.ShouldNotContain(token);
        gravado.Email.ShouldBe("ana@exemplo.com");
        gravado.Papel.ShouldBe(PapelNaFormatura.Formando);
        gravado.UsosMaximos.ShouldBe(1);
        gravado.ExpiraEm.ShouldBe(DateTime.UtcNow.AddDays(Convite.DiasDeValidadeDoNominal), TimeSpan.FromMinutes(1));
        await _emails
            .Received(1)
            .Enfileirar(Arg.Is<NovoEmail>(e => e.Para == "ana@exemplo.com" && e.CorpoHtml.Contains(token)), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Validade fixa de 30 dias e entradas ilimitadas — o teto é o limite do plano.</summary>
    [Fact]
    public async Task Link_da_turma_nao_manda_email_vale_trinta_dias_e_nao_limita_entradas()
    {
        Convite? gravado = null;
        await _convites.Adicionar(Arg.Do<Convite>(c => gravado = c), Arg.Any<CancellationToken>());

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite(null, null), Ct);

        resultado.Sucesso.ShouldBeTrue();
        gravado!.Email.ShouldBeNull();
        gravado.UsosMaximos.ShouldBeNull();
        gravado.ExpiraEm.ShouldBe(DateTime.UtcNow.AddDays(Convite.DiasDeValidadeDoLink), TimeSpan.FromMinutes(1));
        gravado.Token.ShouldBe(resultado.Valor.Link.Split('/').Last());
        await _emails.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
    }

    /// <summary>Um link vigente por turma: o novo revoga o que vale, e o expirado fica como está.</summary>
    [Fact]
    public async Task Link_da_turma_novo_revoga_so_o_vigente()
    {
        var vigente = Link();
        var expirado = Link();
        expirado.ExpiraEm = DateTime.UtcNow.AddDays(-1);
        _convites.ListarLinksNaoRevogadosParaEdicao(Arg.Any<CancellationToken>()).Returns([vigente, expirado]);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite(null, null), Ct);

        resultado.Sucesso.ShouldBeTrue();
        vigente.StatusEm(DateTime.UtcNow).ShouldBe(StatusDoConvite.Revogado);
        expirado.StatusEm(DateTime.UtcNow).ShouldBe(StatusDoConvite.Expirado);
    }

    [Fact]
    public async Task Nominal_nao_grava_o_token_nem_mexe_no_link_da_turma()
    {
        Convite? gravado = null;
        await _convites.Adicionar(Arg.Do<Convite>(c => gravado = c), Arg.Any<CancellationToken>());

        await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", null), Ct);

        gravado!.Token.ShouldBeNull();
        await _convites.DidNotReceiveWithAnyArgs().ListarLinksNaoRevogadosParaEdicao(Ct);
    }

    public static TheoryData<string> Inutilizaveis => ["inexistente", "expirado", "revogado", "esgotado", "turma suspensa"];

    /// <summary>A mesma resposta para todos: diferenciar confirmaria quais tokens existem.</summary>
    [Theory]
    [MemberData(nameof(Inutilizaveis))]
    public async Task Convite_inutilizavel_devolve_sempre_convite_invalido(string caso)
    {
        var convite = Link();
        switch (caso)
        {
            case "expirado":
                convite.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
                break;
            case "revogado":
                convite.Revogar(DateTime.UtcNow);
                break;
            case "esgotado":
                convite.UsosMaximos = 3;
                convite.UsosFeitos = 3;
                break;
            case "turma suspensa":
                _formaturas
                    .ObterDetalheDeTodasAsFormaturas(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(Formatura(StatusDaFormatura.Suspensa));
                break;
        }

        if (caso != "inexistente")
            Existe(convite);

        var consulta = await Servico.ObterPublico("token", Ct);
        var aceite = await Aceitar();

        consulta.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.invalido");
        aceite.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.invalido");
        consulta.PrimeiroErro.Mensagem.ShouldBe(aceite.PrimeiroErro.Mensagem);
    }

    [Fact]
    public async Task Consulta_publica_traz_so_turma_instituicao_e_papel()
    {
        Existe(Link());

        var resultado = await Servico.ObterPublico("token", Ct);

        resultado.Valor.ShouldBe(new ConvitePublico("Medicina 2027.1", "UFPR", PapelNaFormatura.Formando, null));
    }

    [Fact]
    public async Task Nominal_aceito_por_outro_email_devolve_403_sem_consumir_uso()
    {
        var convite = Link();
        convite.Email = "ana@exemplo.com";
        Existe(convite);
        ContaCom("bruno@exemplo.com");

        var resultado = await Aceitar();

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.email_divergente");
        await _convites.DidNotReceiveWithAnyArgs().ConsumirUsoDeTodasAsFormaturas(default, default, Ct);
    }

    [Fact]
    public async Task Nominal_aceito_pelo_email_convidado_confere_sem_diferenciar_maiusculas()
    {
        var convite = Link();
        convite.Email = "ana@exemplo.com";
        Existe(convite);
        ContaCom("Ana@Exemplo.com");

        var resultado = await Aceitar();

        resultado.Sucesso.ShouldBeTrue();
    }

    [Fact]
    public async Task Quem_ja_participa_recebe_409_sem_consumir_uso()
    {
        Existe(Link());
        _vinculos.ObterPapelAtivo(UsuarioId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(PapelNaFormatura.Formando);

        var resultado = await Aceitar();

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.ja_vinculado");
        await _convites.DidNotReceiveWithAnyArgs().ConsumirUsoDeTodasAsFormaturas(default, default, Ct);
    }

    /// <summary>O papel é o do convite gravado; não há por onde a requisição escolher outro.</summary>
    [Fact]
    public async Task Aceite_cria_vinculo_e_sessao_com_o_papel_do_convite()
    {
        var convite = Link();
        convite.Papel = PapelNaFormatura.Tesoureiro;
        Existe(convite);

        var resultado = await Aceitar();

        resultado.Sucesso.ShouldBeTrue();
        await _vinculos
            .Received(1)
            .Adicionar(
                Arg.Is<VinculoDeFormatura>(v => v.UsuarioId == UsuarioId && v.Papel == PapelNaFormatura.Tesoureiro),
                Arg.Any<CancellationToken>()
            );
        await _convites
            .Received(1)
            .RegistrarAceite(Arg.Is<AceiteDeConvite>(a => a.UsuarioId == UsuarioId && a.ConviteId == convite.Id), Arg.Any<CancellationToken>());
        await _auth
            .Received(1)
            .EmitirSessaoDeFormatura(
                UsuarioId,
                convite.FormaturaId,
                PapelNaFormatura.Tesoureiro,
                "refresh",
                "127.0.0.1",
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Uso_esgotado_na_corrida_devolve_409_sem_vinculo()
    {
        Existe(Link());
        _convites.ConsumirUsoDeTodasAsFormaturas(default, default, Ct).ReturnsForAnyArgs(false);

        var resultado = await Aceitar();

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.esgotado");
        await _vinculos.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _auth.DidNotReceiveWithAnyArgs().EmitirSessaoDeFormatura(default, default, default!, default!, default, default, Ct);
    }

    /// <summary>Convite pessoal devolve quem foi removido, reativando o vínculo antigo em vez de criar outro.</summary>
    [Fact]
    public async Task Membro_removido_volta_por_convite_pessoal_reativando_o_vinculo_antigo()
    {
        var convite = Link();
        convite.Email = "ana@exemplo.com";
        Existe(convite);
        ContaCom("ana@exemplo.com");
        var antigo = Removido();

        var resultado = await Aceitar();

        resultado.Sucesso.ShouldBeTrue();
        antigo.Ativo.ShouldBeTrue();
        antigo.Papel.ShouldBe(PapelNaFormatura.Formando);
        await _vinculos.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>Pelo link da turma, a remoção valeria só até alguém repassar o link de novo.</summary>
    [Fact]
    public async Task Membro_removido_nao_volta_pelo_link_da_turma()
    {
        Existe(Link());
        var antigo = Removido();

        var resultado = await Aceitar();

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.vinculo_removido");
        antigo.Ativo.ShouldBeFalse();
        await _auth.DidNotReceiveWithAnyArgs().EmitirSessaoDeFormatura(default, default, default!, default!, default, default, Ct);
    }

    /// <summary>E-mail igual não prova posse: sem confirmar, qualquer um com o link encaminhado entraria.</summary>
    [Fact]
    public async Task Nominal_com_email_nao_confirmado_devolve_403_sem_consumir_uso()
    {
        var convite = Link();
        convite.Email = "ana@exemplo.com";
        Existe(convite);
        ContaCom("ana@exemplo.com", confirmado: false);

        var resultado = await Aceitar();

        var erro = resultado.Erros.ShouldHaveSingleItem();
        erro.Codigo.ShouldBe("convite.email_nao_confirmado");
        erro.Mensagem.ShouldContain("a*a@exemplo.com");
        await _convites.DidNotReceiveWithAnyArgs().ConsumirUsoDeTodasAsFormaturas(default, default, Ct);
    }

    /// <summary>
    /// Com a turma lotada, nenhum papel entra — a comissão também ocupa vaga.
    /// </summary>
    /// <remarks>
    /// Até 22/09/2026 a comissão entrava sempre, e o gratuito virava o produto inteiro: a turma toda
    /// entrava como "Comissão", aderia ao termo e pagava as parcelas sem contratar.
    /// </remarks>
    /// <param name="papel">Papel do convite.</param>
    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Comissao)]
    [InlineData(PapelNaFormatura.Formando)]
    public async Task Com_a_turma_lotada_nenhum_papel_entra(string papel)
    {
        var convite = Link();
        convite.Papel = papel;
        Existe(convite);
        TurmaLotada(limite: 5, ocupadas: 5);

        var resultado = await Aceitar();

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("plano.limite_de_formandos");
    }

    [Fact]
    public async Task Consulta_publica_do_convite_pessoal_mostra_o_email_mascarado()
    {
        var convite = Link();
        convite.Email = "joana.silva@exemplo.com";
        Existe(convite);

        var resultado = await Servico.ObterPublico("token", Ct);

        resultado.Valor.EmailMascarado.ShouldBe("j*********a@exemplo.com");
    }

    /// <summary>
    /// P3 da Sprint 16: passar do limite do plano bloqueia o convite de formando.
    /// </summary>
    /// <remarks>
    /// <c>LimiteDeFormandos</c> existia no plano e aparecia na vitrine, mas nada o aplicava — uma
    /// turma no Essencial (60) aceitava o 61º. O que se vende deixa de ser verdade no dia em que
    /// ninguém confere.
    /// </remarks>
    [Fact]
    public async Task Convite_de_formando_com_a_turma_lotada_devolve_409_sem_gravar()
    {
        TurmaLotada(limite: 60, ocupadas: 60);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", null), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("plano.limite_de_formandos");
        await _convites.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    /// <summary>A última vaga ainda entra: o bloqueio é ao <b>atingir</b> o limite, não antes.</summary>
    [Fact]
    public async Task Convite_de_formando_com_uma_vaga_livre_passa()
    {
        TurmaLotada(limite: 60, ocupadas: 59);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", null), Ct);

        resultado.Sucesso.ShouldBeTrue();
    }

    /// <summary>
    /// Convite de comissão também é recusado com a turma lotada: todo papel ocupa vaga, sem folga.
    /// </summary>
    /// <remarks>Para trocar o tesoureiro com a turma cheia, a comissão remove alguém antes ou troca de plano.</remarks>
    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Comissao)]
    public async Task Convite_de_comissao_com_a_turma_lotada_devolve_409(string papel)
    {
        AutorCom(PapelNaFormatura.Presidente);
        TurmaLotada(limite: 60, ocupadas: 60);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", papel), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("plano.limite_de_formandos");
        await _convites.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>O link da turma é criado antes de encher; quem barra a entrada é o aceite.</summary>
    [Fact]
    public async Task Aceite_de_formando_com_a_turma_lotada_devolve_409()
    {
        Existe(Link());
        TurmaLotada(limite: 60, ocupadas: 60);

        var resultado = await Aceitar();

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("plano.limite_de_formandos");
        await _vinculos.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>Catálogo sem nem o plano gratuito não tem limite a aplicar — quem barra é o status.</summary>
    [Fact]
    public async Task Sem_plano_no_catalogo_o_limite_nao_se_aplica()
    {
        _assinaturas.ObterPlanoVigenteDeTodasAsFormaturas(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Plano?)null);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", null), Ct);

        resultado.Sucesso.ShouldBeTrue();
    }

    /// <summary>
    /// Arma uma turma com o limite do plano e as vagas já ocupadas.
    /// </summary>
    /// <remarks>
    /// As vagas entram como vínculo <b>ativo e não desligado</b>: a conta é de quem ocupa lugar na
    /// turma, e não de quem tem o papel Formando — o presidente também se forma e também paga.
    /// Quem foi desligado sai do denominador (decisão 6 da Sprint 15).
    /// </remarks>
    /// <param name="limite">Quantos formandos o plano comporta.</param>
    /// <param name="ocupadas">Quantos vínculos ativos a turma já tem.</param>
    private void TurmaLotada(int limite, int ocupadas)
    {
        _assinaturas
            .ObterPlanoVigenteDeTodasAsFormaturas(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Plano { LimiteDeFormandos = limite });
        _vinculos
            .ContarMembros(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([
                new ContagemDeMembros(PapelNaFormatura.Formando, true, false, false, ocupadas - 1),
                new ContagemDeMembros(PapelNaFormatura.Presidente, true, false, false, 1),
                new ContagemDeMembros(PapelNaFormatura.Formando, true, true, false, 5),
                new ContagemDeMembros(PapelNaFormatura.Formando, false, false, false, 3),
            ]);
    }

    private VinculoDeFormatura Removido()
    {
        var antigo = new VinculoDeFormatura
        {
            UsuarioId = UsuarioId,
            Papel = PapelNaFormatura.Comissao,
            Ativo = false,
        };
        _vinculos.ObterParaEdicao(UsuarioId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(antigo);

        return antigo;
    }

    private Task<Result<ParDeTokens>> Aceitar() => Servico.Aceitar(UsuarioId, "token", "refresh", new OrigemDoAceite("127.0.0.1", "teste"), Ct);

    private void AutorCom(string papel) => _vinculos.ObterPapelAtivo(UsuarioId, FormaturaId, Arg.Any<CancellationToken>()).Returns(papel);

    private void Existe(Convite convite) => _convites.ObterPorHashDeTodasAsFormaturas(Hash("token"), Arg.Any<CancellationToken>()).Returns(convite);

    private void ContaCom(string email, bool confirmado = true) =>
        _usuarios
            .ObterDetalhe(UsuarioId, Arg.Any<CancellationToken>())
            .Returns(new UsuarioDetalhe(UsuarioId, "Conta", email, confirmado, true, [], DateTime.UtcNow, DateTime.UtcNow));

    private static Convite Link() => new() { TokenHash = Hash("token"), ExpiraEm = DateTime.UtcNow.AddDays(1) };

    /// <summary>Turma de teste. Contratada por padrão: no gratuito o formando nem é convidado.</summary>
    /// <param name="status">Status da turma.</param>
    /// <param name="jaContratou">Se a turma já contratou algum plano.</param>
    private static FormaturaDetalhe Formatura(StatusDaFormatura status, bool jaContratou = true) =>
        new(FormaturaId, "Medicina 2027.1", "UFPR", "Medicina", 2027, 1, null, null, status, null, jaContratou);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

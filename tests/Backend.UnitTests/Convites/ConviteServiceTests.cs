using System.Security.Cryptography;
using System.Text;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Common;
using Backend.Business.Convites.Interfaces;
using Backend.Business.Convites.Models;
using Backend.Business.Convites.Services;
using Backend.Business.Convites.Validators;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
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
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public ConviteServiceTests()
    {
        _tokens.CalcularHash(Arg.Any<string>()).Returns(chamada => Hash(chamada.Arg<string>()));
        _formaturas.ObterDetalhe(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Formatura(StatusDaFormatura.Ativa));
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
            _usuarios,
            _auth,
            _tokens,
            _emails,
            new CriarConviteValidator(),
            Options.Create(new AplicacaoSettings { Nome = "Kapa", UrlDoFrontend = "https://app.kapa" }),
            _unitOfWork
        );

    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Comissao)]
    [InlineData(PapelNaFormatura.Presidente)]
    public async Task Comissao_convidando_para_papel_de_gestao_devolve_403_sem_gravar(string papel)
    {
        // Arrange
        AutorCom(PapelNaFormatura.Comissao);

        // Act
        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", papel, null, null), Ct);

        // Assert
        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.papel_restrito");
        await _convites.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Presidente_convida_para_tesoureiro()
    {
        AutorCom(PapelNaFormatura.Presidente);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", PapelNaFormatura.Tesoureiro, null, null), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _convites.Received(1).Adicionar(Arg.Is<Convite>(c => c.Papel == PapelNaFormatura.Tesoureiro), Arg.Any<CancellationToken>());
    }

    /// <summary>Link da turma circula em grupo: nem o Presidente abre um link de Tesoureiro.</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Presidente)]
    public async Task Link_da_turma_so_convida_formando(string papel)
    {
        AutorCom(PapelNaFormatura.Presidente);

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite(null, papel, null, null), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.link_so_para_formando");
        await _convites.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>O token só existe no link devolvido; o banco recebe o SHA-256 dele.</summary>
    [Fact]
    public async Task Nominal_grava_so_o_hash_vale_sete_dias_um_uso_e_enfileira_o_email()
    {
        Convite? gravado = null;
        await _convites.Adicionar(Arg.Do<Convite>(c => gravado = c), Arg.Any<CancellationToken>());

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite(" ana@exemplo.com ", null, null, 50), Ct);

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

    [Fact]
    public async Task Link_da_turma_nao_manda_email_e_respeita_o_limite_pedido()
    {
        Convite? gravado = null;
        await _convites.Adicionar(Arg.Do<Convite>(c => gravado = c), Arg.Any<CancellationToken>());

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite(null, null, 90, 88), Ct);

        resultado.Sucesso.ShouldBeTrue();
        gravado!.Email.ShouldBeNull();
        gravado.UsosMaximos.ShouldBe(88);
        gravado.ExpiraEm.ShouldBe(DateTime.UtcNow.AddDays(90), TimeSpan.FromMinutes(1));
        await _emails.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
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
                _formaturas.ObterDetalhe(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Formatura(StatusDaFormatura.Suspensa));
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
        await _auth.DidNotReceiveWithAnyArgs().EmitirSessaoDeFormatura(default, default, default!, default!, default, Ct);
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
        await _auth.DidNotReceiveWithAnyArgs().EmitirSessaoDeFormatura(default, default, default!, default!, default, Ct);
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

    /// <summary>Antes de pagar, a comissão já se monta; formando, só com a turma ativa.</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro, true)]
    [InlineData(PapelNaFormatura.Comissao, true)]
    [InlineData(PapelNaFormatura.Formando, false)]
    public async Task Rascunho_aceita_convite_so_da_comissao(string papel, bool aceita)
    {
        AutorCom(PapelNaFormatura.Presidente);
        _formaturas.ObterDetalhe(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Formatura(StatusDaFormatura.Rascunho));

        var resultado = await Servico.Criar(FormaturaId, UsuarioId, new CriarConvite("ana@exemplo.com", papel, null, null), Ct);

        resultado.Sucesso.ShouldBe(aceita);
        if (!aceita)
            resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.formatura_nao_contratada");
    }

    [Theory]
    [InlineData(PapelNaFormatura.Comissao, true)]
    [InlineData(PapelNaFormatura.Formando, false)]
    public async Task Aceite_em_turma_aguardando_pagamento_so_para_a_comissao(string papel, bool aceita)
    {
        var convite = Link();
        convite.Papel = papel;
        Existe(convite);
        _formaturas.ObterDetalhe(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Formatura(StatusDaFormatura.AguardandoPagamento));

        var resultado = await Aceitar();

        resultado.Sucesso.ShouldBe(aceita);
        if (!aceita)
            resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("convite.invalido");
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

    private static FormaturaDetalhe Formatura(StatusDaFormatura status) =>
        new(FormaturaId, "Medicina 2027.1", "UFPR", "Medicina", 2027, 1, null, 80, status, DateTime.UtcNow, null, null);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

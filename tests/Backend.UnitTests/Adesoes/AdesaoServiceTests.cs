using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Adesoes.Services;
using Backend.Business.Adesoes.Validators;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Adesoes;

/// <summary>
/// O aceite do termo: cada recusa da sprint na ordem certa, e que recusar não grava nem gera nada.
/// </summary>
public sealed class AdesaoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid UsuarioId = Guid.CreateVersion7();
    private static readonly Guid VinculoId = Guid.CreateVersion7();
    private static readonly OrigemDoAceite Origem = new("203.0.113.7", "Navegador de teste");

    /// <summary>Os seis dígitos que o <c>UserManager</c> substituto aceita.</summary>
    private const string CodigoCerto = "123456";

    private readonly IAdesaoRepository _adesoes = Substitute.For<IAdesaoRepository>();
    private readonly IPlanoDeCobrancaRepository _planos = Substitute.For<IPlanoDeCobrancaRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IGeracaoDeParcelasService _geracao = Substitute.For<IGeracaoDeParcelasService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly UserManager<Usuario> _userManager = Substitute.For<UserManager<Usuario>>(
        Substitute.For<IUserStore<Usuario>>(),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null
    );
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IConviteDoEventoRepository _convites = Substitute.For<IConviteDoEventoRepository>();

    private readonly VersaoDoTermo _termo = new(Guid.CreateVersion7(), 1, "# Termo\n\nTexto do termo.", DateTime.UtcNow);
    private readonly PlanoDeCobranca _plano = PlanoVigente();

    public AdesaoServiceTests()
    {
        _adesoes.ObterTermoVigente(Arg.Any<CancellationToken>()).Returns(_termo);
        _planos.ObterVigente(Arg.Any<CancellationToken>()).Returns(_plano);
        // As duas portas: a escrita passa por `ObterMembro` (vínculo ativo) e a leitura do próprio
        // histórico por `ObterTitular`, que aceita também quem foi desligado (P5 da Sprint 15).
        var membro = new MembroDoPerfil(VinculoId, UsuarioId, "Ana", "ana@kapa.dev", PapelNaFormatura.Formando);
        _perfis.ObterMembro(FormaturaId, UsuarioId, Arg.Any<CancellationToken>()).Returns(membro);
        _perfis.ObterTitular(FormaturaId, UsuarioId, Arg.Any<CancellationToken>()).Returns(membro);
        _perfis.ObterDoVinculo(VinculoId, Arg.Any<CancellationToken>()).Returns(Perfil());
        _geracao
            .Gerar(VinculoId, Arg.Any<PlanoDeCobranca>(), Arg.Any<IReadOnlyCollection<ItemDeCobranca>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(24));
        _planos.ListarCesta(VinculoId, Arg.Any<CancellationToken>()).Returns([]);
        _formaturas
            .ObterDetalheDeTodasAsFormaturas(FormaturaId, Arg.Any<CancellationToken>())
            .Returns(
                new FormaturaDetalhe(FormaturaId, "Medicina 2027.1", "UFPR", "Medicina", 2027, 1, null, null, StatusDaFormatura.Ativa, null, true)
            );
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<int>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<int>>>>()(Ct));

        _userManager.FindByIdAsync(UsuarioId.ToString()).Returns(new Usuario { Id = UsuarioId, Email = "ana@kapa.dev" });
        _userManager
            .GenerateUserTokenAsync(Arg.Any<Usuario>(), TokenOptions.DefaultEmailProvider, AdesaoService.FinalidadeDoCodigo)
            .Returns(CodigoCerto);
        _userManager
            .VerifyUserTokenAsync(Arg.Any<Usuario>(), TokenOptions.DefaultEmailProvider, AdesaoService.FinalidadeDoCodigo, CodigoCerto)
            .Returns(true);
    }

    private AdesaoService Servico =>
        new(
            _adesoes,
            _planos,
            _perfis,
            _formaturas,
            _geracao,
            new EmailsDeAdesao(_email, Options.Create(new AplicacaoSettings())),
            new EmissaoDeConvites(
                _convites,
                Substitute.For<IEventoDaTurmaRepository>(),
                _formaturas,
                Substitute.For<IFormaturaAtual>(),
                NullLogger<EmissaoDeConvites>.Instance
            ),
            _userManager,
            new AderirAoTermoValidator(),
            _unitOfWork,
            NullLogger<AdesaoService>.Instance
        );

    private static PlanoDeCobranca PlanoVigente()
    {
        var plano = new PlanoDeCobranca { Nome = "Plano 2027" };
        plano.Itens.Add(ItemDeCobranca.Novo(plano.Id, new DadosDoItem(TipoDeCobranca.Mensalidade, null, 840_000, 24, 10, new DateOnly(2027, 3, 1))));
        plano.Itens.Add(Faixa(plano, "10 pessoas", 300_000, 10));
        plano.Itens.Add(Faixa(plano, "15 pessoas", 420_000, 15));
        plano.Vigorar(DateTime.UtcNow);

        return plano;
    }

    /// <summary>Uma faixa do grupo "Festa", que concede um convite por pessoa.</summary>
    private static ItemDeCobranca Faixa(PlanoDeCobranca plano, string descricao, long valor, int convites) =>
        ItemDeCobranca.NovoPacote(
            plano.Id,
            new DadosDoPacote(new DadosDoItem(TipoDeCobranca.Festa, descricao, valor, 10, 10, new DateOnly(2027, 3, 1)), "Festa", convites)
        );

    /// <summary>A cesta padrão dos testes: só o primeiro pacote, a mensalidade.</summary>
    private IReadOnlyList<ItemDeCobranca> Cesta => [_plano.Itens[0]];

    private static PerfilDoFormando Perfil(string? nome = "Ana Souza", string? cpf = "52998224725")
    {
        var perfil = new PerfilDoFormando { VinculoId = VinculoId };
        perfil.Aplicar(new AtualizarPerfil(new DadosPessoais(nome, cpf, null), null));

        return perfil;
    }

    private string HashCerto => AdesaoDoFormando.CalcularHash(_termo.Conteudo, SnapshotDoPlano.De(_plano, Cesta, DataUtils.Hoje()).ParaJson());

    private Task<Result<AdesaoDetalhe>> Aderir(string? hash = null, string codigo = CodigoCerto, IReadOnlyList<Guid>? pacotes = null) =>
        Servico.Aderir(
            FormaturaId,
            UsuarioId,
            new AderirAoTermo(hash ?? HashCerto, codigo, pacotes ?? [.. Cesta.Select(item => item.Id)]),
            Origem,
            Ct
        );

    [Fact]
    public async Task Aceite_grava_prova_plano_congelado_parcelas_e_email_num_so_commit()
    {
        // Act
        var resultado = await Aderir();

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _adesoes
            .Received(1)
            .Adicionar(
                Arg.Is<AdesaoDoFormando>(a =>
                    a.VinculoId == VinculoId
                    && a.TermoId == _termo.Id
                    && a.Versao == 1
                    && a.HashDoConteudo == HashCerto
                    && a.EnderecoIp == "203.0.113.7"
                    && a.UserAgent == "Navegador de teste"
                    && a.NomeCompleto == "Ana Souza"
                    && a.Cpf == "52998224725"
                    && a.EmailDoAceite == "ana@kapa.dev"
                    && a.LerPlano().TotalEmCentavos == 840_000
                ),
                Ct
            );
        await _geracao
            .Received(1)
            .Gerar(VinculoId, _plano, Arg.Is<IReadOnlyCollection<ItemDeCobranca>>(cesta => cesta.Single() == _plano.Itens[0]), Ct);
        await _planos
            .Received(1)
            .AdicionarEscolhas(Arg.Is<IEnumerable<EscolhaDaCesta>>(escolhas => escolhas.Single().ItemDeCobrancaId == _plano.Itens[0].Id), Ct);
        await _email.Received(1).Enfileirar(Arg.Is<NovoEmail>(e => e.Para == "ana@kapa.dev"), Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    /// <summary>
    /// A cesta é o que se cobra: a faixa da festa que ninguém escolheu não vira parcela, e o snapshot congela o pacote
    /// escolhido com o que ele concede (Sprint 47, D14/D16).
    /// </summary>
    [Fact]
    public async Task So_a_cesta_escolhida_entra_no_snapshot_e_nas_parcelas()
    {
        // Arrange
        var festa15 = _plano.Itens[2];
        var hash = AdesaoDoFormando.CalcularHash(_termo.Conteudo, SnapshotDoPlano.De(_plano, [festa15], DataUtils.Hoje()).ParaJson());

        // Act
        var resultado = await Aderir(hash, pacotes: [festa15.Id]);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        var plano = resultado.Valor.Plano;
        plano.TotalEmCentavos.ShouldBe(420_000);
        plano.Cesta.ShouldNotBeNull().ShouldHaveSingleItem().ConvitesDaFesta.ShouldBe(15);
        await _geracao.Received(1).Gerar(VinculoId, _plano, Arg.Is<IReadOnlyCollection<ItemDeCobranca>>(cesta => cesta.Single() == festa15), Ct);
        await _convites.Received(1).EmitirDosPacotes(FormaturaId, VinculoId, Arg.Any<string>(), Ct);
    }

    [Fact]
    public async Task Cesta_vazia_e_recusada_sem_gravar_nada()
    {
        (await Aderir(pacotes: [])).PrimeiroErro.Codigo.ShouldBe("adesao.cesta_sem_escolha");

        await NadaFoiGravado();
    }

    [Fact]
    public async Task Duas_faixas_do_mesmo_grupo_sao_recusadas()
    {
        (await Aderir(pacotes: [_plano.Itens[1].Id, _plano.Itens[2].Id])).PrimeiroErro.Codigo.ShouldBe("cobranca.faixa_invalida");

        await NadaFoiGravado();
    }

    /// <summary>A re-adesão a uma versão nova mantém a cesta contratada, mesmo que a tela mande outra (D4).</summary>
    [Fact]
    public async Task Readesao_mantem_a_cesta_contratada_e_nao_grava_escolha_nova()
    {
        // Arrange
        _planos.ListarCesta(VinculoId, Arg.Any<CancellationToken>()).Returns([_plano.Itens[0].Id]);

        // Act
        var resultado = await Aderir(pacotes: [_plano.Itens[1].Id]);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _planos.DidNotReceiveWithAnyArgs().AdicionarEscolhas(default!, Ct);
    }

    [Fact]
    public async Task Sem_termo_publicado_devolve_conflito()
    {
        _adesoes.ObterTermoVigente(Arg.Any<CancellationToken>()).Returns((VersaoDoTermo?)null);

        (await Aderir(new string('a', 64))).PrimeiroErro.Codigo.ShouldBe("adesao.sem_termo_publicado");
    }

    [Fact]
    public async Task Sem_plano_vigente_devolve_conflito()
    {
        _planos.ObterVigente(Arg.Any<CancellationToken>()).Returns((PlanoDeCobranca?)null);

        (await Aderir(new string('a', 64))).PrimeiroErro.Codigo.ShouldBe("adesao.sem_plano_vigente");
    }

    [Fact]
    public async Task Aderir_de_novo_a_mesma_versao_devolve_conflito()
    {
        _adesoes.JaAderiu(VinculoId, _termo.Id, Arg.Any<CancellationToken>()).Returns(true);

        (await Aderir()).PrimeiroErro.Codigo.ShouldBe("adesao.ja_aderiu");
        await NadaFoiGravado();
    }

    /// <summary>O hash cobre o plano: mudar o plano enquanto a pessoa lê invalida o que ela viu.</summary>
    [Fact]
    public async Task Hash_do_plano_anterior_devolve_termo_desatualizado_e_nao_gera_parcela()
    {
        var hashVisto = HashCerto;
        _plano.PercentualDeMulta = 200;

        (await Aderir(hashVisto)).PrimeiroErro.Codigo.ShouldBe("adesao.termo_desatualizado");
        await NadaFoiGravado();
    }

    [Theory]
    [InlineData(null, "52998224725")]
    [InlineData("Ana Souza", null)]
    public async Task Sem_nome_ou_cpf_no_cadastro_devolve_cadastro_incompleto(string? nome, string? cpf)
    {
        _perfis.ObterDoVinculo(VinculoId, Arg.Any<CancellationToken>()).Returns(Perfil(nome, cpf));

        (await Aderir()).PrimeiroErro.Codigo.ShouldBe("adesao.cadastro_incompleto");
        await NadaFoiGravado();
    }

    [Fact]
    public async Task Cadastro_nunca_aberto_lista_as_tres_pendencias()
    {
        _perfis.ObterDoVinculo(VinculoId, Arg.Any<CancellationToken>()).Returns((PerfilDoFormando?)null);

        var minha = (await Servico.ObterMinha(FormaturaId, UsuarioId, Ct)).Valor;

        minha.Pendencias.ShouldBe(["nomeCompleto", "cpf"]);
        minha.Adesao.ShouldBeNull();
    }

    /// <summary>Decisão de 14/09/2026: o mesmo CPF não adere duas vezes na turma.</summary>
    [Fact]
    public async Task Cpf_ja_usado_por_outro_vinculo_devolve_conflito()
    {
        _adesoes.CpfEmUsoPorOutro("52998224725", VinculoId, Arg.Any<CancellationToken>()).Returns(true);

        (await Aderir()).PrimeiroErro.Codigo.ShouldBe("adesao.cpf_em_uso");
        await NadaFoiGravado();
    }

    /// <summary>Sem o código do e-mail não há aceite: é ele que separa a sessão aberta de quem tem a caixa de entrada.</summary>
    [Fact]
    public async Task Codigo_que_nao_confere_nao_grava_adesao_nem_gera_parcela()
    {
        (await Aderir(codigo: "000000")).PrimeiroErro.Codigo.ShouldBe("adesao.codigo_invalido");
        await NadaFoiGravado();
    }

    /// <summary>O código é conferido por último: o cadastro incompleto aparece antes de a janela começar a correr.</summary>
    [Fact]
    public async Task Cadastro_incompleto_e_recusado_antes_de_conferir_o_codigo()
    {
        _perfis.ObterDoVinculo(VinculoId, Arg.Any<CancellationToken>()).Returns(Perfil(cpf: null));

        (await Aderir(codigo: "000000")).PrimeiroErro.Codigo.ShouldBe("adesao.cadastro_incompleto");
    }

    [Fact]
    public async Task Codigo_solicitado_vai_por_email_com_o_destino_mascarado()
    {
        var resultado = await Servico.SolicitarCodigo(FormaturaId, UsuarioId, Ct);

        resultado.Valor.Email.ShouldBe("an*@kapa.dev");
        await _email
            .Received(1)
            .Enfileirar(
                Arg.Is<NovoEmail>(e =>
                    e.Para == "ana@kapa.dev"
                    && e.Assunto.Contains(CodigoCerto, StringComparison.Ordinal)
                    && e.CorpoHtml.Contains(CodigoCerto, StringComparison.Ordinal)
                ),
                Ct
            );
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    /// <summary>Quem já aderiu não recebe código: seria um código para uma tela que recusa o aceite.</summary>
    [Fact]
    public async Task Codigo_nao_e_enviado_a_quem_ja_aderiu()
    {
        _adesoes.JaAderiu(VinculoId, _termo.Id, Arg.Any<CancellationToken>()).Returns(true);

        (await Servico.SolicitarCodigo(FormaturaId, UsuarioId, Ct)).PrimeiroErro.Codigo.ShouldBe("adesao.ja_aderiu");
        await _email.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
    }

    /// <summary>Se a geração recusa, nada é salvo: sem parcela não há adesão.</summary>
    [Fact]
    public async Task Geracao_recusada_nao_salva_a_adesao()
    {
        _geracao
            .Gerar(VinculoId, Arg.Any<PlanoDeCobranca>(), Arg.Any<IReadOnlyCollection<ItemDeCobranca>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Falha<int>(Erro.Conflito("cobranca.sem_plano_vigente", "Sem plano.")));

        (await Aderir()).Falhou.ShouldBeTrue();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Pdf_de_outro_formando_nao_existe_para_quem_nao_e_da_gestao()
    {
        var adesao = AdesaoGravada(vinculoId: Guid.CreateVersion7());

        var resultado = await Servico.ObterPdf(FormaturaId, adesao.Adesao.Id, UsuarioId, Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("adesao.nao_encontrada");
    }

    [Theory]
    [InlineData(PapelNaFormatura.Comissao)]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Presidente)]
    public async Task Gestao_baixa_o_pdf_de_qualquer_formando(string papel)
    {
        var adesao = AdesaoGravada(vinculoId: Guid.CreateVersion7());
        var membro = new MembroDoPerfil(VinculoId, UsuarioId, "Ana", "ana@kapa.dev", papel);
        _perfis.ObterTitular(FormaturaId, UsuarioId, Arg.Any<CancellationToken>()).Returns(membro);
        _perfis.ObterMembro(FormaturaId, UsuarioId, Arg.Any<CancellationToken>()).Returns(membro);

        (await Servico.ObterPdf(FormaturaId, adesao.Adesao.Id, UsuarioId, Ct)).Sucesso.ShouldBeTrue();
    }

    /// <summary>
    /// Desligar não muda o papel gravado: a comissão que saiu continuava baixando o termo dos colegas
    /// (nome, CPF mascarado, IP). O titular desligado só enxerga o próprio.
    /// </summary>
    [Fact]
    public async Task Gestao_desligada_nao_baixa_o_pdf_de_outro_formando()
    {
        var adesao = AdesaoGravada(vinculoId: Guid.CreateVersion7());
        _perfis
            .ObterTitular(FormaturaId, UsuarioId, Arg.Any<CancellationToken>())
            .Returns(new MembroDoPerfil(VinculoId, UsuarioId, "Ana", "ana@kapa.dev", PapelNaFormatura.Comissao));
        _perfis.ObterMembro(FormaturaId, UsuarioId, Arg.Any<CancellationToken>()).Returns((MembroDoPerfil?)null);

        (await Servico.ObterPdf(FormaturaId, adesao.Adesao.Id, UsuarioId, Ct)).PrimeiroErro.Codigo.ShouldBe("adesao.nao_encontrada");
    }

    [Fact]
    public async Task Lembrete_para_quem_ja_aderiu_a_versao_vigente_devolve_conflito()
    {
        _adesoes.JaAderiu(VinculoId, _termo.Id, Arg.Any<CancellationToken>()).Returns(true);

        (await Servico.Lembrar(FormaturaId, UsuarioId, Ct)).PrimeiroErro.Codigo.ShouldBe("adesao.ja_aderiu");
        await _email.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
    }

    private AdesaoComTermo AdesaoGravada(Guid vinculoId)
    {
        var adesao = new AdesaoComTermo(
            new AdesaoDoFormando
            {
                VinculoId = vinculoId,
                TermoId = _termo.Id,
                Versao = 1,
                HashDoConteudo = HashCerto,
                AceitoEm = DateTime.UtcNow,
                NomeCompleto = "Bruno Lima",
                Cpf = "11144477735",
                PlanoAceito = SnapshotDoPlano.De(_plano, Cesta, DataUtils.Hoje()).ParaJson(),
            },
            _termo.Conteudo
        );
        _adesoes.Obter(adesao.Adesao.Id, Arg.Any<CancellationToken>()).Returns(adesao);

        return adesao;
    }

    private async Task NadaFoiGravado()
    {
        await _adesoes.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _geracao.DidNotReceiveWithAnyArgs().Gerar(default, default!, default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }
}

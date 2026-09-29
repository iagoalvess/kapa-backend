using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Festa.Settings;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Backend.Business.Marketing.Services;
using Backend.Business.Marketing.Settings;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Marketing;

/// <summary>
/// As jornadas de retomada: qual vence quando, a trava de um e-mail por pessoa e o interruptor da P9.
/// </summary>
public sealed class JornadasDeMarketingTests
{
    /// <summary>Uma quarta-feira às 14h em Brasília — dentro da janela de envio.</summary>
    private static readonly DateTime Agora = new(2026, 10, 7, 17, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IComunicacaoDoKapaRepository _repositorio = Substitute.For<IComunicacaoDoKapaRepository>();
    private readonly IEmailService _emails = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public JornadasDeMarketingTests() =>
        _emails.Enfileirar(Arg.Any<NovoEmail>(), Arg.Any<CancellationToken>()).Returns(Result.Ok(Guid.CreateVersion7()));

    private static CandidatoDeMarketing Candidato(
        double diasDaTurma,
        bool voltou = false,
        bool temPlano = false,
        bool temFormando = false,
        bool recebeuCriou = false,
        bool recebeuMontou = false,
        Guid? usuarioId = null
    ) =>
        new(
            usuarioId ?? Guid.CreateVersion7(),
            "Ana Souza",
            "ana@exemplo.com",
            Guid.CreateVersion7(),
            "Medicina 2027",
            Agora.AddDays(-diasDaTurma),
            Criador: true,
            voltou,
            temPlano,
            temFormando,
            recebeuCriou,
            recebeuMontou
        );

    [Theory]
    [InlineData(2.9, null)]
    [InlineData(3, JornadaDeMarketing.CriouENaoVoltou)]
    [InlineData(13.9, JornadaDeMarketing.CriouENaoVoltou)]
    [InlineData(14, null)]
    public void Criou_e_nao_voltou_vence_de_3_a_14_dias(double dias, string? esperada) => Candidato(dias).JornadaVencida(Agora).ShouldBe(esperada);

    [Fact]
    public void Quem_voltou_ou_ja_recebeu_nao_recebe_criou_e_nao_voltou()
    {
        Candidato(5, voltou: true).JornadaVencida(Agora).ShouldBeNull();
        Candidato(5, recebeuCriou: true).JornadaVencida(Agora).ShouldBeNull();
    }

    [Theory]
    [InlineData(14, true, false, false, JornadaDeMarketing.MontouEParou)]
    [InlineData(29.9, true, false, false, JornadaDeMarketing.MontouEParou)]
    [InlineData(30, true, false, false, null)]
    [InlineData(20, false, false, false, null)]
    [InlineData(20, true, true, false, null)]
    [InlineData(20, true, false, true, null)]
    public void Montou_e_parou_vence_de_14_a_30_dias_com_plano_e_sem_formando(
        double dias,
        bool temPlano,
        bool temFormando,
        bool recebeu,
        string? esperada
    ) => Candidato(dias, temPlano: temPlano, temFormando: temFormando, recebeuMontou: recebeu).JornadaVencida(Agora).ShouldBe(esperada);

    [Fact]
    public async Task Duas_jornadas_da_mesma_pessoa_na_mesma_rodada_mandam_uma_so()
    {
        // Arrange
        var pessoa = Guid.CreateVersion7();
        _repositorio
            .ListarCandidatosDeTodasAsFormaturas(Agora, Arg.Any<CancellationToken>())
            .Returns([Candidato(20, temPlano: true, usuarioId: pessoa), Candidato(4, usuarioId: pessoa)]);

        // Act
        var enfileirados = await Servico().Executar(Agora, Ct);

        // Assert
        enfileirados.ShouldBe(1);
        await _emails.Received(1).Enfileirar(Arg.Is<NovoEmail>(e => e.Marketing!.UsuarioId == pessoa), Arg.Any<CancellationToken>());
        await _repositorio
            .Received(1)
            .Adicionar(Arg.Is<EnvioDeMarketing>(e => e.Jornada == JornadaDeMarketing.MontouEParou), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task O_email_sai_como_marketing_com_o_link_de_descadastro_e_o_motivo_no_rodape()
    {
        // Arrange
        _repositorio.ListarCandidatosDeTodasAsFormaturas(Agora, Arg.Any<CancellationToken>()).Returns([Candidato(4)]);

        // Act
        await Servico().Executar(Agora, Ct);

        // Assert
        await _emails
            .Received(1)
            .Enfileirar(
                Arg.Is<NovoEmail>(e =>
                    e.Marketing!.LinkDeDescadastro.StartsWith("https://api.kapa.testes/api/v1/privacidade/descadastro?token=")
                    && e.CorpoHtml.Contains("porque criou a turma Medicina 2027 no Kapa")
                    && e.CorpoHtml.Contains("https://kapa.testes/descadastro?token=")
                    && e.CorpoHtml.Contains("Não quero mais receber")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Com_o_envio_desligado_nada_sai()
    {
        // Act
        var enfileirados = await Servico(ligado: false).Executar(Agora, Ct);

        // Assert
        enfileirados.ShouldBe(0);
        await _repositorio.DidNotReceive().ListarCandidatosDeTodasAsFormaturas(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fora_da_janela_nada_sai()
    {
        // Act
        var enfileirados = await Servico().Executar(new DateTime(2026, 10, 7, 2, 0, 0, DateTimeKind.Utc), Ct);

        // Assert
        enfileirados.ShouldBe(0);
        await _repositorio.DidNotReceive().ListarCandidatosDeTodasAsFormaturas(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    private JornadasDeMarketingService Servico(bool ligado = true)
    {
        var comunicacao = Options.Create(new ComunicacaoDoKapaSettings { EnvioLigado = ligado, UrlDaApi = "https://api.kapa.testes" });
        var aplicacao = Options.Create(new AplicacaoSettings { Nome = "Kapa", UrlDoFrontend = "https://kapa.testes" });

        return new(
            _repositorio,
            new EmailsDeMarketing(_emails, Substitute.For<IAssinaturaRepository>(), Link(comunicacao, aplicacao), aplicacao),
            _unitOfWork,
            comunicacao
        );
    }

    /// <summary>O link com um segredo de teste — o mesmo formato do convite da festa.</summary>
    internal static LinkDeDescadastro Link(IOptions<ComunicacaoDoKapaSettings>? comunicacao = null, IOptions<AplicacaoSettings>? aplicacao = null) =>
        new(
            Options.Create(new ConviteSettings { SegredoDoConvite = Convert.ToBase64String(new byte[32]) }),
            comunicacao ?? Options.Create(new ComunicacaoDoKapaSettings()),
            aplicacao ?? Options.Create(new AplicacaoSettings())
        );
}

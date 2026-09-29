using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Legal.Models;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Backend.Business.Marketing.Services;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Marketing;

/// <summary>
/// A preferência "Receber novidades do Kapa" e o descadastro de um clique.
/// </summary>
public sealed class ComunicacaoDoKapaTests
{
    private static readonly DateTime Agora = new(2026, 10, 7, 17, 0, 0, DateTimeKind.Utc);

    private static readonly OrigemDoAceite De = new("203.0.113.7", "Gmail");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IComunicacaoDoKapaRepository _repositorio = Substitute.For<IComunicacaoDoKapaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly LinkDeDescadastro _link = JornadasDeMarketingTests.Link();

    private ComunicacaoDoKapaService Servico() => new(_repositorio, _link, _unitOfWork, NullLogger<ComunicacaoDoKapaService>.Instance);

    private Usuario Conta(bool recebe)
    {
        var usuario = new Usuario { ReceberComunicacaoDoKapa = recebe };
        _repositorio.ObterUsuario(usuario.Id, Arg.Any<CancellationToken>()).Returns(usuario);

        return usuario;
    }

    [Fact]
    public void Token_valido_devolve_a_conta()
    {
        var usuarioId = Guid.CreateVersion7();

        _link.Conferir(_link.Token(usuarioId, Agora), Agora.AddDays(30)).ShouldBe(usuarioId);
    }

    [Fact]
    public void Token_vencido_adulterado_ou_malformado_nao_devolve_nada()
    {
        var usuarioId = Guid.CreateVersion7();
        var token = _link.Token(usuarioId, Agora);
        var partes = token.Split('.');

        _link.Conferir(token, Agora.AddDays(366)).ShouldBeNull();
        _link.Conferir($"{Guid.CreateVersion7():N}.{partes[1]}.{partes[2]}", Agora).ShouldBeNull();
        _link.Conferir($"{partes[0]}.{long.Parse(partes[1], CultureInfo.InvariantCulture) + 1}.{partes[2]}", Agora).ShouldBeNull();
        _link.Conferir("lixo", Agora).ShouldBeNull();
        _link.Conferir(null, Agora).ShouldBeNull();
    }

    [Fact]
    public async Task Ligar_grava_a_preferencia_e_uma_linha_de_aceite()
    {
        // Arrange
        var usuario = Conta(recebe: false);

        // Act
        var resultado = await Servico().DefinirPreferencia(usuario.Id, true, OrigemDoConsentimentoDeMarketing.MinhaPrivacidade, De, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        usuario.ReceberComunicacaoDoKapa.ShouldBeTrue();
        await _repositorio
            .Received(1)
            .Adicionar(
                Arg.Is<ConsentimentoDeMarketing>(c =>
                    c.Aceito
                    && c.Origem == OrigemDoConsentimentoDeMarketing.MinhaPrivacidade
                    && c.EnderecoIp == "203.0.113.7"
                    && c.VersaoDoTexto == "1"
                ),
                Arg.Any<CancellationToken>()
            );
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pedir_o_que_ja_vale_nao_grava_linha()
    {
        // Arrange
        var usuario = Conta(recebe: true);

        // Act
        var resultado = await Servico().DefinirPreferencia(usuario.Id, true, OrigemDoConsentimentoDeMarketing.MinhaPrivacidade, De, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _repositorio.DidNotReceive().Adicionar(Arg.Any<ConsentimentoDeMarketing>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Descadastro_com_token_valido_desliga_e_registra_a_origem()
    {
        // Arrange
        var usuario = Conta(recebe: true);

        // Act
        var resultado = await Servico().Descadastrar(_link.Token(usuario.Id, DateTime.UtcNow), De, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        usuario.ReceberComunicacaoDoKapa.ShouldBeFalse();
        await _repositorio
            .Received(1)
            .Adicionar(
                Arg.Is<ConsentimentoDeMarketing>(c => !c.Aceito && c.Origem == OrigemDoConsentimentoDeMarketing.DescadastroPeloEmail),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Endpoint anônimo não confirma conta: token ruim responde o mesmo sucesso, e nada muda.</summary>
    [Theory]
    [InlineData("lixo")]
    [InlineData(null)]
    public async Task Descadastro_com_token_ruim_responde_sucesso_sem_mexer_em_nada(string? token)
    {
        // Act
        var resultado = await Servico().Descadastrar(token, De, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _repositorio.DidNotReceive().ObterUsuario(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }
}

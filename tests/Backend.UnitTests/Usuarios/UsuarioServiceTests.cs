using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
using Backend.Business.Usuarios.Services;
using Backend.Business.Usuarios.Validators;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Usuarios;

/// <summary>
/// Cobre as travas que impedem o sistema de ficar sem administrador — o desastre irreversível
/// desta camada, cuja recuperação exige acesso direto ao banco.
/// </summary>
public sealed class UsuarioServiceTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IAdminRepository _adminRepository = Substitute.For<IAdminRepository>();
    private readonly UserManager<Usuario> _userManager = CriarUserManager();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UsuarioService Criar() =>
        new(
            _usuarioRepository,
            _refreshTokenRepository,
            _adminRepository,
            _userManager,
            new AtualizarUsuarioValidator(),
            _unitOfWork,
            NullLogger<UsuarioService>.Instance
        );

    [Fact]
    public async Task Nao_deixa_o_usuario_desativar_o_proprio_acesso()
    {
        var id = Guid.CreateVersion7();

        var resultado = await Criar().AlterarAtivacao(id, ativo: false, idDoSolicitante: id, ct: Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("usuario.autodesativacao");
    }

    [Fact]
    public async Task Nao_desativa_o_ultimo_administrador_ativo()
    {
        var admin = new Usuario { Nome = "Admin", Ativo = true };
        _usuarioRepository.ObterParaEdicao(admin.Id, Arg.Any<CancellationToken>()).Returns(admin);
        _userManager.IsInRoleAsync(admin, PerfisPadrao.Administrador).Returns(true);
        _adminRepository.ContarAdministradoresAtivos(Arg.Any<CancellationToken>()).Returns(1);

        var resultado = await Criar().AlterarAtivacao(admin.Id, ativo: false, idDoSolicitante: Guid.CreateVersion7(), ct: Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("usuario.ultimo_administrador");
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Desativa_um_administrador_quando_existe_outro()
    {
        var admin = new Usuario { Nome = "Admin", Ativo = true };
        _usuarioRepository.ObterParaEdicao(admin.Id, Arg.Any<CancellationToken>()).Returns(admin);
        _userManager.IsInRoleAsync(admin, PerfisPadrao.Administrador).Returns(true);
        _adminRepository.ContarAdministradoresAtivos(Arg.Any<CancellationToken>()).Returns(2);

        var resultado = await Criar().AlterarAtivacao(admin.Id, ativo: false, idDoSolicitante: Guid.CreateVersion7(), ct: Ct);

        resultado.Sucesso.ShouldBeTrue();
        admin.Ativo.ShouldBeFalse();
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Desativar_derruba_as_sessoes_abertas()
    {
        var usuario = new Usuario { Nome = "Comum", Ativo = true };
        _usuarioRepository.ObterParaEdicao(usuario.Id, Arg.Any<CancellationToken>()).Returns(usuario);
        _userManager.IsInRoleAsync(usuario, PerfisPadrao.Administrador).Returns(false);

        await Criar().AlterarAtivacao(usuario.Id, ativo: false, idDoSolicitante: Guid.CreateVersion7(), ct: Ct);

        await _refreshTokenRepository.Received(1).RevogarTodosDoUsuario(usuario.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recusa_perfil_que_o_sistema_nao_conhece()
    {
        var resultado = await Criar().AlterarPerfis(Guid.CreateVersion7(), new AlterarPerfis(["Superusuario"]), Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("usuario.perfil_desconhecido");
        resultado.PrimeiroErro.Campo.ShouldBe("perfis");
    }

    [Fact]
    public async Task Nao_deixa_o_administrador_remover_o_proprio_perfil()
    {
        var admin = new Usuario { Nome = "Admin", Ativo = true };
        _usuarioRepository.ObterParaEdicao(admin.Id, Arg.Any<CancellationToken>()).Returns(admin);
        _userManager.GetRolesAsync(admin).Returns([PerfisPadrao.Administrador]);

        var resultado = await Criar().AlterarPerfis(admin.Id, new AlterarPerfis([PerfisPadrao.Usuario]), idDoSolicitante: admin.Id, ct: Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("usuario.autorrebaixamento");
    }

    [Fact]
    public async Task Nao_rebaixa_o_ultimo_administrador()
    {
        var admin = new Usuario { Nome = "Admin", Ativo = true };
        _usuarioRepository.ObterParaEdicao(admin.Id, Arg.Any<CancellationToken>()).Returns(admin);
        _userManager.GetRolesAsync(admin).Returns([PerfisPadrao.Administrador]);
        _userManager.IsInRoleAsync(admin, PerfisPadrao.Administrador).Returns(true);
        _adminRepository.ContarAdministradoresAtivos(Arg.Any<CancellationToken>()).Returns(1);

        var resultado = await Criar().AlterarPerfis(admin.Id, new AlterarPerfis([PerfisPadrao.Usuario]), Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("usuario.ultimo_administrador");
    }

    [Fact]
    public async Task Atualizar_recusa_nome_vazio_antes_de_tocar_no_banco()
    {
        var resultado = await Criar().Atualizar(Guid.CreateVersion7(), new AtualizarUsuario("   "), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);
        await _usuarioRepository.DidNotReceive().ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static UserManager<Usuario> CriarUserManager() =>
        Substitute.For<UserManager<Usuario>>(Substitute.For<IUserStore<Usuario>>(), null, null, null, null, null, null, null, null);
}

using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formandos.Services;
using Backend.Business.Formandos.Validators;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Formandos;

/// <summary>Regras do cadastro: validação antes de tudo, correção com autor, foto trocada sem sobra.</summary>
public sealed class PerfilServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid UsuarioId = Guid.CreateVersion7();
    private static readonly MembroDoPerfil Membro = new(Guid.CreateVersion7(), UsuarioId, "Ana", "ana@exemplo.com", "Formando");

    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IArquivoService _arquivos = Substitute.For<IArquivoService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public PerfilServiceTests() => _perfis.ObterMembro(FormaturaId, UsuarioId, Arg.Any<CancellationToken>()).Returns(Membro);

    private PerfilService Servico => new(_perfis, _arquivos, new AtualizarPerfilValidator(), _unitOfWork, NullLogger<PerfilService>.Instance);

    private static AtualizarPerfil ComCpf(string cpf) => new(new DadosPessoais("Ana Souza", null, cpf, null, null, null, null, null), null, null);

    [Fact]
    public async Task Cpf_invalido_devolve_erro_no_campo_da_secao_sem_gravar()
    {
        // Act
        var resultado = await Servico.Atualizar(FormaturaId, UsuarioId, ComCpf("529.982.247-24"), Ct);

        // Assert
        var erro = resultado.Erros.ShouldHaveSingleItem();
        erro.Codigo.ShouldBe("perfil.cpf_invalido");
        erro.Campo.ShouldBe("pessoais.cpf");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Primeira_gravacao_cria_o_cadastro_sem_registro_de_correcao()
    {
        // Act
        var resultado = await Servico.Atualizar(FormaturaId, UsuarioId, ComCpf("529.982.247-25"), Ct);

        // Assert
        resultado.Valor.Pessoais.Cpf.ShouldBe("52998224725");
        await _perfis.Received(1).Adicionar(Arg.Is<PerfilDoFormando>(p => p.VinculoId == Membro.VinculoId), Ct);
        await _perfis.DidNotReceiveWithAnyArgs().RegistrarCorrecao(default!, Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    [Fact]
    public async Task Correcao_da_comissao_registra_autor_e_secoes()
    {
        // Arrange
        var autor = Guid.CreateVersion7();
        var perfil = new PerfilDoFormando { VinculoId = Membro.VinculoId };
        _perfis.ObterParaEdicao(Membro.VinculoId, Ct).Returns(perfil);

        // Act
        await Servico.Corrigir(FormaturaId, UsuarioId, autor, ComCpf("529.982.247-25"), Ct);

        // Assert
        await _perfis
            .Received(1)
            .RegistrarCorrecao(Arg.Is<CorrecaoDePerfil>(c => c.PerfilId == perfil.Id && c.AutorUsuarioId == autor && c.Secoes == "pessoais"), Ct);
    }

    [Fact]
    public async Task Membro_sem_vinculo_ativo_na_formatura_devolve_404()
    {
        // Act
        var resultado = await Servico.Obter(FormaturaId, Guid.CreateVersion7(), Ct);

        // Assert
        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formando.nao_encontrado");
    }

    [Fact]
    public async Task Foto_nova_remove_a_anterior_depois_de_salvar()
    {
        // Arrange
        var perfil = new PerfilDoFormando { VinculoId = Membro.VinculoId };
        var anterior = Guid.CreateVersion7();
        perfil.TrocarFoto(anterior);
        _perfis.ObterParaEdicao(Membro.VinculoId, Ct).Returns(perfil);

        var nova = new ArquivoResumo(Guid.CreateVersion7(), "foto.jpg", "image/jpeg", 10, PerfilService.CategoriaDaFoto, UsuarioId, DateTime.UtcNow);
        _arquivos.Enviar(default!, default, Ct).ReturnsForAnyArgs(Result.Ok(nova));
        _arquivos.Remover(default, default, Ct).ReturnsForAnyArgs(Result.Ok());

        var png = FotoDoFormandoTests.Png(40, 40);

        // Act
        var resultado = await Servico.EnviarFoto(FormaturaId, UsuarioId, new MemoryStream(png), png.Length, Ct);

        // Assert
        resultado.Valor.FotoArquivoId.ShouldBe(nova.Id);
        Received.InOrder(() =>
        {
            _unitOfWork.SalvarAsync(Ct);
            _arquivos.Remover(anterior, new SolicitanteDeArquivo(UsuarioId, false), Ct);
        });
    }

    [Fact]
    public async Task Foto_recusada_nao_chega_ao_armazenamento()
    {
        // Arrange
        byte[] exe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00];

        // Act
        var resultado = await Servico.EnviarFoto(FormaturaId, UsuarioId, new MemoryStream(exe), exe.Length, Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        await _arquivos.DidNotReceiveWithAnyArgs().Enviar(default!, default, Ct);
    }
}

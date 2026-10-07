using Backend.Business.Formandos.Models;
using Shouldly;

namespace Backend.UnitTests.Formandos;

/// <summary>Completude e normalização do cadastro: o que a lista da comissão filtra.</summary>
public sealed class PerfilDoFormandoTests
{
    private static readonly DadosPessoais Essenciais = new("Ana Souza", "529.982.247-25", "(41) 99876-5432");

    [Fact]
    public void Cadastro_novo_esta_zerado_e_com_o_essencial_pendente()
    {
        var perfil = new PerfilDoFormando();

        perfil.Completude.ShouldBe(0);
        perfil.EssencialPreenchido.ShouldBeFalse();
        perfil.Faltando().Count.ShouldBe(ItensDoCadastro.Total);
    }

    [Fact]
    public void Nome_cpf_e_telefone_liberam_o_essencial_e_contam_60_por_cento()
    {
        var perfil = new PerfilDoFormando();

        perfil.Aplicar(new AtualizarPerfil(Essenciais, null));

        perfil.EssencialPreenchido.ShouldBeTrue();
        perfil.Completude.ShouldBe(60);
    }

    [Fact]
    public void Grava_cpf_so_com_digitos_e_telefone_em_E164()
    {
        var perfil = new PerfilDoFormando();

        perfil.Aplicar(new AtualizarPerfil(Essenciais, null));

        perfil.Cpf.ShouldBe("52998224725");
        perfil.Telefone.ShouldBe("+5541998765432");
    }

    /// <summary>O formulário salva por seção: a que não veio não pode ser apagada.</summary>
    [Fact]
    public void Secao_ausente_fica_como_estava()
    {
        var perfil = new PerfilDoFormando();
        perfil.Aplicar(new AtualizarPerfil(Essenciais, null));

        perfil.Aplicar(new AtualizarPerfil(null, new DadosDeEmergencia("Marta Souza", "(41) 98888-0000", "mãe")));

        perfil.NomeCompleto.ShouldBe("Ana Souza");
        perfil.ContatoDeEmergencia.Telefone.ShouldBe("+5541988880000");
        perfil.Completude.ShouldBe(80);
    }

    /// <summary>Contato de emergência conta inteiro ou não conta: sem parentesco não se sabe para quem se liga.</summary>
    [Fact]
    public void Contato_sem_parentesco_nao_conta()
    {
        var perfil = new PerfilDoFormando();

        perfil.Aplicar(new AtualizarPerfil(null, new DadosDeEmergencia("Marta Souza", "(41) 98888-0000", " ")));

        perfil.Faltando().ShouldContain(ItensDoCadastro.ContatoDeEmergencia);
        perfil.ContatoDeEmergencia.Parentesco.ShouldBeNull();
    }

    [Fact]
    public void Apagar_o_cpf_volta_a_pendencia_do_essencial()
    {
        var perfil = new PerfilDoFormando();
        perfil.Aplicar(new AtualizarPerfil(Essenciais, null));

        perfil.Aplicar(new AtualizarPerfil(Essenciais with { Cpf = "" }, null));

        perfil.Cpf.ShouldBeNull();
        perfil.EssencialPreenchido.ShouldBeFalse();
    }

    [Fact]
    public void Trocar_a_foto_devolve_a_anterior_e_conta_na_completude()
    {
        var perfil = new PerfilDoFormando();
        var primeira = Guid.CreateVersion7();

        perfil.TrocarFoto(primeira).ShouldBeNull();
        var anterior = perfil.TrocarFoto(Guid.CreateVersion7());

        anterior.ShouldBe(primeira);
        perfil.Completude.ShouldBe(20);
    }
}

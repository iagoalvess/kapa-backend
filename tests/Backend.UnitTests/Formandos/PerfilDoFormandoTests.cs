using Backend.Business.Formandos.Models;
using Shouldly;

namespace Backend.UnitTests.Formandos;

/// <summary>Completude e normalização do cadastro: o que a lista da comissão filtra.</summary>
public sealed class PerfilDoFormandoTests
{
    private static readonly DadosPessoais Essenciais = new("Ana Souza", null, "529.982.247-25", null, null, "(41) 99876-5432", null, null);

    [Fact]
    public void Cadastro_novo_esta_zerado_e_com_o_essencial_pendente()
    {
        var perfil = new PerfilDoFormando();

        perfil.Completude.ShouldBe(0);
        perfil.EssencialPreenchido.ShouldBeFalse();
        perfil.Faltando().Count.ShouldBe(ItensDoCadastro.Total);
    }

    [Fact]
    public void Nome_cpf_e_telefone_liberam_o_essencial_e_contam_30_por_cento()
    {
        var perfil = new PerfilDoFormando();

        perfil.Aplicar(new AtualizarPerfil(Essenciais, null, null));

        perfil.EssencialPreenchido.ShouldBeTrue();
        perfil.Completude.ShouldBe(30);
    }

    [Fact]
    public void Grava_cpf_so_com_digitos_e_telefone_em_E164()
    {
        var perfil = new PerfilDoFormando();

        perfil.Aplicar(new AtualizarPerfil(Essenciais, null, null));

        perfil.Cpf.ShouldBe("52998224725");
        perfil.Telefone.ShouldBe("+5541998765432");
    }

    /// <summary>O formulário salva por seção: a que não veio não pode ser apagada.</summary>
    [Fact]
    public void Secao_ausente_fica_como_estava()
    {
        var perfil = new PerfilDoFormando();
        perfil.Aplicar(new AtualizarPerfil(Essenciais, null, null));

        perfil.Aplicar(new AtualizarPerfil(null, new DadosDeEndereco("80000-000", "Rua XV", "10", null, "Centro", "Curitiba", "pr"), null));

        perfil.NomeCompleto.ShouldBe("Ana Souza");
        perfil.Endereco.Cep.ShouldBe("80000000");
        perfil.Endereco.Uf.ShouldBe("PR");
        perfil.Completude.ShouldBe(40);
    }

    /// <summary>Endereço conta inteiro ou não conta: sem número não se entrega nada.</summary>
    [Fact]
    public void Endereco_sem_numero_nao_conta()
    {
        var perfil = new PerfilDoFormando();

        perfil.Aplicar(new AtualizarPerfil(null, new DadosDeEndereco("80000000", "Rua XV", " ", null, "Centro", "Curitiba", "PR"), null));

        perfil.Faltando().ShouldContain(ItensDoCadastro.Endereco);
        perfil.Endereco.Numero.ShouldBeNull();
    }

    [Fact]
    public void Apagar_o_cpf_volta_a_pendencia_do_essencial()
    {
        var perfil = new PerfilDoFormando();
        perfil.Aplicar(new AtualizarPerfil(Essenciais, null, null));

        perfil.Aplicar(new AtualizarPerfil(Essenciais with { Cpf = "" }, null, null));

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
        perfil.Completude.ShouldBe(10);
    }
}

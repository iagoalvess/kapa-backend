using Backend.Business.Abstractions;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Validators;
using Shouldly;

namespace Backend.UnitTests.Recebimentos;

/// <summary>A chave é conferida pelo tipo; os demais meios, só por presença e tamanho (P5 de 21/09/2026).</summary>
public sealed class ContaDeRecebimentoValidatorTests
{
    private readonly ContaDeRecebimentoValidator _validator = new();

    [Theory]
    [InlineData(TipoDeChavePix.Cpf, "529.982.247-25", "52998224725")]
    [InlineData(TipoDeChavePix.Cpf, "529.982.247-24", null)]
    [InlineData(TipoDeChavePix.Cnpj, "11.222.333/0001-81", "11222333000181")]
    [InlineData(TipoDeChavePix.Cnpj, "12.abc.345/01de-35", "12ABC34501DE35")]
    [InlineData(TipoDeChavePix.Email, " Tesouraria@Turma.com.br ", "tesouraria@turma.com.br")]
    [InlineData(TipoDeChavePix.Email, "tesouraria@turma", null)]
    [InlineData(TipoDeChavePix.Email, "tesouraria turma@turma.com", null)]
    [InlineData(TipoDeChavePix.Telefone, "(41) 99876-5432", "+5541998765432")]
    [InlineData(TipoDeChavePix.Telefone, "+55 41 99876-5432", "+5541998765432")]
    [InlineData(TipoDeChavePix.Telefone, "99876-5432", null)]
    [InlineData(TipoDeChavePix.Telefone, "(41) 3333-4444", null)]
    [InlineData(TipoDeChavePix.Telefone, "+1 415 555 0100", null)]
    [InlineData(TipoDeChavePix.Aleatoria, "123E4567-E89B-12D3-A456-426614174000", "123e4567-e89b-12d3-a456-426614174000")]
    [InlineData(TipoDeChavePix.Aleatoria, "123e4567e89b12d3a456426614174000", null)]
    [InlineData(TipoDeChavePix.Cpf, "tesouraria@turma.com.br", null)]
    public void Chave_e_normalizada_pelo_tipo(TipoDeChavePix tipo, string chave, string? esperada) =>
        ChavePix.Normalizar(tipo, chave).ShouldBe(esperada);

    /// <summary>Os três casos do critério de aceite: o motivo vem no campo, para a tela mostrar embaixo dele.</summary>
    [Theory]
    [InlineData(TipoDeChavePix.Cpf, "529.982.247-24", "CPF inválido: confira os 11 dígitos.")]
    [InlineData(TipoDeChavePix.Telefone, "99876-5432", "Informe o celular com DDD, como (41) 99876-5432.")]
    [InlineData(TipoDeChavePix.Email, "tesouraria@", "E-mail inválido.")]
    public void Chave_invalida_para_o_tipo_diz_o_motivo_no_campo(TipoDeChavePix tipo, string chave, string motivo)
    {
        var erro = _validator.Validar(Pix(new ChavePixDaConta(tipo, chave, "Ana Souza", "Curitiba"))).Erros.ShouldHaveSingleItem();

        erro.Campo.ShouldBe("pix.chave");
        erro.Mensagem.ShouldBe(motivo);
        erro.Tipo.ShouldBe(ETipoErro.Validacao);
    }

    [Fact]
    public void Campos_vazios_acendem_um_erro_cada()
    {
        var erros = _validator.Validar(Pix(new ChavePixDaConta(TipoDeChavePix.Cpf, " ", "", ""))).Erros;

        erros.Select(e => e.Campo).ShouldBe(["pix.chave", "pix.nome_do_titular", "pix.cidade"]);
    }

    [Fact]
    public void Nome_sem_letra_latina_nao_cabe_no_br_code()
    {
        var erro = _validator.Validar(Pix(new ChavePixDaConta(TipoDeChavePix.Cpf, "52998224725", "李小龍", "Curitiba"))).Erros.ShouldHaveSingleItem();

        erro.Campo.ShouldBe("pix.nome_do_titular");
    }

    /// <summary>P4 de 21/09/2026: a chave virou opcional, mas a turma precisa ter por onde receber.</summary>
    [Fact]
    public void Sem_meio_nenhum_a_conta_nao_existe()
    {
        var erro = _validator.Validar(new MeiosDaConta(null, null, null)).Erros.ShouldHaveSingleItem();

        erro.Codigo.ShouldBe("recebimento.sem_meio");
        erro.Tipo.ShouldBe(ETipoErro.Validacao);
    }

    /// <summary>Cada meio é conferido só quando existe: TED habilitado sem banco não passa, desligado não incomoda.</summary>
    [Theory]
    [InlineData("", "1234-5", "transferencia.banco")]
    [InlineData("Banco do Brasil", "", "transferencia.agencia")]
    public void Meio_habilitado_com_campo_em_branco_acende_o_campo(string banco, string agencia, string campo)
    {
        var meios = new MeiosDaConta(null, new DadosBancarios(banco, agencia, "98765-4", "Corrente", "Comissão"), null);

        _validator.Validar(meios).Erros.ShouldHaveSingleItem().Campo.ShouldBe(campo);
    }

    /// <summary>Dinheiro exige o nome de quem recebe — é o contrapeso de anunciá-lo na tela (P2).</summary>
    [Fact]
    public void Dinheiro_sem_nome_de_quem_recebe_nao_passa()
    {
        var meios = new MeiosDaConta(null, null, new DinheiroComAlguem(" ", "na sala 12"));

        _validator.Validar(meios).Erros.ShouldHaveSingleItem().Campo.ShouldBe("dinheiro.nome");
    }

    [Fact]
    public void Turma_que_so_recebe_em_dinheiro_e_valida()
    {
        var meios = new MeiosDaConta(null, null, new DinheiroComAlguem("Ana Souza", null));

        _validator.Validar(meios).Sucesso.ShouldBeTrue();
    }

    /// <summary>Só o PIX, como toda turma até a Sprint 18 — os outros grupos nulos não pedem nada.</summary>
    private static MeiosDaConta Pix(ChavePixDaConta chave) => new(chave, null, null);
}

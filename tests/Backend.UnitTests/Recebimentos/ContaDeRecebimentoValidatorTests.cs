using Backend.Business.Abstractions;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Validators;
using Shouldly;

namespace Backend.UnitTests.Recebimentos;

/// <summary>A chave é conferida pelo tipo e gravada no formato do diretório do PIX.</summary>
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
        var erro = _validator.Validar(new DadosDaConta(tipo, chave, "Ana Souza", "Curitiba")).Erros.ShouldHaveSingleItem();

        erro.Campo.ShouldBe("chave");
        erro.Mensagem.ShouldBe(motivo);
        erro.Tipo.ShouldBe(ETipoErro.Validacao);
    }

    [Fact]
    public void Campos_vazios_acendem_um_erro_cada()
    {
        var erros = _validator.Validar(new DadosDaConta(TipoDeChavePix.Cpf, " ", "", "")).Erros;

        erros.Select(e => e.Campo).ShouldBe(["chave", "nome_do_titular", "cidade"]);
    }

    [Fact]
    public void Nome_sem_letra_latina_nao_cabe_no_br_code()
    {
        var erro = _validator.Validar(new DadosDaConta(TipoDeChavePix.Cpf, "52998224725", "李小龍", "Curitiba")).Erros.ShouldHaveSingleItem();

        erro.Campo.ShouldBe("nome_do_titular");
    }
}

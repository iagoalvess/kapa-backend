using Backend.Business.Abstractions;
using Backend.Business.Common.Texto;

namespace Backend.Business.Financeiro.Models;

/// <summary>
/// Quem a turma contrata: buffet, banda, fotógrafo, gráfica.
/// </summary>
/// <remarks>
/// Cadastro leve (decisão 4): nome, documento, contato, categoria e observações. Sem portal do
/// fornecedor, sem contrato eletrônico, sem cotação.
/// <para>
/// Fornecedor com despesa lançada não é excluído — é desativado (<see cref="Ativo"/>). Apagar levaria
/// junto a origem de um gasto já pago, e prestação de contas com lançamento órfão não se sustenta.
/// </para>
/// </remarks>
public class Fornecedor : EntidadeDaFormatura
{
    /// <summary>Nome ou razão social, como a comissão digitou.</summary>
    public string Nome { get; private set; } = string.Empty;

    /// <summary>CNPJ ou CPF, só dígitos. Nulo: a comissão não tem o documento.</summary>
    public string? Documento { get; private set; }

    /// <summary>Categoria padrão das despesas deste fornecedor — o formulário já vem preenchido com ela.</summary>
    public CategoriaDeDespesa Categoria { get; private set; }

    /// <summary>Telefone em E.164, se informado.</summary>
    public string? Telefone { get; private set; }

    /// <summary>E-mail de contato, se informado.</summary>
    public string? Email { get; private set; }

    /// <summary>Anotações da comissão: condição de pagamento, nome do vendedor, o que foi combinado.</summary>
    public string? Observacoes { get; private set; }

    /// <summary>Se aparece na hora de lançar uma despesa. Desativado continua nos lançamentos antigos.</summary>
    public bool Ativo { get; private set; } = true;

    /// <summary>Um fornecedor novo.</summary>
    /// <param name="dados">Dados já validados.</param>
    public static Fornecedor Novo(DadosDoFornecedor dados)
    {
        var fornecedor = new Fornecedor();
        fornecedor.Aplicar(dados);

        return fornecedor;
    }

    /// <summary>Grava nome, documento, categoria, contato, observações e ativação.</summary>
    /// <remarks>Documento e telefone entram normalizados: o que a tela mostra é a máscara, o que compara é o dígito.</remarks>
    /// <param name="dados">Dados já validados.</param>
    public void Aplicar(DadosDoFornecedor dados)
    {
        Nome = dados.Nome.Trim();
        Documento = Vazio(FormatosBrasileiros.SomenteDigitos(dados.Documento));
        Categoria = dados.Categoria;
        Telefone = FormatosBrasileiros.TelefoneE164(dados.Telefone);
        Email = Vazio(dados.Email?.Trim());
        Observacoes = Vazio(dados.Observacoes?.Trim());
        Ativo = dados.Ativo;
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
}

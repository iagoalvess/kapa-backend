namespace Backend.Business.Financeiro.Models;

/// <summary>Fornecedor, como a comissão o informa.</summary>
/// <param name="Nome">Nome ou razão social.</param>
/// <param name="Documento">CNPJ ou CPF, com ou sem máscara. Ausente: a comissão não tem.</param>
/// <param name="Categoria">Categoria padrão das despesas dele.</param>
/// <param name="Telefone">Telefone de contato.</param>
/// <param name="Email">E-mail de contato.</param>
/// <param name="Observacoes">Anotações da comissão.</param>
/// <param name="Ativo">Se aparece na hora de lançar uma despesa.</param>
public sealed record DadosDoFornecedor(
    string Nome,
    string? Documento,
    CategoriaDeDespesa Categoria,
    string? Telefone,
    string? Email,
    string? Observacoes,
    bool Ativo = true
);

/// <summary>Um fornecedor na lista da turma, com o que já foi gasto com ele.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome ou razão social.</param>
/// <param name="Documento">CNPJ ou CPF, só dígitos.</param>
/// <param name="Categoria">Categoria padrão.</param>
/// <param name="Telefone">Telefone em E.164.</param>
/// <param name="Email">E-mail de contato.</param>
/// <param name="Observacoes">Anotações da comissão.</param>
/// <param name="Ativo">Se aparece na hora de lançar uma despesa.</param>
/// <param name="QuantidadeDeDespesas">Despesas lançadas com ele, canceladas de fora — é o que impede a exclusão.</param>
/// <param name="PagoEmCentavos">O que já saiu para ele.</param>
/// <param name="PrevistoEmCentavos">O que ainda vai sair.</param>
public sealed record FornecedorResumo(
    Guid Id,
    string Nome,
    string? Documento,
    CategoriaDeDespesa Categoria,
    string? Telefone,
    string? Email,
    string? Observacoes,
    bool Ativo,
    int QuantidadeDeDespesas,
    long PagoEmCentavos,
    long PrevistoEmCentavos
);

/// <summary>Quantos fornecedores a turma tem em cada situação — os números das pílulas da tela.</summary>
/// <remarks>Uma consulta agrupada no lugar de duas listas de um item só pedidas pelo <c>total</c>.</remarks>
/// <param name="Ativos">Os que aparecem na hora de lançar uma despesa.</param>
/// <param name="Inativos">Os desativados, que ficam pelo histórico das despesas deles.</param>
public sealed record ContagemDeFornecedores(int Ativos, int Inativos);

/// <summary>Filtros da lista de fornecedores.</summary>
/// <param name="Ativo">Só os ativos, só os inativos, ou todos.</param>
/// <param name="Categoria">Só os desta categoria.</param>
/// <param name="Busca">Trecho do nome ou do documento.</param>
public sealed record FiltroDeFornecedores(bool? Ativo = null, CategoriaDeDespesa? Categoria = null, string? Busca = null);

/// <summary>
/// Um lançamento de despesa, como a tesouraria o informa.
/// </summary>
/// <remarks>
/// <paramref name="ValorEmCentavos"/> é o valor <b>total</b> do compromisso; a parcelada o divide em
/// <paramref name="NumeroDeParcelas"/> linhas mensais, com o resto na primeira.
/// </remarks>
/// <param name="FornecedorId">A quem se paga. Ausente: gasto sem fornecedor cadastrado.</param>
/// <param name="ItemDaFestaId">Item da festa que esta despesa paga. Ausente: gasto que não é da festa.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="Categoria">Em que a turma gastou.</param>
/// <param name="ValorEmCentavos">Valor total, em centavos.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes. À vista é 1.</param>
/// <param name="Competencia">Mês em que o gasto aconteceu; o dia é ignorado.</param>
/// <param name="Vencimento">Vencimento da primeira parcela — as demais caem no mesmo dia dos meses seguintes.</param>
/// <param name="PagaEm">Dia do pagamento, quando a despesa já nasce paga. Exige comprovante; na parcelada vale para a primeira.</param>
public sealed record NovaDespesa(
    Guid? FornecedorId,
    Guid? ItemDaFestaId,
    string Descricao,
    CategoriaDeDespesa Categoria,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    DateOnly Competencia,
    DateOnly Vencimento,
    DateOnly? PagaEm = null
);

/// <summary>A correção de uma linha já lançada.</summary>
/// <param name="FornecedorId">A quem se paga.</param>
/// <param name="ItemDaFestaId">Item da festa que esta despesa paga. Ausente: gasto que não é da festa.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="Categoria">Em que a turma gastou.</param>
/// <param name="ValorEmCentavos">Valor desta linha, em centavos.</param>
/// <param name="Competencia">Mês do gasto.</param>
/// <param name="Vencimento">Dia do pagamento desta linha.</param>
public sealed record DadosDaDespesa(
    Guid? FornecedorId,
    Guid? ItemDaFestaId,
    string Descricao,
    CategoriaDeDespesa Categoria,
    long ValorEmCentavos,
    DateOnly Competencia,
    DateOnly Vencimento
);

/// <summary>O pagamento de uma despesa.</summary>
/// <param name="PagoEm">Dia em que o dinheiro saiu.</param>
public sealed record PagarDespesa(DateOnly PagoEm);

/// <summary>Filtros da lista de despesas.</summary>
/// <param name="LancamentoId">Só as linhas deste lançamento — as parcelas de uma parcelada.</param>
/// <param name="FornecedorId">Só as deste fornecedor.</param>
/// <param name="Categoria">Só as desta categoria.</param>
/// <param name="Status">Só nesta situação.</param>
/// <param name="Atrasadas">Só as previstas com vencimento no passado.</param>
/// <param name="De">Vencimento a partir deste dia, inclusive.</param>
/// <param name="Ate">Vencimento até este dia, inclusive.</param>
/// <param name="Busca">Trecho da descrição ou do nome do fornecedor.</param>
public sealed record FiltroDeDespesas(
    Guid? LancamentoId = null,
    Guid? FornecedorId = null,
    CategoriaDeDespesa? Categoria = null,
    StatusDaDespesa? Status = null,
    bool Atrasadas = false,
    DateOnly? De = null,
    DateOnly? Ate = null,
    string? Busca = null
);

/// <summary>Uma despesa, como a tesouraria a vê.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="LancamentoId">O lançamento que a criou; as parcelas irmãs têm o mesmo.</param>
/// <param name="FornecedorId">A quem se paga, se houver.</param>
/// <param name="ItemDaFestaId">Item da festa que esta despesa paga, se houver.</param>
/// <param name="Fornecedor">Nome do fornecedor, se houver.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="Categoria">Em que a turma gastou.</param>
/// <param name="ValorEmCentavos">Valor desta linha.</param>
/// <param name="Competencia">Mês do gasto.</param>
/// <param name="Vencimento">Dia do pagamento.</param>
/// <param name="Numero">Posição na parcelada.</param>
/// <param name="TotalDeParcelas">Total de parcelas do lançamento.</param>
/// <param name="Status">Situação gravada.</param>
/// <param name="PagoEm">Dia em que saiu, se paga.</param>
/// <param name="TemComprovante">Se há comprovante — abre em <c>/financeiro/despesas/{id}/comprovante</c>.</param>
/// <param name="Atrasada">Prevista com vencimento no passado. Calculado, nunca gravado.</param>
public sealed record DespesaResumo(
    Guid Id,
    Guid LancamentoId,
    Guid? FornecedorId,
    Guid? ItemDaFestaId,
    string? Fornecedor,
    string Descricao,
    CategoriaDeDespesa Categoria,
    long ValorEmCentavos,
    DateOnly Competencia,
    DateOnly Vencimento,
    int Numero,
    int TotalDeParcelas,
    StatusDaDespesa Status,
    DateOnly? PagoEm,
    bool TemComprovante,
    bool Atrasada = false
);

/// <summary>Quantas despesas há numa situação e quanto somam.</summary>
/// <param name="Quantidade">Despesas.</param>
/// <param name="ValorEmCentavos">Soma dos valores.</param>
public sealed record SomaDeDespesas(int Quantidade, long ValorEmCentavos)
{
    /// <summary>Nenhuma despesa.</summary>
    public static readonly SomaDeDespesas Zero = new(0, 0);
}

/// <summary>A faixa da tela Despesas: quantas e quanto, por situação, dentro do filtro.</summary>
/// <param name="Todas">Todas as do filtro.</param>
/// <param name="Prevista">A pagar, atrasadas incluídas.</param>
/// <param name="Atrasada">Previstas com vencimento no passado.</param>
/// <param name="Paga">Já pagas.</param>
/// <param name="Cancelada">Canceladas.</param>
public sealed record ResumoDeDespesas(
    SomaDeDespesas Todas,
    SomaDeDespesas Prevista,
    SomaDeDespesas Atrasada,
    SomaDeDespesas Paga,
    SomaDeDespesas Cancelada
);

/// <summary>Uma linha da contagem agrupada de despesas, como sai do banco.</summary>
/// <param name="Status">Situação gravada.</param>
/// <param name="Atrasada">Prevista com vencimento no passado.</param>
/// <param name="Quantidade">Despesas.</param>
/// <param name="ValorEmCentavos">Soma dos valores.</param>
public sealed record ContagemDeDespesas(StatusDaDespesa Status, bool Atrasada, int Quantidade, long ValorEmCentavos);

/// <summary>O comprovante de uma despesa: o arquivo e quem o enviou — o dono no módulo de arquivos.</summary>
/// <param name="ArquivoId">Arquivo.</param>
/// <param name="EnviadoPorUsuarioId">Quem enviou.</param>
public sealed record ComprovanteDaDespesa(Guid ArquivoId, Guid EnviadoPorUsuarioId);

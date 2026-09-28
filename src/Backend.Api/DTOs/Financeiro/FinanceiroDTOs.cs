using Backend.Business.Comunicacao.Models;
using Backend.Business.Financeiro.Models;

namespace Backend.Api.DTOs.Financeiro;

/// <summary>Corpo do cadastro de fornecedor.</summary>
/// <param name="Nome">Nome ou razão social.</param>
/// <param name="Documento">CNPJ ou CPF, com ou sem máscara.</param>
/// <param name="Categoria">Categoria padrão das despesas dele.</param>
/// <param name="Telefone">Telefone com DDD.</param>
/// <param name="Email">E-mail de contato.</param>
/// <param name="Observacoes">Anotações da comissão.</param>
/// <param name="Ativo">Se aparece na hora de lançar uma despesa. Ausente, vale <c>true</c>.</param>
public sealed record FornecedorRequestDTO(
    string? Nome,
    string? Documento,
    CategoriaDeDespesa Categoria,
    string? Telefone,
    string? Email,
    string? Observacoes,
    bool? Ativo
);

/// <summary>Um fornecedor da turma.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome ou razão social.</param>
/// <param name="Documento">CNPJ ou CPF, só dígitos.</param>
/// <param name="Categoria">Categoria padrão.</param>
/// <param name="Telefone">Telefone em E.164.</param>
/// <param name="Email">E-mail de contato.</param>
/// <param name="Observacoes">Anotações da comissão.</param>
/// <param name="Ativo">Se aparece na hora de lançar uma despesa.</param>
/// <param name="QuantidadeDeDespesas">Despesas lançadas com ele — com alguma, a exclusão devolve 409.</param>
/// <param name="PagoEmCentavos">O que já saiu para ele.</param>
/// <param name="PrevistoEmCentavos">O que ainda vai sair.</param>
public sealed record FornecedorDTO(
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
/// <param name="Ativos">Os que aparecem na hora de lançar uma despesa.</param>
/// <param name="Inativos">Os desativados, que ficam pelo histórico das despesas deles.</param>
public sealed record ContagemDeFornecedoresDTO(int Ativos, int Inativos);

/// <summary>Corpo do lançamento de despesa (multipart, com o comprovante).</summary>
/// <param name="FornecedorId">A quem se paga. Ausente: gasto sem fornecedor cadastrado.</param>
/// <param name="ItemDaFestaId">Item da festa que esta despesa paga. Ausente: gasto que não é da festa.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="Categoria">Em que a turma gastou.</param>
/// <param name="ValorEmCentavos">Valor <b>total</b> do compromisso, em centavos.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes; à vista é 1.</param>
/// <param name="Competencia">Mês do gasto, <c>aaaa-mm-dd</c>; o dia é ignorado.</param>
/// <param name="Vencimento">Vencimento da primeira parcela, <c>aaaa-mm-dd</c>.</param>
/// <param name="PagaEm">Dia do pagamento, quando a despesa já nasce paga — exige comprovante.</param>
public sealed record NovaDespesaRequestDTO(
    Guid? FornecedorId,
    Guid? ItemDaFestaId,
    string? Descricao,
    CategoriaDeDespesa Categoria,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    DateOnly Competencia,
    DateOnly Vencimento,
    DateOnly? PagaEm
);

/// <summary>Corpo da correção de uma despesa.</summary>
/// <param name="FornecedorId">A quem se paga.</param>
/// <param name="ItemDaFestaId">Item da festa que esta despesa paga. Ausente: gasto que não é da festa.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="Categoria">Em que a turma gastou.</param>
/// <param name="ValorEmCentavos">Valor desta linha, em centavos.</param>
/// <param name="Competencia">Mês do gasto.</param>
/// <param name="Vencimento">Dia do pagamento desta linha.</param>
public sealed record AtualizarDespesaRequestDTO(
    Guid? FornecedorId,
    Guid? ItemDaFestaId,
    string? Descricao,
    CategoriaDeDespesa Categoria,
    long ValorEmCentavos,
    DateOnly Competencia,
    DateOnly Vencimento
);

/// <summary>Uma despesa da turma.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="LancamentoId">O lançamento que a criou; as parcelas irmãs têm o mesmo.</param>
/// <param name="FornecedorId">A quem se paga, se houver.</param>
/// <param name="ItemDaFestaId">Item da festa que esta despesa paga, se houver.</param>
/// <param name="Fornecedor">Nome do fornecedor, se houver.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="Categoria">Em que a turma gastou.</param>
/// <param name="ValorEmCentavos">Valor desta linha.</param>
/// <param name="Competencia">Mês do gasto, no dia 1.</param>
/// <param name="Vencimento">Dia do pagamento.</param>
/// <param name="Numero">Posição na parcelada.</param>
/// <param name="TotalDeParcelas">Total de parcelas do lançamento.</param>
/// <param name="Status"><c>Prevista</c>, <c>Paga</c> ou <c>Cancelada</c>.</param>
/// <param name="PagoEm">Dia em que saiu, se paga.</param>
/// <param name="TemComprovante">Se há comprovante — abre em <c>/financeiro/despesas/{id}/comprovante</c>.</param>
/// <param name="Atrasada">Prevista com vencimento no passado. Calculado, nunca gravado.</param>
public sealed record DespesaDTO(
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
    bool Atrasada
);

/// <summary>Quantos lançamentos — despesas ou receitas — e quanto somam.</summary>
/// <param name="Quantidade">Lançamentos.</param>
/// <param name="ValorEmCentavos">Soma dos valores.</param>
public sealed record SomaDeLancamentosDTO(int Quantidade, long ValorEmCentavos);

/// <summary>A faixa da tela Despesas, dentro do mesmo filtro da lista.</summary>
/// <param name="Todas">Todas as do filtro.</param>
/// <param name="Prevista">A pagar, atrasadas incluídas.</param>
/// <param name="Atrasada">Previstas com vencimento no passado.</param>
/// <param name="Paga">Já pagas.</param>
/// <param name="Cancelada">Canceladas.</param>
public sealed record ResumoDeDespesasDTO(
    SomaDeLancamentosDTO Todas,
    SomaDeLancamentosDTO Prevista,
    SomaDeLancamentosDTO Atrasada,
    SomaDeLancamentosDTO Paga,
    SomaDeLancamentosDTO Cancelada
);

/// <summary>O caixa da turma, hoje.</summary>
/// <param name="ArrecadadoEmCentavos">O que entrou.</param>
/// <param name="GastoEmCentavos">O que saiu.</param>
/// <param name="SaldoEmCentavos">Arrecadado menos gasto. Pode ser negativo.</param>
/// <param name="AReceberEmCentavos">Parcelas que ainda vencem.</param>
/// <param name="EmAtrasoEmCentavos">Parcelas vencidas e não pagas.</param>
/// <param name="SaldoProjetadoEmCentavos">O que sobra se todos pagarem e todas as contas forem pagas.</param>
/// <param name="PorCategoria">O quadro por categoria, do maior gasto para o menor.</param>
/// <param name="OutrasReceitasPorCategoria">O quadro das receitas que não vêm de formando, da maior para a menor.</param>
/// <param name="Ultimos">Os últimos lançamentos, do mais recente.</param>
/// <remarks>
/// As despesas previstas entram no <see cref="SaldoProjetadoEmCentavos"/> e não saem em campo
/// próprio: o total a pagar tem tela dedicada, em Despesas, com o filtro e o detalhe de cada linha.
/// Repeti-lo aqui seria um número solto que a tela não sabe para onde levar.
/// </remarks>
public sealed record CaixaDTO(
    long ArrecadadoEmCentavos,
    long GastoEmCentavos,
    long SaldoEmCentavos,
    long AReceberEmCentavos,
    long EmAtrasoEmCentavos,
    long SaldoProjetadoEmCentavos,
    IReadOnlyList<GastoPorCategoriaDTO> PorCategoria,
    IReadOnlyList<OutraReceitaPorCategoriaDTO> OutrasReceitasPorCategoria,
    IReadOnlyList<LancamentoDTO> Ultimos
);

/// <summary>Quanto a turma gastou numa categoria.</summary>
/// <param name="Categoria">Categoria.</param>
/// <param name="Quantidade">Despesas lançadas.</param>
/// <param name="PagoEmCentavos">O que já saiu.</param>
/// <param name="PrevistoEmCentavos">O que ainda vai sair.</param>
public sealed record GastoPorCategoriaDTO(CategoriaDeDespesa Categoria, int Quantidade, long PagoEmCentavos, long PrevistoEmCentavos);

/// <summary>Quanto entrou — ou ainda vai entrar — numa categoria de receita.</summary>
/// <param name="Categoria">Categoria.</param>
/// <param name="Quantidade">Receitas lançadas, canceladas de fora.</param>
/// <param name="RecebidoEmCentavos">O que já entrou — é o que soma no arrecadado.</param>
/// <param name="PrevistoEmCentavos">O que ainda vai entrar; nunca soma no arrecadado.</param>
public sealed record OutraReceitaPorCategoriaDTO(CategoriaDeOutraReceita Categoria, int Quantidade, long RecebidoEmCentavos, long PrevistoEmCentavos);

/// <summary>Uma linha do extrato do caixa.</summary>
/// <param name="Data">Dia em que o dinheiro se moveu.</param>
/// <param name="Descricao">"Pagamento de parcela", ou a descrição da despesa ou da receita.</param>
/// <param name="ValorEmCentavos">Valor, sempre positivo.</param>
/// <param name="Entrada">Entrada de dinheiro; falso, saída.</param>
public sealed record LancamentoDTO(DateOnly Data, string Descricao, long ValorEmCentavos, bool Entrada);

/// <summary>O caixa mês a mês: realizado até hoje, projetado até a colação.</summary>
/// <remarks>
/// A data da colação não vem junto: ela já decide até onde <see cref="Meses"/> vai, e o último mês
/// da série é a própria resposta. O cadastro da turma é quem a exibe.
/// </remarks>
/// <param name="Meses">Do primeiro mês com movimento até a colação, sem buraco.</param>
/// <param name="SaldoEmCentavos">O saldo de hoje.</param>
/// <param name="EmAtrasoEmCentavos">O que está vencido e não entrou em mês nenhum.</param>
public sealed record ProjecaoDTO(IReadOnlyList<MesDoCaixaDTO> Meses, long SaldoEmCentavos, long EmAtrasoEmCentavos);

/// <summary>O total juntado pela turma ao fim de um mês — o gráfico do Início.</summary>
/// <param name="Mes">Primeiro dia do mês.</param>
/// <param name="ArrecadadoEmCentavos">Tudo o que entrou até o fim do mês, acumulado.</param>
/// <param name="Projetado">Mês no futuro: o que já entrou mais o que vence nele.</param>
public sealed record MesDaArrecadacaoDTO(DateOnly Mes, long ArrecadadoEmCentavos, bool Projetado);

/// <summary>Um mês do fluxo de caixa.</summary>
/// <param name="Mes">Primeiro dia do mês.</param>
/// <param name="EntradasEmCentavos">O que entrou de fato.</param>
/// <param name="SaidasEmCentavos">O que saiu de fato.</param>
/// <param name="EntradasPrevistasEmCentavos">Parcelas que vencem no mês e ainda não foram pagas.</param>
/// <param name="SaidasPrevistasEmCentavos">Despesas previstas que vencem no mês.</param>
/// <param name="SaldoAcumuladoEmCentavos">Saldo ao fim do mês, somando realizado e previsto.</param>
/// <param name="Projetado">Mês no futuro — a tela desenha pontilhado e rotula como projeção.</param>
public sealed record MesDoCaixaDTO(
    DateOnly Mes,
    long EntradasEmCentavos,
    long SaidasEmCentavos,
    long EntradasPrevistasEmCentavos,
    long SaidasPrevistasEmCentavos,
    long SaldoAcumuladoEmCentavos,
    bool Projetado
);

/// <summary>Corpo do lançamento de uma receita.</summary>
/// <param name="Descricao">O que é.</param>
/// <param name="Origem">De quem veio, em texto livre ("Clínica Sorriso").</param>
/// <param name="Categoria"><c>Patrocinio</c>, <c>Evento</c>, <c>Doacao</c>, <c>Rendimento</c>, <c>VendaDeConvite</c> ou <c>Outros</c>.</param>
/// <param name="ValorEmCentavos">Valor, em centavos.</param>
/// <param name="Data">Dia previsto, <c>aaaa-mm-dd</c>; ou, se <paramref name="Recebida"/>, o dia em que entrou.</param>
/// <param name="Recebida">O dinheiro já caiu na conta. Ausente ou falso: prevista.</param>
/// <param name="DocumentoId">Comprovante no acervo, visível para a turma.</param>
public sealed record NovaOutraReceitaRequestDTO(
    string? Descricao,
    string? Origem,
    CategoriaDeOutraReceita Categoria,
    long ValorEmCentavos,
    DateOnly Data,
    bool Recebida,
    Guid? DocumentoId
);

/// <summary>Corpo da correção de uma receita.</summary>
/// <param name="Descricao">O que é.</param>
/// <param name="Origem">De quem veio.</param>
/// <param name="Categoria">De onde vem o dinheiro.</param>
/// <param name="ValorEmCentavos">Valor, em centavos.</param>
/// <param name="Data">Dia previsto, ou dia em que entrou.</param>
/// <param name="DocumentoId">Comprovante no acervo.</param>
public sealed record AtualizarOutraReceitaRequestDTO(
    string? Descricao,
    string? Origem,
    CategoriaDeOutraReceita Categoria,
    long ValorEmCentavos,
    DateOnly Data,
    Guid? DocumentoId
);

/// <summary>Corpo do recebimento de uma receita prevista.</summary>
/// <param name="RecebidaEm">Dia em que o dinheiro entrou, <c>aaaa-mm-dd</c>.</param>
public sealed record ReceberOutraReceitaRequestDTO(DateOnly RecebidaEm);

/// <summary>Uma receita da turma que não vem de formando.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="Origem">De quem veio, se informado.</param>
/// <param name="Categoria">De onde vem o dinheiro.</param>
/// <param name="ValorEmCentavos">Valor.</param>
/// <param name="Data">Dia previsto, ou dia em que entrou.</param>
/// <param name="Status"><c>Prevista</c>, <c>Recebida</c> ou <c>Cancelada</c>.</param>
/// <param name="Documento">Comprovante no acervo — abre pelo download de lá. Nulo: sem comprovante, ou deixou de ser visível à turma.</param>
/// <param name="Atrasada">Prevista com data no passado. Calculado, nunca gravado.</param>
public sealed record OutraReceitaDTO(
    Guid Id,
    string Descricao,
    string? Origem,
    CategoriaDeOutraReceita Categoria,
    long ValorEmCentavos,
    DateOnly Data,
    StatusDaOutraReceita Status,
    DocumentoDoAcervo? Documento,
    bool Atrasada
);

/// <summary>A faixa da tela Receitas, dentro do mesmo filtro da lista.</summary>
/// <param name="Todas">Todas as do filtro.</param>
/// <param name="Prevista">A receber, atrasadas incluídas.</param>
/// <param name="Atrasada">Previstas com data no passado.</param>
/// <param name="Recebida">Já recebidas.</param>
/// <param name="Cancelada">Canceladas.</param>
public sealed record ResumoDeOutrasReceitasDTO(
    SomaDeLancamentosDTO Todas,
    SomaDeLancamentosDTO Prevista,
    SomaDeLancamentosDTO Atrasada,
    SomaDeLancamentosDTO Recebida,
    SomaDeLancamentosDTO Cancelada
);

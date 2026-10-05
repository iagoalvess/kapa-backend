using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Financeiro.Models;

namespace Backend.Business.Financeiro.Interfaces;

/// <summary>
/// O dinheiro que entra sem ser parcela de formando (Sprint 28).
/// </summary>
/// <remarks>
/// O espelho de <see cref="IDespesaService"/> (decisão 1). Receber não exige comprovante: rendimento
/// de aplicação não tem anexo, e o extrato do banco que o prova já está com a tesouraria.
/// </remarks>
public interface IOutraReceitaService
{
    /// <summary>Uma página das receitas da turma, da mais recente.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Categoria, situação, período e busca.</param>
    Task<Result<PaginaDe<OutraReceitaResumo>>> Listar(PaginacaoRequest paginacao, FiltroDeOutrasReceitas filtro, CancellationToken ct = default);

    /// <summary>Quantas e quanto, por situação, dentro do mesmo filtro — a faixa da tela, numa consulta.</summary>
    /// <param name="filtro">Categoria, período e busca; a situação é ignorada.</param>
    Task<Result<ResumoDeOutrasReceitas>> Resumir(FiltroDeOutrasReceitas filtro, CancellationToken ct = default);

    /// <summary>Uma receita da turma.</summary>
    /// <param name="id">Receita.</param>
    Task<Result<OutraReceitaResumo>> ObterPorId(Guid id, CancellationToken ct = default);

    /// <summary>Lança a receita, prevista ou já recebida.</summary>
    /// <remarks>A mesma receita duas vezes seguidas (descrição, origem e data) devolve 409 <c>financeiro.outra_receita_duplicada</c>.</remarks>
    /// <param name="dados">Descrição, origem, categoria, valor, data e comprovante.</param>
    /// <param name="usuarioId">Quem lança — dono do comprovante enviado, se vier arquivo.</param>
    /// <param name="comprovante">Comprovante novo, se anexado aqui; ausente, vale o <c>DocumentoId</c>.</param>
    Task<Result<OutraReceitaResumo>> Lancar(NovaOutraReceita dados, Guid usuarioId, NovoArquivo? comprovante, CancellationToken ct = default);

    /// <summary>Corrige uma receita lançada — prevista ou recebida.</summary>
    /// <param name="id">Receita.</param>
    /// <param name="dados">Dados novos.</param>
    /// <param name="usuarioId">Quem corrige — dono do comprovante enviado, se vier arquivo.</param>
    /// <param name="comprovante">Comprovante novo, se anexado aqui; ausente, vale o <c>DocumentoId</c>.</param>
    Task<Result<OutraReceitaResumo>> Atualizar(
        Guid id,
        DadosDaOutraReceita dados,
        Guid usuarioId,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    );

    /// <summary>Registra a entrada do dinheiro de uma receita prevista.</summary>
    /// <param name="id">Receita.</param>
    /// <param name="dados">Dia em que entrou.</param>
    Task<Result<OutraReceitaResumo>> Receber(Guid id, ReceberOutraReceita dados, CancellationToken ct = default);

    /// <summary>Cancela uma receita prevista.</summary>
    /// <param name="id">Receita.</param>
    /// <param name="autorId">Quem cancelou — vai na trilha com o retrato da receita.</param>
    Task<Result<OutraReceitaResumo>> Cancelar(Guid id, Guid autorId, CancellationToken ct = default);
}

/// <summary>
/// Receitas da formatura selecionada.
/// </summary>
/// <remarks>Isoladas pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IOutraReceitaRepository
{
    /// <summary>Uma página das receitas, da mais recente.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Categoria, situação, período e busca.</param>
    /// <param name="hoje">Dia que separa prevista de atrasada.</param>
    Task<PaginaDe<OutraReceitaResumo>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeOutrasReceitas filtro,
        DateOnly hoje,
        CancellationToken ct = default
    );

    /// <summary>Quantas e quanto, por situação, numa consulta agrupada.</summary>
    /// <param name="filtro">Filtro da tela; a situação é ignorada.</param>
    /// <param name="hoje">Dia que separa prevista de atrasada.</param>
    Task<IReadOnlyList<ContagemDeOutrasReceitas>> Contar(FiltroDeOutrasReceitas filtro, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Uma receita, como a lista a mostra; nula se não existir aqui.</summary>
    /// <param name="id">Receita.</param>
    /// <param name="hoje">Dia que separa prevista de atrasada.</param>
    Task<OutraReceitaResumo?> Obter(Guid id, DateOnly hoje, CancellationToken ct = default);

    /// <summary>A receita rastreada para alteração; nula se não existir aqui.</summary>
    /// <param name="id">Receita.</param>
    Task<OutraReceita?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>Se a turma já tem esta receita (descrição, origem e data), cancelada de fora.</summary>
    /// <param name="descricao">Descrição, já aparada.</param>
    /// <param name="origem">Origem, já aparada; nula se não houver.</param>
    /// <param name="data">Data da receita.</param>
    Task<bool> ExisteIgual(string descricao, string? origem, DateOnly data, CancellationToken ct = default);

    /// <summary>Marca uma receita nova para inclusão.</summary>
    /// <param name="outraReceita">Receita.</param>
    Task Adicionar(OutraReceita outraReceita, CancellationToken ct = default);
}

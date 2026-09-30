using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// As parcelas da turma como a gestão as consulta, pela formatura da sessão.
/// </summary>
public interface IConsultaDeParcelasService
{
    /// <summary>Uma página das parcelas da turma.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Formando, situação e período.</param>
    Task<Result<PaginaDe<ParcelaResumo>>> Listar(PaginacaoRequest paginacao, FiltroDeParcelas filtro, CancellationToken ct = default);

    /// <summary>Quantas parcelas e quanto somam por situação — a faixa da tela Parcelas.</summary>
    /// <param name="filtro">Formando, período e busca; a situação é ignorada.</param>
    Task<Result<ResumoDeParcelas>> Resumir(FiltroDeParcelas filtro, CancellationToken ct = default);
}

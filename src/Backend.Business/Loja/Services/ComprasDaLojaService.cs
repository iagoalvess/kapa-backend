using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Arquivos.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Relatorios.Models;
using Backend.Business.Relatorios.Services;

namespace Backend.Business.Loja.Services;

/// <summary>
/// As compras da loja do lado da Gestão (Sprint 26, P5): a lista da devolução, o resumo do que vendeu e a planilha.
/// </summary>
/// <param name="compras">As compras da turma.</param>
/// <param name="agenda">A festa, para o resumo apontar o evento.</param>
public sealed class ComprasDaLojaService(ICompraDeConviteRepository compras, IEventoDaTurmaRepository agenda) : IComprasDaLojaService
{
    /// <inheritdoc />
    public async Task<Result<PaginaDe<CompraNaGestao>>> Listar(PaginacaoRequest paginacao, FiltroDeCompras filtro, CancellationToken ct = default) =>
        await compras.Listar(paginacao.Normalizar(), filtro, ct);

    /// <inheritdoc />
    public async Task<Result<ResumoDaLoja>> Resumir(CancellationToken ct = default) =>
        await compras.Resumir(ct) with
        {
            FestaId = (await agenda.ObterDoTipo(TipoDeEvento.Festa, ct))?.Id,
        };

    /// <inheritdoc />
    public async Task<Result<ArquivoParaDownload>> Exportar(FiltroDeCompras filtro, CancellationToken ct = default)
    {
        var linhas = await compras.ListarTodas(filtro, ct);

        var tabela = new TabelaDoRelatorio(
            "Compras da loja",
            $"Gerado em {DataUtils.ParaExibicao(DateTime.UtcNow).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} — a devolução é da turma",
            [
                new("Data", 1.2f),
                new("Comprador", 2f),
                new("E-mail", 2.4f),
                new("CPF", 1.4f),
                new("Convite", 1.6f),
                new("Qtd.", 0.6f, Direita: true),
                new("Valor", 1.2f, Direita: true),
                new("Meio", 0.9f),
                new("Situação", 1.2f),
                new("Pago em", 1.1f, Direita: true),
                new("Valor pago", 1.2f, Direita: true),
            ],
            [
                .. linhas.Select(linha =>
                    (IReadOnlyList<Celula>)
                        [
                            Celula.Data(DateOnly.FromDateTime(DataUtils.ParaExibicao(linha.CriadaEm))),
                            Celula.De(linha.Nome ?? "Dados apagados"),
                            Celula.De(linha.Email),
                            Celula.De(linha.Cpf),
                            Celula.De(linha.Item),
                            Celula.Inteiro(linha.Quantidade),
                            Celula.Reais(linha.ValorEmCentavos),
                            Celula.De(MeiosDePagamento.Rotulo(linha.Meio)),
                            Celula.De(Rotulo(linha.Status)),
                            Celula.Data(linha.PagaEm is { } paga ? DateOnly.FromDateTime(DataUtils.ParaExibicao(paga)) : null),
                            Celula.Reais(linha.ValorPagoEmCentavos),
                        ]
                ),
            ]
        );

        return new ArquivoParaDownload(
            new MemoryStream(RelatorioEmExcel.Gerar(tabela)),
            "compras-da-loja.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        );
    }

    /// <summary>Como a situação aparece na planilha.</summary>
    /// <param name="status">Situação.</param>
    public static string Rotulo(StatusDaCompra status) =>
        status switch
        {
            StatusDaCompra.Pendente => "Aguardando pagamento",
            StatusDaCompra.Paga => "Paga",
            StatusDaCompra.Expirada => "Expirada",
            StatusDaCompra.ADevolver => "A devolver",
            _ => "Devolvida",
        };
}

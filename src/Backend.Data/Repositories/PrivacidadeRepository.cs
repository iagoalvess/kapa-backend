using Backend.Business.Cobrancas.Models;
using Backend.Business.Enquetes.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>Consultas internas do suporte, sempre recortadas pelo titular e abrangendo todas as turmas.</summary>
/// <param name="db">Contexto de dados do escopo.</param>
public sealed class PrivacidadeRepository(AppDbContext db) : IPrivacidadeRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RespostaDaEnquete>> ListarRespostasParaAnonimizarDeTodasAsFormaturas(
        Guid usuarioId,
        CancellationToken ct = default
    ) =>
        await db
            .RespostasDeEnquetes.IgnoreQueryFilters()
            .Where(r => db.Vinculos.IgnoreQueryFilters().Any(v => v.Id == r.VinculoId && v.UsuarioId == usuarioId))
            .Include(r => r.Escolhas)
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Quatro consultas, e não uma: a conta, os vínculos com cadastro, as parcelas e as preferências.
    /// Um <c>JOIN</c> só multiplicaria a linha do perfil pela quantidade de parcelas, e o titular de
    /// três turmas com quarenta parcelas cada traria o endereço dele cento e vinte vezes.
    /// <para>
    /// Nos consentimentos, a ordenação vem <b>antes</b> da projeção: ordenar pelo campo do record já
    /// construído não traduz para SQL, e o EF Core recusa a consulta inteira em tempo de execução.
    /// </para>
    /// </remarks>
    public async Task<MeusDados?> ObterMeusDadosDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default)
    {
        var conta = await db
            .Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new DadosDaConta(u.Id, u.Nome, u.Email!, u.EmailConfirmed, u.PhoneNumber, u.CriadoEm, u.AnonimizadoEm))
            .FirstOrDefaultAsync(ct);

        if (conta is null)
            return null;

        var vinculos = await db
            .Vinculos.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.UsuarioId == usuarioId)
            .Join(db.Formaturas.IgnoreQueryFilters(), v => v.FormaturaId, f => f.Id, (v, f) => new { Vinculo = v, Formatura = f })
            .OrderBy(x => x.Formatura.Nome)
            .ThenBy(x => x.Vinculo.Id)
            .ToListAsync(ct);

        var vinculoIds = vinculos.ConvertAll(x => x.Vinculo.Id);

        var respostas = await (
            from resposta in db.RespostasDeEnquetes.IgnoreQueryFilters().AsNoTracking()
            join enquete in db.Enquetes.IgnoreQueryFilters() on resposta.EnqueteId equals enquete.Id
            where resposta.VinculoId != null && vinculoIds.Contains(resposta.VinculoId.Value)
            select new
            {
                resposta.VinculoId,
                resposta.Id,
                resposta.EnqueteId,
                enquete.Pergunta,
                resposta.RespondidaEm,
            }
        ).ToListAsync(ct);
        var respostaIds = respostas.Select(r => r.Id).ToArray();
        var escolhas = await (
            from escolha in db.EscolhasDeEnquetes.IgnoreQueryFilters().AsNoTracking()
            join opcao in db.OpcoesDeEnquetes.IgnoreQueryFilters() on escolha.OpcaoId equals opcao.Id
            where respostaIds.Contains(escolha.RespostaId)
            orderby opcao.Ordem
            select new { escolha.RespostaId, opcao.Texto }
        ).ToListAsync(ct);

        var perfis = await db.PerfisDeFormandos.IgnoreQueryFilters().AsNoTracking().Where(p => vinculoIds.Contains(p.VinculoId)).ToListAsync(ct);

        var parcelas = await db
            .Parcelas.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => vinculoIds.Contains(p.VinculoId))
            .Join(
                db.ItensDeCobranca.IgnoreQueryFilters(),
                p => p.ItemDeCobrancaId,
                i => i.Id,
                (p, i) =>
                    new
                    {
                        p.VinculoId,
                        Parcela = p,
                        Item = i,
                    }
            )
            .OrderBy(x => x.Parcela.Vencimento)
            .ThenBy(x => x.Parcela.Numero)
            .Select(x => new
            {
                x.VinculoId,
                Linha = new MinhaParcela(
                    x.Parcela.Id,
                    x.Item.Descricao ?? x.Item.Tipo.ToString(),
                    x.Parcela.Numero,
                    x.Parcela.Vencimento,
                    x.Parcela.ValorOriginalEmCentavos,
                    x.Parcela.Status.ToString(),
                    x.Parcela.ValorPagoEmCentavos,
                    x.Parcela.PagoEm
                ),
                Cancelada = x.Parcela.Status == StatusDaParcela.Cancelada,
            })
            .ToListAsync(ct);

        var adesoes = await db
            .Adesoes.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => vinculoIds.Contains(a.VinculoId))
            .Select(a => new { a.VinculoId, Adesao = new MinhaAdesao(a.Versao, a.AceitoEm, a.EnderecoIp) })
            .ToListAsync(ct);

        var consentimentos = await (
            from consentimento in db.Consentimentos.IgnoreQueryFilters().AsNoTracking()
            join documento in db.DocumentosLegais on consentimento.DocumentoLegalId equals documento.Id
            where consentimento.UsuarioId == usuarioId
            orderby consentimento.AceitoEm descending, consentimento.Id descending
            select new ConsentimentoDoUsuario(consentimento.Id, documento.Tipo, consentimento.Versao, consentimento.AceitoEm)
        ).ToListAsync(ct);

        var enviadas = await db
            .NotificacoesEnviadas.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.VinculoId != null && vinculoIds.Contains(n.VinculoId.Value))
            .GroupBy(_ => 1)
            .Select(g => new { Quantidade = g.Count(), Ultima = (DateTime?)g.Max(n => n.CriadoEm) })
            .FirstOrDefaultAsync(ct);

        var turmas = vinculos.ConvertAll(x =>
        {
            var doVinculo = parcelas.Where(p => p.VinculoId == x.Vinculo.Id).ToList();

            return new MeusDadosDaTurma(
                x.Formatura.Id,
                x.Formatura.Nome,
                x.Formatura.Instituicao,
                x.Vinculo.Papel,
                x.Vinculo.Ativo,
                perfis.FirstOrDefault(p => p.VinculoId == x.Vinculo.Id) is { } perfil ? Exportar(perfil) : null,
                new MeuFinanceiro(
                    doVinculo.Where(p => !p.Cancelada).Sum(p => p.Linha.ValorOriginalEmCentavos),
                    doVinculo.Sum(p => p.Linha.ValorPagoEmCentavos ?? 0),
                    [.. doVinculo.Select(p => p.Linha)]
                ),
                adesoes.FirstOrDefault(a => a.VinculoId == x.Vinculo.Id)?.Adesao,
                respostas
                    .Where(r => r.VinculoId == x.Vinculo.Id)
                    .Select(r => new MinhaRespostaExportada(
                        r.EnqueteId,
                        r.Pergunta,
                        r.RespondidaEm,
                        escolhas.Where(e => e.RespostaId == r.Id).Select(e => e.Texto).ToArray()
                    ))
                    .ToArray()
            );
        });

        var doKapa = await ComunicacaoDoKapaRepository.DoTitular(db, usuarioId, maximoDeEnvios: null, ct);

        return new MeusDados(conta, turmas, consentimentos, new MinhasComunicacoes(enviadas?.Quantidade ?? 0, enviadas?.Ultima, doKapa));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PerfilDoFormando>> ListarPerfisParaAnonimizarDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default)
    {
        var vinculoIds = await db.Vinculos.IgnoreQueryFilters().AsNoTracking().Where(v => v.UsuarioId == usuarioId).Select(v => v.Id).ToListAsync(ct);

        return await db.PerfisDeFormandos.IgnoreQueryFilters().Where(p => vinculoIds.Contains(p.VinculoId)).ToListAsync(ct);
    }

    /// <summary>O cadastro no formato da exportação interna.</summary>
    /// <param name="perfil">Cadastro carregado.</param>
    private static PerfilExportado Exportar(PerfilDoFormando perfil) =>
        new(
            perfil.NomeCompleto,
            perfil.Cpf,
            perfil.Telefone,
            perfil.ContatoDeEmergencia.ParaDados(),
            perfil.FotoArquivoId is not null,
            perfil.Completude
        );
}

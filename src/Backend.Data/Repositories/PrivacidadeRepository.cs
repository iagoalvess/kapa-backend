using Backend.Business.Cobrancas.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Os dados do titular, atravessando todas as turmas dele, e a fila de solicitações.
/// </summary>
/// <remarks>
/// <b>Todo método aqui usa <c>IgnoreQueryFilters()</c></b> — é o único repositório do projeto em que
/// isso é a regra e não a exceção, e o sufixo <c>DeTodasAsFormaturas</c> marca os que leem dado de
/// turma. A autorização não some junto com o filtro: o recorte passa a ser <c>usuarioId</c>, que vem
/// do token e não da rota, e nenhuma consulta daqui aceita o identificador de outra pessoa.
/// <para>
/// As solicitações não têm filtro global para ignorar — a entidade não pertence a formatura nenhuma.
/// </para>
/// </remarks>
/// <param name="db">Contexto de dados do escopo.</param>
public sealed class PrivacidadeRepository(AppDbContext db) : IPrivacidadeRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Quatro consultas, e não uma: a conta, os vínculos com cadastro, as parcelas e as preferências.
    /// Um <c>JOIN</c> só multiplicaria a linha do perfil pela quantidade de parcelas, e o titular de
    /// três turmas com quarenta parcelas cada traria o endereço dele cento e vinte vezes.
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

        // A ordenação vem **antes** da projeção: ordenar pelo campo do record já construído não
        // traduz para SQL, e o EF Core recusa a consulta inteira em tempo de execução.
        var consentimentos = await (
            from consentimento in db.Consentimentos.IgnoreQueryFilters().AsNoTracking()
            join documento in db.DocumentosLegais on consentimento.DocumentoLegalId equals documento.Id
            where consentimento.UsuarioId == usuarioId
            orderby consentimento.AceitoEm descending, consentimento.Id descending
            select new ConsentimentoDoUsuario(consentimento.Id, documento.Tipo, consentimento.Versao, consentimento.AceitoEm, consentimento.Revogado)
        ).ToListAsync(ct);

        var preferencias = await db
            .PreferenciasDeNotificacao.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => vinculoIds.Contains(p.VinculoId))
            .Join(
                db.Vinculos.IgnoreQueryFilters(),
                p => p.VinculoId,
                v => v.Id,
                (p, v) => new MinhaPreferencia(v.FormaturaId, p.Tipo.ToString(), p.Ativa)
            )
            .ToListAsync(ct);

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
                adesoes.FirstOrDefault(a => a.VinculoId == x.Vinculo.Id)?.Adesao
            );
        });

        return new MeusDados(conta, turmas, consentimentos, new MinhasComunicacoes(preferencias, enviadas?.Quantidade ?? 0, enviadas?.Ultima));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SolicitacaoDePrivacidade>> ListarDoTitular(Guid usuarioId, CancellationToken ct = default) =>
        await db
            .SolicitacoesDePrivacidade.AsNoTracking()
            .Where(s => s.TitularUsuarioId == usuarioId)
            .OrderByDescending(s => s.CriadoEm)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<SolicitacaoDePrivacidade?> ObterDoTitular(Guid id, Guid usuarioId, CancellationToken ct = default) =>
        db.SolicitacoesDePrivacidade.FirstOrDefaultAsync(s => s.Id == id && s.TitularUsuarioId == usuarioId, ct);

    /// <inheritdoc />
    public Task<SolicitacaoDePrivacidade?> ObterPendente(TipoDeSolicitacao tipo, Guid usuarioId, CancellationToken ct = default) =>
        db
            .SolicitacoesDePrivacidade.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TitularUsuarioId == usuarioId && s.Tipo == tipo && s.Status == StatusDaSolicitacaoDePrivacidade.Pendente, ct);

    /// <inheritdoc />
    /// <remarks>Da mais antiga: pedido de titular não pode ficar para trás enquanto chegam outros.</remarks>
    public async Task<IReadOnlyList<PrivacidadePendente>> ListarVencidas(DateTime agora, int quantidade, CancellationToken ct = default) =>
        await db
            .SolicitacoesDePrivacidade.AsNoTracking()
            .Where(s => s.Status == StatusDaSolicitacaoDePrivacidade.Pendente && s.PrazoEm <= agora)
            .OrderBy(s => s.PrazoEm)
            .ThenBy(s => s.Id)
            .Take(quantidade)
            .Select(s => new PrivacidadePendente(s.Id, s.Tipo))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SolicitacaoDePrivacidade>> ListarArquivosVencidos(
        DateTime agora,
        int quantidade,
        CancellationToken ct = default
    ) =>
        await db
            .SolicitacoesDePrivacidade.Where(s => s.ArquivoId != null && s.ExpiraEm != null && s.ExpiraEm <= agora)
            .OrderBy(s => s.ExpiraEm)
            .Take(quantidade)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<SolicitacaoDePrivacidade?> Obter(Guid id, CancellationToken ct = default) =>
        db.SolicitacoesDePrivacidade.FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <inheritdoc />
    public async Task Adicionar(SolicitacaoDePrivacidade solicitacao, CancellationToken ct = default) =>
        await db.SolicitacoesDePrivacidade.AddAsync(solicitacao, ct);

    /// <inheritdoc />
    public Task<TitularParaAviso?> ObterTitular(Guid usuarioId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => new TitularParaAviso(u.Id, u.Nome, u.Email!)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Um presidente por turma em que o titular tem vínculo — e só os ativos: avisar quem saiu da
    /// comissão é vazar um pedido de LGPD para fora dela.
    /// </remarks>
    public async Task<IReadOnlyList<PresidenteParaAviso>> ListarPresidentesParaAviso(Guid usuarioId, CancellationToken ct = default)
    {
        var turmas = await db
            .Vinculos.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.UsuarioId == usuarioId && v.Ativo)
            .Select(v => new { v.FormaturaId, v.Id })
            .ToListAsync(ct);

        if (turmas.Count == 0)
            return [];

        var formaturaIds = turmas.ConvertAll(t => t.FormaturaId);
        var vinculoIds = turmas.ConvertAll(t => t.Id);

        var emAberto = await db
            .Parcelas.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => vinculoIds.Contains(p.VinculoId) && (p.Status == StatusDaParcela.Aberta || p.Status == StatusDaParcela.Vencida))
            .GroupBy(p => p.FormaturaId)
            .Select(g => new { FormaturaId = g.Key, Total = g.Sum(p => p.ValorOriginalEmCentavos) })
            .ToListAsync(ct);

        var presidentes = await db
            .Vinculos.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => formaturaIds.Contains(v.FormaturaId) && v.Ativo && v.Papel == PapelNaFormatura.Presidente && v.UsuarioId != usuarioId)
            .Join(db.Users, v => v.UsuarioId, u => u.Id, (v, u) => new { v.FormaturaId, u.Email })
            .Join(
                db.Formaturas.IgnoreQueryFilters(),
                x => x.FormaturaId,
                f => f.Id,
                (x, f) =>
                    new
                    {
                        x.FormaturaId,
                        x.Email,
                        f.Nome,
                    }
            )
            .ToListAsync(ct);

        return
        [
            .. presidentes
                .Where(p => p.Email is not null)
                .Select(p => new PresidenteParaAviso(p.Email!, p.Nome, emAberto.FirstOrDefault(a => a.FormaturaId == p.FormaturaId)?.Total ?? 0)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PerfilDoFormando>> ListarPerfisParaAnonimizarDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default)
    {
        var vinculoIds = await db.Vinculos.IgnoreQueryFilters().AsNoTracking().Where(v => v.UsuarioId == usuarioId).Select(v => v.Id).ToListAsync(ct);

        return await db.PerfisDeFormandos.IgnoreQueryFilters().Where(p => vinculoIds.Contains(p.VinculoId)).ToListAsync(ct);
    }

    /// <summary>O cadastro no formato que a tela e o pacote leem.</summary>
    /// <param name="perfil">Cadastro carregado.</param>
    private static PerfilExportado Exportar(PerfilDoFormando perfil) =>
        new(
            perfil.NomeCompleto,
            perfil.NomeNoDiploma,
            perfil.Cpf,
            perfil.Rg,
            perfil.Matricula,
            perfil.Telefone,
            perfil.DataDeNascimento,
            perfil.Observacoes,
            perfil.Endereco.ParaDados(),
            perfil.ContatoDeEmergencia.ParaDados(),
            perfil.FotoArquivoId is not null,
            perfil.Completude
        );
}

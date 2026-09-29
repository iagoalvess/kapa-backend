using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Models;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Texto;
using Backend.Business.Formaturas.Models;
using Backend.Business.Usuarios.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Consultas agregadas do painel administrativo e as leituras do painel de suporte.
/// </summary>
/// <remarks>
/// <b>Tudo aqui atravessa formaturas.</b> Quem chama é o perfil <c>Administrador</c> da plataforma,
/// que não tem turma na sessão — então o filtro global do <c>AppDbContext</c> devolveria zero linha
/// em toda consulta que passe por vínculo, parcela, adesão ou cadastro. É a exceção documentada de
/// <c>IgnoreQueryFilters</c>, e é por isso que os métodos da interface dizem "de todas".
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class AdminRepository(AppDbContext db) : IAdminRepository
{
    /// <summary>Quantos e-mails de marketing a conta no suporte mostra — os últimos respondem "por que recebi isto?".</summary>
    private const int EnviosDeMarketingNoSuporte = 10;

    /// <inheritdoc />
    /// <remarks>
    /// As contagens de usuário saem em uma consulta só (agregação no banco); sessões e
    /// administradores vão em mais duas. Três idas ao banco para uma tela chamada algumas vezes
    /// por dia não justifica a view materializada que a alternativa exigiria.
    /// </remarks>
    public async Task<ResumoAdmin> ObterResumo(DateTime agoraUtc, DateTime cadastradosDesde, CancellationToken ct = default)
    {
        var usuarios = await db
            .Users.GroupBy(_ => 1)
            .Select(grupo => new
            {
                Total = grupo.LongCount(),
                Ativos = grupo.LongCount(u => u.Ativo),
                Recentes = grupo.LongCount(u => u.CriadoEm >= cadastradosDesde),
            })
            .SingleOrDefaultAsync(ct);

        var administradores = await UsuarioRepository.AdministradoresAtivos(db).LongCountAsync(ct);

        var sessoesAtivas = await db.RefreshTokens.CountAsync(t => t.RevogadoEm == null && t.ExpiraEm > agoraUtc, ct);

        var total = usuarios?.Total ?? 0;
        var ativos = usuarios?.Ativos ?? 0;

        return new ResumoAdmin(total, ativos, total - ativos, administradores, sessoesAtivas, usuarios?.Recentes ?? 0, agoraUtc);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TurmaEncontrada>> BuscarTurmasDeTodasAsFormaturas(string termo, int limite, CancellationToken ct = default)
    {
        var padrao = Busca.Padrao(termo);

        return await db
            .Formaturas.AsNoTracking()
            .Where(f =>
                EF.Functions.ILike(EF.Functions.Unaccent(f.Nome), padrao)
                || EF.Functions.ILike(EF.Functions.Unaccent(f.Instituicao), padrao)
                || EF.Functions.ILike(EF.Functions.Unaccent(f.Curso), padrao)
            )
            .OrderByDescending(f => f.CriadoEm)
            .Take(limite)
            .Select(f => new TurmaEncontrada(
                f.Id,
                f.Nome,
                f.Instituicao,
                f.Curso,
                f.Status.ToString(),
                db.Vinculos.IgnoreQueryFilters().Count(v => v.FormaturaId == f.Id && v.Ativo && v.DesligadoEm == null)
            ))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O e-mail é comparado sem <c>unaccent</c>: endereço não tem acento, e passar a coluna pela
    /// função tiraria dela qualquer chance de índice.
    /// </remarks>
    public async Task<IReadOnlyList<UsuarioEncontrado>> BuscarUsuariosDeTodasAsFormaturas(string termo, int limite, CancellationToken ct = default)
    {
        var padrao = Busca.Padrao(termo);

        return await db
            .Users.AsNoTracking()
            .Where(u => EF.Functions.ILike(EF.Functions.Unaccent(u.Nome), padrao) || EF.Functions.ILike(u.Email!, padrao))
            .OrderBy(u => u.Nome)
            .Take(limite)
            .Select(u => new UsuarioEncontrado(
                u.Id,
                u.Nome,
                u.Email!,
                u.Ativo,
                db.Vinculos.IgnoreQueryFilters().Count(v => v.UsuarioId == u.Id && v.Ativo && v.DesligadoEm == null)
            ))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Duas idas ao banco: a turma com as contagens, e os membros. Juntar as duas multiplicaria as
    /// linhas da turma pelo número de membros só para desfazer o produto em memória.
    /// <para>
    /// O CPF sai do cadastro <b>já mascarado</b>. A coluna é cifrada, então o valor é decifrado ao
    /// materializar e mascarado aqui, na borda de leitura — o painel nunca vê o número inteiro.
    /// </para>
    /// </remarks>
    public async Task<TurmaNoSuporte?> ObterTurmaDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default)
    {
        var turma = await (
            from f in db.Formaturas.AsNoTracking()
            where f.Id == formaturaId
            join a in db.Assinaturas.AsNoTracking().IgnoreQueryFilters() on f.Id equals a.FormaturaId into assinaturas
            select new
            {
                f.Id,
                f.Nome,
                f.Instituicao,
                f.Curso,
                f.Ano,
                f.Semestre,
                f.Status,
                f.CriadoEm,
                f.AtivadaEm,
                Assinatura = assinaturas
                    .OrderByDescending(a => a.CriadoEm)
                    .ThenByDescending(a => a.Id)
                    .Select(a => new
                    {
                        a.Id,
                        a.PlanoId,
                        a.Status,
                        a.VigenteAte,
                        a.CanceladaEm,
                        a.CriadoEm,
                    })
                    .FirstOrDefault(),
                Parcelas = db.Parcelas.IgnoreQueryFilters().Count(p => p.FormaturaId == f.Id && p.Status != StatusDaParcela.Cancelada),
                ParcelasPagas = db.Parcelas.IgnoreQueryFilters().Count(p => p.FormaturaId == f.Id && p.Status == StatusDaParcela.Paga),
                Adesoes = db.Adesoes.IgnoreQueryFilters().Count(a => a.FormaturaId == f.Id),
            }
        ).FirstOrDefaultAsync(ct);

        if (turma is null)
            return null;

        var plano = turma.Assinatura is null
            ? null
            : await db
                .Planos.AsNoTracking()
                .Where(p => p.Id == turma.Assinatura.PlanoId)
                .Select(p => new
                {
                    p.Nome,
                    p.Codigo,
                    p.LimiteDeFormandos,
                })
                .FirstOrDefaultAsync(ct);

        var pagamentos = await (
            from c in db.CobrancasDaAssinatura.AsNoTracking()
            join a in db.Assinaturas.AsNoTracking().IgnoreQueryFilters() on c.AssinaturaId equals a.Id
            join p in db.Planos.AsNoTracking() on c.PlanoId equals p.Id
            where a.FormaturaId == formaturaId
            orderby c.CriadoEm descending, c.Id descending
            select new CobrancaDoPlanoResumo(
                c.Id,
                p.Nome,
                c.Motivo,
                c.Meio,
                c.ValorEmCentavos,
                c.Situacao,
                c.Url,
                c.CriadoEm,
                c.PagaEm,
                c.ValorEstornadoEmCentavos,
                c.EstornadaEm
            )
        ).ToListAsync(ct);

        var membros = await (
            from v in db.Vinculos.AsNoTracking().IgnoreQueryFilters()
            join u in db.Users.AsNoTracking() on v.UsuarioId equals u.Id
            where v.FormaturaId == formaturaId
            join p in db.PerfisDeFormandos.AsNoTracking().IgnoreQueryFilters() on v.Id equals p.VinculoId into perfis
            orderby v.Ativo descending, u.Nome
            select new
            {
                v.UsuarioId,
                u.Nome,
                Email = u.Email!,
                v.Papel,
                v.Ativo,
                v.DesligadoEm,
                Cpf = perfis.Select(p => p.Cpf).FirstOrDefault(),
            }
        ).ToListAsync(ct);

        return new TurmaNoSuporte(
            turma.Id,
            turma.Nome,
            turma.Instituicao,
            turma.Curso,
            turma.Ano,
            turma.Semestre,
            turma.Status.ToString(),
            turma.CriadoEm,
            turma.AtivadaEm,
            turma.Assinatura is null
                ? null
                : new AssinaturaNoSuporte(
                    turma.Assinatura.Id,
                    plano?.Nome ?? "(plano removido)",
                    plano?.Codigo ?? string.Empty,
                    plano?.LimiteDeFormandos ?? 0,
                    turma.Assinatura.Status.ToString(),
                    turma.Assinatura.VigenteAte,
                    turma.Assinatura.CanceladaEm,
                    turma.Assinatura.CriadoEm
                ),
            [
                .. membros.Select(m => new MembroNoSuporte(
                    m.UsuarioId,
                    m.Nome,
                    m.Email,
                    m.Papel,
                    m.Ativo,
                    m.DesligadoEm,
                    FormatosBrasileiros.MascararCpf(m.Cpf)
                )),
            ],
            turma.Parcelas,
            turma.ParcelasPagas,
            turma.Adesoes,
            pagamentos
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Paga e estornada entram — a estornada saiu na nota do mês em que foi paga, e quem emite precisa ver a devolução
    /// ao lado. O Presidente é o de vínculo ativo mais antigo; o CPF é decifrado ao materializar e sai inteiro, ao
    /// contrário do resto do painel: é o que o portal da prefeitura pede para identificar o tomador.
    /// </remarks>
    public async Task<IReadOnlyList<PagamentoParaNota>> ListarPagamentosParaNotaDeTodasAsFormaturas(
        DateTime inicio,
        DateTime fim,
        CancellationToken ct = default
    )
    {
        var linhas = await (
            from c in db.CobrancasDaAssinatura.AsNoTracking()
            join a in db.Assinaturas.AsNoTracking().IgnoreQueryFilters() on c.AssinaturaId equals a.Id
            join f in db.Formaturas.AsNoTracking() on a.FormaturaId equals f.Id
            join p in db.Planos.AsNoTracking() on c.PlanoId equals p.Id
            where
                c.PagaEm >= inicio
                && c.PagaEm < fim
                && (c.Situacao == SituacaoDaCobrancaDoPlano.Paga || c.Situacao == SituacaoDaCobrancaDoPlano.Estornada)
            orderby c.PagaEm, c.Id
            select new
            {
                PagaEm = c.PagaEm!.Value,
                Turma = f.Nome,
                f.Instituicao,
                Plano = p.Nome,
                c.Motivo,
                c.Meio,
                c.ValorEmCentavos,
                c.ValorEstornadoEmCentavos,
                c.IdDoPagamento,
                Presidente = (
                    from v in db.Vinculos.IgnoreQueryFilters()
                    join u in db.Users on v.UsuarioId equals u.Id
                    where v.FormaturaId == f.Id && v.Ativo && v.Papel == PapelNaFormatura.Presidente
                    orderby v.CriadoEm
                    select new
                    {
                        u.Nome,
                        u.Email,
                        v.Id,
                    }
                ).FirstOrDefault(),
            }
        ).ToListAsync(ct);

        var vinculos = linhas.Where(l => l.Presidente != null).Select(l => l.Presidente!.Id).Distinct().ToList();

        var cpfs = await db
            .PerfisDeFormandos.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(perfil => vinculos.Contains(perfil.VinculoId))
            .Select(perfil => new { perfil.VinculoId, perfil.Cpf })
            .ToListAsync(ct);

        return
        [
            .. linhas.Select(l => new PagamentoParaNota(
                l.PagaEm,
                l.Turma,
                l.Instituicao,
                l.Plano,
                l.Motivo,
                l.Meio,
                l.ValorEmCentavos,
                l.ValorEstornadoEmCentavos,
                l.Presidente?.Nome,
                l.Presidente?.Email,
                cpfs.FirstOrDefault(cpf => cpf.VinculoId == l.Presidente?.Id)?.Cpf,
                l.IdDoPagamento
            )),
        ];
    }

    /// <inheritdoc />
    public async Task<UsuarioNoSuporte?> ObterUsuarioDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default)
    {
        var conta = await db
            .Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new
            {
                u.Id,
                u.Nome,
                Email = u.Email!,
                u.EmailConfirmed,
                u.Ativo,
                u.LockoutEnd,
                u.AccessFailedCount,
                u.AnonimizadoEm,
                u.CriadoEm,
                Perfis = (
                    from vinculo in db.UserRoles
                    join perfil in db.Roles on vinculo.RoleId equals perfil.Id
                    where vinculo.UserId == u.Id
                    select perfil.Name!
                ).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (conta is null)
            return null;

        var vinculos = await (
            from v in db.Vinculos.AsNoTracking().IgnoreQueryFilters()
            join f in db.Formaturas.AsNoTracking() on v.FormaturaId equals f.Id
            where v.UsuarioId == usuarioId
            orderby v.Ativo descending, f.Nome
            select new VinculoNoSuporte(f.Id, f.Nome, f.Instituicao, f.Status.ToString(), v.Papel, v.Ativo, v.DesligadoEm)
        ).ToListAsync(ct);

        return new UsuarioNoSuporte(
            conta.Id,
            conta.Nome,
            conta.Email,
            conta.EmailConfirmed,
            conta.Ativo,
            conta.LockoutEnd?.UtcDateTime,
            conta.AccessFailedCount,
            conta.Perfis,
            conta.AnonimizadoEm,
            conta.CriadoEm,
            vinculos,
            await ComunicacaoDoKapaRepository.DoTitular(db, usuarioId, EnviosDeMarketingNoSuporte, ct)
        );
    }
}

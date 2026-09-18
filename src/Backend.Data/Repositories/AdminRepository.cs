using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Texto;
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
    /// <inheritdoc />
    /// <remarks>
    /// As contagens de usuário saem em uma consulta só (agregação no banco); sessões e
    /// administradores vão em mais duas. Três idas ao banco para uma tela chamada algumas vezes
    /// por dia não justifica a view materializada que a alternativa exigiria.
    /// </remarks>
    public async Task<ResumoAdmin> ObterResumo(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var limiteDeCadastro = agora.AddDays(-30);

        var usuarios = await db
            .Users.GroupBy(_ => 1)
            .Select(grupo => new
            {
                Total = grupo.LongCount(),
                Ativos = grupo.LongCount(u => u.Ativo),
                Recentes = grupo.LongCount(u => u.CriadoEm >= limiteDeCadastro),
            })
            .SingleOrDefaultAsync(ct);

        var administradores = await ConsultarAdministradoresAtivos().LongCountAsync(ct);

        var sessoesAtivas = await db.RefreshTokens.CountAsync(t => t.RevogadoEm == null && t.ExpiraEm > agora, ct);

        var total = usuarios?.Total ?? 0;
        var ativos = usuarios?.Ativos ?? 0;

        return new ResumoAdmin(total, ativos, total - ativos, administradores, sessoesAtivas, usuarios?.Recentes ?? 0, agora);
    }

    /// <inheritdoc />
    public Task<int> ContarAdministradoresAtivos(CancellationToken ct = default) => ConsultarAdministradoresAtivos().CountAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TurmaEncontrada>> BuscarTurmas(string termo, int limite, CancellationToken ct = default)
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
    public async Task<IReadOnlyList<UsuarioEncontrado>> BuscarUsuarios(string termo, int limite, CancellationToken ct = default)
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
    public async Task<TurmaNoSuporte?> ObterTurma(Guid formaturaId, CancellationToken ct = default)
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
            turma.Adesoes
        );
    }

    /// <inheritdoc />
    public async Task<UsuarioNoSuporte?> ObterUsuario(Guid usuarioId, CancellationToken ct = default)
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
            vinculos
        );
    }

    private IQueryable<Usuario> ConsultarAdministradoresAtivos() =>
        from usuario in db.Users.AsNoTracking()
        join vinculo in db.UserRoles on usuario.Id equals vinculo.UserId
        join perfil in db.Roles on vinculo.RoleId equals perfil.Id
        where perfil.Name == PerfisPadrao.Administrador && usuario.Ativo
        select usuario;
}

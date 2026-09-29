using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Backend.Business.Usuarios.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Preferência, histórico e envios de marketing.
/// </summary>
/// <param name="db">Contexto de dados.</param>
public sealed class ComunicacaoDoKapaRepository(AppDbContext db) : IComunicacaoDoKapaRepository
{
    /// <summary>Os papéis de Gestão, em array: é o que o Npgsql traduz para <c>= ANY</c>.</summary>
    private static readonly string[] Gestao = [.. PapelNaFormatura.Gestao];

    /// <inheritdoc />
    public Task<Usuario?> ObterUsuario(Guid usuarioId, CancellationToken ct = default) => db.Users.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);

    /// <inheritdoc />
    public async Task Adicionar(ConsentimentoDeMarketing consentimento, CancellationToken ct = default) =>
        await db.ConsentimentosDeMarketing.AddAsync(consentimento, ct);

    /// <inheritdoc />
    public async Task Adicionar(EnvioDeMarketing envio, CancellationToken ct = default) => await db.EnviosDeMarketing.AddAsync(envio, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Uma consulta só, com as condições de cada jornada devolvidas como colunas: quem decide é
    /// <see cref="CandidatoDeMarketing.JornadaVencida"/>, e o teste dela não precisa de banco.
    /// <para>
    /// "A comissão voltou" é uma sessão aberta por alguém da comissão a partir do dia seguinte à criação — o
    /// refresh token nasce em cada login e em cada renovação, e fica 30 dias depois de inativo, mais que a janela.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<CandidatoDeMarketing>> ListarCandidatosDeTodasAsFormaturas(DateTime agoraUtc, CancellationToken ct = default)
    {
        var criadasDepoisDe = agoraUtc - JornadaDeMarketing.FimDasJornadas;
        var criadasAte = agoraUtc - JornadaDeMarketing.InicioDeCriouENaoVoltou;
        var ultimoEnvioAntesDe = agoraUtc - JornadaDeMarketing.Intervalo;

        return await (
            from formatura in db.Formaturas.AsNoTracking()
            where
                formatura.Status == StatusDaFormatura.Ativa
                && formatura.CriadoEm > criadasDepoisDe
                && formatura.CriadoEm <= criadasAte
                && !db
                    .Assinaturas.IgnoreQueryFilters()
                    .Any(a => a.FormaturaId == formatura.Id && (a.Status == StatusDaAssinatura.Ativa || a.Status == StatusDaAssinatura.Cancelada))
            join vinculo in db.Vinculos.AsNoTracking() on formatura.Id equals vinculo.FormaturaId
            where vinculo.Ativo && vinculo.DesligadoEm == null && Gestao.Contains(vinculo.Papel)
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where
                usuario.ReceberComunicacaoDoKapa
                && usuario.Ativo
                && usuario.EmailConfirmed
                && usuario.AnonimizadoEm == null
                && !db.EnviosDeMarketing.Any(e => e.UsuarioId == usuario.Id && e.EnviadoEm > ultimoEnvioAntesDe)
            orderby formatura.CriadoEm, usuario.Id
            select new CandidatoDeMarketing(
                usuario.Id,
                usuario.Nome,
                usuario.Email!,
                formatura.Id,
                formatura.Nome,
                formatura.CriadoEm,
                formatura.CriadoPorUsuarioId == usuario.Id,
                db.RefreshTokens.Any(t =>
                    t.CriadoEm >= formatura.CriadoEm.AddDays(1)
                    && db.Vinculos.Any(c => c.FormaturaId == formatura.Id && c.UsuarioId == t.UsuarioId && Gestao.Contains(c.Papel))
                ),
                db.ItensDeCobranca.IgnoreQueryFilters().Any(i => i.FormaturaId == formatura.Id),
                db.Vinculos.Any(f => f.FormaturaId == formatura.Id && f.Ativo && f.Papel == PapelNaFormatura.Formando),
                db.EnviosDeMarketing.Any(e =>
                    e.UsuarioId == usuario.Id && e.FormaturaId == formatura.Id && e.Jornada == JornadaDeMarketing.CriouENaoVoltou
                ),
                db.EnviosDeMarketing.Any(e =>
                    e.UsuarioId == usuario.Id && e.FormaturaId == formatura.Id && e.Jornada == JornadaDeMarketing.MontouEParou
                )
            )
        ).ToListAsync(ct);
    }

    /// <summary>
    /// A preferência de marketing do titular, com o histórico e os envios.
    /// </summary>
    /// <remarks>
    /// Estático e interno porque é parte de duas leituras de outros repositórios — "Minha privacidade" e o painel
    /// de suporte —, e as duas precisam dizer a mesma coisa.
    /// </remarks>
    /// <param name="db">Contexto de dados.</param>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="maximoDeEnvios">Quantos envios trazer, dos mais recentes; nulo traz todos.</param>
    internal static async Task<ComunicacaoDoKapa> DoTitular(AppDbContext db, Guid usuarioId, int? maximoDeEnvios, CancellationToken ct)
    {
        var receber = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.ReceberComunicacaoDoKapa).FirstOrDefaultAsync(ct);

        var historico = await db
            .ConsentimentosDeMarketing.AsNoTracking()
            .Where(c => c.UsuarioId == usuarioId)
            .OrderByDescending(c => c.RegistradoEm)
            .ThenByDescending(c => c.Id)
            .Select(c => new RegistroDaComunicacaoDoKapa(c.Aceito, c.Origem, c.VersaoDoTexto, c.RegistradoEm))
            .ToListAsync(ct);

        var envios =
            from envio in db.EnviosDeMarketing.AsNoTracking()
            join formatura in db.Formaturas on envio.FormaturaId equals formatura.Id
            where envio.UsuarioId == usuarioId
            orderby envio.EnviadoEm descending, envio.Id descending
            select new EnvioDoKapa(envio.Jornada, formatura.Nome, envio.EnviadoEm);

        return new ComunicacaoDoKapa(receber, historico, await (maximoDeEnvios is { } maximo ? envios.Take(maximo) : envios).ToListAsync(ct));
    }
}

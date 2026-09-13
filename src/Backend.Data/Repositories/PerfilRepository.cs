using Backend.Business.Abstractions;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Cadastros dos formandos da formatura selecionada.
/// </summary>
/// <remarks>
/// O perfil é isolado pelo filtro global; o vínculo, não — por isso toda consulta que parte do
/// vínculo leva a formatura explícita, como em <see cref="VinculoRepository"/>.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class PerfilRepository(AppDbContext db) : IPerfilRepository
{
    /// <inheritdoc />
    public Task<MembroDoPerfil?> ObterMembro(Guid formaturaId, Guid usuarioId, CancellationToken ct = default) =>
        (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where vinculo.FormaturaId == formaturaId && vinculo.UsuarioId == usuarioId && vinculo.Ativo
            select new MembroDoPerfil(vinculo.Id, usuario.Id, usuario.Nome, usuario.Email ?? string.Empty, vinculo.Papel)
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<PerfilDoFormando?> ObterDoVinculo(Guid vinculoId, CancellationToken ct = default) =>
        db.PerfisDeFormandos.AsNoTracking().FirstOrDefaultAsync(p => p.VinculoId == vinculoId, ct);

    /// <inheritdoc />
    public Task<PerfilDoFormando?> ObterParaEdicao(Guid vinculoId, CancellationToken ct = default) =>
        db.PerfisDeFormandos.FirstOrDefaultAsync(p => p.VinculoId == vinculoId, ct);

    /// <inheritdoc />
    /// <remarks>
    /// <c>LEFT JOIN</c> do vínculo com o perfil: quem nunca abriu o cadastro precisa aparecer —
    /// é justamente quem a comissão quer cobrar. Sem perfil, conta como 0% e essencial pendente.
    /// <para>
    /// Ordem por nome de exibição com desempate por <c>Id</c> do usuário — sem critério único,
    /// dois homônimos trocam de lugar entre páginas e um some da lista.
    /// </para>
    /// </remarks>
    public async Task<PaginaDe<FormandoResumo>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeFormandos filtro,
        CancellationToken ct = default
    )
    {
        var consulta =
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
            from perfil in perfis.DefaultIfEmpty()
            where vinculo.FormaturaId == formaturaId && vinculo.Ativo
            select new
            {
                vinculo,
                usuario,
                NomeCompleto = perfil == null ? null : perfil.NomeCompleto,
                Completude = perfil == null ? 0 : perfil.Completude,
                EssencialPreenchido = perfil != null && perfil.EssencialPreenchido,
            };

        consulta = filtro.Situacao switch
        {
            SituacaoDoCadastro.Pendente => consulta.Where(linha => !linha.EssencialPreenchido),
            SituacaoDoCadastro.Incompleto => consulta.Where(linha => linha.Completude < 100),
            SituacaoDoCadastro.Completo => consulta.Where(linha => linha.Completude == 100),
            _ => consulta,
        };

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = $"%{filtro.Busca.Trim()}%";
            consulta = consulta.Where(linha =>
                EF.Functions.ILike(linha.usuario.Nome, termo)
                || EF.Functions.ILike(linha.usuario.Email!, termo)
                || (linha.NomeCompleto != null && EF.Functions.ILike(linha.NomeCompleto, termo))
            );
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<FormandoResumo>.Vazia(paginacao);

        var itens = await consulta
            .OrderBy(linha => linha.usuario.Nome)
            .ThenBy(linha => linha.usuario.Id)
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .Select(linha => new FormandoResumo(
                linha.usuario.Id,
                linha.usuario.Nome,
                linha.usuario.Email ?? string.Empty,
                linha.vinculo.Papel,
                linha.NomeCompleto,
                linha.Completude,
                !linha.EssencialPreenchido
            ))
            .ToListAsync(ct);

        return new PaginaDe<FormandoResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task Adicionar(PerfilDoFormando perfil, CancellationToken ct = default) => await db.PerfisDeFormandos.AddAsync(perfil, ct);

    /// <inheritdoc />
    public async Task RegistrarCorrecao(CorrecaoDePerfil correcao, CancellationToken ct = default) =>
        await db.CorrecoesDePerfil.AddAsync(correcao, ct);
}

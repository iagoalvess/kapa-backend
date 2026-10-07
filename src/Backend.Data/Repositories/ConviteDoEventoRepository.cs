using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Os convites da festa e as entradas da portaria da formatura selecionada.
/// </summary>
/// <remarks>
/// As duas escritas que importam — emitir e validar a entrada — são SQL à mão, uma instrução cada, com
/// <c>ON CONFLICT</c> sobre os índices únicos (decisões 12 e 15). É o que faz a garantia morar no
/// banco: nenhuma trava de aplicação, nenhuma leitura antes de gravar. Por serem SQL à mão, levam a
/// <c>formatura_id</c> da sessão explicitamente — o filtro global não alcança um <c>INSERT</c>.
/// <para>
/// Documento e e-mail do convidado saem daqui <b>decifrados</b> (o conversor do contexto faz isso); quem
/// mascara é o service. A lista exportada pela Gestão é o único lugar em que o número sai inteiro.
/// </para>
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class ConviteDoEventoRepository(AppDbContext db) : IConviteDoEventoRepository
{
    /// <summary>Tentativas de sortear os códigos que colidiram com outros da base.</summary>
    /// <remarks>Com 31⁴ códigos por turma, a segunda tentativa já é rara; a quinta não acontece.</remarks>
    private const int TentativasDeSorteio = 5;

    /// <inheritdoc />
    public async Task<int> EmitirDoPedido(
        Guid eventoId,
        Guid vinculoId,
        Guid pedidoId,
        int quantidade,
        string prefixo,
        CancellationToken ct = default
    )
    {
        var formaturaId = FormaturaDaSessao();
        var validos = 0;

        for (var tentativa = 0; tentativa < TentativasDeSorteio && validos < quantidade; tentativa++)
        {
            var codigos = Enumerable.Range(0, quantidade).Select(_ => CodigoDoConvite.Sortear(prefixo)).ToArray();
            var agora = DateTime.UtcNow;

            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO convites_do_evento
                    (id, formatura_id, evento_id, vinculo_id, pedido_id, sequencial, codigo,
                     nome_do_convidado, tipo_do_documento, numero_do_documento, email_do_convidado,
                     emitido_em, criado_em, atualizado_em)
                SELECT gen_random_uuid(), {formaturaId}, {eventoId}, {vinculoId}, {pedidoId}, g, ({codigos}::text[])[g],
                       anterior.nome_do_convidado, anterior.tipo_do_documento, anterior.numero_do_documento, anterior.email_do_convidado,
                       {agora}, {agora}, {agora}
                  FROM generate_series(1, {quantidade}) AS g
                  LEFT JOIN LATERAL (
                        SELECT a.nome_do_convidado, a.tipo_do_documento, a.numero_do_documento, a.email_do_convidado
                          FROM convites_do_evento a
                         WHERE a.evento_id = {eventoId} AND a.vinculo_id = {vinculoId} AND a.pedido_id = {pedidoId} AND a.sequencial = g
                         ORDER BY a.revogado_em DESC
                         LIMIT 1
                  ) AS anterior ON TRUE
                ON CONFLICT DO NOTHING
                """,
                ct
            );

            validos = await db.ConvitesDoEvento.CountAsync(c => c.PedidoId == pedidoId && c.EventoId == eventoId && c.RevogadoEm == null, ct);
        }

        return validos;
    }

    /// <inheritdoc />
    /// <remarks>
    /// O mesmo desenho de <see cref="EmitirDoPedido"/>, sem dono: o índice que segura a posição é
    /// <c>ux_convites_do_evento_posicao_da_compra</c>, em <c>(compra_id, sequencial)</c> nos válidos.
    /// </remarks>
    public async Task<int> EmitirDaCompra(Guid eventoId, Guid compraId, int quantidade, string prefixo, CancellationToken ct = default)
    {
        var formaturaId = FormaturaDaSessao();
        var validos = 0;

        for (var tentativa = 0; tentativa < TentativasDeSorteio && validos < quantidade; tentativa++)
        {
            var codigos = Enumerable.Range(0, quantidade).Select(_ => CodigoDoConvite.Sortear(prefixo)).ToArray();
            var agora = DateTime.UtcNow;

            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO convites_do_evento
                    (id, formatura_id, evento_id, compra_id, sequencial, codigo, emitido_em, criado_em, atualizado_em)
                SELECT gen_random_uuid(), {formaturaId}, {eventoId}, {compraId}, g, ({codigos}::text[])[g], {agora}, {agora}, {agora}
                  FROM generate_series(1, {quantidade}) AS g
                ON CONFLICT DO NOTHING
                """,
                ct
            );

            validos = await db.ConvitesDoEvento.CountAsync(c => c.CompraId == compraId && c.RevogadoEm == null, ct);
        }

        return validos;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConviteDoVinculo>> ListarDaCompra(Guid compraId, CancellationToken ct = default)
    {
        var linhas = await db
            .ConvitesDoEvento.AsNoTracking()
            .Where(c => c.CompraId == compraId && c.RevogadoEm == null)
            .OrderBy(c => c.Sequencial)
            .Select(c => new
            {
                Convite = c,
                ValidadoEm = db
                    .CheckIns.Where(k => k.ConviteId == c.Id && k.DesfeitoEm == null)
                    .Select(k => (DateTime?)k.ValidadoEm)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return [.. linhas.Select(linha => new ConviteDoVinculo(linha.Convite, linha.ValidadoEm))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Os benefícios somam por vínculo e por evento: "Festa 15" e "Colação 3" na mesma cesta dão 15 convites de uma
    /// porta e 3 da outra, e a posição (<c>sequencial</c>) vai de 1 à soma — a chave natural é a mesma da cota.
    /// </remarks>
    public async Task<int> EmitirDosPacotes(Guid formaturaId, Guid? vinculoId, string prefixo, CancellationToken ct = default)
    {
        const string alfabeto = CodigoDoConvite.Alfabeto;
        var emitidos = 0;

        for (var tentativa = 0; tentativa < TentativasDeSorteio; tentativa++)
        {
            var agora = DateTime.UtcNow;

            var inseridos = await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO convites_do_evento
                    (id, formatura_id, evento_id, vinculo_id, pedido_id, sequencial, codigo, emitido_em, criado_em, atualizado_em)
                SELECT gen_random_uuid(), e.formatura_id, e.id, b.vinculo_id, NULL, g,
                       {prefixo} || '-'
                           || substr({alfabeto}, 1 + floor(random() * {alfabeto.Length})::int, 1)
                           || substr({alfabeto}, 1 + floor(random() * {alfabeto.Length})::int, 1)
                           || substr({alfabeto}, 1 + floor(random() * {alfabeto.Length})::int, 1)
                           || substr({alfabeto}, 1 + floor(random() * {alfabeto.Length})::int, 1),
                       {agora}, {agora}, {agora}
                  FROM eventos_da_turma e
                  JOIN (
                        SELECT x.vinculo_id, SUM(i.convites_da_festa)::int AS festa, SUM(i.convites_da_colacao)::int AS colacao
                          FROM escolhas_da_cesta x
                          JOIN itens_de_cobranca i ON i.id = x.item_de_cobranca_id
                          JOIN vinculos_de_formatura v ON v.id = x.vinculo_id AND v.ativo
                         WHERE x.formatura_id = {formaturaId}
                           AND ({vinculoId}::uuid IS NULL OR x.vinculo_id = {vinculoId}::uuid)
                         GROUP BY x.vinculo_id
                       ) b ON TRUE
                 CROSS JOIN LATERAL generate_series(1, CASE e.tipo WHEN 'Festa' THEN b.festa ELSE b.colacao END) AS g
                 WHERE e.formatura_id = {formaturaId}
                   AND e.tipo IN ('Festa', 'Colacao')
                   AND e.situacao <> 'Cancelado'
                   AND e.hora IS NOT NULL
                   AND coalesce(btrim(e.local), '') <> ''
                ON CONFLICT DO NOTHING
                """,
                ct
            );

            emitidos += inseridos;

            if (inseridos == 0)
                break;
        }

        return emitidos;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Grava por conta própria, como o <see cref="EmitirDosPacotes"/> que vem logo antes: os convites acabaram de nascer
    /// por SQL e não estão rastreados, e o <c>WHERE nome_do_convidado IS NULL</c> é o que impede pisar numa
    /// nomeação feita no mesmo instante. Roda na transação de quem chama.
    /// <para>
    /// Uma atualização por formando, não um <c>UPDATE … FROM</c>: nome e documento são cifrados pelo contexto, e só o
    /// parâmetro do <c>ExecuteUpdate</c> passa pelo conversor.
    /// </para>
    /// </remarks>
    public async Task<int> NomearOsDoProprioFormando(Guid formaturaId, Guid? vinculoId, CancellationToken ct = default)
    {
        var semNome = db
            .ConvitesDoEvento.IgnoreQueryFilters()
            .Where(c =>
                c.FormaturaId == formaturaId
                && c.VinculoId != null
                && (vinculoId == null || c.VinculoId == vinculoId)
                && c.PedidoId == null
                && c.CompraId == null
                && c.Sequencial == 1
                && c.NomeDoConvidado == null
                && c.RevogadoEm == null
            );

        var formandos = await (
            from vinculo in db.Vinculos.IgnoreQueryFilters().AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            join perfil in db.PerfisDeFormandos.IgnoreQueryFilters().AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
            from perfil in perfis.DefaultIfEmpty()
            where semNome.Any(c => c.VinculoId == vinculo.Id)
            select new
            {
                vinculo.Id,
                Nome = perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto : usuario.Nome,
                Cpf = perfil != null ? perfil.Cpf : null,
            }
        ).ToListAsync(ct);

        var nomeados = 0;
        foreach (var formando in formandos)
            nomeados += await semNome
                .Where(c => c.VinculoId == formando.Id)
                .ExecuteUpdateAsync(
                    campos =>
                        campos
                            .SetProperty(c => c.NomeDoConvidado, formando.Nome)
                            .SetProperty(c => c.TipoDoDocumento, formando.Cpf == null ? null : TipoDeDocumento.Cpf)
                            .SetProperty(c => c.NumeroDoDocumento, formando.Cpf)
                            .SetProperty(c => c.AtualizadoEm, DateTime.UtcNow),
                    ct
                );

        return nomeados;
    }

    /// <inheritdoc />
    /// <remarks>Em ordem de id, como as outras travas: duas revogações do mesmo vínculo nunca se travam em cruz.</remarks>
    public async Task<IReadOnlyList<ConviteDoEvento>> TravarDosPacotes(Guid vinculoId, CancellationToken ct = default) =>
        await db
            .ConvitesDoEvento.FromSql(
                $"SELECT * FROM convites_do_evento WHERE vinculo_id = {vinculoId} AND pedido_id IS NULL AND revogado_em IS NULL ORDER BY id FOR UPDATE"
            )
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<ContagemDoEvento> ContarDoPainel(Guid eventoId, TipoDeEvento tipo, CancellationToken ct = default)
    {
        var formaturaId = FormaturaDaSessao();
        var validos = db.ConvitesDoEvento.Where(c => c.EventoId == eventoId && c.RevogadoEm == null);
        var dosPacotes = validos.Where(c => c.PedidoId == null && c.CompraId == null && c.VinculoId != null);
        var daFesta = tipo == TipoDeEvento.Festa;
        var beneficios =
            from escolha in db.EscolhasDaCesta
            join item in db.ItensDeCobranca on escolha.ItemDeCobrancaId equals item.Id
            join vinculo in db.Vinculos on escolha.VinculoId equals vinculo.Id
            where vinculo.Ativo
            select daFesta ? item.ConvitesDaFesta : item.ConvitesDaColacao;

        return new ContagemDoEvento(
            await db.Vinculos.CountAsync(v => v.FormaturaId == formaturaId && v.Ativo, ct),
            await beneficios.SumAsync(ct),
            await dosPacotes.CountAsync(ct),
            await dosPacotes.CountAsync(c => c.NomeDoConvidado != null && c.NumeroDoDocumento != null, ct),
            await validos.CountAsync(c => c.PedidoId != null || c.CompraId != null, ct),
            await validos.CountAsync(c => c.VinculoId == null && c.CompraId == null, ct)
        );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FormandoComConvitePreso>> ListarPresos(Guid eventoId, CancellationToken ct = default)
    {
        var limite = await LimiteDoAtraso(ct);

        var presos = await (
            from convite in db.ConvitesDoEvento.AsNoTracking()
            where convite.EventoId == eventoId && convite.RevogadoEm == null
            where convite.VinculoId != null && convite.PedidoId == null && convite.CompraId == null && convite.LiberadoEm == null
            where db.Parcelas.Any(p => p.VinculoId == convite.VinculoId && p.Status == StatusDaParcela.Aberta && p.Vencimento < limite)
            join vinculo in db.Vinculos.AsNoTracking() on convite.VinculoId equals (Guid?)vinculo.Id
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
            from perfil in perfis.DefaultIfEmpty()
            select new { VinculoId = vinculo.Id, Nome = perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto : usuario.Nome }
        ).ToListAsync(ct);

        return
        [
            .. presos
                .GroupBy(preso => (preso.VinculoId, preso.Nome))
                .OrderBy(grupo => grupo.Key.Nome, StringComparer.CurrentCulture)
                .Select(grupo => new FormandoComConvitePreso(grupo.Key.VinculoId, grupo.Key.Nome, grupo.Count())),
        ];
    }

    /// <inheritdoc />
    /// <remarks>Em ordem de id, como a trava da baixa: duas revogações do mesmo pedido nunca se travam em cruz.</remarks>
    public async Task<IReadOnlyList<ConviteDoEvento>> TravarDoPedido(Guid pedidoId, CancellationToken ct = default) =>
        await db
            .ConvitesDoEvento.FromSql($"SELECT * FROM convites_do_evento WHERE pedido_id = {pedidoId} AND revogado_em IS NULL ORDER BY id FOR UPDATE")
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>Em ordem de id, como a do pedido.</remarks>
    public async Task<IReadOnlyList<ConviteDoEvento>> TravarDaCompra(Guid compraId, CancellationToken ct = default) =>
        await db
            .ConvitesDoEvento.FromSql($"SELECT * FROM convites_do_evento WHERE compra_id = {compraId} AND revogado_em IS NULL ORDER BY id FOR UPDATE")
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>Em ordem de id, como a do pedido.</remarks>
    public async Task<IReadOnlyList<ConviteDoEvento>> TravarValidosDoEvento(Guid eventoId, CancellationToken ct = default) =>
        await db
            .ConvitesDoEvento.FromSql($"SELECT * FROM convites_do_evento WHERE evento_id = {eventoId} AND revogado_em IS NULL ORDER BY id FOR UPDATE")
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> ListarComEntrada(IReadOnlyCollection<Guid> conviteIds, CancellationToken ct = default) =>
        conviteIds.Count == 0
            ? new HashSet<Guid>()
            : (
                await db
                    .CheckIns.AsNoTracking()
                    .Where(k => conviteIds.Contains(k.ConviteId) && k.DesfeitoEm == null)
                    .Select(k => k.ConviteId)
                    .ToListAsync(ct)
            ).ToHashSet();

    /// <inheritdoc />
    public Task<ConviteDoEvento?> Travar(Guid conviteId, CancellationToken ct = default) =>
        db.ConvitesDoEvento.FromSql($"SELECT * FROM convites_do_evento WHERE id = {conviteId} FOR UPDATE").FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Sem filtro de formatura, porque o convidado não tem sessão — é a página pública (decisão 11).
    /// Quem protege é a assinatura, conferida antes de chegar aqui, e o limite por IP da rota. Só o
    /// convite válido volta: o revogado responde igual ao inexistente.
    /// </remarks>
    public async Task<ConvitePublicoGravado?> ObterPublicoDeTodasAsFormaturas(string codigo, CancellationToken ct = default)
    {
        var linha = await (
            from convite in db.ConvitesDoEvento.IgnoreQueryFilters().AsNoTracking()
            join evento in db.EventosDaTurma.IgnoreQueryFilters().AsNoTracking() on convite.EventoId equals evento.Id
            join formatura in db.Formaturas.AsNoTracking() on convite.FormaturaId equals formatura.Id
            where convite.Codigo == codigo && convite.RevogadoEm == null
            select new
            {
                formatura.Nome,
                formatura.Instituicao,
                Evento = new EventoDoConvite(evento.Id, evento.Tipo, evento.Titulo, evento.Data, evento.Hora, evento.Local),
                convite.NomeDoConvidado,
                convite.TipoDoDocumento,
                convite.NumeroDoDocumento,
            }
        ).FirstOrDefaultAsync(ct);

        return linha is null
            ? null
            : new ConvitePublicoGravado(
                codigo,
                linha.Nome,
                linha.Instituicao,
                linha.Evento,
                linha.NomeDoConvidado,
                linha.TipoDoDocumento,
                linha.NumeroDoDocumento
            );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConviteDoVinculo>> ListarDoVinculo(Guid vinculoId, Guid eventoId, CancellationToken ct = default)
    {
        var linhas = await db
            .ConvitesDoEvento.AsNoTracking()
            .Where(c => c.VinculoId == vinculoId && c.EventoId == eventoId && c.RevogadoEm == null)
            .OrderBy(c => c.Sequencial)
            .ThenBy(c => c.EmitidoEm)
            .Select(c => new
            {
                Convite = c,
                ValidadoEm = db
                    .CheckIns.Where(k => k.ConviteId == c.Id && k.DesfeitoEm == null)
                    .Select(k => (DateTime?)k.ValidadoEm)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return [.. linhas.Select(linha => new ConviteDoVinculo(linha.Convite, linha.ValidadoEm))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Válidos primeiro, por nome; os revogados no fim — a portaria os mostra em vermelho, mas quem
    /// procura um nome quer achar o que vale.
    /// </remarks>
    public async Task<IReadOnlyList<ConviteGravadoNaPortaria>> ListarNaPortaria(Guid eventoId, string? busca, CancellationToken ct = default)
    {
        var consulta = Linhas(await LimiteDoAtraso(ct)).Where(linha => linha.Convite.EventoId == eventoId);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = Busca.Padrao(busca);
            consulta = consulta.Where(linha =>
                EF.Functions.ILike(EF.Functions.Unaccent(linha.Convite.NomeDoConvidado ?? string.Empty), termo)
                || EF.Functions.ILike(EF.Functions.Unaccent(linha.ConvidadoDe ?? string.Empty), termo)
                || EF.Functions.ILike(linha.Convite.Codigo, termo)
            );
        }

        var linhas = await consulta
            .OrderBy(linha => linha.Convite.RevogadoEm != null)
            .ThenBy(linha => linha.Convite.NomeDoConvidado == null)
            .ThenBy(linha => linha.Convite.NomeDoConvidado)
            .ThenBy(linha => linha.Convite.Codigo)
            .ToListAsync(ct);

        return [.. linhas.Select(ParaPortaria)];
    }

    /// <inheritdoc />
    public async Task<ConviteGravadoNaPortaria?> ObterNaPortaria(string codigo, CancellationToken ct = default) =>
        await Linhas(await LimiteDoAtraso(ct)).Where(linha => linha.Convite.Codigo == codigo).FirstOrDefaultAsync(ct) is { } linha
            ? ParaPortaria(linha)
            : null;

    /// <inheritdoc />
    /// <remarks>
    /// <c>FOR SHARE</c> no convite: a revogação trava a linha com <c>FOR UPDATE</c> antes de marcar, então
    /// um check-in que chega no meio espera e reavalia o <c>revogado_em IS NULL</c> sobre o estado final —
    /// nunca o do meio (decisão 15). O <c>ON CONFLICT</c> no índice parcial é a "uma entrada por convite"
    /// da decisão 12: zero linhas afetadas, e não exceção.
    /// </remarks>
    public async Task<bool> RegistrarEntrada(
        Guid conviteId,
        Guid eventoId,
        Guid usuarioId,
        DateTime validadoEm,
        string? aparelho,
        CancellationToken ct = default
    )
    {
        var formaturaId = FormaturaDaSessao();
        var id = Guid.CreateVersion7();
        var agora = DateTime.UtcNow;

        var gravadas = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO check_ins (id, formatura_id, convite_id, validado_por_usuario_id, validado_em, aparelho, criado_em, atualizado_em)
            SELECT {id}, c.formatura_id, c.id, {usuarioId}, {validadoEm}, {aparelho}::text, {agora}, {agora}
              FROM convites_do_evento c
             WHERE c.id = {conviteId} AND c.formatura_id = {formaturaId} AND c.evento_id = {eventoId} AND c.revogado_em IS NULL
               FOR SHARE OF c
            ON CONFLICT (convite_id) WHERE desfeito_em IS NULL DO NOTHING
            """,
            ct
        );

        return gravadas == 1;
    }

    /// <inheritdoc />
    public Task<EntradaNaPortaria?> ObterEntradaAtiva(Guid conviteId, CancellationToken ct = default) =>
        (
            from checkIn in db.CheckIns.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on checkIn.ValidadoPorUsuarioId equals usuario.Id
            where checkIn.ConviteId == conviteId && checkIn.DesfeitoEm == null
            select new EntradaNaPortaria(checkIn.Id, checkIn.ValidadoEm, usuario.Nome, usuario.Id)
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<CheckIn?> ObterCheckInParaEdicao(Guid checkInId, CancellationToken ct = default) =>
        db.CheckIns.FirstOrDefaultAsync(c => c.Id == checkInId, ct);

    /// <inheritdoc />
    public async Task<(int Emitidos, int SemTitular)> Contar(Guid eventoId, CancellationToken ct = default)
    {
        var validos = db.ConvitesDoEvento.Where(c => c.EventoId == eventoId && c.RevogadoEm == null);

        return (await validos.CountAsync(ct), await validos.CountAsync(c => c.NomeDoConvidado == null || c.NumeroDoDocumento == null, ct));
    }

    /// <inheritdoc />
    public async Task Adicionar(ConviteDoEvento convite, CancellationToken ct = default) => await db.ConvitesDoEvento.AddAsync(convite, ct);

    /// <inheritdoc />
    public async Task Adicionar(CheckIn checkIn, CancellationToken ct = default) => await db.CheckIns.AddAsync(checkIn, ct);

    /// <inheritdoc />
    public Task<int> DescartarDocumentosDeTodasAsFormaturas(DateOnly eventosAte, CancellationToken ct = default) =>
        db
            .ConvitesDoEvento.IgnoreQueryFilters()
            .Where(c =>
                (c.NumeroDoDocumento != null || c.EmailDoConvidado != null || c.Observacoes != null)
                && db.EventosDaTurma.IgnoreQueryFilters().Any(e => e.Id == c.EventoId && e.Data < eventosAte)
            )
            .ExecuteUpdateAsync(
                campos =>
                    campos
                        .SetProperty(c => c.NumeroDoDocumento, (string?)null)
                        .SetProperty(c => c.EmailDoConvidado, (string?)null)
                        .SetProperty(c => c.Observacoes, (string?)null)
                        .SetProperty(c => c.TipoDoDocumento, (TipoDeDocumento?)null),
                ct
            );

    /// <summary>A formatura da sessão, que o SQL à mão precisa escrever — o carimbo do contexto não o alcança.</summary>
    private Guid FormaturaDaSessao() =>
        db.FormaturaAtualId ?? throw new InvalidOperationException("Escrita de convite sem formatura selecionada na sessão.");

    /// <summary>
    /// O convite com quem o convidou e a entrada ativa, numa consulta só.
    /// </summary>
    /// <remarks>
    /// Dono pelo vínculo, com o nome civil do cadastro quando houver — a mesma regra do pedido; na compra da
    /// loja, quem comprou (Sprint 26). A entrada
    /// e a tentativa repetida sem rede vêm por subconsulta: a lista de um evento são centenas de linhas, e
    /// as subconsultas batem no índice <c>(convite_id, validado_em)</c>.
    /// </remarks>
    /// <param name="limite">Parcela aberta com vencimento antes deste dia prende o convite do pacote (D24).</param>
    private IQueryable<LinhaDeConvite> Linhas(DateOnly limite) =>
        from convite in db.ConvitesDoEvento.AsNoTracking()
        join vinculo in db.Vinculos.AsNoTracking() on convite.VinculoId equals (Guid?)vinculo.Id into vinculos
        from vinculo in vinculos.DefaultIfEmpty()
        join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id into usuarios
        from usuario in usuarios.DefaultIfEmpty()
        join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
        from perfil in perfis.DefaultIfEmpty()
        join compra in db.ComprasDeConvite.AsNoTracking() on convite.CompraId equals (Guid?)compra.Id into compras
        from compra in compras.DefaultIfEmpty()
        select new LinhaDeConvite
        {
            Convite = convite,
            ConvidadoDe =
                perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto
                : usuario != null ? usuario.Nome
                : compra != null ? compra.NomeDoComprador
                : null,
            Entrada = (
                from checkIn in db.CheckIns
                join validador in db.Users on checkIn.ValidadoPorUsuarioId equals validador.Id
                where checkIn.ConviteId == convite.Id && checkIn.DesfeitoEm == null
                select new EntradaNaPortaria(checkIn.Id, checkIn.ValidadoEm, validador.Nome, validador.Id)
            ).FirstOrDefault(),
            Repetida = db.CheckIns.Any(checkIn => checkIn.ConviteId == convite.Id && checkIn.Motivo == CheckIn.DuplicadaOffline),
            Preso =
                convite.VinculoId != null
                && convite.PedidoId == null
                && convite.CompraId == null
                && convite.LiberadoEm == null
                && db.Parcelas.Any(p => p.VinculoId == convite.VinculoId && p.Status == StatusDaParcela.Aberta && p.Vencimento < limite),
        };

    /// <summary>
    /// O dia antes do qual uma parcela aberta prende o convite: hoje menos a carência do plano vigente (Sprint 47, D24).
    /// </summary>
    /// <remarks>
    /// <c>ponytail:</c> a carência do plano vigente, não a do snapshot de cada adesão — são iguais enquanto a comissão
    /// não muda a carência depois de gente aderir. Ler por adesão se isso passar a acontecer.
    /// </remarks>
    private async Task<DateOnly> LimiteDoAtraso(CancellationToken ct)
    {
        var carencia =
            await db.PlanosDeCobranca.Where(p => p.Status == StatusDoPlano.Vigente).Select(p => (int?)p.CarenciaEmDias).FirstOrDefaultAsync(ct) ?? 0;

        return DataUtils.Hoje().AddDays(-carencia);
    }

    private static ConviteGravadoNaPortaria ParaPortaria(LinhaDeConvite linha) =>
        new(linha.Convite, linha.ConvidadoDe, linha.Entrada, linha.Repetida, linha.Preso);
}

/// <summary>Um convite com quem o convidou e a entrada — a forma intermediária das consultas da portaria.</summary>
internal sealed class LinhaDeConvite
{
    /// <summary>O convite.</summary>
    public required ConviteDoEvento Convite { get; init; }

    /// <summary>Nome do dono; nulo na cortesia.</summary>
    public string? ConvidadoDe { get; init; }

    /// <summary>A entrada ativa.</summary>
    public EntradaNaPortaria? Entrada { get; init; }

    /// <summary>Se há tentativa repetida sem rede.</summary>
    public bool Repetida { get; init; }

    /// <summary>Se é convite de pacote preso por atraso (D24).</summary>
    public bool Preso { get; init; }
}

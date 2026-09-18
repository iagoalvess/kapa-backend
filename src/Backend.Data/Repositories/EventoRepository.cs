using System.Text.Json;
using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Acesso à tabela de eventos.
/// </summary>
/// <param name="db">Contexto de dados do escopo.</param>
public sealed class EventoRepository(AppDbContext db) : IEventoRepository
{
    /// <summary>As chaves do corpo que guardam uma pessoa, e não uma entidade.</summary>
    private static readonly string[] ChavesDePessoa = ["usuarioId", "membroUsuarioId", "titularUsuarioId"];

    /// <summary>As chaves do corpo que guardam uma parcela — que na tela vira o nome do dono dela.</summary>
    private static readonly string[] ChavesDeParcela = ["parcelaId"];

    /// <summary>A janela do "recente" da faixa: um mês é o intervalo entre duas reuniões de comissão.</summary>
    private const int DiasDoResumo = 30;

    /// <inheritdoc />
    /// <remarks>
    /// Diferente do resto do projeto, grava e persiste na mesma chamada. Não há o que compor:
    /// quem chama é o serviço de descarga da fila, que já roda fora de qualquer requisição e não
    /// tem outra escrita para agrupar.
    /// </remarks>
    public async Task GravarLote(IReadOnlyList<Evento> eventos, CancellationToken ct = default)
    {
        if (eventos.Count == 0)
            return;

        await db.Eventos.AddRangeAsync(eventos, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task Adicionar(Evento evento, CancellationToken ct = default) => await db.Eventos.AddAsync(evento, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Dois limites numa consulta só, e não duas passadas: os nomes de <see cref="NomesDeAuditoria"/>
    /// obedecem ao limite longo e o resto ao curto (decisão 3 da Sprint 14). Sem o segundo limite o
    /// comportamento é o de antes — um prazo para todo mundo.
    /// </remarks>
    public Task<int> RemoverAnterioresA(DateTime limiteUtc, DateTime? limiteDaAuditoriaUtc = null, CancellationToken ct = default)
    {
        if (limiteDaAuditoriaUtc is not { } auditoria)
            return db.Eventos.Where(e => e.OcorridoEm < limiteUtc).ExecuteDeleteAsync(ct);

        var auditaveis = NomesDeAuditoria.Todos;

        return db.Eventos.Where(e => auditaveis.Contains(e.Nome) ? e.OcorridoEm < auditoria : e.OcorridoEm < limiteUtc).ExecuteDeleteAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O autor sai de uma subconsulta em <c>users</c>, e não de um <c>JOIN</c>: o evento de sistema
    /// tem autor nulo, e um <c>JOIN</c> interno o esconderia — justamente as linhas da régua e do
    /// worker, que são as que alguém procura quando não reconhece uma cobrança.
    /// </remarks>
    public async Task<PaginaDe<LinhaDeAuditoria>> ListarAuditoria(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeAuditoria filtro,
        CancellationToken ct = default
    )
    {
        var consulta = DaFormatura(formaturaId, await FonteDaBusca(filtro.Busca, ct));

        if (filtro.De is { } de)
        {
            var inicio = DataUtils.InicioDoDiaEmUtc(de);
            consulta = consulta.Where(e => e.OcorridoEm >= inicio);
        }

        if (filtro.Ate is { } ate)
        {
            var fim = DataUtils.FimDoDiaEmUtc(ate);
            consulta = consulta.Where(e => e.OcorridoEm < fim);
        }

        if (filtro.AutorUsuarioId is { } autor)
            consulta = consulta.Where(e => e.UsuarioId == autor);

        if (!string.IsNullOrWhiteSpace(filtro.Nome))
        {
            var nome = filtro.Nome.Trim();
            consulta = consulta.Where(e => e.Nome == nome);
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<LinhaDeAuditoria>.Vazia(paginacao);

        var itens = await consulta
            .OrderByDescending(e => e.OcorridoEm)
            .ThenByDescending(e => e.Id)
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .Select(e => new LinhaDeAuditoria(
                e.Id,
                e.Nome,
                e.OcorridoEm,
                e.UsuarioId,
                db.Users.Where(u => u.Id == e.UsuarioId).Select(u => u.Nome).FirstOrDefault(),
                e.Dados
            ))
            .ToListAsync(ct);

        return new PaginaDe<LinhaDeAuditoria>(await ComNomes(itens, ct), paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <summary>
    /// Os eventos que casam com a busca, ou <c>null</c> quando não há busca.
    /// </summary>
    /// <remarks>
    /// Duas coisas de uma vez, porque para quem digita são a mesma: o texto escrito no corpo do
    /// evento (título, justificativa, motivo) e o <b>nome de uma pessoa</b> — que no corpo não está,
    /// só o id dela. Daí a primeira consulta: os ids de quem tem nome parecido viram padrões de
    /// busca, e o evento que os cita entra junto com o que foi feito por eles.
    /// <para>
    /// As parcelas dessas pessoas entram pelo mesmo caminho: a baixa e o estorno guardam a parcela e
    /// não o dono, e sem elas procurar por um nome acharia o desligamento da pessoa e não a baixa
    /// que ela mesma pediu. São algumas dezenas de ids por formando — o array cresce com a turma,
    /// não com a tabela de eventos.
    /// </para>
    /// <para>
    /// SQL na mão porque <c>dados</c> é <c>jsonb</c> e o Postgres não aplica <c>ILIKE</c> nele sem o
    /// <c>::text</c>, que o EF Core não escreve. O <c>ANY</c> recebe o array inteiro num parâmetro
    /// só — um <c>OR</c> por id montado em C# viraria SQL de tamanho variável, que o banco replaneja
    /// a cada busca.
    /// </para>
    /// <para>
    /// O nome é comparado sem acento (como no resto do produto); o corpo, não — ele é texto de
    /// máquina, e quem procura "João" no título de um aviso digita o acento que está lá.
    /// </para>
    /// </remarks>
    /// <param name="busca">O que a pessoa digitou.</param>
    private async Task<IQueryable<Evento>?> FonteDaBusca(string? busca, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(busca))
            return null;

        var termo = Busca.Padrao(busca);

        var pessoas = await db
            .Users.AsNoTracking()
            .Where(u => EF.Functions.ILike(EF.Functions.Unaccent(u.Nome), termo))
            .Select(u => u.Id)
            .ToArrayAsync(ct);

        var parcelas =
            pessoas.Length == 0
                ? []
                : await (
                    from parcela in db.Parcelas.AsNoTracking()
                    join vinculo in db.Vinculos on parcela.VinculoId equals vinculo.Id
                    where pessoas.Contains(vinculo.UsuarioId)
                    select parcela.Id
                ).ToArrayAsync(ct);

        var padroes = pessoas.Concat(parcelas).Select(id => $"%{id}%").Prepend(termo).ToArray();

        return db.Eventos.FromSql($"select * from eventos where dados::text ilike any({padroes}) or usuario_id = any({pessoas})");
    }

    /// <summary>
    /// A página com o nome de cada pessoa citada no corpo dos eventos.
    /// </summary>
    /// <remarks>
    /// Duas consultas para a página inteira, e não uma por linha. O nome sai de <c>users</c> na hora
    /// da leitura: titular anonimizado aparece pelo marcador, sem precisar reescrever a trilha — que
    /// é <i>append-only</i> justamente para não poder ser reescrita.
    /// <para>
    /// A segunda consulta é a da parcela. A baixa e o estorno não guardam pessoa nenhuma — guardam a
    /// parcela —, e sem ela a linha dizia quem baixou e não de quem era a parcela, que é a metade da
    /// pergunta que a assembleia faz. O dono sai do vínculo da parcela, pelo mesmo caminho.
    /// </para>
    /// <para>
    /// Id que não resolve (conta apagada pelo suporte) fica de fora do dicionário, e a tela omite o
    /// campo: um GUID solto não responde nada a quem lê.
    /// </para>
    /// </remarks>
    /// <param name="itens">A página, como veio do banco.</param>
    private async Task<List<LinhaDeAuditoria>> ComNomes(List<LinhaDeAuditoria> itens, CancellationToken ct)
    {
        var porLinha = itens.Select(linha => IdsDoCorpo(linha.Dados)).ToList();
        var pessoas = porLinha.SelectMany(linha => linha.Pessoas).Distinct().ToArray();
        var parcelas = porLinha.SelectMany(linha => linha.Parcelas).Distinct().ToArray();

        var nomes =
            pessoas.Length == 0 ? [] : await db.Users.AsNoTracking().Where(u => pessoas.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct);

        if (parcelas.Length > 0)
        {
            var donos = await (
                from parcela in db.Parcelas.AsNoTracking()
                where parcelas.Contains(parcela.Id)
                join vinculo in db.Vinculos on parcela.VinculoId equals vinculo.Id
                join usuario in db.Users on vinculo.UsuarioId equals usuario.Id
                select new { parcela.Id, usuario.Nome }
            ).ToListAsync(ct);

            foreach (var dono in donos)
                nomes[dono.Id] = dono.Nome;
        }

        return itens
            .Select(
                (linha, posicao) =>
                    linha with
                    {
                        Pessoas = porLinha[posicao]
                            .Pessoas.Concat(porLinha[posicao].Parcelas)
                            .Where(nomes.ContainsKey)
                            .Distinct()
                            .ToDictionary(id => id.ToString(), id => nomes[id]),
                    }
            )
            .ToList();
    }

    /// <summary>
    /// Os ids do corpo de um evento que viram nome na tela: as pessoas e as parcelas.
    /// </summary>
    /// <remarks>
    /// Só o primeiro nível, e só as chaves conhecidas: <c>antes</c> e <c>depois</c> guardam o que
    /// mudou — papel, chave PIX —, e nenhum evento põe gente lá dentro. A coluna é <c>jsonb</c>, e
    /// o Postgres não aceita gravar JSON inválido nela: não há o que falhar ao ler.
    /// </remarks>
    /// <param name="dados">Corpo do evento.</param>
    private static (List<Guid> Pessoas, List<Guid> Parcelas) IdsDoCorpo(string? dados)
    {
        if (string.IsNullOrWhiteSpace(dados))
            return ([], []);

        using var documento = JsonDocument.Parse(dados);

        if (documento.RootElement.ValueKind is not JsonValueKind.Object)
            return ([], []);

        var raiz = documento.RootElement;

        List<Guid> Ler(string[] chaves) =>
            [
                .. chaves
                    .Where(chave => raiz.TryGetProperty(chave, out var valor) && valor.TryGetGuid(out _))
                    .Select(chave => raiz.GetProperty(chave).GetGuid()),
            ];

        return (Ler(ChavesDePessoa), Ler(ChavesDeParcela));
    }

    /// <inheritdoc />
    public async Task<OpcoesDeAuditoria> OpcoesDeAuditoria(Guid formaturaId, CancellationToken ct = default)
    {
        var daTurma = DaFormatura(formaturaId);

        // A ordenação vem **antes** da projeção: ordenar pelo campo do record já construído não
        // traduz para SQL, e o EF Core recusa a consulta inteira em tempo de execução.
        var autores = await (
            from usuario in db.Users.AsNoTracking()
            where daTurma.Any(e => e.UsuarioId == usuario.Id)
            orderby usuario.Nome
            select new AutorDeAuditoria(usuario.Id, usuario.Nome)
        ).ToListAsync(ct);

        var nomes = await daTurma.Select(e => e.Nome).Distinct().OrderBy(nome => nome).ToListAsync(ct);

        return new OpcoesDeAuditoria(autores, nomes);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cinco consultas pequenas sobre o mesmo recorte, e não uma com cinco subconsultas: cada uma
    /// usa o índice parcial da turma, e o Postgres resolve as duas agregações por <c>GROUP BY</c> em
    /// cima de algumas centenas de linhas. O <c>OrderBy</c> vem do <c>grupo.Count()</c> e não do
    /// campo do record — ordenar pelo record já projetado não traduz para SQL, e o EF Core recusa a
    /// consulta inteira em tempo de execução.
    /// <para>
    /// O nome de quem mais fez sai de <c>users</c> na leitura, como na trilha: titular anonimizado
    /// aparece pelo marcador, sem a tabela de eventos precisar ser reescrita.
    /// </para>
    /// </remarks>
    public async Task<ResumoDaAuditoria> ResumirAuditoria(Guid formaturaId, CancellationToken ct = default)
    {
        var daTurma = DaFormatura(formaturaId);

        var total = await daTurma.LongCountAsync(ct);

        if (total == 0)
            return new ResumoDaAuditoria(0, 0, null, null, null, null);

        var desde = DateTime.UtcNow.AddDays(-DiasDoResumo);

        var recentes = await daTurma.CountAsync(e => e.OcorridoEm >= desde, ct);

        var ultima = await daTurma
            .OrderByDescending(e => e.OcorridoEm)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.OcorridoEm, e.Nome })
            .FirstAsync(ct);

        var autor = await (
            from evento in daTurma
            where evento.UsuarioId != null
            group evento by evento.UsuarioId into grupo
            orderby grupo.Count() descending
            select new { UsuarioId = grupo.Key, Quantidade = grupo.Count() }
        ).FirstOrDefaultAsync(ct);

        var quemMaisFez = autor is null
            ? null
            : await db
                .Users.AsNoTracking()
                .Where(usuario => usuario.Id == autor.UsuarioId)
                .Select(usuario => new ContagemDaAuditoria(usuario.Nome, autor.Quantidade))
                .FirstOrDefaultAsync(ct);

        var acaoMaisComum = await (
            from evento in daTurma
            group evento by evento.Nome into grupo
            orderby grupo.Count() descending
            select new ContagemDaAuditoria(grupo.Key, grupo.Count())
        ).FirstOrDefaultAsync(ct);

        return new ResumoDaAuditoria(total, recentes, ultima.OcorridoEm, ultima.Nome, quemMaisFez, acaoMaisComum);
    }

    /// <summary>
    /// Os eventos auditáveis de uma formatura.
    /// </summary>
    /// <remarks>
    /// A tabela de eventos é da plataforma inteira: ela guarda o clique de analytics e a baixa de
    /// parcela na mesma linha de tempo, e não tem filtro global. O recorte por turma é a coluna
    /// <c>formatura_id</c>, preenchida por <c>Auditoria.Auditar</c> e coberta por um índice parcial.
    /// <para>
    /// O recorte por <see cref="NomesDeAuditoria"/> é o que mantém a tela legível: sem ele, cada
    /// clique de analytics da turma entraria na linha do tempo da assembleia.
    /// </para>
    /// </remarks>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="fonte">
    /// De onde partir; ausente, a tabela inteira. É por aqui que a busca entra, e o recorte por turma
    /// continua na mesma consulta — o Postgres achata a subconsulta e o índice parcial vale para as
    /// duas.
    /// </param>
    private IQueryable<Evento> DaFormatura(Guid formaturaId, IQueryable<Evento>? fonte = null)
    {
        var auditaveis = NomesDeAuditoria.Todos;

        return (fonte ?? db.Eventos).AsNoTracking().Where(e => e.FormaturaId == formaturaId && auditaveis.Contains(e.Nome));
    }
}

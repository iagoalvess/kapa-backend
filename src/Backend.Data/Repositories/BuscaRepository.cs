using Backend.Business.Assinaturas.Models;
using Backend.Business.Busca.Interfaces;
using Backend.Business.Busca.Models;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Formaturas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// As consultas da busca do topo.
/// </summary>
/// <remarks>
/// Cinco consultas pequenas, cada uma com <c>LIMIT</c>, e não uma <c>UNION</c>: os cinco corpos não
/// têm as mesmas colunas, e o <c>UNION</c> obrigaria a inventar um formato comum no SQL para depois
/// desfazê-lo em C#. Elas vão juntas na mesma chamada e, com o teto por grupo, a conta é sempre a
/// mesma — vinte e cinco linhas, qualquer que seja o tamanho da turma.
/// <para>
/// O recorte por formatura é o filtro global do <c>AppDbContext</c>: toda entidade daqui herda de
/// <c>EntidadeDaFormatura</c>. O vínculo é a exceção que o próprio filtro não cobre do mesmo jeito,
/// e por isso ele compara <c>FormaturaId</c> na mão, como o <c>VinculoRepository</c>.
/// </para>
/// <para>
/// A comparação é sem acento (<c>unaccent</c>), como no resto do produto: quem procura "Joao" tem
/// de achar "João". O e-mail escapa disso — ele não tem acento, e passar a coluna por
/// <c>unaccent</c> só custaria o índice.
/// </para>
/// </remarks>
/// <param name="db">Contexto de dados do escopo.</param>
public sealed class BuscaRepository(AppDbContext db) : IBuscaRepository
{
    /// <inheritdoc />
    public async Task<BuscaNaTurma> Buscar(QuemBusca quem, string termo, int porGrupo, CancellationToken ct = default)
    {
        var padrao = Busca.Padrao(termo);
        var gestao = quem.Papel is not null && PapelNaFormatura.Gestao.Contains(quem.Papel);
        var tesouraria = quem.Papel is not null && PapelNaFormatura.Tesouraria.Contains(quem.Papel);
        var veInternos = Visibilidades.VeInternos(quem.Papel);

        var mural = quem.Inclui(Modulo.Mural);
        var despesas = quem.Inclui(Modulo.Despesas);

        return new BuscaNaTurma(
            gestao && quem.Inclui(Modulo.Membros) ? await Membros(quem.FormaturaId, padrao, porGrupo, ct) : [],
            despesas ? await Despesas(padrao, porGrupo, ct) : [],
            tesouraria && despesas ? await Fornecedores(padrao, porGrupo, ct) : [],
            mural ? await Avisos(padrao, porGrupo, veInternos, ct) : [],
            mural ? await Documentos(padrao, porGrupo, veInternos, ct) : []
        );
    }

    /// <summary>
    /// Membros ativos, pelo nome da conta, pelo nome completo do cadastro ou pelo e-mail.
    /// </summary>
    /// <remarks>
    /// Quem saiu não entra: a busca serve para abrir a ficha de alguém, e a de quem foi desligado só
    /// é alcançada de propósito, pela lista com o filtro ligado.
    /// </remarks>
    private Task<List<ResultadoDaBusca>> Membros(Guid formaturaId, string padrao, int porGrupo, CancellationToken ct) =>
        (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
            from perfil in perfis.DefaultIfEmpty()
            where
                vinculo.FormaturaId == formaturaId
                && vinculo.Ativo
                && vinculo.DesligadoEm == null
                && (
                    EF.Functions.ILike(EF.Functions.Unaccent(usuario.Nome), padrao)
                    || EF.Functions.ILike(usuario.Email!, padrao)
                    || (perfil != null && perfil.NomeCompleto != null && EF.Functions.ILike(EF.Functions.Unaccent(perfil.NomeCompleto), padrao))
                )
            orderby usuario.Nome
            select new ResultadoDaBusca(TipoDeResultado.Membro, vinculo.UsuarioId, usuario.Nome, vinculo.Papel)
        )
            .Take(porGrupo)
            .ToListAsync(ct);

    /// <summary>
    /// Despesas pela descrição ou pelo nome do fornecedor, da mais recente.
    /// </summary>
    /// <remarks>
    /// A parcela entra no detalhe porque a descrição não a distingue: "Buffet — saldo em 10 parcelas"
    /// é o texto das dez linhas, e cinco resultados idênticos não dizem em qual clicar. O detalhe é
    /// montado em C#, e não no <c>SELECT</c>: são cinco linhas, e concatenar número com texto no SQL
    /// só acrescentaria um <c>CAST</c> para o banco fazer o que a memória faz de graça.
    /// </remarks>
    private async Task<IReadOnlyList<ResultadoDaBusca>> Despesas(string padrao, int porGrupo, CancellationToken ct)
    {
        var linhas = await (
            from despesa in db.Despesas.AsNoTracking()
            join fornecedor in db.Fornecedores.AsNoTracking() on despesa.FornecedorId equals fornecedor.Id into fornecedores
            from fornecedor in fornecedores.DefaultIfEmpty()
            where
                EF.Functions.ILike(EF.Functions.Unaccent(despesa.Descricao), padrao)
                || (fornecedor != null && EF.Functions.ILike(EF.Functions.Unaccent(fornecedor.Nome), padrao))
            orderby despesa.Vencimento descending
            select new
            {
                despesa.Id,
                despesa.Descricao,
                despesa.Numero,
                despesa.TotalDeParcelas,
                Fornecedor = fornecedor == null ? null : fornecedor.Nome,
            }
        )
            .Take(porGrupo)
            .ToListAsync(ct);

        return
        [
            .. linhas.Select(linha => new ResultadoDaBusca(
                TipoDeResultado.Despesa,
                linha.Id,
                linha.Descricao,
                linha.TotalDeParcelas > 1
                    ? $"{linha.Fornecedor} · parcela {linha.Numero} de {linha.TotalDeParcelas}".TrimStart(' ', '·')
                    : linha.Fornecedor
            )),
        ];
    }

    /// <summary>Fornecedores pelo nome.</summary>
    private Task<List<ResultadoDaBusca>> Fornecedores(string padrao, int porGrupo, CancellationToken ct) =>
        db
            .Fornecedores.AsNoTracking()
            .Where(fornecedor => EF.Functions.ILike(EF.Functions.Unaccent(fornecedor.Nome), padrao))
            .OrderBy(fornecedor => fornecedor.Nome)
            .Take(porGrupo)
            .Select(fornecedor => new ResultadoDaBusca(TipoDeResultado.Fornecedor, fornecedor.Id, fornecedor.Nome, fornecedor.Email))
            .ToListAsync(ct);

    /// <summary>Avisos pelo título ou pelo texto, do mais recente. O interno só para a Gestão.</summary>
    private Task<List<ResultadoDaBusca>> Avisos(string padrao, int porGrupo, bool veInternos, CancellationToken ct) =>
        db
            .Avisos.AsNoTracking()
            .Where(aviso =>
                (veInternos || aviso.Visibilidade == Visibilidade.Turma)
                && (
                    EF.Functions.ILike(EF.Functions.Unaccent(aviso.Titulo), padrao)
                    || EF.Functions.ILike(EF.Functions.Unaccent(aviso.Conteudo), padrao)
                )
            )
            .OrderByDescending(aviso => aviso.CriadoEm)
            .Take(porGrupo)
            .Select(aviso => new ResultadoDaBusca(TipoDeResultado.Aviso, aviso.Id, aviso.Titulo, null))
            .ToListAsync(ct);

    /// <summary>Documentos do acervo pelo título. O interno só para a Gestão.</summary>
    private Task<List<ResultadoDaBusca>> Documentos(string padrao, int porGrupo, bool veInternos, CancellationToken ct) =>
        db
            .Documentos.AsNoTracking()
            .Where(documento =>
                (veInternos || documento.Visibilidade == Visibilidade.Turma) && EF.Functions.ILike(EF.Functions.Unaccent(documento.Titulo), padrao)
            )
            .OrderBy(documento => documento.Titulo)
            .Take(porGrupo)
            .Select(documento => new ResultadoDaBusca(TipoDeResultado.Documento, documento.Id, documento.Titulo, documento.Categoria.ToString()))
            .ToListAsync(ct);
}

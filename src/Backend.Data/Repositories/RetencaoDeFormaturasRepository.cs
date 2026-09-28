using System.Reflection;
using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;
using Backend.Business.Agenda.Models;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Convites.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Relatorios.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Consultas e remoção em massa da retenção de turmas.
/// </summary>
/// <param name="db">Contexto de dados do escopo, apontado para a turma na remoção.</param>
public sealed class RetencaoDeFormaturasRepository(AppDbContext db) : IRetencaoDeFormaturasRepository
{
    /// <summary>
    /// O que sai com a turma, na ordem em que sai: quem aponta vem antes de quem é apontado.
    /// </summary>
    /// <remarks>
    /// As chaves estrangeiras são <c>Restrict</c>, então a ordem é o que faz a remoção passar. Entidade
    /// nova da formatura entra aqui ou em <see cref="Mantidas"/> — o teste
    /// <c>Toda_entidade_da_formatura_tem_destino_na_retencao</c> quebra enquanto ela não tiver lugar.
    /// </remarks>
    public static readonly Type[] Apagadas =
    [
        typeof(VotoNaProposta),
        typeof(PropostaDoItem),
        typeof(CheckIn),
        typeof(ConviteDoEvento),
        typeof(CobrancaBancaria),
        typeof(CompraDeConvite),
        typeof(NotificacaoEnviada),
        typeof(Recebimento),
        typeof(InformeDePagamento),
        typeof(Parcela),
        typeof(Pedido),
        typeof(Despesa),
        typeof(ItemDeCobranca),
        typeof(PlanoDeCobranca),
        typeof(OutraReceita),
        typeof(ItemDaFesta),
        typeof(Fornecedor),
        typeof(Documento),
        typeof(PerfilDoFormando),
        typeof(EventoDaTurma),
        typeof(Mesa),
        typeof(Aviso),
        typeof(PreferenciaDeNotificacao),
        typeof(RegraDeNotificacao),
        typeof(ContaDeRecebimento),
        typeof(CredencialDeProvedor),
        typeof(SolicitacaoDeRelatorio),
    ];

    /// <summary>
    /// O que fica depois da eliminação, porque é prova: o aceite do termo de adesão e o termo aceito, o
    /// convite de quem entrou na comissão (e o aceite dele) e a assinatura com o Kapa, que é registro fiscal
    /// do próprio Kapa.
    /// </summary>
    public static readonly Type[] Mantidas = [typeof(AdesaoDoFormando), typeof(TermoDaFormatura), typeof(Convite), typeof(Assinatura)];

    private static readonly MethodInfo ApagarDaTurma = typeof(RetencaoDeFormaturasRepository).GetMethod(
        nameof(Apagar),
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Formatura>> ListarSuspensasAnterioresA(DateTime suspensasAte, int limite, CancellationToken ct = default) =>
        await db
            .Formaturas.Where(f => f.Status == StatusDaFormatura.Suspensa && f.StatusDesde < suspensasAte)
            .OrderBy(f => f.StatusDesde)
            .Take(limite)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListarParaEliminar(
        DateTime encerradasAte,
        DateTime descartadasAte,
        int limite,
        CancellationToken ct = default
    ) =>
        await db
            .Formaturas.AsNoTracking()
            .Where(f =>
                f.EliminadaEm == null
                && (
                    (f.Status == StatusDaFormatura.Encerrada && f.EncerradaEm < encerradasAte)
                    || (f.Status == StatusDaFormatura.Descartada && f.StatusDesde < descartadasAte)
                )
            )
            .OrderBy(f => f.StatusDesde)
            .Select(f => f.Id)
            .Take(limite)
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// A correção de perfil não é da formatura (não tem a coluna) e aponta para o perfil, então sai
    /// primeiro, pela subconsulta. Os metadados dos arquivos saem por último, pelo prefixo da chave — é
    /// quando nada mais aponta para eles. Os vínculos ficam, desativados: o aceite do termo aponta para
    /// eles, e desativar é o que tira a turma da lista de quem era membro.
    /// </remarks>
    public async Task ApagarDados(Guid formaturaId, string prefixoDosArquivos, CancellationToken ct = default)
    {
        await db
            .CorrecoesDePerfil.Where(c => db.PerfisDeFormandos.Any(p => p.Id == c.PerfilId && p.FormaturaId == formaturaId))
            .ExecuteDeleteAsync(ct);

        foreach (var tipo in Apagadas)
            await (Task<int>)ApagarDaTurma.MakeGenericMethod(tipo).Invoke(this, [formaturaId, ct])!;

        await db.Arquivos.Where(a => a.Chave.StartsWith(prefixoDosArquivos + "/")).ExecuteDeleteAsync(ct);

        await db.Vinculos.Where(v => v.FormaturaId == formaturaId && v.Ativo).ExecuteUpdateAsync(v => v.SetProperty(x => x.Ativo, false), ct);
    }

    /// <summary>
    /// Apaga as linhas de um tipo da turma.
    /// </summary>
    /// <remarks>
    /// A cláusula explícita soma ao filtro global, não o substitui: com o escopo apontado para outra
    /// turma, as duas juntas não casam com nada — o erro vira "não apagou", nunca "apagou a turma errada".
    /// </remarks>
    private Task<int> Apagar<TEntidade>(Guid formaturaId, CancellationToken ct)
        where TEntidade : EntidadeDaFormatura => db.Set<TEntidade>().Where(e => e.FormaturaId == formaturaId).ExecuteDeleteAsync(ct);
}

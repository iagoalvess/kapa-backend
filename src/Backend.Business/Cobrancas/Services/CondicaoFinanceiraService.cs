using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formandos.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// O desconto ou a isenção individual que a tesouraria autoriza (Sprint 54).
/// </summary>
/// <remarks>
/// Só autoriza: o desconto acontece quando a pessoa escolhe um pacote alcançado e aceita — na adesão ou num aditivo.
/// Por isso alterar ou revogar aqui não mexe em parcela nenhuma, e quem está com a prévia aberta recebe
/// <c>adesao.termo_desatualizado</c> no aceite: o desconto entra no hash.
/// </remarks>
/// <param name="condicoes">As condições da turma.</param>
/// <param name="planos">O catálogo vigente.</param>
/// <param name="perfis">O vínculo da pessoa.</param>
/// <param name="validator">Forma da condição.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class CondicaoFinanceiraService(
    ICondicaoFinanceiraRepository condicoes,
    IPlanoDeCobrancaRepository planos,
    IPerfilRepository perfis,
    IValidator<DadosDaCondicao> validator,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<CondicaoFinanceiraService> logger
) : ICondicaoFinanceiraService
{
    private static readonly Erro SemPlano = Erro.Conflito(
        "cobranca.sem_plano_vigente",
        "A turma ainda não tem plano de cobrança em vigor: a condição aponta pacotes do catálogo."
    );

    private static readonly Erro PessoaNaoEncontrada = Erro.NaoEncontrado("cobranca.formando_nao_encontrado", "Pessoa não encontrada nesta turma.");

    /// <inheritdoc />
    public async Task<Result<CondicaoDaPessoa>> Obter(Guid formaturaId, PessoaDaCondicao pessoa, CancellationToken ct = default)
    {
        var alvo = await Localizar(formaturaId, pessoa, ct);
        if (alvo.Falhou)
            return Result.Falha<CondicaoDaPessoa>(alvo.Erros);

        return await Ler(alvo.Valor!.Condicao, ct);
    }

    /// <inheritdoc />
    public async Task<Result<CondicaoDaPessoa>> Salvar(
        Guid formaturaId,
        PessoaDaCondicao pessoa,
        DadosDaCondicao dados,
        Guid autorId,
        CancellationToken ct = default
    )
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<CondicaoDaPessoa>(validacao.Erros);

        if (await planos.ObterVigente(ct) is not { } plano)
            return SemPlano;

        if (ConferirCatalogo(dados, plano) is { } fora)
            return fora;

        var alvo = await Localizar(formaturaId, pessoa, ct);
        if (alvo.Falhou)
            return Result.Falha<CondicaoDaPessoa>(alvo.Erros);

        var (vinculoId, email, condicao) = alvo.Valor!;

        if (condicao is null)
        {
            condicao = vinculoId is { } id
                ? CondicaoFinanceira.DoVinculo(id, dados, autorId)
                : CondicaoFinanceira.DoConvidado(email!, dados, autorId);
            await condicoes.Adicionar(condicao, ct);
        }
        else
        {
            condicao.Alterar(dados, autorId);
        }

        await Auditar(formaturaId, condicao, "concedida", autorId, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Condição financeira {CondicaoId} salva por {AutorId}.", condicao.Id, autorId);

        return await Ler(condicao, ct);
    }

    /// <inheritdoc />
    public async Task<Result> Remover(Guid formaturaId, PessoaDaCondicao pessoa, Guid autorId, CancellationToken ct = default)
    {
        var alvo = await Localizar(formaturaId, pessoa, ct);
        if (alvo.Falhou)
            return Result.Falha(alvo.Erros);

        if (alvo.Valor!.Condicao is not { } condicao)
            return Result.Ok();

        condicoes.Remover(condicao);
        await Auditar(formaturaId, condicao, "revogada", autorId, ct);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <summary>
    /// Todo benefício aponta um pacote à venda, e no máximo um por grupo de faixas — dois benefícios no mesmo grupo
    /// deixariam a faixa escolhida sem saber qual vale.
    /// </summary>
    /// <param name="dados">Benefícios pedidos.</param>
    /// <param name="plano">Plano vigente.</param>
    private static Erro? ConferirCatalogo(DadosDaCondicao dados, PlanoDeCobranca plano)
    {
        var pacotes = plano.Pacotes().ToDictionary(pacote => pacote.Id);

        if (dados.Beneficios.Any(beneficio => !pacotes.ContainsKey(beneficio.ItemDeCobrancaId)))
            return Erro.Validacao("cobranca.condicao_fora_do_catalogo", "Algum pacote escolhido não está mais no catálogo.", campo: "beneficios");

        var grupos = dados.Beneficios.Select(beneficio => pacotes[beneficio.ItemDeCobrancaId].Grupo).OfType<string>().ToList();
        if (grupos.Count != grupos.Distinct(StringComparer.Ordinal).Count())
            return Erro.Validacao(
                "cobranca.condicao_grupo_repetido",
                "Escolha uma faixa só por grupo: o benefício vale para o grupo inteiro, até o preço da faixa escolhida.",
                campo: "beneficios"
            );

        return null;
    }

    /// <summary>O vínculo ou o e-mail da pessoa, e a condição que ela já tem.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="pessoa">Usuário ou e-mail.</param>
    private async Task<Result<(Guid? VinculoId, string? Email, CondicaoFinanceira? Condicao)>> Localizar(
        Guid formaturaId,
        PessoaDaCondicao pessoa,
        CancellationToken ct
    )
    {
        if (pessoa.UsuarioId is { } usuarioId)
        {
            if (await perfis.ObterMembro(formaturaId, usuarioId, ct) is not { } membro)
                return PessoaNaoEncontrada;

            return (membro.VinculoId, null, await condicoes.ObterDoVinculoParaEdicao(membro.VinculoId, ct));
        }

        if (string.IsNullOrWhiteSpace(pessoa.Email))
            return Erro.Validacao("cobranca.condicao_sem_pessoa", "Diga de quem é a condição.", campo: "email");

        var email = CondicaoFinanceira.NormalizarEmail(pessoa.Email);

        return (null, email, await condicoes.ObterDoConvidadoParaEdicao(email, ct));
    }

    /// <summary>A condição com o desconto de cada benefício no catálogo de hoje.</summary>
    /// <param name="condicao">Condição, se houver.</param>
    private async Task<CondicaoDaPessoa> Ler(CondicaoFinanceira? condicao, CancellationToken ct)
    {
        if (condicao is null)
            return new CondicaoDaPessoa(null);

        IReadOnlyCollection<ItemDeCobranca> catalogo = (await planos.ObterVigente(ct))?.Itens ?? [];

        return new CondicaoDaPessoa(
            new CondicaoLida(
                [
                    .. condicao.Beneficios.Select(beneficio =>
                    {
                        var pacote = catalogo.FirstOrDefault(item => item.Id == beneficio.ItemDeCobrancaId);

                        return new BeneficioLido(
                            beneficio.ItemDeCobrancaId,
                            pacote is null ? null
                                : pacote.Grupo is null ? RotuloDoItem.De(pacote.Tipo, pacote.Descricao)
                                : $"{pacote.Grupo} — {RotuloDoItem.De(pacote.Tipo, pacote.Descricao)}",
                            beneficio.Percentual,
                            beneficio.ValorEmCentavos,
                            pacote?.ValorEmCentavos ?? 0,
                            pacote is null ? 0 : condicao.DescontoDe(pacote, catalogo)
                        );
                    }),
                ],
                condicao.AtualizadoEm
            )
        );
    }

    /// <summary>Registra a concessão ou a revogação: dinheiro que deixa de ser cobrado, pela mão de alguém.</summary>
    private Task Auditar(Guid formaturaId, CondicaoFinanceira condicao, string acao, Guid autorId, CancellationToken ct) =>
        eventos.Auditar(
            NomesDeAuditoria.CondicaoFinanceira,
            autorId,
            new
            {
                formaturaId,
                acao,
                condicaoId = condicao.Id,
                condicao.VinculoId,
                condicao.Email,
                beneficios = condicao.Beneficios,
            },
            ct
        );
}

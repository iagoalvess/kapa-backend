using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using FluentValidation;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// Cupons de desconto da primeira cobrança (Sprint 51): o Administrador cria e desativa; a turma consulta antes de pagar.
/// </summary>
/// <remarks>
/// A regra "este cupom vale para esta turma" mora em <see cref="Aplicavel"/>, e o checkout chama a mesma — a tela
/// que mostra o preço riscado e a cobrança que vai ao provedor não podem discordar.
/// </remarks>
/// <param name="cupomRepository">Cupons.</param>
/// <param name="assinaturaRepository">Para saber se a turma já pagou alguma vez.</param>
/// <param name="eventos">Auditoria de criar e desativar.</param>
/// <param name="validator">Forma do cupom novo.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class CupomService(
    ICupomRepository cupomRepository,
    IAssinaturaRepository assinaturaRepository,
    IEventoRepository eventos,
    IValidator<NovoCupom> validator,
    IUnitOfWork unitOfWork
)
{
    /// <summary>
    /// A resposta única do cupom que não vale (D3): inexistente, vencido, esgotado, desativado ou turma que já pagou.
    /// </summary>
    /// <remarks>Distinguir os casos transformaria o campo num verificador de códigos.</remarks>
    public static readonly Erro Invalido = Erro.Validacao("cupom.invalido", "Cupom inválido ou expirado.", campo: "cupom_codigo");

    /// <summary>Se o cupom vale agora para a turma: existe, está disponível e a turma nunca pagou um plano.</summary>
    /// <param name="formaturaId">Turma que contrata.</param>
    /// <param name="codigo">O que foi digitado.</param>
    public async Task<Result<Cupom>> Aplicavel(Guid formaturaId, string? codigo, CancellationToken ct = default)
    {
        var normalizado = Cupom.Normalizar(codigo);

        if (!Cupom.FormaValida(normalizado))
            return Invalido;

        var cupom = await cupomRepository.ObterPorCodigo(normalizado, ct);

        if (cupom is null || !cupom.Disponivel(DateTime.UtcNow))
            return Invalido;

        if (await assinaturaRepository.ExisteAlgumaDeTodasAsFormaturas(formaturaId, ct))
            return Invalido;

        return cupom;
    }

    /// <summary>O cupom como o checkout o mostra: código e percentual.</summary>
    /// <param name="formaturaId">Turma que contrata.</param>
    /// <param name="codigo">O que foi digitado.</param>
    public async Task<Result<CupomAplicavel>> Consultar(Guid formaturaId, string? codigo, CancellationToken ct = default) =>
        (await Aplicavel(formaturaId, codigo, ct)).Map(cupom => new CupomAplicavel(cupom.Codigo, cupom.Percentual));

    /// <summary>Todos os cupons, para o painel.</summary>
    public async Task<Result<IReadOnlyList<Cupom>>> Listar(CancellationToken ct = default) => Result.Ok(await cupomRepository.Listar(ct));

    /// <summary>Cria um cupom. O código é único entre todos, inclusive os desativados.</summary>
    /// <param name="dados">Cupom novo.</param>
    /// <param name="autorId">Administrador que criou.</param>
    public async Task<Result<Cupom>> Criar(NovoCupom dados, Guid autorId, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<Cupom>(validacao.Erros);

        var codigo = Cupom.Normalizar(dados.Codigo);

        if (await cupomRepository.ObterPorCodigo(codigo, ct) is not null)
            return Erro.Conflito("cupom.codigo_em_uso", "Já existe um cupom com este código.");

        var cupom = new Cupom
        {
            Codigo = codigo,
            Percentual = dados.Percentual,
            ValidoAte = DataUtils.FimDoDiaEmUtc(dados.ValidoAte),
            LimiteDeUsos = dados.LimiteDeUsos,
        };

        await cupomRepository.Adicionar(cupom, ct);
        await eventos.Auditar(
            NomesDeAuditoria.CupomCriado,
            autorId,
            new
            {
                cupomId = cupom.Id,
                cupom.Codigo,
                cupom.Percentual,
                cupom.ValidoAte,
                cupom.LimiteDeUsos,
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        return cupom;
    }

    /// <summary>Desativa o cupom: deixa de valer na hora. Quem já contratou com ele não muda.</summary>
    /// <param name="cupomId">Cupom.</param>
    /// <param name="autorId">Administrador que desativou.</param>
    public async Task<Result<Cupom>> Desativar(Guid cupomId, Guid autorId, CancellationToken ct = default)
    {
        var cupom = await cupomRepository.ObterParaEdicao(cupomId, ct);

        if (cupom is null)
            return Erro.NaoEncontrado("cupom.nao_encontrado", "Cupom não encontrado.");

        if (!cupom.Ativo)
            return cupom;

        cupom.Desativar();
        await eventos.Auditar(
            NomesDeAuditoria.CupomDesativado,
            autorId,
            new
            {
                cupomId = cupom.Id,
                cupom.Codigo,
                cupom.Usos,
            },
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        return cupom;
    }
}

using Backend.Business.Abstractions;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Formandos.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// As mesas do jantar: o cadastro da comissão e a mesa vendida atribuída a quem a comprou.
/// </summary>
/// <remarks>
/// Só a comissão escreve (P1). A mesa é nome e lugares, sem ocupantes (23/09/2026): quem comprou leva
/// a mesa inteira e resolve quem senta nela.
/// </remarks>
/// <param name="mesas">Mesas da turma e o direito de cada formando.</param>
/// <param name="perfis">Vínculo de quem lê as próprias mesas.</param>
/// <param name="validator">Forma do cadastro.</param>
/// <param name="validatorDoSalao">Forma do mapa salvo.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class MesaService(
    IMesaRepository mesas,
    IPerfilRepository perfis,
    IValidator<DadosDaMesa> validator,
    IValidator<DesenhoDoSalao> validatorDoSalao,
    IUnitOfWork unitOfWork,
    ILogger<MesaService> logger
) : IMesaService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado("festa.mesa_nao_encontrada", "Mesa não encontrada.");

    private static readonly Erro Reservada = Erro.Conflito(
        "festa.mesa_reservada",
        "Mesa reservada não tem dono: tire a reserva antes de atribuí-la."
    );

    private static readonly Erro IdentificacaoEmUso = Erro.Conflito("festa.identificacao_em_uso", "Já existe uma mesa com esse nome.");

    /// <inheritdoc />
    /// <remarks>
    /// As contas da faixa saem das duas listas que a tela já recebe, na memória: são dezenas de mesas,
    /// e assim a faixa e a lista nunca discordam.
    /// </remarks>
    public async Task<Result<MapaDeMesas>> Mapa(CancellationToken ct = default)
    {
        var lista = await mesas.Listar(null, ct);
        var compradores = await mesas.ListarCompradores(ct);
        var salao = await mesas.ObterSalao(ct) ?? PlantaDoSalao.Padrao;

        return new MapaDeMesas(
            lista.Count,
            lista.Sum(mesa => mesa.Lugares),
            lista.Count(mesa => mesa.Reservada),
            lista.Count(mesa => mesa.VinculoId is not null),
            compradores.Sum(comprador => Math.Max(0, comprador.Compradas - comprador.Atribuidas)),
            lista,
            compradores,
            salao
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Todas as mesas, mas sem o dono (P9, 28/09/2026): o formando vê onde fica a própria mesa e o que
    /// há em volta, e não de quem é cada uma.
    /// </remarks>
    public async Task<Result<SalaoDoFormando>> SalaoDoFormando(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        if (await perfis.ObterMembro(formaturaId, usuarioId, ct) is not { } membro)
            return Erro.NaoEncontrado("formatura.vinculo_nao_encontrado", "Você não é membro ativo desta turma.");

        var lista = await mesas.Listar(null, ct);

        return new SalaoDoFormando(
            await mesas.ObterSalao(ct) ?? PlantaDoSalao.Padrao,
            [
                .. lista.Select(mesa => new MesaNoSalao(
                    mesa.Id,
                    mesa.Identificacao,
                    mesa.Lugares,
                    mesa.Reservada,
                    mesa.Formato,
                    mesa.X,
                    mesa.Y,
                    mesa.Girada,
                    mesa.VinculoId == membro.VinculoId
                )),
            ]
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uma gravação só, para o salão e as mesas: o mapa que a comissão vê depois de salvar é o que ela
    /// montou, e não metade dele. Duas pessoas salvando o mapa ao mesmo tempo: vence a última — é o
    /// comportamento de qualquer editor de documento sem trava, e a comissão é de três ou quatro.
    /// <para>
    /// ponytail: sem controle de versão do mapa. Se duas pessoas passarem a perder trabalho uma da
    /// outra, o salão ganha <c>xmin</c> e o segundo a salvar recebe 409.
    /// </para>
    /// </remarks>
    public async Task<Result> SalvarSalao(DesenhoDoSalao desenho, CancellationToken ct = default)
    {
        var salao = await mesas.ObterSalaoParaEdicao(ct);
        var plantaAtual = salao is null ? PlantaDoSalao.Padrao : new PlantaDoSalao(salao.Largura, salao.Altura, []);
        var desenhoComTamanhoAtual = desenho with { Planta = desenho.Planta with { Largura = plantaAtual.Largura, Altura = plantaAtual.Altura } };
        var validacao = validatorDoSalao.Validar(desenhoComTamanhoAtual);
        if (validacao.Falhou)
            return Result.Falha(validacao.Erros);

        if (salao is not null)
            salao.Redesenhar(desenhoComTamanhoAtual.Planta);
        else
            await mesas.AdicionarSalao(Salao.Novo(desenhoComTamanhoAtual.Planta), ct);

        var posicoes = desenho.Posicoes.DistinctBy(posicao => posicao.MesaId).ToDictionary(posicao => posicao.MesaId);
        foreach (var mesa in await mesas.ListarParaEdicao(posicoes.Keys, ct))
        {
            var posicao = posicoes[mesa.Id];
            mesa.Posicionar(posicao.X, posicao.Y, posicao.Girada);
        }

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<MesaResumo>> Criar(DadosDaMesa dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<MesaResumo>(validacao.Erros);

        if (await mesas.IdentificacaoEmUso(dados.Identificacao.Trim(), null, ct))
            return IdentificacaoEmUso;

        var mesa = Mesa.Nova(dados);
        await mesas.Adicionar(mesa, ct);
        await unitOfWork.SalvarAsync(ct);

        return await Resumo(mesa.Id, ct);
    }

    /// <inheritdoc />
    public async Task<Result<MesaResumo>> Atualizar(Guid id, DadosDaMesa dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<MesaResumo>(validacao.Erros);

        if (await mesas.ObterParaEdicao(id, ct) is not { } mesa)
            return NaoEncontrada;

        if (dados.Reservada && mesa.VinculoId is not null)
            return Reservada;

        if (await mesas.IdentificacaoEmUso(dados.Identificacao.Trim(), id, ct))
            return IdentificacaoEmUso;

        mesa.Aplicar(dados);
        await unitOfWork.SalvarAsync(ct);

        return await Resumo(id, ct);
    }

    /// <inheritdoc />
    /// <remarks>Recusar é grátis e não mente: a mesa com dono é de alguém que pagou por ela.</remarks>
    public async Task<Result> Excluir(Guid id, CancellationToken ct = default)
    {
        if (await mesas.ObterParaEdicao(id, ct) is not { } mesa)
            return Result.Falha(NaoEncontrada);

        if (mesa.VinculoId is not null)
            return Result.Falha(Erro.Conflito("festa.mesa_com_dono", "Esta mesa já é de um formando. Solte o dono antes de excluir."));

        mesas.Remover(mesa);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A contagem das mesas do formando acontece sob a trava dele (<see cref="IMesaRepository.TravarDono"/>):
    /// duas atribuições simultâneas ao mesmo formando nunca passam juntas do que ele comprou.
    /// </remarks>
    public async Task<Result<MesaResumo>> DefinirDono(Guid id, Guid? vinculoId, CancellationToken ct = default)
    {
        var definido = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                if (vinculoId is { } dono)
                    await mesas.TravarDono(dono, token);

                if (await mesas.ObterParaEdicao(id, token) is not { } mesa)
                    return Result.Falha(NaoEncontrada);

                if (vinculoId is { } novo && mesa.VinculoId != novo)
                {
                    if (mesa.Reservada)
                        return Result.Falha(Reservada);

                    var compradas = await mesas.Compradas(novo, token);
                    var atribuidas = (await mesas.ListarDoDonoParaEdicao(novo, token)).Count;

                    if (atribuidas >= compradas)
                        return Result.Falha(
                            Erro.Conflito(
                                "festa.mesas_alem_do_pedido",
                                compradas == 0
                                    ? "Este formando não tem pedido de mesa confirmado."
                                    : $"Este formando comprou {compradas} {(compradas == 1 ? "mesa" : "mesas")} e já tem todas no mapa."
                            )
                        );
                }

                mesa.DefinirDono(vinculoId);
                await unitOfWork.SalvarAsync(token);

                return Result.Ok();
            },
            ct
        );

        if (definido.Falhou)
            return Result.Falha<MesaResumo>(definido.Erros);

        logger.LogInformation("Mesa {MesaId}: dono {VinculoId}.", id, vinculoId);

        return await Resumo(id, ct);
    }

    private async Task<Result<MesaResumo>> Resumo(Guid id, CancellationToken ct) => await mesas.Obter(id, ct) is { } mesa ? mesa : NaoEncontrada;
}

/// <summary>
/// Solta as mesas que um formando tem além do que ainda compra — o lado da festa do pedido de mesa.
/// </summary>
/// <remarks>
/// Quem decide <b>quando</b> é o <c>PedidoService</c>: cancelar ou diminuir um pedido de mesa (decisão
/// 6). O direito nasce com o pedido confirmado (23/09/2026), então o estorno de uma parcela não mexe
/// aqui: o pedido continua de pé e a régua cobra. Não chama <c>SalvarAsync</c> — marca as mesas
/// rastreadas, e o chamador persiste. A mesa fica no mapa, sem dono, para a comissão resolver.
/// <para>Sem interface, como <c>EmissaoDeConvites</c>: uma implementação, e ninguém de fora a substitui.</para>
/// </remarks>
/// <param name="mesas">Mesas e o direito do formando.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class DonosDeMesa(IMesaRepository mesas, ILogger<DonosDeMesa> logger)
{
    /// <summary>Solta as mesas do formando além da quantidade que os pedidos de mesa dele ainda confirmam.</summary>
    /// <remarks>Lê o pedido do banco: quem chama salva a mudança do pedido antes.</remarks>
    /// <param name="vinculoId">Formando.</param>
    /// <returns>Quantas mesas ficaram sem dono.</returns>
    public async Task<int> SoltarAlemDoPedido(Guid vinculoId, CancellationToken ct = default)
    {
        await mesas.TravarDono(vinculoId, ct);

        var compradas = await mesas.Compradas(vinculoId, ct);
        var dele = await mesas.ListarDoDonoParaEdicao(vinculoId, ct);
        var excedentes = dele.Take(Math.Max(0, dele.Count - compradas)).ToList();

        foreach (var mesa in excedentes)
            mesa.DefinirDono(null);

        if (excedentes.Count > 0)
            logger.LogInformation("Formando {VinculoId}: {Soltas} mesas sem dono depois do pedido.", vinculoId, excedentes.Count);

        return excedentes.Count;
    }
}

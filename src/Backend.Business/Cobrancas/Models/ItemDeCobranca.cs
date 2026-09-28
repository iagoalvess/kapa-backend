using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// Uma linha do plano: "Mensalidade, R$ 8.400,00 em 24×, todo dia 10, a partir de março".
/// </summary>
/// <remarks>
/// O valor é o <b>total por formando</b>, dividido pela grade (<see cref="GradeDeParcelas"/>). Com
/// o total guardado, "R$ 1.000,00 em 3×" fecha no centavo; com o valor da parcela guardado, o
/// centavo que sobra não teria onde morar.
/// <para>
/// A exceção é o item <see cref="Opcional"/> (Sprint 20, decisão 2): ali o valor é o preço de
/// <b>uma unidade</b>, e quem multiplica pela quantidade é o pedido.
/// </para>
/// </remarks>
public class ItemDeCobranca : EntidadeDaFormatura
{
    /// <summary>Plano dono.</summary>
    public Guid PlanoId { get; set; }

    /// <summary>O que cobra.</summary>
    public TipoDeCobranca Tipo { get; private set; }

    /// <summary>Nome na tela, se diferente do tipo.</summary>
    public string? Descricao { get; private set; }

    /// <summary>
    /// Valor total por formando, em centavos — ou o preço <b>unitário</b> no item <see cref="Opcional"/>.
    /// </summary>
    /// <remarks>
    /// A diferença é a decisão 2 da Sprint 20 e precisa estar escrita aqui: "convite extra, R$ 180,
    /// em 2×" com quantidade 3 vira duas parcelas de R$ 270, e não de R$ 90. Quem multiplica é o
    /// pedido, antes de montar a grade.
    /// </remarks>
    public long ValorEmCentavos { get; private set; }

    /// <summary>Em quantas vezes.</summary>
    public int NumeroDeParcelas { get; private set; }

    /// <summary>Dia do vencimento, de 1 a 31. No mês que não tem o dia, vale o último.</summary>
    public int DiaDeVencimento { get; private set; }

    /// <summary>Mês do primeiro vencimento, sempre no dia 1.</summary>
    public DateOnly PrimeiroMes { get; private set; }

    /// <summary>Quando deixou de cobrar. Item encerrado não gera parcela nova.</summary>
    public DateOnly? EncerradoEm { get; private set; }

    /// <summary>
    /// Onde a turma decidiu este item — "assembleia de 12/10". Só no rateio extraordinário.
    /// </summary>
    /// <remarks>
    /// Preenchido quer dizer que o item alcançou também quem já tinha aderido (revisão de
    /// 17/09/2026 da decisão 8 da Sprint 7). É a prova da cobrança: o snapshot da adesão não a
    /// cita — ele é o que a pessoa leu no dia, e reescrevê-lo destruiria a defesa da comissão.
    /// </remarks>
    public string? OrigemDaDecisao { get; private set; }

    /// <summary>
    /// O item é opcional: só cobra quem pedir (Sprint 20, decisão 1).
    /// </summary>
    /// <remarks>
    /// <see cref="PlanoDeCobranca.ItensAtivos"/> os ignora, e é isso que impede o convite extra de
    /// entrar na adesão da turma inteira. O pedido do formando é o único caminho que os alcança.
    /// </remarks>
    public bool Opcional { get; private set; }

    /// <summary>Quantas unidades cada formando pode pedir. Nulo: sem cota (P6).</summary>
    public int? LimitePorFormando { get; private set; }

    /// <summary>Último dia em que se pode pedir. Nulo: sem prazo (P4).</summary>
    public DateOnly? PedidosAteDia { get; private set; }

    /// <summary>Quantas unidades existem ao todo. Nulo: sem teto (decisão 7).</summary>
    public int? Estoque { get; private set; }

    /// <summary>
    /// Quantas unidades já foram pedidas — contador, não contagem (decisão 7).
    /// </summary>
    /// <remarks>
    /// Sobe no pedido e desce no cancelamento, sempre sob a trava da linha do item (decisão 10). O
    /// <c>CHECK</c> de <c>reservados</c> é o que impede o contador de divergir para um lugar
    /// impossível — cancelar duas vezes não devolve duas vezes.
    /// </remarks>
    public int Reservados { get; private set; }

    /// <summary>A partir de quando se pode pedir, em UTC. Nulo: aberto desde sempre (decisão 8).</summary>
    /// <remarks>
    /// Com hora desde a Sprint 26 (P10): a loja abre "às 20h", e quem decide se já abriu é o relógio do
    /// servidor, nunca o do celular. Vale para as duas portas.
    /// </remarks>
    public DateTime? AberturaDeVendas { get; private set; }

    /// <summary>Por qual porta o item se vende: a vitrine do formando ou a loja pública (Sprint 26, decisão 1).</summary>
    /// <remarks>
    /// As portas são exclusivas (P8): o item da loja sai da vitrine, e o formando que quiser compra pelo
    /// link como qualquer pessoa. O estoque, o contador e o <c>CHECK</c> são os mesmos nas duas.
    /// </remarks>
    public ModoDeVenda ModoDeVenda { get; private set; }

    /// <summary>O preço de uma unidade na loja, se a comissão quis outro. Nulo: o mesmo do formando (P4).</summary>
    public long? PrecoPublicoEmCentavos { get; private set; }

    /// <summary>Quanto uma unidade custa na loja.</summary>
    public long PrecoNaLoja => PrecoPublicoEmCentavos ?? ValorEmCentavos;

    /// <summary>Se o item se vende pela loja pública.</summary>
    public bool NaLoja => Opcional && ModoDeVenda == ModoDeVenda.Publica;

    /// <summary>
    /// O item da festa que este item vende (Sprint 20, decisão 11). Nulo é o caso comum.
    /// </summary>
    /// <remarks>
    /// Ligado, o cartão da festa passa a mostrar <c>preço × pedidos confirmados</c> no lugar da
    /// estimativa — o mesmo "o previsto cede lugar ao real" da decisão 3 da Sprint 17, com um
    /// degrau a mais no meio. Só item <see cref="Opcional"/> aceita o vínculo.
    /// </remarks>
    public Guid? ItemDaFestaId { get; private set; }

    /// <summary>Quantas unidades ainda cabem. Nulo no item sem teto — número onde não há teto é ruído.</summary>
    public int? Disponivel => Estoque is { } teto ? Math.Max(0, teto - Reservados) : null;

    /// <summary>Cria o item a partir dos dados informados.</summary>
    /// <param name="planoId">Plano dono.</param>
    /// <param name="dados">Dados já validados.</param>
    /// <param name="origemDaDecisao">Onde a turma decidiu, no rateio extraordinário; nulo no item comum.</param>
    public static ItemDeCobranca Novo(Guid planoId, DadosDoItem dados, string? origemDaDecisao = null)
    {
        var item = new ItemDeCobranca { PlanoId = planoId, OrigemDaDecisao = origemDaDecisao?.Trim() };
        item.Aplicar(dados);

        return item;
    }

    /// <summary>Cria um item opcional — o que só cobra quem pedir.</summary>
    /// <param name="planoId">Plano dono.</param>
    /// <param name="dados">Dados já validados.</param>
    public static ItemDeCobranca NovoOpcional(Guid planoId, DadosDoOpcional dados)
    {
        var item = new ItemDeCobranca { PlanoId = planoId, Opcional = true };
        item.AplicarDadosDoOpcional(dados);

        return item;
    }

    /// <summary>Grava os dados informados.</summary>
    /// <param name="dados">Dados já validados.</param>
    public void Aplicar(DadosDoItem dados)
    {
        Tipo = dados.Tipo;
        Descricao = string.IsNullOrWhiteSpace(dados.Descricao) ? null : dados.Descricao.Trim();
        ValorEmCentavos = dados.ValorEmCentavos;
        NumeroDeParcelas = dados.NumeroDeParcelas;
        DiaDeVencimento = dados.DiaDeVencimento;
        PrimeiroMes = GradeDeParcelas.PrimeiroDoMes(dados.PrimeiroMes);
    }

    /// <summary>
    /// Grava os dados do item opcional, com estoque, cota, prazo, abertura e o vínculo com a festa.
    /// </summary>
    /// <remarks>
    /// Reduzir o estoque abaixo do que já foi reservado é recusado (P8): aceitar exigiria afrouxar o
    /// <c>CHECK</c> que impede vender a mais, e quem decide qual pedido cancelar é a comissão.
    /// </remarks>
    /// <param name="dados">Dados já validados.</param>
    public Result AplicarDadosDoOpcional(DadosDoOpcional dados)
    {
        if (dados.Estoque is { } teto && teto < Reservados)
            return Result.Falha(
                Erro.Conflito(
                    "cobranca.estoque_menor_que_reservado",
                    $"Já foram pedidas {Reservados} unidades deste item. Cancele pedidos antes de reduzir o estoque."
                )
            );

        Aplicar(dados.Item);

        LimitePorFormando = dados.LimitePorFormando;
        PedidosAteDia = dados.PedidosAteDia;
        Estoque = dados.Estoque;
        AberturaDeVendas = dados.AberturaDeVendas;
        ItemDaFestaId = dados.ItemDaFestaId;
        ModoDeVenda = dados.ModoDeVenda;
        PrecoPublicoEmCentavos = dados.ModoDeVenda == ModoDeVenda.Publica ? dados.PrecoPublicoEmCentavos : null;

        return Result.Ok();
    }

    /// <summary>
    /// Se os dados mudam a forma da grade — e não só o valor.
    /// </summary>
    /// <remarks>
    /// Com parcela gerada, só o valor (e a descrição) pode mudar: trocar o número de parcelas ou o
    /// dia reescreveria vencimentos que alguém já pagou ou já tem na agenda.
    /// </remarks>
    /// <param name="dados">Dados pretendidos.</param>
    public bool MudaAGrade(DadosDoItem dados) =>
        dados.Tipo != Tipo
        || dados.NumeroDeParcelas != NumeroDeParcelas
        || dados.DiaDeVencimento != DiaDeVencimento
        || GradeDeParcelas.PrimeiroDoMes(dados.PrimeiroMes) != PrimeiroMes;

    /// <summary>Deixa de cobrar a partir de hoje. Encerrar de novo não muda a data.</summary>
    /// <param name="hoje">Dia do encerramento.</param>
    public void Encerrar(DateOnly hoje) => EncerradoEm ??= hoje;

    /// <summary>
    /// Confere se o item aceita o pedido hoje e move o contador de reservas.
    /// </summary>
    /// <remarks>
    /// Chamado <b>sob a trava da linha</b> (decisão 10): o delta depende da quantidade que o pedido
    /// já tinha, então ler e escrever acontecem na mesma transação, depois do
    /// <c>SELECT … FOR UPDATE</c>. A checagem aqui existe para dar a mensagem certa; a garantia é o
    /// <c>CHECK</c> do banco, que aborta a transação mesmo quando este método é pulado.
    /// </remarks>
    /// <param name="delta">Unidades a reservar; negativo devolve ao estoque.</param>
    /// <param name="quantidadeFinal">Quantidade que o pedido fica tendo — é ela que a cota limita.</param>
    /// <param name="agoraUtc">Instante de referência, em UTC.</param>
    public Result Reservar(int delta, int quantidadeFinal, DateTime agoraUtc)
    {
        if (delta > 0)
        {
            if (NaLoja)
                return Result.Falha(Erro.Conflito("cobranca.item_da_loja", "Este item é vendido pela loja da turma. Compre pelo link da loja."));

            if (Fechado(agoraUtc) is { } fechado)
                return Result.Falha(fechado);

            if (LimitePorFormando is { } limite && quantidadeFinal > limite)
                return Result.Falha(
                    Erro.Conflito("cobranca.limite_do_item_excedido", $"Cada formando pode pedir no máximo {limite} unidades deste item.")
                );

            if (Estoque is { } teto && Reservados + delta > teto)
                return Result.Falha(Erro.Conflito("cobranca.estoque_esgotado", "Não há mais unidades deste item disponíveis."));
        }

        Reservados = Math.Max(0, Reservados + delta);

        return Result.Ok();
    }

    /// <summary>
    /// Ocupa unidades do estoque sem pedido — a cortesia da turma (Sprint 21, decisão 14).
    /// </summary>
    /// <remarks>
    /// Só o teto conta: prazo, abertura e cota são regras de <b>venda</b>, e a cortesia não é vendida.
    /// Chamado sob a mesma trava da linha que o pedido usa; a garantia continua sendo o <c>CHECK</c>.
    /// </remarks>
    /// <param name="unidades">Quantas cadeiras a cortesia ocupa.</param>
    public Result Ocupar(int unidades)
    {
        if (Estoque is { } teto && Reservados + unidades > teto)
            return Result.Falha(Erro.Conflito("cobranca.estoque_esgotado", "Não há mais unidades deste item disponíveis."));

        Reservados += unidades;

        return Result.Ok();
    }

    /// <summary>Se o item está aberto a pedido agora — o que decide entre o botão e a data na vitrine.</summary>
    /// <param name="agoraUtc">Instante de referência, em UTC.</param>
    public bool AbertoAPedido(DateTime agoraUtc) => Opcional && Fechado(agoraUtc) is null;

    /// <summary>
    /// Por que o item não aceita venda agora — encerrado, antes da abertura ou depois do prazo —, ou nulo se
    /// aceita. As duas portas conferem a mesma coisa.
    /// </summary>
    /// <param name="agoraUtc">Instante de referência, em UTC.</param>
    public Erro? Fechado(DateTime agoraUtc)
    {
        if (EncerradoEm is not null)
            return Erro.Conflito("cobranca.item_encerrado", "Este item não está mais à venda.");

        if (AberturaDeVendas is { } abertura && agoraUtc < abertura)
            return Erro.Conflito("cobranca.venda_nao_aberta", "As vendas deste item ainda não abriram.");

        if (PedidosAteDia is { } prazo && DateOnly.FromDateTime(DataUtils.ParaExibicao(agoraUtc)) > prazo)
            return Erro.Conflito("cobranca.pedido_fora_do_prazo", "O prazo para pedir este item já passou.");

        return null;
    }

    /// <summary>O item como dados — a forma que a grade e a simulação leem.</summary>
    public DadosDoItem ParaDados() => new(Tipo, Descricao, ValorEmCentavos, NumeroDeParcelas, DiaDeVencimento, PrimeiroMes);
}

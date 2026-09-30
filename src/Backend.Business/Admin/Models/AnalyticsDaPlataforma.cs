namespace Backend.Business.Admin.Models;

/// <summary>
/// Como a plataforma está: contas, turmas, dinheiro e uso, num período (Sprint 44).
/// </summary>
/// <remarks>
/// O dinheiro vem em dois blocos que nunca se somam (P1): <see cref="Kapa"/> é a receita do Kapa — as assinaturas —,
/// e <see cref="Turmas"/> é o dinheiro que as turmas movimentam entre elas e os formandos, só agregado. Confundir os
/// dois é anunciar como faturamento o que é caixa de formatura.
/// </remarks>
/// <param name="De">Primeiro dia do período, no fuso de exibição.</param>
/// <param name="Ate">Último dia do período, inclusive.</param>
/// <param name="Contas">As contas da plataforma.</param>
/// <param name="Formaturas">As turmas, por licença.</param>
/// <param name="Kapa">A receita do Kapa.</param>
/// <param name="Turmas">O dinheiro das turmas, agregado.</param>
/// <param name="Uso">O que se faz na plataforma, por recurso, do mais usado ao menos (P2).</param>
/// <param name="GeradoEm">Momento da apuração, em UTC.</param>
public sealed record AnalyticsDaPlataforma(
    DateOnly De,
    DateOnly Ate,
    ContasDaPlataforma Contas,
    TurmasDaPlataforma Formaturas,
    DinheiroDoKapa Kapa,
    DinheiroDasTurmas Turmas,
    IReadOnlyList<UsoDoRecurso> Uso,
    DateTime GeradoEm
);

/// <summary>As contas da plataforma.</summary>
/// <param name="Total">Contas cadastradas.</param>
/// <param name="NoPeriodo">Cadastradas no período.</param>
/// <param name="Confirmadas">Com o e-mail confirmado.</param>
/// <param name="SemTurma">Sem vínculo ativo em turma nenhuma — cadastrou e parou.</param>
public sealed record ContasDaPlataforma(long Total, long NoPeriodo, long Confirmadas, long SemTurma);

/// <summary>As turmas da plataforma.</summary>
/// <param name="Total">Turmas criadas, descartadas inclusive.</param>
/// <param name="NovasNoPeriodo">Criadas no período.</param>
/// <param name="Pagantes">Com assinatura em dia (P4): <c>Ativa</c>, ou <c>Cancelada</c> dentro do período pago.</param>
/// <param name="PorLicenca">Quantas em cada licença (<see cref="LicencaDaTurma"/>), da maior para a menor.</param>
/// <param name="MembrosPorTurmaMedia">Média de membros ativos por turma, sem as descartadas.</param>
/// <param name="MembrosPorTurmaMediana">Mediana de membros ativos por turma, sem as descartadas.</param>
public sealed record TurmasDaPlataforma(
    long Total,
    long NovasNoPeriodo,
    long Pagantes,
    IReadOnlyList<TurmasNaLicenca> PorLicenca,
    double MembrosPorTurmaMedia,
    double MembrosPorTurmaMediana
);

/// <summary>Quantas turmas numa licença.</summary>
/// <param name="Licenca">A licença (<see cref="LicencaDaTurma"/>).</param>
/// <param name="Turmas">Quantas.</param>
public sealed record TurmasNaLicenca(string Licenca, long Turmas);

/// <summary>A receita do Kapa: as assinaturas das turmas (P1).</summary>
/// <param name="Assinaturas">Assinaturas que renovam — <c>Ativa</c>; a cancelada vale até o fim e não volta.</param>
/// <param name="MrrEmCentavos">Receita recorrente mensal das que renovam; o plano anual entra dividido por doze.</param>
/// <param name="RecebidoEmCentavos">Pagamentos do plano confirmados no período, estornados inclusive — o estorno sai à parte.</param>
/// <param name="AVencerEmCentavos">Renovações que vencem nos próximos 30 dias, pelo preço do ciclo.</param>
/// <param name="EstornadoEmCentavos">Devolvido no período.</param>
public sealed record DinheiroDoKapa(long Assinaturas, long MrrEmCentavos, long RecebidoEmCentavos, long AVencerEmCentavos, long EstornadoEmCentavos);

/// <summary>
/// O dinheiro das turmas, só agregado (P1): nunca por turma nem por formando. Não é receita do Kapa.
/// </summary>
/// <param name="ParcelasPagas">Parcelas baixadas no período.</param>
/// <param name="PagoEmCentavos">O que essas parcelas pagaram.</param>
/// <param name="ParcelasAReceber">Parcelas em aberto hoje, vencidas inclusive.</param>
/// <param name="AReceberEmCentavos">O valor delas.</param>
public sealed record DinheiroDasTurmas(long ParcelasPagas, long PagoEmCentavos, long ParcelasAReceber, long AReceberEmCentavos);

/// <summary>Um recurso no ranking de uso (P2): o prefixo de <c>recurso.acao</c> da tabela de eventos.</summary>
/// <param name="Recurso">O recurso (<c>pagamento</c>, <c>adesao</c>…).</param>
/// <param name="Eventos">Ações gravadas no período.</param>
/// <param name="Turmas">Turmas distintas que agiram.</param>
/// <param name="Usuarios">Pessoas distintas que agiram.</param>
public sealed record UsoDoRecurso(string Recurso, long Eventos, long Turmas, long Usuarios);

/// <summary>Um mês da série do painel, no fuso de exibição.</summary>
/// <param name="Ano">Ano.</param>
/// <param name="Mes">Mês, de 1 a 12.</param>
/// <param name="Cadastros">Contas criadas.</param>
/// <param name="TurmasNovas">Turmas criadas.</param>
/// <param name="RecebidoEmCentavos">Pagamentos do plano confirmados.</param>
public sealed record MesDaPlataforma(int Ano, int Mes, long Cadastros, long TurmasNovas, long RecebidoEmCentavos);

/// <summary>
/// A licença de uma turma, como o painel a agrupa: o plano vigente enquanto ela está <c>Ativa</c>, e o status dela
/// quando não está.
/// </summary>
/// <remarks>
/// "A contratar" não existe mais: desde 18/09/2026 toda turma nasce <c>Ativa</c> no gratuito, e é ele que
/// responde por quem ainda não contratou. O plano pago vem pelo nome — Essencial e Essencial anual são a mesma
/// licença.
/// </remarks>
public static class LicencaDaTurma
{
    /// <summary>Ativa, sem assinatura paga em dia.</summary>
    public const string Gratuita = "Gratuito";
}

/// <summary>Filtro da lista de turmas do painel.</summary>
/// <param name="Termo">Trecho do nome, da instituição ou do curso.</param>
/// <param name="Licenca">Só as desta licença (<see cref="LicencaDaTurma"/>).</param>
public sealed record FiltroDeTurmasNoPainel(string? Termo, string? Licenca);

/// <summary>Uma turma na lista do painel.</summary>
/// <param name="Id">Formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Status">Situação da formatura.</param>
/// <param name="Licenca">Licença (<see cref="LicencaDaTurma"/>).</param>
/// <param name="Membros">Vínculos ativos.</param>
/// <param name="CriadaEm">Quando nasceu, em UTC.</param>
public sealed record TurmaNoPainel(
    Guid Id,
    string Nome,
    string Instituicao,
    string Curso,
    string Status,
    string Licenca,
    int Membros,
    DateTime CriadaEm
);

/// <summary>Situação de uma conta, para o filtro da lista.</summary>
public enum SituacaoDaConta
{
    /// <summary>Ativa e com o e-mail confirmado.</summary>
    Confirmada,

    /// <summary>Travada por tentativas de senha agora.</summary>
    Bloqueada,

    /// <summary>Desativada pela administração.</summary>
    Desativada,
}

/// <summary>Filtro da lista de contas do painel.</summary>
/// <param name="Termo">Trecho do nome ou do e-mail.</param>
/// <param name="Situacao">Só as desta situação.</param>
public sealed record FiltroDeContasNoPainel(string? Termo, SituacaoDaConta? Situacao);

/// <summary>Uma conta na lista do painel.</summary>
/// <param name="Id">Usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Ativo">Se pode autenticar.</param>
/// <param name="EmailConfirmado">Se confirmou o e-mail.</param>
/// <param name="BloqueadoAte">Fim do bloqueio por tentativas, em UTC, ou nulo.</param>
/// <param name="Turmas">Em quantas turmas tem vínculo ativo.</param>
/// <param name="CriadoEm">Quando a conta nasceu, em UTC.</param>
public sealed record ContaNoPainel(
    Guid Id,
    string Nome,
    string Email,
    bool Ativo,
    bool EmailConfirmado,
    DateTime? BloqueadoAte,
    int Turmas,
    DateTime CriadoEm
);

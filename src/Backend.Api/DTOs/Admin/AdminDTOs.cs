namespace Backend.Api.DTOs.Admin;

/// <summary>Como a plataforma está no período: contas, turmas, dinheiro e uso (Sprint 44).</summary>
/// <param name="De">Primeiro dia do período (<c>aaaa-mm-dd</c>).</param>
/// <param name="Ate">Último dia do período, inclusive.</param>
/// <param name="Contas">As contas.</param>
/// <param name="Formaturas">As turmas, por licença.</param>
/// <param name="Kapa">A receita do Kapa: as assinaturas.</param>
/// <param name="Turmas">O dinheiro das turmas, só agregado. Não é receita do Kapa.</param>
/// <param name="Uso">Ações gravadas por recurso, da mais usada à menos.</param>
/// <param name="GeradoEm">Momento da apuração, em UTC (ISO 8601).</param>
public sealed record AnalyticsDaPlataformaDTO(
    DateOnly De,
    DateOnly Ate,
    ContasDaPlataformaDTO Contas,
    TurmasDaPlataformaDTO Formaturas,
    DinheiroDoKapaDTO Kapa,
    DinheiroDasTurmasDTO Turmas,
    IReadOnlyList<UsoDoRecursoDTO> Uso,
    DateTime GeradoEm
);

/// <summary>As contas da plataforma.</summary>
/// <param name="Total">Contas cadastradas.</param>
/// <param name="NoPeriodo">Cadastradas no período.</param>
/// <param name="Confirmadas">Com o e-mail confirmado.</param>
/// <param name="SemTurma">Sem vínculo ativo em turma nenhuma.</param>
public sealed record ContasDaPlataformaDTO(long Total, long NoPeriodo, long Confirmadas, long SemTurma);

/// <summary>As turmas da plataforma.</summary>
/// <param name="Total">Turmas criadas.</param>
/// <param name="NovasNoPeriodo">Criadas no período.</param>
/// <param name="Pagantes">Com assinatura em dia.</param>
/// <param name="PorLicenca">Quantas em cada licença, da maior para a menor.</param>
/// <param name="MembrosPorTurmaMedia">Média de membros ativos por turma.</param>
/// <param name="MembrosPorTurmaMediana">Mediana de membros ativos por turma.</param>
public sealed record TurmasDaPlataformaDTO(
    long Total,
    long NovasNoPeriodo,
    long Pagantes,
    IReadOnlyList<TurmasNaLicencaDTO> PorLicenca,
    double MembrosPorTurmaMedia,
    double MembrosPorTurmaMediana
);

/// <summary>Quantas turmas numa licença.</summary>
/// <param name="Licenca"><c>Gratuito</c>, o nome do plano pago, ou o status da turma parada (<c>Suspensa</c>, <c>Encerrada</c>, <c>Descartada</c>).</param>
/// <param name="Turmas">Quantas.</param>
public sealed record TurmasNaLicencaDTO(string Licenca, long Turmas);

/// <summary>A receita do Kapa.</summary>
/// <param name="Assinaturas">Assinaturas que renovam.</param>
/// <param name="MrrEmCentavos">Receita recorrente mensal; o anual entra dividido por doze.</param>
/// <param name="RecebidoEmCentavos">Pagamentos do plano confirmados no período.</param>
/// <param name="AVencerEmCentavos">Renovações nos próximos 30 dias.</param>
/// <param name="EstornadoEmCentavos">Devolvido no período.</param>
public sealed record DinheiroDoKapaDTO(
    long Assinaturas,
    long MrrEmCentavos,
    long RecebidoEmCentavos,
    long AVencerEmCentavos,
    long EstornadoEmCentavos
);

/// <summary>O dinheiro das turmas, só agregado.</summary>
/// <param name="ParcelasPagas">Parcelas baixadas no período.</param>
/// <param name="PagoEmCentavos">O que elas pagaram.</param>
/// <param name="ParcelasAReceber">Parcelas em aberto hoje.</param>
/// <param name="AReceberEmCentavos">O valor delas.</param>
public sealed record DinheiroDasTurmasDTO(long ParcelasPagas, long PagoEmCentavos, long ParcelasAReceber, long AReceberEmCentavos);

/// <summary>Um recurso no ranking de uso.</summary>
/// <param name="Recurso">O prefixo de <c>recurso.acao</c>.</param>
/// <param name="Eventos">Ações gravadas.</param>
/// <param name="Turmas">Turmas distintas.</param>
/// <param name="Usuarios">Pessoas distintas.</param>
public sealed record UsoDoRecursoDTO(string Recurso, long Eventos, long Turmas, long Usuarios);

/// <summary>Um mês da série do painel.</summary>
/// <param name="Ano">Ano.</param>
/// <param name="Mes">Mês, de 1 a 12.</param>
/// <param name="Cadastros">Contas criadas.</param>
/// <param name="TurmasNovas">Turmas criadas.</param>
/// <param name="RecebidoEmCentavos">Pagamentos do plano confirmados.</param>
public sealed record MesDaPlataformaDTO(int Ano, int Mes, long Cadastros, long TurmasNovas, long RecebidoEmCentavos);

/// <summary>Uma turma na lista do painel.</summary>
/// <param name="Id">Formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Status">Situação da formatura.</param>
/// <param name="Licenca">Licença, como em <see cref="TurmasNaLicencaDTO"/>.</param>
/// <param name="Membros">Vínculos ativos.</param>
/// <param name="CriadaEm">Nascimento, em UTC (ISO 8601).</param>
public sealed record TurmaNoPainelDTO(
    Guid Id,
    string Nome,
    string Instituicao,
    string Curso,
    string Status,
    string Licenca,
    int Membros,
    DateTime CriadaEm
);

/// <summary>Uma conta na lista do painel.</summary>
/// <param name="Id">Usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Ativo">Se pode autenticar.</param>
/// <param name="EmailConfirmado">Se confirmou o e-mail.</param>
/// <param name="BloqueadoAte">Fim do bloqueio por tentativas, em UTC, ou nulo.</param>
/// <param name="Turmas">Em quantas turmas tem vínculo ativo.</param>
/// <param name="CriadoEm">Nascimento, em UTC (ISO 8601).</param>
public sealed record ContaNoPainelDTO(
    Guid Id,
    string Nome,
    string Email,
    bool Ativo,
    bool EmailConfirmado,
    DateTime? BloqueadoAte,
    int Turmas,
    DateTime CriadoEm
);

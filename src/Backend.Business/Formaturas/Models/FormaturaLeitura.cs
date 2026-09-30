namespace Backend.Business.Formaturas.Models;

/// <summary>Formatura da qual o usuário participa, com o papel dele.</summary>
/// <remarks>
/// Curso, instituição e ano vêm junto do nome porque duas turmas homônimas no seletor são
/// indistinguíveis — e escolher a errada mostra o caixa de outra turma.
/// </remarks>
/// <param name="Id">Identificador da formatura.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão: 1 ou 2. Com o ano, forma a turma ("2027.1").</param>
/// <param name="Papel">Papel do usuário nesta formatura.</param>
/// <param name="DesligadoEm">
/// Quando ele foi desligado desta turma, em UTC; nulo para quem continua nela. A turma fica no
/// seletor mesmo depois da saída, em leitura, porque o extrato é a prova do que ele pagou (P5).
/// </param>
public sealed record FormaturaDoUsuario(
    Guid Id,
    string Nome,
    string Curso,
    string Instituicao,
    int Ano,
    int Semestre,
    string Papel,
    DateTime? DesligadoEm
);

/// <summary>
/// Dados cadastrais da formatura, informados na criação e na edição.
/// </summary>
/// <remarks>
/// <b>Sem as datas de colação e de festa</b> desde a Sprint 19: elas são eventos da agenda, e quem
/// as escreve é <c>POST /agenda</c>. Mantê-las aqui daria dois caminhos de escrita para o mesmo
/// dia — que é exatamente o que a decisão 1 fecha.
/// </remarks>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão: 1 ou 2.</param>
public sealed record DadosDaFormatura(string Nome, string Instituicao, string Curso, int Ano, int Semestre);

/// <summary>
/// A formatura selecionada, como a tela de configurações e a faixa de status a enxergam.
/// </summary>
/// <remarks>
/// <c>PrevisaoDeColacao</c> e <c>PrevisaoDaFesta</c> <b>não são colunas</b> desde a
/// Sprint 19: saem dos eventos de tipo <c>Colacao</c> e <c>Festa</c> da agenda, por subconsulta. O
/// contrato ficou idêntico de propósito — é o que faz o contador do Início, os três marcos e a
/// janela da projeção do caixa continuarem certos sem nenhuma alteração neles.
/// </remarks>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome da turma.</param>
/// <param name="Instituicao">Instituição de ensino.</param>
/// <param name="Curso">Curso.</param>
/// <param name="Ano">Ano de conclusão.</param>
/// <param name="Semestre">Semestre de conclusão.</param>
/// <param name="PrevisaoDeColacao">Data prevista da colação.</param>
/// <param name="PrevisaoDaFesta">Data prevista da festa.</param>
/// <param name="Status">Situação no ciclo de vida.</param>
/// <param name="EncerradaEm">Encerramento, em UTC.</param>
/// <param name="JaContratou">Se a turma já contratou um plano alguma vez. Falsa é a turma no gratuito.</param>
public sealed record FormaturaDetalhe(
    Guid Id,
    string Nome,
    string Instituicao,
    string Curso,
    int Ano,
    int Semestre,
    DateOnly? PrevisaoDeColacao,
    DateOnly? PrevisaoDaFesta,
    StatusDaFormatura Status,
    DateTime? EncerradaEm,
    bool JaContratou
);

/// <summary>Vínculo do usuário, do jeito que a emissão de sessão precisa dele.</summary>
/// <param name="FormaturaId">Formatura à qual o vínculo pertence.</param>
/// <param name="Papel">Papel do usuário nela.</param>
/// <param name="DesligadoEm">Quando ele saiu da turma, em UTC; nulo para quem continua nela.</param>
public sealed record VinculoAtivo(Guid FormaturaId, string Papel, DateTime? DesligadoEm);

/// <summary>Membro de uma formatura, como a gestão enxerga: acesso e cadastro na mesma linha.</summary>
/// <param name="UsuarioId">Usuário vinculado.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Ativo">Se o vínculo ainda vale.</param>
/// <param name="NomeCompleto">Nome civil, se já informado no cadastro.</param>
/// <param name="Completude">Percentual do cadastro preenchido, de 0 a 100.</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
/// <param name="TemAdesao">
/// Se aderiu ao termo alguma vez. É o que decide a porta de saída da linha: quem aderiu deve, e sai
/// por Desligar; quem não aderiu é erro de cadastro, e sai por Remover (decisão 1 da Sprint 15).
/// </param>
/// <param name="DesligadoEm">Quando saiu da turma, em UTC; nulo em quem está nela e em quem foi removido.</param>
/// <param name="MotivoDoDesligamento">Por que saiu. Ver <see cref="MotivoDeSaida"/>.</param>
/// <param name="DetalheDoDesligamento">A justificativa, quando o motivo é <see cref="MotivoDeSaida.Outro"/>.</param>
public sealed record MembroDaFormatura(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    bool Ativo,
    string? NomeCompleto,
    int Completude,
    bool EssencialPendente,
    bool TemAdesao,
    DateTime? DesligadoEm,
    string? MotivoDoDesligamento,
    string? DetalheDoDesligamento
);

/// <summary>Recorte da lista por situação do cadastro.</summary>
public enum SituacaoDoCadastro
{
    /// <summary>Falta nome completo, CPF ou telefone.</summary>
    Pendente,

    /// <summary>Falta qualquer item.</summary>
    Incompleto,

    /// <summary>Tudo preenchido.</summary>
    Completo,
}

/// <summary>Filtros da listagem de membros.</summary>
/// <param name="Busca">Trecho do nome de exibição, do nome civil ou do e-mail, sem diferenciar maiúsculas.</param>
/// <param name="Ativo">Só ativos (<c>true</c>), só quem saiu (<c>false</c>) ou todos (nulo).</param>
/// <param name="Papel">Só este papel, ou todos (nulo). Ver <see cref="PapelNaFormatura"/>.</param>
/// <param name="Cadastro">Só esta situação do cadastro, ou todas (nulo).</param>
/// <param name="Desligado">
/// Só desligados (<c>true</c>), só quem nunca foi desligado (<c>false</c>) ou todos (nulo). Somado a
/// <paramref name="Ativo"/> é o que separa as três situações da tela: ativo, desligado e removido —
/// os dois últimos compartilham <c>Ativo = false</c> e se distinguem só por aqui.
/// </param>
public sealed record FiltroDeMembros(string? Busca, bool? Ativo, string? Papel = null, SituacaoDoCadastro? Cadastro = null, bool? Desligado = null);

/// <summary>Quantos vínculos a formatura tem num papel e numa situação.</summary>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Ativo">Se o vínculo ainda vale.</param>
/// <param name="Desligado">
/// Se saiu por desligamento. Com <paramref name="Ativo"/> falso, separa desligado de removido — sem
/// ele, a pílula "Desligados" da tela não teria número, ou teria o número dos removidos junto.
/// </param>
/// <param name="EssencialPendente">
/// Se falta nome completo, CPF ou telefone no cadastro. É mais uma dimensão do agrupamento, e não um
/// resumo à parte: somar as linhas com ele ligado dá o número "sem o essencial" da tela sem uma
/// segunda consulta, e quem só quer o total por papel ignora a coluna.
/// </param>
/// <param name="Quantidade">Vínculos nessa combinação.</param>
public sealed record ContagemDeMembros(string Papel, bool Ativo, bool Desligado, bool EssencialPendente, int Quantidade);

/// <summary>Novo papel de um membro.</summary>
/// <param name="Papel">Papel pretendido. Ver <see cref="PapelNaFormatura"/>.</param>
public sealed record AlterarPapel(string Papel);

/// <summary>
/// O que o desligamento vai mexer, somado antes de a comissão confirmar.
/// </summary>
/// <remarks>
/// Desligar sem ver estes números é assinar em branco: o que já entrou fica na turma (decisão 5), o
/// que está em aberto some da projeção, e o que está em atraso só some se a comissão marcar
/// (P1 de 17/09/2026). O atraso é contado <b>dentro</b> do que está em aberto — parcela vencida é
/// parcela em aberto com o vencimento no passado, e somar as duas colunas contaria duas vezes.
/// </remarks>
/// <param name="JaPagoEmCentavos">Quanto já entrou na conta da turma por ele.</param>
/// <param name="ParcelasEmAberto">Quantas parcelas ainda são devidas, vencidas incluídas.</param>
/// <param name="EmAbertoEmCentavos">Quanto elas somam, pelo valor original.</param>
/// <param name="ParcelasEmAtraso">Quantas das em aberto já venceram.</param>
/// <param name="EmAtrasoEmCentavos">Quanto elas somam, pelo valor original.</param>
public sealed record ResumoDaSaida(
    long JaPagoEmCentavos,
    int ParcelasEmAberto,
    long EmAbertoEmCentavos,
    int ParcelasEmAtraso,
    long EmAtrasoEmCentavos
);

/// <summary>Como a comissão desliga alguém da turma.</summary>
/// <param name="Motivo">Motivo da lista de <see cref="MotivoDeSaida"/>.</param>
/// <param name="Detalhe">Justificativa, obrigatória quando o motivo é <see cref="MotivoDeSaida.Outro"/>.</param>
/// <param name="CancelarAtraso">Cancela também o que já venceu e não foi pago (P1 de 17/09/2026).</param>
public sealed record DesligarFormando(string Motivo, string? Detalhe, bool CancelarAtraso);

/// <summary>O que o desligamento cancelou — o corpo do evento de auditoria e do e-mail.</summary>
/// <param name="Parcelas">Quantas parcelas deixaram de ser devidas.</param>
/// <param name="ValorEmCentavos">Quanto elas somavam.</param>
public sealed record CancelamentoDaSaida(int Parcelas, long ValorEmCentavos);

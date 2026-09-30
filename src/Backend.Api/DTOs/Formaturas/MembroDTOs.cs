namespace Backend.Api.DTOs.Formaturas;

/// <summary>Membro da formatura selecionada, com a situação do cadastro.</summary>
/// <param name="UsuarioId">Usuário, usado nas rotas de papel, remoção e cadastro.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel"><c>Presidente</c>, <c>Tesoureiro</c>, <c>Comissao</c> ou <c>Formando</c>.</param>
/// <param name="Ativo">Se o vínculo ainda vale.</param>
/// <param name="NomeCompleto">Nome civil, se já informado.</param>
/// <param name="Completude">Percentual do cadastro preenchido, de 0 a 100.</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
/// <param name="TemAdesao">Se aderiu ao termo. Decide a porta de saída: com adesão é Desligar, sem ela é Remover.</param>
/// <param name="DesligadoEm">
/// Quando saiu da turma, em UTC; nulo em quem está nela e em quem foi <b>removido</b>. É o que
/// separa as duas situações de <c>Ativo = false</c> na tela.
/// </param>
/// <param name="MotivoDoDesligamento">
/// <c>Trancamento</c>, <c>Transferencia</c>, <c>FormaturaEmOutraTurma</c>, <c>DesistenciaDaFesta</c>,
/// <c>DificuldadeFinanceira</c> ou <c>Outro</c>.
/// </param>
/// <param name="DetalheDoDesligamento">A justificativa, preenchida só quando o motivo é <c>Outro</c>.</param>
public sealed record MembroDaFormaturaDTO(
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

/// <summary>Quantos vínculos a formatura tem num papel e numa situação.</summary>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Ativo">Se o vínculo ainda vale.</param>
/// <param name="Desligado">Se saiu por desligamento. Com <paramref name="Ativo"/> falso, separa desligado de removido.</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone no cadastro.</param>
/// <param name="Quantidade">Vínculos nessa combinação.</param>
public sealed record ContagemDeMembrosDTO(string Papel, bool Ativo, bool Desligado, bool EssencialPendente, int Quantidade);

/// <summary>Corpo da troca de papel.</summary>
/// <param name="Papel">Papel novo.</param>
public sealed record AlterarPapelRequestDTO(string Papel);

/// <summary>
/// O que o desligamento vai mexer, para a comissão conferir antes de confirmar.
/// </summary>
/// <remarks>
/// O atraso é um recorte do que está <b>em aberto</b>, e não uma parcela a mais: somar as duas
/// linhas contaria a mesma parcela vencida duas vezes.
/// </remarks>
/// <param name="JaPagoEmCentavos">Quanto já entrou na conta da turma por ele.</param>
/// <param name="ParcelasEmAberto">Quantas parcelas ainda são devidas, vencidas incluídas.</param>
/// <param name="EmAbertoEmCentavos">Quanto elas somam, pelo valor original.</param>
/// <param name="ParcelasEmAtraso">Quantas das em aberto já venceram.</param>
/// <param name="EmAtrasoEmCentavos">Quanto elas somam, pelo valor original.</param>
public sealed record ResumoDaSaidaDTO(
    long JaPagoEmCentavos,
    int ParcelasEmAberto,
    long EmAbertoEmCentavos,
    int ParcelasEmAtraso,
    long EmAtrasoEmCentavos
);

/// <summary>Corpo do desligamento.</summary>
/// <param name="Motivo">
/// <c>Trancamento</c>, <c>Transferencia</c>, <c>FormaturaEmOutraTurma</c>, <c>DesistenciaDaFesta</c>,
/// <c>DificuldadeFinanceira</c> ou <c>Outro</c>.
/// </param>
/// <param name="Detalhe">A justificativa. Obrigatória quando o motivo é <c>Outro</c>, ignorada nos demais.</param>
/// <param name="CancelarAtraso">
/// Cancela também as parcelas já vencidas e não pagas. <c>false</c> mantém a cobrança do atraso.
/// </param>
public sealed record DesligarMembroRequestDTO(string Motivo, string? Detalhe, bool CancelarAtraso);

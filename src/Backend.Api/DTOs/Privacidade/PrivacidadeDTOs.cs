using Backend.Api.DTOs.Formandos;
using Backend.Api.DTOs.Legal;
using Backend.Business.Privacidade.Models;

namespace Backend.Api.DTOs.Privacidade;

/// <summary>Tudo o que a Kapa guarda sobre o titular, por seção.</summary>
/// <param name="Conta">O que é da pessoa, e não de uma turma.</param>
/// <param name="Turmas">Um bloco por vínculo, com cadastro, financeiro e adesão.</param>
/// <param name="Consentimentos">Histórico de aceite e revogação, do mais recente.</param>
/// <param name="Comunicacoes">Preferências e o que já foi enviado.</param>
public sealed record MeusDadosDTO(
    DadosDaContaDTO Conta,
    IReadOnlyList<MeusDadosDaTurmaDTO> Turmas,
    IReadOnlyList<ConsentimentoDoUsuarioDTO> Consentimentos,
    MinhasComunicacoesDTO Comunicacoes
);

/// <summary>A conta do titular.</summary>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail de acesso.</param>
/// <param name="EmailConfirmado">Se o e-mail foi confirmado.</param>
/// <param name="Telefone">Telefone da conta. Nulo quando não informado.</param>
/// <param name="CriadoEm">Quando a conta nasceu, em UTC.</param>
/// <param name="AnonimizadoEm">Quando foi anonimizada. Nulo no caso normal.</param>
public sealed record DadosDaContaDTO(
    Guid Id,
    string Nome,
    string Email,
    bool EmailConfirmado,
    string? Telefone,
    DateTime CriadoEm,
    DateTime? AnonimizadoEm
);

/// <summary>O que a pessoa tem dentro de uma turma.</summary>
/// <param name="FormaturaId">Turma.</param>
/// <param name="Formatura">Nome da turma.</param>
/// <param name="Instituicao">Instituição.</param>
/// <param name="Papel">Papel no vínculo.</param>
/// <param name="Ativo">Se o vínculo continua ativo.</param>
/// <param name="Perfil">Cadastro do formando. Nulo quando ainda não existe.</param>
/// <param name="Financeiro">Parcelas e o que já foi pago.</param>
/// <param name="Adesao">Aceite do termo. Nulo em quem não aderiu.</param>
public sealed record MeusDadosDaTurmaDTO(
    Guid FormaturaId,
    string Formatura,
    string Instituicao,
    string Papel,
    bool Ativo,
    PerfilExportadoDTO? Perfil,
    MeuFinanceiroDTO Financeiro,
    MinhaAdesaoDTO? Adesao
);

/// <summary>O cadastro, campo a campo.</summary>
/// <param name="NomeCompleto">Nome civil.</param>
/// <param name="NomeNoDiploma">Nome no diploma.</param>
/// <param name="Cpf">CPF, só dígitos — é o do próprio titular, então vai inteiro.</param>
/// <param name="Rg">RG.</param>
/// <param name="Matricula">Matrícula.</param>
/// <param name="Telefone">Telefone em E.164.</param>
/// <param name="DataDeNascimento">Data de nascimento.</param>
/// <param name="Observacoes">Recado livre para a comissão.</param>
/// <param name="Endereco">Endereço.</param>
/// <param name="ContatoDeEmergencia">Quem avisar numa emergência.</param>
/// <param name="TemFoto">Se há foto guardada.</param>
/// <param name="Completude">Percentual do cadastro preenchido.</param>
public sealed record PerfilExportadoDTO(
    string? NomeCompleto,
    string? NomeNoDiploma,
    string? Cpf,
    string? Rg,
    string? Matricula,
    string? Telefone,
    DateOnly? DataDeNascimento,
    string? Observacoes,
    DadosDeEnderecoDTO Endereco,
    DadosDeEmergenciaDTO ContatoDeEmergencia,
    bool TemFoto,
    int Completude
);

/// <summary>O financeiro do titular numa turma.</summary>
/// <param name="TotalEmCentavos">Soma do valor original das parcelas não canceladas.</param>
/// <param name="PagoEmCentavos">Soma do que foi recebido.</param>
/// <param name="Parcelas">Uma linha por parcela.</param>
public sealed record MeuFinanceiroDTO(long TotalEmCentavos, long PagoEmCentavos, IReadOnlyList<MinhaParcelaDTO> Parcelas);

/// <summary>Uma parcela do titular.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Item">Nome do item de cobrança.</param>
/// <param name="Numero">Número da parcela.</param>
/// <param name="Vencimento">Vencimento.</param>
/// <param name="ValorOriginalEmCentavos">Valor sem multa e sem juros.</param>
/// <param name="Status">Situação gravada.</param>
/// <param name="ValorPagoEmCentavos">Valor recebido. Nulo se não houve baixa.</param>
/// <param name="PagoEm">Dia do pagamento. Nulo se não houve baixa.</param>
public sealed record MinhaParcelaDTO(
    Guid Id,
    string Item,
    int Numero,
    DateOnly Vencimento,
    long ValorOriginalEmCentavos,
    string Status,
    long? ValorPagoEmCentavos,
    DateOnly? PagoEm
);

/// <summary>O aceite do termo.</summary>
/// <param name="Versao">Versão aceita.</param>
/// <param name="AceitoEm">Momento do aceite, em UTC.</param>
/// <param name="EnderecoIp">IP de onde veio o aceite.</param>
public sealed record MinhaAdesaoDTO(int Versao, DateTime AceitoEm, string EnderecoIp);

/// <summary>Preferências de comunicação e o que já foi mandado.</summary>
/// <param name="Preferencias">O que a pessoa deixou ligado, por turma.</param>
/// <param name="NotificacoesEnviadas">Quantas notificações já saíram para ela.</param>
/// <param name="UltimaEnviadaEm">Quando saiu a última. Nulo se nunca saiu nenhuma.</param>
public sealed record MinhasComunicacoesDTO(IReadOnlyList<MinhaPreferenciaDTO> Preferencias, int NotificacoesEnviadas, DateTime? UltimaEnviadaEm);

/// <summary>Uma preferência de notificação.</summary>
/// <param name="FormaturaId">Turma.</param>
/// <param name="Tipo">Tipo de notificação.</param>
/// <param name="Ativa">Se está ligada.</param>
public sealed record MinhaPreferenciaDTO(Guid FormaturaId, string Tipo, bool Ativa);

/// <summary>Uma solicitação do titular.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Tipo"><c>Exportacao</c> ou <c>Exclusao</c>.</param>
/// <param name="Status"><c>Pendente</c>, <c>Concluida</c>, <c>Cancelada</c> ou <c>Falhou</c>.</param>
/// <param name="CriadoEm">Quando foi pedida, em UTC.</param>
/// <param name="PrazoEm">Quando será executada, em UTC.</param>
/// <param name="ConfirmadaEm">Quando o titular confirmou. Nulo se não confirmou.</param>
/// <param name="ConcluidaEm">Quando foi atendida. Nulo enquanto pendente.</param>
/// <param name="ExpiraEm">Quando o pacote deixa de estar disponível. Nulo fora da exportação concluída.</param>
/// <param name="Disponivel">Se o pacote pode ser baixado agora.</param>
/// <param name="Motivo">Por que falhou. Nulo quando não falhou.</param>
public sealed record SolicitacaoDePrivacidadeDTO(
    Guid Id,
    TipoDeSolicitacao Tipo,
    StatusDaSolicitacaoDePrivacidade Status,
    DateTime CriadoEm,
    DateTime PrazoEm,
    DateTime? ConfirmadaEm,
    DateTime? ConcluidaEm,
    DateTime? ExpiraEm,
    bool Disponivel,
    string? Motivo
);

/// <summary>Corpo do pedido de solicitação.</summary>
/// <remarks>
/// Anulável porque o corpo inteiro pode não vir: <c>POST /privacidade/exportacao</c> não tem nada a
/// dizer, e obrigar um <c>{}</c> ali seria burocracia de contrato.
/// </remarks>
/// <param name="Tipo">O que pedir. Ausente, exportação.</param>
/// <param name="Senha">
/// Senha da conta. Obrigatória <b>só</b> na exclusão — a sessão prova quem entrou, não quem está na
/// frente da tela agora, e a eliminação é irreversível.
/// </param>
public sealed record SolicitarPrivacidadeDTO(TipoDeSolicitacao? Tipo, string? Senha);

/// <summary>Um terceiro que trata dado pessoal por conta da Kapa.</summary>
/// <param name="Nome">Quem é.</param>
/// <param name="Finalidade">Para quê.</param>
/// <param name="Dados">Que categoria de dado chega até ele.</param>
public sealed record OperadorDTO(string Nome, string Finalidade, string Dados);

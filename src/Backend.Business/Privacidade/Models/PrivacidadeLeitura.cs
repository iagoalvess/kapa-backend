using Backend.Business.Formandos.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Marketing.Models;

namespace Backend.Business.Privacidade.Models;

/// <summary>
/// Tudo o que a Kapa guarda sobre uma pessoa, por seção.
/// </summary>
/// <remarks>
/// É a resposta aos direitos de confirmação e acesso (LGPD, art. 18, I e II) e o conteúdo da
/// exportação — o mesmo objeto serve à tela e ao pacote, e é isso que garante que os dois digam a
/// mesma coisa. Uma tela que mostra menos do que a exportação leva é uma tela que mente.
/// <para>
/// O recorte é <b>a pessoa</b>, não a turma selecionada: <see cref="Turmas"/> traz todos os
/// vínculos, ativos e inativos.
/// </para>
/// </remarks>
/// <param name="Conta">O que é da pessoa, e não de uma turma.</param>
/// <param name="Turmas">Um bloco por vínculo, com cadastro, financeiro e adesão.</param>
/// <param name="Consentimentos">Histórico de aceite e revogação, do mais recente.</param>
/// <param name="Comunicacoes">Preferências e o que já foi enviado.</param>
public sealed record MeusDados(
    DadosDaConta Conta,
    IReadOnlyList<MeusDadosDaTurma> Turmas,
    IReadOnlyList<ConsentimentoDoUsuario> Consentimentos,
    MinhasComunicacoes Comunicacoes
);

/// <summary>A conta: o que existe uma vez por pessoa.</summary>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail de acesso.</param>
/// <param name="EmailConfirmado">Se o e-mail foi confirmado.</param>
/// <param name="Telefone">Telefone da conta, quando houver.</param>
/// <param name="CriadoEm">Quando a conta nasceu, em UTC.</param>
/// <param name="AnonimizadoEm">Quando foi anonimizada por pedido de eliminação. Nulo no caso normal.</param>
public sealed record DadosDaConta(
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
/// <param name="Instituicao">Instituição da turma.</param>
/// <param name="Papel">Papel no vínculo.</param>
/// <param name="Ativo">Se o vínculo continua ativo.</param>
/// <param name="Perfil">Cadastro do formando. Nulo quando ainda não existe.</param>
/// <param name="Financeiro">Parcelas e o que já foi pago.</param>
/// <param name="Adesao">Aceite do termo. Nulo em quem não aderiu.</param>
public sealed record MeusDadosDaTurma(
    Guid FormaturaId,
    string Formatura,
    string Instituicao,
    string Papel,
    bool Ativo,
    PerfilExportado? Perfil,
    MeuFinanceiro Financeiro,
    MinhaAdesao? Adesao
);

/// <summary>O cadastro, campo a campo — inclusive o CPF, que é da própria pessoa.</summary>
/// <param name="NomeCompleto">Nome civil.</param>
/// <param name="NomeNoDiploma">Nome no diploma.</param>
/// <param name="Cpf">CPF, só dígitos.</param>
/// <param name="Rg">RG.</param>
/// <param name="Matricula">Matrícula.</param>
/// <param name="Telefone">Telefone em E.164.</param>
/// <param name="DataDeNascimento">Data de nascimento.</param>
/// <param name="Observacoes">Recado livre para a comissão.</param>
/// <param name="Endereco">Endereço.</param>
/// <param name="ContatoDeEmergencia">Quem avisar numa emergência.</param>
/// <param name="TemFoto">Se há foto de rosto guardada.</param>
/// <param name="Completude">Percentual do cadastro preenchido.</param>
public sealed record PerfilExportado(
    string? NomeCompleto,
    string? NomeNoDiploma,
    string? Cpf,
    string? Rg,
    string? Matricula,
    string? Telefone,
    DateOnly? DataDeNascimento,
    string? Observacoes,
    DadosDeEndereco Endereco,
    DadosDeEmergencia ContatoDeEmergencia,
    bool TemFoto,
    int Completude
);

/// <summary>O financeiro da pessoa naquela turma.</summary>
/// <param name="TotalEmCentavos">Soma do valor original das parcelas não canceladas.</param>
/// <param name="PagoEmCentavos">Soma do que foi efetivamente recebido.</param>
/// <param name="Parcelas">Uma linha por parcela.</param>
public sealed record MeuFinanceiro(long TotalEmCentavos, long PagoEmCentavos, IReadOnlyList<MinhaParcela> Parcelas);

/// <summary>Uma parcela do titular.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Item">Nome do item de cobrança.</param>
/// <param name="Numero">Número da parcela dentro do item.</param>
/// <param name="Vencimento">Vencimento.</param>
/// <param name="ValorOriginalEmCentavos">Valor sem multa e sem juros.</param>
/// <param name="Status">Situação.</param>
/// <param name="ValorPagoEmCentavos">Valor recebido, quando houve baixa.</param>
/// <param name="PagoEm">Dia do pagamento informado na baixa.</param>
public sealed record MinhaParcela(
    Guid Id,
    string Item,
    int Numero,
    DateOnly Vencimento,
    long ValorOriginalEmCentavos,
    string Status,
    long? ValorPagoEmCentavos,
    DateOnly? PagoEm
);

/// <summary>O aceite do termo, que é prova de contrato e não é anonimizável.</summary>
/// <param name="Versao">Versão do termo aceita.</param>
/// <param name="AceitoEm">Momento do aceite, em UTC.</param>
/// <param name="EnderecoIp">IP de onde veio o aceite.</param>
public sealed record MinhaAdesao(int Versao, DateTime AceitoEm, string EnderecoIp);

/// <summary>Preferências de comunicação e o que já foi mandado.</summary>
/// <param name="Preferencias">O que a pessoa desligou ou deixou ligado, por turma.</param>
/// <param name="NotificacoesEnviadas">Quantas notificações já saíram para ela.</param>
/// <param name="UltimaEnviadaEm">Quando saiu a última, em UTC.</param>
/// <param name="DoKapa">"Receber novidades do Kapa": a preferência, o histórico e os e-mails de marketing mandados (Sprint 40).</param>
public sealed record MinhasComunicacoes(
    IReadOnlyList<MinhaPreferencia> Preferencias,
    int NotificacoesEnviadas,
    DateTime? UltimaEnviadaEm,
    ComunicacaoDoKapa DoKapa
);

/// <summary>Uma preferência de notificação do titular.</summary>
/// <param name="FormaturaId">Turma a que ela pertence.</param>
/// <param name="Tipo">Tipo de notificação.</param>
/// <param name="Ativa">Se está ligada.</param>
public sealed record MinhaPreferencia(Guid FormaturaId, string Tipo, bool Ativa);

/// <summary>Uma solicitação do titular, como a tela a lista.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Tipo">Exportação ou exclusão.</param>
/// <param name="Status">Situação.</param>
/// <param name="CriadoEm">Quando foi pedida, em UTC.</param>
/// <param name="PrazoEm">Quando será executada, em UTC.</param>
/// <param name="ConfirmadaEm">Quando o titular confirmou, antecipando o prazo.</param>
/// <param name="ConcluidaEm">Quando foi atendida, em UTC.</param>
/// <param name="ExpiraEm">Quando o pacote deixa de estar disponível.</param>
/// <param name="Disponivel">Se o pacote pode ser baixado agora.</param>
/// <param name="Motivo">Por que falhou, quando falhou.</param>
public sealed record SolicitacaoResumo(
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

/// <summary>Uma solicitação pendente que já venceu, para o worker processar.</summary>
/// <param name="SolicitacaoId">Identificador.</param>
/// <param name="Tipo">O que fazer.</param>
public sealed record PrivacidadePendente(Guid SolicitacaoId, TipoDeSolicitacao Tipo);

/// <summary>Um terceiro que trata dado pessoal por conta da Kapa (LGPD, art. 18, VII).</summary>
/// <param name="Nome">Quem é.</param>
/// <param name="Finalidade">Para quê.</param>
/// <param name="Dados">Que categoria de dado chega até ele.</param>
public sealed record Operador(string Nome, string Finalidade, string Dados);

/// <summary>Dados mínimos do titular usados pelos e-mails do portal.</summary>
/// <param name="UsuarioId">Titular.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail de acesso.</param>
public sealed record TitularParaAviso(Guid UsuarioId, string Nome, string Email);

/// <summary>Um presidente a avisar de um pedido de eliminação, e o quanto o titular deve na turma dele.</summary>
/// <param name="Email">E-mail do presidente.</param>
/// <param name="Formatura">Nome da turma.</param>
/// <param name="EmAbertoEmCentavos">Soma das parcelas em aberto do titular naquela turma.</param>
public sealed record PresidenteParaAviso(string Email, string Formatura, long EmAbertoEmCentavos);

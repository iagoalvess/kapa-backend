using Backend.Business.Enquetes.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Marketing.Models;

namespace Backend.Business.Privacidade.Models;

/// <summary>
/// Dados de conta e das turmas reunidos para o atendimento pelo suporte.
/// </summary>
/// <remarks>
/// É a resposta aos direitos de confirmação e acesso (LGPD, art. 18, I e II) e o conteúdo da
/// exportação interna. Arquivos, compras avulsas e registros técnicos devem ser avaliados
/// separadamente pelo suporte conforme o alcance do pedido.
/// <para>
/// O recorte é <b>a pessoa</b>, não a turma selecionada: <see cref="Turmas"/> traz todos os
/// vínculos, ativos e inativos.
/// </para>
/// </remarks>
/// <param name="Conta">O que é da pessoa, e não de uma turma.</param>
/// <param name="Turmas">Um bloco por vínculo, com cadastro, financeiro e adesão.</param>
/// <param name="Consentimentos">Histórico de aceite dos documentos, do mais recente.</param>
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
    MinhaAdesao? Adesao,
    IReadOnlyList<MinhaRespostaExportada>? Enquetes = null
);

/// <summary>O cadastro, campo a campo — inclusive o CPF, que é da própria pessoa.</summary>
/// <param name="NomeCompleto">Nome civil.</param>
/// <param name="Cpf">CPF, só dígitos.</param>
/// <param name="Telefone">Telefone em E.164.</param>
/// <param name="ContatoDeEmergencia">Quem avisar numa emergência.</param>
/// <param name="TemFoto">Se há foto de rosto guardada.</param>
/// <param name="Completude">Percentual do cadastro preenchido.</param>
public sealed record PerfilExportado(
    string? NomeCompleto,
    string? Cpf,
    string? Telefone,
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

/// <summary>O aceite do termo, retido como prova contratual conforme a análise de necessidade e prazo.</summary>
/// <param name="Versao">Versão do termo aceita.</param>
/// <param name="AceitoEm">Momento do aceite, em UTC.</param>
/// <param name="EnderecoIp">IP de onde veio o aceite.</param>
public sealed record MinhaAdesao(int Versao, DateTime AceitoEm, string EnderecoIp);

/// <summary>O que já foi mandado e as novidades do Kapa.</summary>
/// <param name="NotificacoesEnviadas">Quantas notificações já saíram para ela.</param>
/// <param name="UltimaEnviadaEm">Quando saiu a última, em UTC.</param>
/// <param name="DoKapa">"Receber novidades do Kapa": a preferência, o histórico e os e-mails de marketing mandados (Sprint 40).</param>
public sealed record MinhasComunicacoes(int NotificacoesEnviadas, DateTime? UltimaEnviadaEm, ComunicacaoDoKapa DoKapa);

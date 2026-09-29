using Backend.Business.Recebimentos.Models;

namespace Backend.Api.DTOs.Recebimentos;

/// <summary>A chave PIX da turma.</summary>
/// <param name="TipoDeChave">Tipo da chave: <c>Cpf</c>, <c>Cnpj</c>, <c>Email</c>, <c>Telefone</c> ou <c>Aleatoria</c>.</param>
/// <param name="Chave">A chave, com ou sem máscara na ida; no formato do diretório do PIX na volta.</param>
/// <param name="NomeDoTitular">Nome do titular, como o banco mostra.</param>
/// <param name="Cidade">Cidade do titular.</param>
public sealed record ChavePixDTO(TipoDeChavePix TipoDeChave, string? Chave, string? NomeDoTitular, string? Cidade);

/// <summary>A conta para quem vai transferir. Texto livre: o Kapa não confere dígito nem consulta banco.</summary>
/// <param name="Banco">Nome do banco.</param>
/// <param name="Agencia">Agência.</param>
/// <param name="Conta">Conta, com o dígito.</param>
/// <param name="TipoDeConta">"Corrente" ou "Poupança".</param>
/// <param name="Titular">Nome do titular da conta.</param>
public sealed record DadosBancariosDTO(string? Banco, string? Agencia, string? Conta, string? TipoDeConta, string? Titular);

/// <summary>Com quem o formando fala para pagar em espécie.</summary>
/// <param name="Nome">Quem recebe.</param>
/// <param name="Onde">Onde encontrar essa pessoa. Opcional.</param>
public sealed record DinheiroDTO(string? Nome, string? Onde);

/// <summary>
/// Os meios que a turma aceita — o corpo do <c>PUT</c> e o que volta na consulta.
/// </summary>
/// <remarks>
/// Meio nulo é meio desligado, e ao menos um precisa vir preenchido: 400 <c>recebimento.sem_meio</c>
/// se todos vierem nulos. O erro de cada campo volta no caminho dele (<c>pix.chave</c>,
/// <c>transferencia.banco</c>).
/// </remarks>
/// <param name="Pix">A chave da comissão.</param>
/// <param name="Transferencia">Os dados bancários.</param>
/// <param name="Dinheiro">Com quem falar.</param>
public sealed record MeiosDaContaDTO(ChavePixDTO? Pix, DadosBancariosDTO? Transferencia, DinheiroDTO? Dinheiro);

/// <summary>A conta de recebimento gravada.</summary>
/// <param name="Meios">Os meios que a turma aceita.</param>
/// <param name="AtualizadaEm">Última gravação, em UTC.</param>
/// <param name="ConferidaEm">Quando o Presidente confirmou o titular pelo PIX de teste, em UTC. Nulo: não conferida.</param>
/// <param name="ConferidaPor">Nome de quem confirmou.</param>
public sealed record ContaDeRecebimentoDTO(MeiosDaContaDTO Meios, DateTime AtualizadaEm, DateTime? ConferidaEm, string? ConferidaPor);

/// <summary>A conta da turma. Nula, a comissão ainda não cadastrou meio nenhum.</summary>
/// <param name="Conta">A conta gravada.</param>
public sealed record ContaDeRecebimentoDaTurmaDTO(ContaDeRecebimentoDTO? Conta);

/// <summary>O PIX de teste.</summary>
/// <param name="CopiaECola">O BR Code — a tela desenha o QR a partir dele, no navegador.</param>
/// <param name="ValorEmCentavos">Valor do teste.</param>
public sealed record PixDeTesteDTO(string CopiaECola, long ValorEmCentavos);

/// <summary>A conta do Mercado Pago conectada à turma. Nunca traz o token.</summary>
/// <param name="ContaNoProvedor">E-mail ou apelido da conta que autorizou.</param>
/// <param name="ConectadoEm">Quando a autorização atual foi dada, em UTC.</param>
/// <param name="ConectadoPor">Nome de quem autorizou.</param>
/// <param name="Cartao">O cartão da turma (Sprint 39).</param>
/// <param name="CobrancaAutomaticaEm">Desde quando parcelas e opcionais se pagam só pelo Mercado Pago, em UTC; nulo: cobrança manual.</param>
public sealed record ProvedorConectadoDTO(
    string ContaNoProvedor,
    DateTime ConectadoEm,
    string? ConectadoPor,
    CartaoDaTurmaDTO Cartao,
    DateTime? CobrancaAutomaticaEm
);

/// <summary>Trocar o modo de cobrança das parcelas.</summary>
/// <param name="Automatica">Verdadeiro: só Mercado Pago; falso ou ausente: os meios da comissão, com aviso e conferência.</param>
public sealed record ModoDeCobrancaRequestDTO(bool? Automatica);

/// <summary>O cartão da turma (Sprint 39, P2 e P7).</summary>
/// <param name="Disponivel">Se a conexão permite ligar; falso, o Presidente conecta a conta de novo antes.</param>
/// <param name="LigadoEm">Quando foi ligado, em UTC; nulo, desligado.</param>
/// <param name="LigadoPor">Nome de quem ligou.</param>
/// <param name="TaxaRepassada">Taxa repassada a quem paga, base 10.000; nula, a turma absorve.</param>
public sealed record CartaoDaTurmaDTO(bool Disponivel, DateTime? LigadoEm, string? LigadoPor, int? TaxaRepassada);

/// <summary>Ligar ou desligar o cartão (Sprint 39).</summary>
/// <param name="Ligado">Liga ou desliga.</param>
/// <param name="TaxaRepassada">Base 10.000, de 1 a 1500; nula, a turma absorve.</param>
public sealed record ConfiguracaoDoCartaoRequestDTO(bool? Ligado, int? TaxaRepassada);

/// <summary>O Mercado Pago da turma. <c>provedor</c> nulo: ainda não conectou.</summary>
/// <param name="Provedor">A conexão, ou nula.</param>
public sealed record ProvedorDaTurmaDTO(ProvedorConectadoDTO? Provedor);

/// <summary>Para onde mandar o navegador do presidente autorizar o Kapa.</summary>
/// <param name="Url">A página de autorização do Mercado Pago.</param>
public sealed record AutorizacaoDoProvedorDTO(string Url);

using Backend.Business.Recebimentos.Models;

namespace Backend.Api.DTOs.Recebimentos;

/// <summary>Corpo do cadastro ou da troca da chave PIX.</summary>
/// <param name="TipoDeChave">Tipo da chave: <c>Cpf</c>, <c>Cnpj</c>, <c>Email</c>, <c>Telefone</c> ou <c>Aleatoria</c>.</param>
/// <param name="Chave">A chave, com ou sem máscara.</param>
/// <param name="NomeDoTitular">Nome do titular, como o banco mostra.</param>
/// <param name="Cidade">Cidade do titular.</param>
public sealed record ContaDeRecebimentoRequestDTO(TipoDeChavePix TipoDeChave, string? Chave, string? NomeDoTitular, string? Cidade);

/// <summary>A conta de recebimento gravada.</summary>
/// <param name="TipoDeChave">Tipo da chave.</param>
/// <param name="Chave">Chave, no formato do diretório do PIX (CPF só dígitos, celular em <c>+55…</c>).</param>
/// <param name="NomeDoTitular">Nome do titular.</param>
/// <param name="Cidade">Cidade do titular.</param>
/// <param name="AtualizadaEm">Última gravação, em UTC.</param>
/// <param name="ConferidaEm">Quando o Presidente confirmou o titular pelo PIX de teste, em UTC. Ausente: não conferida.</param>
/// <param name="ConferidaPor">Nome de quem confirmou.</param>
public sealed record ContaDeRecebimentoDTO(
    TipoDeChavePix TipoDeChave,
    string Chave,
    string NomeDoTitular,
    string Cidade,
    DateTime AtualizadaEm,
    DateTime? ConferidaEm,
    string? ConferidaPor
);

/// <summary>A conta da turma. Ausente, a comissão ainda não cadastrou a chave.</summary>
/// <param name="Conta">A conta gravada.</param>
public sealed record ContaDeRecebimentoDaTurmaDTO(ContaDeRecebimentoDTO? Conta);

/// <summary>O PIX de teste.</summary>
/// <param name="CopiaECola">O BR Code — a tela desenha o QR a partir dele, no navegador.</param>
/// <param name="ValorEmCentavos">Valor do teste.</param>
public sealed record PixDeTesteDTO(string CopiaECola, long ValorEmCentavos);

namespace Backend.Business.Recebimentos.Models;

/// <summary>A chave PIX da turma, como a comissão informa.</summary>
/// <param name="TipoDeChave">Tipo da chave.</param>
/// <param name="Chave">Chave, com ou sem máscara.</param>
/// <param name="NomeDoTitular">Nome do titular, como o banco mostra.</param>
/// <param name="Cidade">Cidade do titular.</param>
public sealed record DadosDaConta(TipoDeChavePix TipoDeChave, string Chave, string NomeDoTitular, string Cidade);

/// <summary>A conta de recebimento gravada.</summary>
/// <param name="TipoDeChave">Tipo da chave.</param>
/// <param name="Chave">Chave, no formato do diretório do PIX.</param>
/// <param name="NomeDoTitular">Nome do titular.</param>
/// <param name="Cidade">Cidade do titular.</param>
/// <param name="AtualizadaEm">Última gravação, em UTC.</param>
/// <param name="ConferidaEm">Quando o Presidente confirmou o titular, em UTC. Nulo: não conferida.</param>
/// <param name="ConferidaPor">Nome de quem confirmou.</param>
public sealed record ContaDeRecebimentoDetalhe(
    TipoDeChavePix TipoDeChave,
    string Chave,
    string NomeDoTitular,
    string Cidade,
    DateTime AtualizadaEm,
    DateTime? ConferidaEm,
    string? ConferidaPor
);

/// <summary>A conta da turma, se a comissão já cadastrou. Ausente, a tela abre o formulário.</summary>
/// <param name="Conta">A conta gravada, ou nulo.</param>
public sealed record ContaDeRecebimentoDaTurma(ContaDeRecebimentoDetalhe? Conta);

/// <summary>O PIX de R$ 1,00 que o Presidente paga para ver o titular que o banco mostra.</summary>
/// <param name="CopiaECola">O BR Code, que a tela transforma em QR.</param>
/// <param name="ValorEmCentavos">Valor do teste.</param>
public sealed record PixDeTeste(string CopiaECola, long ValorEmCentavos);

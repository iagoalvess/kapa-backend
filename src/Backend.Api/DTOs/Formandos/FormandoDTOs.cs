namespace Backend.Api.DTOs.Formandos;

/// <summary>Corpo da alteração do cadastro. Seção ausente não é tocada.</summary>
/// <param name="Pessoais">Nome, CPF e telefone.</param>
/// <param name="ContatoDeEmergencia">Quem avisar numa emergência.</param>
public sealed record AtualizarPerfilRequestDTO(DadosPessoaisDTO? Pessoais, DadosDeEmergenciaDTO? ContatoDeEmergencia);

/// <summary>Seção de dados pessoais.</summary>
/// <param name="NomeCompleto">Nome civil completo.</param>
/// <param name="Cpf">
/// CPF, com ou sem máscara. Sai só com os dígitos para o titular e mascarado para a comissão
/// (<c>***.982.247-**</c>). Na correção pela comissão, é ignorado.
/// </param>
/// <param name="Telefone">Telefone com DDD ou em E.164. Sai sempre em E.164.</param>
public sealed record DadosPessoaisDTO(string? NomeCompleto, string? Cpf, string? Telefone);

/// <summary>Seção de contato de emergência.</summary>
/// <param name="Nome">Nome de quem avisar.</param>
/// <param name="Telefone">Telefone.</param>
/// <param name="Parentesco">Parentesco.</param>
public sealed record DadosDeEmergenciaDTO(string? Nome, string? Telefone, string? Parentesco);

/// <summary>O cadastro inteiro.</summary>
/// <param name="UsuarioId">Usuário, o id das rotas da comissão.</param>
/// <param name="Nome">Nome de exibição da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Pessoais">Nome, CPF e telefone.</param>
/// <param name="ContatoDeEmergencia">Contato de emergência.</param>
/// <param name="FotoArquivoId">
/// Arquivo da foto. O dono baixa por <c>/arquivos/{id}/conteudo</c>; a comissão, por
/// <c>/formandos/{usuarioId}/foto</c>. Muda a cada foto nova — serve de chave de cache.
/// </param>
/// <param name="Completude">Percentual preenchido, de 0 a 100.</param>
/// <param name="Faltando">Itens vazios: <c>nomeCompleto</c>, <c>cpf</c>, <c>telefone</c>, <c>contatoDeEmergencia</c>, <c>foto</c></param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
public sealed record PerfilDoFormandoDTO(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    DadosPessoaisDTO Pessoais,
    DadosDeEmergenciaDTO ContatoDeEmergencia,
    Guid? FotoArquivoId,
    int Completude,
    IReadOnlyList<string> Faltando,
    bool EssencialPendente
);

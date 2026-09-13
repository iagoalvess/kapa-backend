namespace Backend.Api.DTOs.Formandos;

/// <summary>Corpo da alteração do cadastro. Seção ausente não é tocada.</summary>
/// <param name="Pessoais">Documentos e contato.</param>
/// <param name="Endereco">Endereço.</param>
/// <param name="ContatoDeEmergencia">Quem avisar numa emergência.</param>
public sealed record AtualizarPerfilRequestDTO(DadosPessoaisDTO? Pessoais, DadosDeEnderecoDTO? Endereco, DadosDeEmergenciaDTO? ContatoDeEmergencia);

/// <summary>Seção de dados pessoais.</summary>
/// <param name="NomeCompleto">Nome civil completo.</param>
/// <param name="NomeNoDiploma">Nome como sai no diploma.</param>
/// <param name="Cpf">CPF, com ou sem máscara. Sai só com os dígitos.</param>
/// <param name="Rg">RG.</param>
/// <param name="Matricula">Matrícula na instituição.</param>
/// <param name="Telefone">Telefone com DDD ou em E.164. Sai sempre em E.164.</param>
/// <param name="DataDeNascimento">Data de nascimento, <c>aaaa-mm-dd</c>.</param>
/// <param name="Observacoes">Recado para a comissão.</param>
public sealed record DadosPessoaisDTO(
    string? NomeCompleto,
    string? NomeNoDiploma,
    string? Cpf,
    string? Rg,
    string? Matricula,
    string? Telefone,
    DateOnly? DataDeNascimento,
    string? Observacoes
);

/// <summary>Seção de endereço.</summary>
/// <param name="Cep">CEP, com ou sem máscara. Sai só com os dígitos.</param>
/// <param name="Logradouro">Rua, avenida.</param>
/// <param name="Numero">Número.</param>
/// <param name="Complemento">Complemento.</param>
/// <param name="Bairro">Bairro.</param>
/// <param name="Cidade">Cidade.</param>
/// <param name="Uf">Sigla da UF.</param>
public sealed record DadosDeEnderecoDTO(
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf
);

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
/// <param name="Pessoais">Documentos e contato.</param>
/// <param name="Endereco">Endereço.</param>
/// <param name="ContatoDeEmergencia">Contato de emergência.</param>
/// <param name="FotoArquivoId">
/// Arquivo da foto. O dono baixa por <c>/arquivos/{id}/conteudo</c>; a comissão, por
/// <c>/formandos/{usuarioId}/foto</c>. Muda a cada foto nova — serve de chave de cache.
/// </param>
/// <param name="Completude">Percentual preenchido, de 0 a 100.</param>
/// <param name="Faltando">Itens vazios: <c>nomeCompleto</c>, <c>cpf</c>, <c>endereco</c>, <c>foto</c>…</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
public sealed record PerfilDoFormandoDTO(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    DadosPessoaisDTO Pessoais,
    DadosDeEnderecoDTO Endereco,
    DadosDeEmergenciaDTO ContatoDeEmergencia,
    Guid? FotoArquivoId,
    int Completude,
    IReadOnlyList<string> Faltando,
    bool EssencialPendente
);

/// <summary>Um formando na lista da comissão.</summary>
/// <param name="UsuarioId">Usuário, o id do detalhe e da correção.</param>
/// <param name="Nome">Nome de exibição da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="NomeCompleto">Nome civil, se já informado.</param>
/// <param name="Completude">Percentual preenchido, de 0 a 100.</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
public sealed record FormandoResumoDTO(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    string? NomeCompleto,
    int Completude,
    bool EssencialPendente
);

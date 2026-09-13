namespace Backend.Business.Formandos.Models;

/// <summary>
/// Pedido de alteração do cadastro, em seções.
/// </summary>
/// <remarks>
/// Seção ausente não é tocada: o formulário salva uma seção por vez, e mandar as três sempre
/// obrigaria o cliente a reenviar o que não mudou — e a sobrescrever o que outra aba acabou de
/// salvar.
/// </remarks>
/// <param name="Pessoais">Documentos e contato.</param>
/// <param name="Endereco">Endereço.</param>
/// <param name="ContatoDeEmergencia">Quem avisar numa emergência.</param>
public sealed record AtualizarPerfil(DadosPessoais? Pessoais, DadosDeEndereco? Endereco, DadosDeEmergencia? ContatoDeEmergencia)
{
    /// <summary>Nomes das seções presentes no pedido, para o registro de correção.</summary>
    public string SecoesInformadas() =>
        string.Join(
            ',',
            new[]
            {
                Pessoais is null ? null : "pessoais",
                Endereco is null ? null : "endereco",
                ContatoDeEmergencia is null ? null : "contatoDeEmergencia",
            }.OfType<string>()
        );
}

/// <summary>Seção de dados pessoais.</summary>
/// <param name="NomeCompleto">Nome civil completo.</param>
/// <param name="NomeNoDiploma">Nome como sai no diploma.</param>
/// <param name="Cpf">CPF, com ou sem máscara.</param>
/// <param name="Rg">RG.</param>
/// <param name="Matricula">Matrícula na instituição.</param>
/// <param name="Telefone">Telefone, em E.164 ou no formato nacional com DDD.</param>
/// <param name="DataDeNascimento">Data de nascimento.</param>
/// <param name="Observacoes">Recado para a comissão.</param>
public sealed record DadosPessoais(
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
/// <param name="Cep">CEP, com ou sem máscara.</param>
/// <param name="Logradouro">Rua, avenida.</param>
/// <param name="Numero">Número.</param>
/// <param name="Complemento">Complemento.</param>
/// <param name="Bairro">Bairro.</param>
/// <param name="Cidade">Cidade.</param>
/// <param name="Uf">Sigla da UF.</param>
public sealed record DadosDeEndereco(
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
public sealed record DadosDeEmergencia(string? Nome, string? Telefone, string? Parentesco);

/// <summary>O membro dono do cadastro, com o que vem da conta.</summary>
/// <param name="VinculoId">Vínculo ativo na formatura.</param>
/// <param name="UsuarioId">Usuário.</param>
/// <param name="Nome">Nome de exibição da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
public sealed record MembroDoPerfil(Guid VinculoId, Guid UsuarioId, string Nome, string Email, string Papel);

/// <summary>O cadastro inteiro, como o próprio formando e a comissão o veem.</summary>
/// <param name="UsuarioId">Usuário, o id das rotas da comissão.</param>
/// <param name="Nome">Nome de exibição da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="Pessoais">Documentos e contato.</param>
/// <param name="Endereco">Endereço.</param>
/// <param name="ContatoDeEmergencia">Contato de emergência.</param>
/// <param name="FotoArquivoId">Arquivo da foto: o dono baixa pelo módulo de arquivos, a comissão por <see cref="Interfaces.IPerfilService.BaixarFoto"/>.</param>
/// <param name="Completude">Percentual preenchido, de 0 a 100.</param>
/// <param name="Faltando">Itens vazios, pelos nomes de <see cref="ItensDoCadastro"/>.</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
public sealed record PerfilDetalhe(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    DadosPessoais Pessoais,
    DadosDeEndereco Endereco,
    DadosDeEmergencia ContatoDeEmergencia,
    Guid? FotoArquivoId,
    int Completude,
    IReadOnlyList<string> Faltando,
    bool EssencialPendente
);

/// <summary>Um formando na lista da comissão.</summary>
/// <param name="UsuarioId">Usuário, o id das rotas de detalhe e correção.</param>
/// <param name="Nome">Nome de exibição da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="NomeCompleto">Nome civil, se já informado.</param>
/// <param name="Completude">Percentual preenchido, de 0 a 100.</param>
/// <param name="EssencialPendente">Se falta nome completo, CPF ou telefone.</param>
public sealed record FormandoResumo(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    string? NomeCompleto,
    int Completude,
    bool EssencialPendente
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

/// <summary>Filtros da lista de formandos.</summary>
/// <param name="Busca">Trecho do nome de exibição, do nome civil ou do e-mail.</param>
/// <param name="Situacao">Situação do cadastro; nulo traz todos.</param>
public sealed record FiltroDeFormandos(string? Busca, SituacaoDoCadastro? Situacao);

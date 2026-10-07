using Backend.Business.Common.Texto;

namespace Backend.Business.Formandos.Models;

/// <summary>
/// Pedido de alteração do cadastro, em seções.
/// </summary>
/// <remarks>
/// Seção ausente não é tocada: o formulário salva uma seção por vez, e mandar as três sempre
/// obrigaria o cliente a reenviar o que não mudou — e a sobrescrever o que outra aba acabou de
/// salvar.
/// </remarks>
/// <param name="Pessoais">Nome, CPF e telefone.</param>
/// <param name="ContatoDeEmergencia">Quem avisar numa emergência.</param>
public sealed record AtualizarPerfil(DadosPessoais? Pessoais, DadosDeEmergencia? ContatoDeEmergencia)
{
    /// <summary>Nomes das seções presentes no pedido, para o registro de correção.</summary>
    public string SecoesInformadas() =>
        string.Join(',', new[] { Pessoais is null ? null : "pessoais", ContatoDeEmergencia is null ? null : "contatoDeEmergencia" }.OfType<string>());

    /// <summary>O mesmo pedido com outro CPF na seção pessoal, se ela veio.</summary>
    /// <param name="cpf">CPF que substitui o informado.</param>
    public AtualizarPerfil ComCpf(string? cpf) => Pessoais is null ? this : this with { Pessoais = Pessoais with { Cpf = cpf } };
}

/// <summary>Seção de dados pessoais.</summary>
/// <param name="NomeCompleto">Nome civil completo.</param>
/// <param name="Cpf">CPF, com ou sem máscara.</param>
/// <param name="Telefone">Telefone, em E.164 ou no formato nacional com DDD.</param>
public sealed record DadosPessoais(string? NomeCompleto, string? Cpf, string? Telefone);

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
/// <param name="Pessoais">Nome, CPF e telefone.</param>
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
    DadosDeEmergencia ContatoDeEmergencia,
    Guid? FotoArquivoId,
    int Completude,
    IReadOnlyList<string> Faltando,
    bool EssencialPendente
)
{
    /// <summary>O mesmo cadastro com o CPF mascarado — a forma que a comissão recebe.</summary>
    public PerfilDetalhe ComCpfMascarado() => this with { Pessoais = Pessoais with { Cpf = FormatosBrasileiros.MascararCpf(Pessoais.Cpf) } };
}

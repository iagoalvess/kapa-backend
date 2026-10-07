using Backend.Business.Abstractions;
using Backend.Business.Common.Texto;

namespace Backend.Business.Formandos.Models;

/// <summary>
/// Cadastro do formando dentro de uma turma: nome, CPF, contato e foto.
/// </summary>
/// <remarks>
/// É do <b>vínculo</b>, não do usuário: dado de turma mora na turma. O que é da pessoa (nome de
/// exibição, e-mail, senha) continua no <c>Usuario</c>.
/// <para>
/// Só o que alguma função usa (06/10): nome e CPF assinam o termo e saem no convite 1, o telefone é como
/// a comissão fala com a pessoa. Nome no diploma, RG, matrícula, nascimento e endereço saíram — a idade é
/// declarada nos Termos de Uso, não conferida aqui.
/// </para>
/// <para>
/// Nasce vazio e é preenchido aos poucos: cadastro incompleto não bloqueia nada. O que existe é
/// <see cref="Completude"/> e <see cref="EssencialPreenchido"/>, recalculados a cada alteração e
/// gravados em coluna — é o que deixa a lista da comissão filtrar e paginar por completude no
/// banco, sem trazer a turma inteira para a memória.
/// </para>
/// <para>
/// <see cref="Cpf"/> é cifrado com AES-256 no mapeamento. Efeito colateral: não dá para buscar
/// por CPF. <c>ponytail:</c> se um dia precisar, a solução é uma coluna irmã com HMAC
/// determinístico do CPF e índice nela — não trocar a cifra por algo previsível.
/// </para>
/// </remarks>
public class PerfilDoFormando : EntidadeDaFormatura
{
    /// <summary>
    /// Nome e CPF identificam quem assina o termo de adesão; o telefone é como a comissão fala com a
    /// pessoa. Falta um destes, a turma é avisada.
    /// </summary>
    public static readonly IReadOnlyList<string> Essenciais = [ItensDoCadastro.NomeCompleto, ItensDoCadastro.Cpf, ItensDoCadastro.Telefone];

    /// <summary>Vínculo dono do cadastro. Um cadastro por vínculo.</summary>
    public Guid VinculoId { get; init; }

    /// <summary>Nome civil completo, como no documento.</summary>
    public string? NomeCompleto { get; private set; }

    /// <summary>CPF, só os 11 dígitos. Cifrado na coluna.</summary>
    public string? Cpf { get; private set; }

    /// <summary>Telefone em E.164.</summary>
    public string? Telefone { get; private set; }

    /// <summary>Quem avisar numa emergência. Sempre existe; os campos é que começam vazios.</summary>
    public ContatoDeEmergencia ContatoDeEmergencia { get; private set; } = new();

    /// <summary>Foto de rosto, já redimensionada, no módulo de arquivos.</summary>
    public Guid? FotoArquivoId { get; private set; }

    /// <summary>Percentual dos itens do cadastro preenchidos, de 0 a 100.</summary>
    public int Completude { get; private set; }

    /// <summary>Se os <see cref="Essenciais"/> estão todos preenchidos.</summary>
    public bool EssencialPreenchido { get; private set; }

    /// <summary>
    /// Aplica as seções informadas e recalcula a completude.
    /// </summary>
    /// <remarks>
    /// Seção nula fica como está: é o que deixa o formulário salvar uma seção por vez sem apagar
    /// as outras. Dentro de uma seção enviada, campo vazio apaga.
    /// <para>Espera dados já validados — a normalização assume a forma conferida pelo validator.</para>
    /// </remarks>
    /// <param name="dados">Seções a gravar.</param>
    public void Aplicar(AtualizarPerfil dados)
    {
        if (dados.Pessoais is { } pessoais)
        {
            NomeCompleto = Limpar(pessoais.NomeCompleto);
            Cpf = Limpar(pessoais.Cpf) is null ? null : FormatosBrasileiros.SomenteDigitos(pessoais.Cpf);
            Telefone = FormatosBrasileiros.TelefoneE164(pessoais.Telefone);
        }

        if (dados.ContatoDeEmergencia is { } contato)
            ContatoDeEmergencia = ContatoDeEmergencia.De(contato);

        Recalcular();
    }

    /// <summary>
    /// Apaga tudo o que identifica a pessoa, mantendo a linha e o vínculo.
    /// </summary>
    /// <remarks>
    /// É a eliminação da LGPD como ela de fato acontece aqui (Sprint 14): o cadastro fica, vazio,
    /// porque <see cref="VinculoId"/> é o que amarra as parcelas e os recebimentos ao lançamento. A
    /// chave estrangeira sobrevive; o que era dado pessoal vira nulo.
    /// <para>
    /// Irreversível, e por isso idempotente: rodar de novo não tem o que apagar e não falha.
    /// </para>
    /// </remarks>
    /// <returns>A foto que havia, para quem chamou apagar os bytes dela. Nulo se não havia.</returns>
    public Guid? Anonimizar()
    {
        var foto = FotoArquivoId;

        NomeCompleto = null;
        Cpf = null;
        Telefone = null;
        ContatoDeEmergencia = new ContatoDeEmergencia();
        FotoArquivoId = null;

        Recalcular();

        return foto;
    }

    /// <summary>Troca a foto e devolve a anterior, para quem chamou remover o arquivo dela.</summary>
    /// <param name="arquivoId">Arquivo da foto nova.</param>
    public Guid? TrocarFoto(Guid arquivoId)
    {
        var anterior = FotoArquivoId;
        FotoArquivoId = arquivoId;
        Recalcular();

        return anterior;
    }

    /// <summary>Itens do cadastro ainda vazios, pelos nomes de <see cref="ItensDoCadastro"/>.</summary>
    public IReadOnlyList<string> Faltando() => [.. Itens().Where(item => !item.Preenchido).Select(item => item.Nome)];

    /// <summary>Os mesmos dados, na forma que a leitura devolve.</summary>
    public DadosPessoais ParaDadosPessoais() => new(NomeCompleto, Cpf, Telefone);

    private void Recalcular()
    {
        var faltando = Faltando();

        Completude = (ItensDoCadastro.Total - faltando.Count) * 100 / ItensDoCadastro.Total;
        EssencialPreenchido = !faltando.Intersect(Essenciais).Any();
    }

    private IEnumerable<(string Nome, bool Preenchido)> Itens() =>
        [
            (ItensDoCadastro.NomeCompleto, NomeCompleto is not null),
            (ItensDoCadastro.Cpf, Cpf is not null),
            (ItensDoCadastro.Telefone, Telefone is not null),
            (ItensDoCadastro.ContatoDeEmergencia, ContatoDeEmergencia.Completo),
            (ItensDoCadastro.Foto, FotoArquivoId is not null),
        ];

    /// <summary>Texto aparado; vazio vira nulo.</summary>
    internal static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}

/// <summary>
/// Os itens que a completude conta, com o nome que o cliente recebe em <c>faltando</c>.
/// </summary>
/// <remarks>Os nomes são os campos do JSON: o front traduz para rótulo sem tabela de-para.</remarks>
public static class ItensDoCadastro
{
    /// <summary>Nome civil completo.</summary>
    public const string NomeCompleto = "nomeCompleto";

    /// <summary>CPF.</summary>
    public const string Cpf = "cpf";

    /// <summary>Telefone.</summary>
    public const string Telefone = "telefone";

    /// <summary>Contato de emergência, contado como um item quando nome, telefone e parentesco estão preenchidos.</summary>
    public const string ContatoDeEmergencia = "contatoDeEmergencia";

    /// <summary>Foto de rosto.</summary>
    public const string Foto = "foto";

    /// <summary>Quantos itens a completude conta.</summary>
    public const int Total = 5;
}

/// <summary>Contato de emergência do formando, gravado nas colunas <c>contato_de_emergencia_*</c>.</summary>
public class ContatoDeEmergencia
{
    /// <summary>Nome de quem avisar.</summary>
    public string? Nome { get; init; }

    /// <summary>Telefone em E.164.</summary>
    public string? Telefone { get; init; }

    /// <summary>Mãe, pai, cônjuge.</summary>
    public string? Parentesco { get; init; }

    /// <summary>Se dá para ligar e saber para quem.</summary>
    public bool Completo => Nome is not null && Telefone is not null && Parentesco is not null;

    /// <summary>Normaliza a seção recebida.</summary>
    /// <param name="dados">Seção de contato.</param>
    public static ContatoDeEmergencia De(DadosDeEmergencia dados) =>
        new()
        {
            Nome = PerfilDoFormando.Limpar(dados.Nome),
            Telefone = FormatosBrasileiros.TelefoneE164(dados.Telefone),
            Parentesco = PerfilDoFormando.Limpar(dados.Parentesco),
        };

    /// <summary>Os mesmos dados, na forma que a leitura devolve.</summary>
    public DadosDeEmergencia ParaDados() => new(Nome, Telefone, Parentesco);
}

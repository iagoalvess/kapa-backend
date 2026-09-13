using Backend.Business.Abstractions;
using Backend.Business.Common.Texto;

namespace Backend.Business.Formandos.Models;

/// <summary>
/// Cadastro do formando dentro de uma turma: documentos, contato, endereço e foto.
/// </summary>
/// <remarks>
/// É do <b>vínculo</b>, não do usuário. A mesma pessoa tem matrícula diferente em duas turmas e
/// endereço diferente de um ano para o outro — dado de turma mora na turma. O que é da pessoa
/// (nome de exibição, e-mail, senha) continua no <c>Usuario</c>.
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
    /// <summary>O que a comissão precisa para emitir cobrança. Falta um destes, a turma é avisada.</summary>
    public static readonly IReadOnlyList<string> Essenciais = [ItensDoCadastro.NomeCompleto, ItensDoCadastro.Cpf, ItensDoCadastro.Telefone];

    /// <summary>Vínculo dono do cadastro. Um cadastro por vínculo.</summary>
    public Guid VinculoId { get; init; }

    /// <summary>Nome civil completo, como no documento.</summary>
    public string? NomeCompleto { get; private set; }

    /// <summary>Nome como deve sair no diploma e no convite, quando difere do civil.</summary>
    public string? NomeNoDiploma { get; private set; }

    /// <summary>CPF, só os 11 dígitos. Cifrado na coluna.</summary>
    public string? Cpf { get; private set; }

    /// <summary>RG como a pessoa informou — o formato varia por estado.</summary>
    public string? Rg { get; private set; }

    /// <summary>Matrícula na instituição.</summary>
    public string? Matricula { get; private set; }

    /// <summary>Telefone em E.164.</summary>
    public string? Telefone { get; private set; }

    /// <summary>Data de nascimento.</summary>
    public DateOnly? DataDeNascimento { get; private set; }

    /// <summary>Recado livre do formando para a comissão.</summary>
    public string? Observacoes { get; private set; }

    /// <summary>Endereço. Sempre existe; os campos é que começam vazios.</summary>
    public Endereco Endereco { get; private set; } = new();

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
            NomeNoDiploma = Limpar(pessoais.NomeNoDiploma);
            Cpf = Limpar(pessoais.Cpf) is null ? null : FormatosBrasileiros.SomenteDigitos(pessoais.Cpf);
            Rg = Limpar(pessoais.Rg);
            Matricula = Limpar(pessoais.Matricula);
            Telefone = FormatosBrasileiros.TelefoneE164(pessoais.Telefone);
            DataDeNascimento = pessoais.DataDeNascimento;
            Observacoes = Limpar(pessoais.Observacoes);
        }

        if (dados.Endereco is { } endereco)
            Endereco = Endereco.De(endereco);

        if (dados.ContatoDeEmergencia is { } contato)
            ContatoDeEmergencia = ContatoDeEmergencia.De(contato);

        Recalcular();
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
    public DadosPessoais ParaDadosPessoais() => new(NomeCompleto, NomeNoDiploma, Cpf, Rg, Matricula, Telefone, DataDeNascimento, Observacoes);

    private void Recalcular()
    {
        var faltando = Faltando();

        Completude = (ItensDoCadastro.Total - faltando.Count) * 100 / ItensDoCadastro.Total;
        EssencialPreenchido = !faltando.Intersect(Essenciais).Any();
    }

    private IEnumerable<(string Nome, bool Preenchido)> Itens() =>
        [
            (ItensDoCadastro.NomeCompleto, NomeCompleto is not null),
            (ItensDoCadastro.NomeNoDiploma, NomeNoDiploma is not null),
            (ItensDoCadastro.Cpf, Cpf is not null),
            (ItensDoCadastro.Rg, Rg is not null),
            (ItensDoCadastro.Matricula, Matricula is not null),
            (ItensDoCadastro.Telefone, Telefone is not null),
            (ItensDoCadastro.DataDeNascimento, DataDeNascimento is not null),
            (ItensDoCadastro.Endereco, Endereco.Completo),
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

    /// <summary>Nome no diploma.</summary>
    public const string NomeNoDiploma = "nomeNoDiploma";

    /// <summary>CPF.</summary>
    public const string Cpf = "cpf";

    /// <summary>RG.</summary>
    public const string Rg = "rg";

    /// <summary>Matrícula.</summary>
    public const string Matricula = "matricula";

    /// <summary>Telefone.</summary>
    public const string Telefone = "telefone";

    /// <summary>Data de nascimento.</summary>
    public const string DataDeNascimento = "dataDeNascimento";

    /// <summary>Endereço, contado como um item quando CEP, logradouro, número, bairro, cidade e UF estão preenchidos.</summary>
    public const string Endereco = "endereco";

    /// <summary>Contato de emergência, contado como um item quando nome, telefone e parentesco estão preenchidos.</summary>
    public const string ContatoDeEmergencia = "contatoDeEmergencia";

    /// <summary>Foto de rosto.</summary>
    public const string Foto = "foto";

    /// <summary>Quantos itens a completude conta.</summary>
    public const int Total = 10;
}

/// <summary>Endereço do formando, gravado nas colunas <c>endereco_*</c> do perfil.</summary>
public class Endereco
{
    /// <summary>CEP, só os 8 dígitos.</summary>
    public string? Cep { get; init; }

    /// <summary>Rua, avenida.</summary>
    public string? Logradouro { get; init; }

    /// <summary>Número — texto, porque "s/n" e "12A" existem.</summary>
    public string? Numero { get; init; }

    /// <summary>Apartamento, bloco. Opcional: não conta na completude.</summary>
    public string? Complemento { get; init; }

    /// <summary>Bairro.</summary>
    public string? Bairro { get; init; }

    /// <summary>Cidade.</summary>
    public string? Cidade { get; init; }

    /// <summary>Sigla da UF, em maiúsculas.</summary>
    public string? Uf { get; init; }

    /// <summary>Se tem o necessário para uma entrega: tudo menos o complemento.</summary>
    public bool Completo =>
        Cep is not null && Logradouro is not null && Numero is not null && Bairro is not null && Cidade is not null && Uf is not null;

    /// <summary>Normaliza a seção recebida.</summary>
    /// <param name="dados">Seção de endereço.</param>
    public static Endereco De(DadosDeEndereco dados) =>
        new()
        {
            Cep = PerfilDoFormando.Limpar(dados.Cep) is null ? null : FormatosBrasileiros.SomenteDigitos(dados.Cep),
            Logradouro = PerfilDoFormando.Limpar(dados.Logradouro),
            Numero = PerfilDoFormando.Limpar(dados.Numero),
            Complemento = PerfilDoFormando.Limpar(dados.Complemento),
            Bairro = PerfilDoFormando.Limpar(dados.Bairro),
            Cidade = PerfilDoFormando.Limpar(dados.Cidade),
            Uf = PerfilDoFormando.Limpar(dados.Uf)?.ToUpperInvariant(),
        };

    /// <summary>Os mesmos dados, na forma que a leitura devolve.</summary>
    public DadosDeEndereco ParaDados() => new(Cep, Logradouro, Numero, Complemento, Bairro, Cidade, Uf);
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

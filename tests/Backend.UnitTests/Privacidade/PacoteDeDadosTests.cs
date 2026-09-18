using System.IO.Compression;
using System.Text;
using Backend.Business.Formandos.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Privacidade.Models;
using Backend.Business.Privacidade.Services;
using Shouldly;

namespace Backend.UnitTests.Privacidade;

/// <summary>
/// O pacote de portabilidade: o que ele leva, e o que ele não pode deixar escapar.
///
/// É o arquivo que o titular baixa e abre no Excel. Um campo que não entra aqui é um campo que a
/// Kapa guarda e não devolveu — que é exatamente o que o art. 18, V proíbe.
/// </summary>
public sealed class PacoteDeDadosTests
{
    private static readonly DateTime Agora = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Traz_json_csv_e_o_leia_me()
    {
        var nomes = Abrir(Dados()).Keys;

        nomes.ShouldBe(
            ["LEIA-ME.txt", "dados.json", "conta.csv", "turmas.csv", "cadastro.csv", "parcelas.csv", "consentimentos.csv", "comunicacoes.csv"],
            ignoreOrder: true
        );
    }

    [Fact]
    public void Cadastro_leva_o_cpf_e_o_endereco_do_titular()
    {
        var cadastro = Abrir(Dados())["cadastro.csv"];

        cadastro.ShouldContain("12345678901");
        cadastro.ShouldContain("Rua das Flores");
        cadastro.ShouldContain("Medicina 2027");
    }

    [Fact]
    public void Json_leva_a_estrutura_inteira()
    {
        var json = Abrir(Dados())["dados.json"];

        json.ShouldContain("\"nomeCompleto\": \"Ana Souza\"");
        json.ShouldContain("\"totalEmCentavos\": 840000");
    }

    /// <summary>O Excel em português abre com ponto e vírgula, e o BOM impede "JosÃ©".</summary>
    [Fact]
    public void Csv_usa_ponto_e_virgula_e_bom()
    {
        var bytes = Bytes(Dados(), "conta.csv");

        bytes[..3].ShouldBe(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(bytes).ShouldContain("campo;valor");
    }

    /// <summary>
    /// Injeção de fórmula em CSV: o titular escreve a observação, e é ele mesmo quem abre o arquivo.
    /// </summary>
    [Fact]
    public void Observacao_que_comeca_com_igual_nao_vira_formula()
    {
        var cadastro = Abrir(Dados(observacoes: "=1+1"))["cadastro.csv"];

        cadastro.ShouldContain("'=1+1");
    }

    [Fact]
    public void Valor_com_ponto_e_virgula_vai_entre_aspas()
    {
        var cadastro = Abrir(Dados(observacoes: "moro com a mãe; ligar à noite"))["cadastro.csv"];

        cadastro.ShouldContain("\"moro com a mãe; ligar à noite\"");
    }

    /// <summary>A exportação sai do vínculo do titular — nenhum outro nome pode aparecer nela.</summary>
    [Fact]
    public void Nao_leva_dado_de_terceiro()
    {
        var pacote = Abrir(Dados());

        foreach (var (nome, conteudo) in pacote)
            conteudo.ShouldNotContain("Bruno Lima", customMessage: $"{nome} vazou o nome de outro titular.");
    }

    private static Dictionary<string, string> Abrir(MeusDados dados)
    {
        using var memoria = new MemoryStream(PacoteDeDados.Gerar(dados, Agora));
        using var zip = new ZipArchive(memoria, ZipArchiveMode.Read);

        return zip.Entries.ToDictionary(entrada => entrada.FullName, Ler, StringComparer.Ordinal);
    }

    private static byte[] Bytes(MeusDados dados, string arquivo)
    {
        using var memoria = new MemoryStream(PacoteDeDados.Gerar(dados, Agora));
        using var zip = new ZipArchive(memoria, ZipArchiveMode.Read);
        using var conteudo = zip.GetEntry(arquivo)!.Open();
        using var destino = new MemoryStream();

        conteudo.CopyTo(destino);

        return destino.ToArray();
    }

    private static string Ler(ZipArchiveEntry entrada)
    {
        using var fluxo = entrada.Open();
        using var leitor = new StreamReader(fluxo, Encoding.UTF8);

        return leitor.ReadToEnd();
    }

    /// <summary>Um titular com uma turma, uma parcela, um consentimento e uma preferência.</summary>
    private static MeusDados Dados(string? observacoes = null)
    {
        var formaturaId = Guid.CreateVersion7();

        return new MeusDados(
            new DadosDaConta(Guid.CreateVersion7(), "Ana Souza", "ana@exemplo.com", true, "+5541999990000", Agora, null),
            [
                new MeusDadosDaTurma(
                    formaturaId,
                    "Medicina 2027",
                    "UFPR",
                    "Formando",
                    true,
                    new PerfilExportado(
                        "Ana Souza",
                        null,
                        "12345678901",
                        "1234567",
                        "2021001",
                        "+5541999990000",
                        new DateOnly(2000, 3, 14),
                        observacoes,
                        new DadosDeEndereco("80000000", "Rua das Flores", "100", null, "Centro", "Curitiba", "PR"),
                        new DadosDeEmergencia("Marta Souza", "+5541988880000", "mãe"),
                        true,
                        90
                    ),
                    new MeuFinanceiro(
                        840000,
                        140000,
                        [
                            new MinhaParcela(
                                Guid.CreateVersion7(),
                                "Mensalidade",
                                1,
                                new DateOnly(2026, 10, 10),
                                70000,
                                "Paga",
                                70000,
                                new DateOnly(2026, 10, 9)
                            ),
                        ]
                    ),
                    new MinhaAdesao(2, Agora, "203.0.113.7")
                ),
            ],
            [new ConsentimentoDoUsuario(Guid.CreateVersion7(), TipoDeDocumento.PoliticaDePrivacidade, "1", Agora, false)],
            new MinhasComunicacoes([new MinhaPreferencia(formaturaId, "ParcelaAVencer", true)], 3, Agora)
        );
    }
}

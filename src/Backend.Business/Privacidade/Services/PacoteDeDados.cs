using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Business.Marketing.Models;
using Backend.Business.Privacidade.Models;

namespace Backend.Business.Privacidade.Services;

/// <summary>
/// O pacote de portabilidade: um ZIP com os mesmos dados em JSON e em CSV.
/// </summary>
/// <remarks>
/// Dois formatos porque servem a duas pessoas. O <c>dados.json</c> é a estrutura inteira, com o
/// aninhamento preservado, e é o que outro sistema consegue ler (art. 18, V). Os <c>.csv</c> são
/// planos, abrem no Excel com dois cliques, e é o que o titular de fato olha.
/// <para>
/// Um ZIP e não sete anexos: o pacote é um arquivo só no módulo de arquivos, com um prazo só e uma
/// remoção só. Sete linhas em <c>arquivos</c> por exportação seriam sete chances de sobrar órfão.
/// </para>
/// <para>
/// <b>CSV com ponto e vírgula e BOM.</b> É o que o Excel em português abre sem a caixa de diálogo de
/// importação e sem transformar "José" em "JosÃ©". O JSON, que é o formato interoperável de verdade,
/// continua UTF-8 puro e separado — a concessão ao Excel não contamina o que a lei pede.
/// </para>
/// </remarks>
public static class PacoteDeDados
{
    /// <summary>Extensão e tipo do pacote gerado.</summary>
    public const string Extensao = ".zip";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>Os caracteres que obrigam a célula a ir entre aspas.</summary>
    private static readonly SearchValues<char> Especiais = SearchValues.Create([';', '"', '\n', '\r']);

    /// <summary>Monta o ZIP a partir do que a tela "Meus dados" mostra.</summary>
    /// <remarks>
    /// Recebe o mesmo objeto que a tela recebe, de propósito: exportação montada por uma consulta
    /// própria é exportação que, um dia, deixa de trazer o campo que a tela passou a mostrar.
    /// </remarks>
    /// <param name="dados">Tudo o que a Kapa guarda sobre o titular.</param>
    /// <param name="geradoEm">Momento da geração, em UTC, impresso no leia-me.</param>
    public static byte[] Gerar(MeusDados dados, DateTime geradoEm)
    {
        using var memoria = new MemoryStream();

        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Gravar(zip, "LEIA-ME.txt", Encoding.UTF8.GetBytes(LeiaMe(dados, geradoEm)));
            Gravar(zip, "dados.json", JsonSerializer.SerializeToUtf8Bytes(dados, Json));
            Gravar(zip, "conta.csv", Csv(["campo", "valor"], Conta(dados.Conta)));
            Gravar(zip, "turmas.csv", Csv(["turma", "instituicao", "papel", "vinculo_ativo", "aderiu_em", "total", "pago"], Turmas(dados)));
            Gravar(zip, "cadastro.csv", Csv(CabecalhoDoCadastro, Cadastros(dados)));
            Gravar(
                zip,
                "parcelas.csv",
                Csv(["turma", "item", "numero", "vencimento", "valor", "situacao", "valor_pago", "pago_em"], Parcelas(dados))
            );
            Gravar(zip, "consentimentos.csv", Csv(["documento", "versao", "registrado_em", "revogacao"], Consentimentos(dados)));
            Gravar(zip, "novidades-do-kapa.csv", Csv(["registro", "detalhe", "em"], NovidadesDoKapa(dados.Comunicacoes.DoKapa)));
        }

        return memoria.ToArray();
    }

    private static readonly string[] CabecalhoDoCadastro =
    [
        "turma",
        "nome_completo",
        "cpf",
        "telefone",
        "emergencia_nome",
        "emergencia_telefone",
        "emergencia_parentesco",
        "tem_foto",
    ];

    private static IEnumerable<string[]> Conta(DadosDaConta conta) =>
        [
            ["identificador", conta.Id.ToString()],
            ["nome", conta.Nome],
            ["e-mail", conta.Email],
            ["e-mail confirmado", Sim(conta.EmailConfirmado)],
            ["telefone", conta.Telefone ?? string.Empty],
            ["conta criada em", DataHora(conta.CriadoEm)],
            ["anonimizada em", conta.AnonimizadoEm is { } quando ? DataHora(quando) : string.Empty],
        ];

    private static IEnumerable<string[]> Turmas(MeusDados dados) =>
        dados.Turmas.Select(turma =>
            (string[])
                [
                    turma.Formatura,
                    turma.Instituicao,
                    turma.Papel,
                    Sim(turma.Ativo),
                    turma.Adesao is { } adesao ? DataHora(adesao.AceitoEm) : string.Empty,
                    Reais(turma.Financeiro.TotalEmCentavos),
                    Reais(turma.Financeiro.PagoEmCentavos),
                ]
        );

    private static IEnumerable<string[]> Cadastros(MeusDados dados) =>
        dados
            .Turmas.Where(turma => turma.Perfil is not null)
            .Select(turma =>
            {
                var perfil = turma.Perfil!;

                return (string[])
                    [
                        turma.Formatura,
                        perfil.NomeCompleto ?? string.Empty,
                        perfil.Cpf ?? string.Empty,
                        perfil.Telefone ?? string.Empty,
                        perfil.ContatoDeEmergencia.Nome ?? string.Empty,
                        perfil.ContatoDeEmergencia.Telefone ?? string.Empty,
                        perfil.ContatoDeEmergencia.Parentesco ?? string.Empty,
                        Sim(perfil.TemFoto),
                    ];
            });

    private static IEnumerable<string[]> Parcelas(MeusDados dados) =>
        dados.Turmas.SelectMany(turma =>
            turma.Financeiro.Parcelas.Select(parcela =>
                (string[])
                    [
                        turma.Formatura,
                        parcela.Item,
                        parcela.Numero.ToString(CultureInfo.InvariantCulture),
                        Dia(parcela.Vencimento),
                        Reais(parcela.ValorOriginalEmCentavos),
                        parcela.Status,
                        parcela.ValorPagoEmCentavos is { } pago ? Reais(pago) : string.Empty,
                        parcela.PagoEm is { } dia ? Dia(dia) : string.Empty,
                    ]
            )
        );

    private static IEnumerable<string[]> Consentimentos(MeusDados dados) =>
        dados.Consentimentos.Select(consentimento =>
            (string[])[consentimento.Tipo, consentimento.Versao, DataHora(consentimento.AceitoEm), consentimento.Revogado ? "revogação" : "aceite"]
        );

    /// <summary>A preferência de marketing: o estado atual, cada aceite e oposição, e cada e-mail mandado (Sprint 40).</summary>
    /// <param name="doKapa">Preferência, histórico e envios.</param>
    private static IEnumerable<string[]> NovidadesDoKapa(ComunicacaoDoKapa doKapa) =>
        [
            ["preferência atual", doKapa.Receber ? "recebe" : "não recebe", string.Empty],
            .. doKapa.Historico.Select(registro =>
                (string[])
                    [
                        registro.Aceito ? "aceite" : "oposição",
                        $"{registro.Origem} (texto versão {registro.VersaoDoTexto})",
                        DataHora(registro.RegistradoEm),
                    ]
            ),
            .. doKapa.Envios.Select(envio => (string[])["e-mail enviado", $"{envio.Jornada} — {envio.Formatura}", DataHora(envio.EnviadoEm)]),
        ];

    /// <summary>
    /// O texto que explica o pacote a quem o abriu.
    /// </summary>
    /// <remarks>
    /// Um ZIP com sete arquivos e nenhuma explicação é o mesmo que não responder ao pedido de acesso:
    /// o direito é de <b>entender</b> o que está guardado, não de receber os bytes.
    /// </remarks>
    /// <param name="dados">O conteúdo do pacote.</param>
    /// <param name="geradoEm">Momento da geração, em UTC.</param>
    private static string LeiaMe(MeusDados dados, DateTime geradoEm) =>
        $"""
            Seus dados na Kapa
            ==================

            Pacote gerado em {DataHora(geradoEm)} (UTC) para {dados.Conta.Email}.

            O que tem aqui
            --------------
            dados.json          Tudo, com a estrutura preservada. É o formato para levar a outro sistema.
            conta.csv           Sua conta: nome, e-mail, telefone e quando ela foi criada.
            turmas.csv          Uma linha por turma sua, com o total cobrado e o total pago.
            cadastro.csv        Seu cadastro em cada turma: documentos, endereço e contato de emergência.
            parcelas.csv        Uma linha por parcela, com vencimento, valor e situação.
            consentimentos.csv  Cada aceite e cada revogação de Termos de Uso e Política de Privacidade.
            novidades-do-kapa.csv  Se você recebe as novidades do Kapa, quando aceitou ou saiu, e o que já mandamos.

            Os arquivos .csv usam ponto e vírgula e abrem direto no Excel.

            O que NÃO está aqui
            -------------------
            Dados de outras pessoas, mesmo da sua turma. Este pacote é só seu.
            Os documentos e avisos publicados pela comissão, que são da turma e não do titular.
            Seu histórico de acesso e os registros técnicos de segurança, guardados por prazo próprio.

            Dúvidas
            -------
            A Política de Privacidade explica por que cada dado existe e por quanto tempo fica.
            Você pode pedir correção, revogar um consentimento ou pedir eliminação na tela Meus dados.
            """;

    private static void Gravar(ZipArchive zip, string nome, byte[] conteudo)
    {
        using var fluxo = zip.CreateEntry(nome, CompressionLevel.Optimal).Open();

        fluxo.Write(conteudo);
    }

    /// <summary>Uma tabela em CSV, com BOM e ponto e vírgula.</summary>
    /// <param name="cabecalho">Nomes das colunas.</param>
    /// <param name="linhas">Linhas, já como texto.</param>
    private static byte[] Csv(string[] cabecalho, IEnumerable<string[]> linhas)
    {
        var texto = new StringBuilder();

        texto.AppendLine(string.Join(';', cabecalho));

        foreach (var linha in linhas)
            texto.AppendLine(string.Join(';', linha.Select(Escapar)));

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(texto.ToString())).ToArray();
    }

    /// <summary>
    /// Um valor pronto para entrar numa célula.
    /// </summary>
    /// <remarks>
    /// Além das aspas do CSV, prefixa com apóstrofo o valor que começa com <c>= + - @</c>: é o que
    /// impede uma observação começada por <c>=</c> de virar fórmula quando o titular abrir o arquivo
    /// no Excel. O dado é escrito pelo próprio titular, mas o arquivo é aberto por ele — e injeção de
    /// fórmula em CSV não precisa de um atacante para estragar o conteúdo.
    /// </remarks>
    /// <param name="valor">Texto cru.</param>
    private static string Escapar(string valor)
    {
        var seguro = valor.Length > 0 && valor[0] is '=' or '+' or '-' or '@' ? $"'{valor}" : valor;

        return seguro.AsSpan().ContainsAny(Especiais) ? $"\"{seguro.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : seguro;
    }

    private static string Sim(bool valor) => valor ? "sim" : "não";

    private static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", PtBr);

    private static string DataHora(DateTime instante) => instante.ToString("dd/MM/yyyy HH:mm", PtBr);

    private static string Reais(long centavos) => (centavos / 100m).ToString("N2", PtBr);
}

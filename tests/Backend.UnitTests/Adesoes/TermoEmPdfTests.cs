using System.Globalization;
using System.Text;
using Backend.Business.Adesoes.Models;
using Backend.Business.Adesoes.Services;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Pdf;
using Shouldly;

namespace Backend.UnitTests.Adesoes;

/// <summary>
/// O que o termo aceito precisa garantir sem banco: snapshot estável, hash que muda com o conteúdo e
/// PDF igual hoje e daqui a um ano.
/// </summary>
public sealed class TermoEmPdfTests
{
    private static SnapshotDoPlano Snapshot()
    {
        var plano = new PlanoDeCobranca
        {
            Nome = "Plano 2027",
            PercentualDeMulta = 200,
            PercentualDeJurosAoMes = 100,
        };
        plano.Itens.Add(ItemDeCobranca.Novo(plano.Id, new DadosDoItem(TipoDeCobranca.Mensalidade, null, 840_000, 24, 10, new DateOnly(2027, 3, 1))));
        plano.Vigorar(DateTime.UtcNow);

        return SnapshotDoPlano.De(plano);
    }

    private static AdesaoComTermo Adesao(string cpf = "52998224725") =>
        new(
            new AdesaoDoFormando
            {
                Versao = 2,
                HashDoConteudo = new string('f', 64),
                AceitoEm = new DateTime(2026, 9, 14, 13, 32, 5, DateTimeKind.Utc),
                EnderecoIp = "203.0.113.7",
                UserAgent = "Navegador de teste",
                NomeCompleto = "Ana Souza",
                Cpf = cpf,
                PlanoAceito = Snapshot().ParaJson(),
            },
            "# Do objeto\n\nA **turma** contrata a formatura.\n\n- Cláusula com acentuação: ção, ã, é.\n1. Primeira cláusula"
        );

    /// <summary>Em texto: o conteúdo do PDF não é comprimido, e o que não é ASCII vai em octal.</summary>
    private static string Texto(byte[] pdf) => Encoding.ASCII.GetString(pdf);

    [Fact]
    public void Snapshot_volta_igual_do_json_e_ignora_campo_desconhecido()
    {
        var snapshot = Snapshot();
        var json = snapshot.ParaJson();

        var lido = SnapshotDoPlano.Ler(json.Replace("{\"versaoDoEsquema\"", "{\"campoDoFuturo\":1,\"versaoDoEsquema\"", StringComparison.Ordinal));

        lido.ParaJson().ShouldBe(json);
        lido.Parcelas.Count.ShouldBe(24);
        lido.TotalEmCentavos.ShouldBe(840_000);
        json.ShouldContain("\"tipo\":\"Mensalidade\"");
    }

    [Fact]
    public void Hash_muda_com_o_termo_e_com_o_plano()
    {
        var snapshot = Snapshot();
        var hash = AdesaoDoFormando.CalcularHash("Termo", snapshot.ParaJson());

        hash.ShouldMatch("^[0-9a-f]{64}$");
        AdesaoDoFormando.CalcularHash("Termo", SnapshotDoPlano.Ler(snapshot.ParaJson()).ParaJson()).ShouldBe(hash);
        AdesaoDoFormando.CalcularHash("Termo.", snapshot.ParaJson()).ShouldNotBe(hash);
        AdesaoDoFormando.CalcularHash("Termo", (snapshot with { PercentualDeMulta = 300 }).ParaJson()).ShouldNotBe(hash);
    }

    [Fact]
    public void Mesma_adesao_gera_os_mesmos_bytes()
    {
        var adesao = Adesao();

        TermoEmPdf.Gerar(adesao, mascararCpf: false).ShouldBe(TermoEmPdf.Gerar(adesao, mascararCpf: false));
    }

    [Fact]
    public void Pdf_traz_o_resumo_o_termo_e_o_registro_do_aceite()
    {
        var texto = Texto(TermoEmPdf.Gerar(Adesao(), mascararCpf: false));

        texto.ShouldStartWith("%PDF-1.4");
        texto.ShouldEndWith("%%EOF\n");
        texto.ShouldContain("(Total: R$ 8.400,00 em 24 parcelas.)");
        texto.ShouldContain("multa de 2% e juros de 1% ao m\\352s");
        texto.ShouldContain("(Do objeto)");
        texto.ShouldContain("(A turma contrata a formatura.)");
        texto.ShouldContain("(1. Primeira cl\\341usula)");
        texto.ShouldContain("(CPF: 529.982.247-25)");
        texto.ShouldContain("14/09/2026 \\340s 10:32:05 \\(hor\\341rio de Bras\\355lia\\)");
    }

    [Fact]
    public void Gestao_recebe_o_cpf_mascarado()
    {
        Texto(TermoEmPdf.Gerar(Adesao(), mascararCpf: true)).ShouldContain("(CPF: ***.982.247-**)");
    }

    /// <summary>A tabela de posições aponta para o início de cada objeto — é o que o leitor usa para abrir o arquivo.</summary>
    [Fact]
    public void Tabela_de_posicoes_aponta_para_cada_objeto_e_texto_longo_quebra_pagina()
    {
        var pdf = new DocumentoPdf();
        for (var i = 0; i < 120; i++)
            pdf.Paragrafo($"Parágrafo {i} com texto suficiente para ocupar uma linha inteira da página e quebrar na seguinte, se preciso.");

        var texto = Texto(pdf.Gerar("Rodapé"));
        var xref = texto.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var posicoes = texto[xref..].Split('\n').Skip(3).TakeWhile(linha => linha.EndsWith(" n ", StringComparison.Ordinal)).ToList();

        texto.ShouldContain("(P\\341gina 1 de ");
        texto.ShouldContain("/Count ");
        posicoes.Count.ShouldBeGreaterThan(6);
        for (var i = 0; i < posicoes.Count; i++)
            texto[int.Parse(posicoes[i][..10], CultureInfo.InvariantCulture)..].ShouldStartWith($"{i + 1} 0 obj");
        texto[(texto.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].ShouldStartWith(xref.ToString(CultureInfo.InvariantCulture));
    }
}

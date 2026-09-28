using System.Text;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Festa.Services;
using Backend.Business.Festa.Settings;
using Backend.Business.Festa.Validators;
using Backend.Business.Formaturas.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Festa;

/// <summary>
/// O que o convite da festa decide sem banco: código, assinatura, horários, titular e o PDF.
/// </summary>
public sealed class ConviteDaFestaTests
{
    private const string Segredo = "ZmVzdGEtZGUtdGVzdGUtY29tLTMyLWJ5dGVzLW91LW1haXM=";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CodigoDoConvite Codigos(string segredo = Segredo) => new(Options.Create(new ConviteSettings { SegredoDoConvite = segredo }));

    // ---- Código e assinatura (decisões 4 e 5) ----

    [Fact]
    public void Alfabeto_nao_tem_caracteres_que_se_confundem()
    {
        CodigoDoConvite.Alfabeto.ShouldNotContain('0');
        CodigoDoConvite.Alfabeto.ShouldNotContain('O');
        CodigoDoConvite.Alfabeto.ShouldNotContain('1');
        CodigoDoConvite.Alfabeto.ShouldNotContain('I');
        CodigoDoConvite.Alfabeto.ShouldNotContain('L');
        CodigoDoConvite.Alfabeto.Distinct().Count().ShouldBe(CodigoDoConvite.Alfabeto.Length);
    }

    /// <summary>Mil sorteios: todos no alfabeto, e nenhuma sequência — sequencial seria convite inventado por quem sabe contar.</summary>
    [Fact]
    public void Sorteio_usa_so_o_alfabeto_e_nao_e_sequencial()
    {
        var codigos = Enumerable.Range(0, 1000).Select(_ => CodigoDoConvite.Sortear("MED27")).ToList();

        codigos.ShouldAllBe(codigo => codigo.StartsWith("MED27-") && codigo.Length == 10);
        codigos.SelectMany(codigo => codigo[6..]).ShouldAllBe(caractere => CodigoDoConvite.Alfabeto.Contains(caractere));
        codigos.Distinct().Count().ShouldBeGreaterThan(990);
        codigos.Zip(codigos.Skip(1)).Count(par => string.CompareOrdinal(par.First, par.Second) < 0).ShouldBeInRange(350, 650);
    }

    [Theory]
    [InlineData("Medicina", 2027, "MED27")]
    [InlineData("Ódontologia", 2031, "ODO31")]
    [InlineData("TI", 2026, "TIX26")]
    public void Prefixo_sai_do_curso_e_do_ano(string curso, int ano, string esperado) => CodigoDoConvite.Prefixo(curso, ano).ShouldBe(esperado);

    [Fact]
    public void Token_confere_e_devolve_o_codigo()
    {
        var codigos = Codigos();
        var token = codigos.Token("MED27-7QK4");

        token.Length.ShouldBe("MED27-7QK4".Length + 1 + CodigoDoConvite.TamanhoDaAssinatura);
        codigos.Conferir(token).ShouldBe("MED27-7QK4");
        codigos.Conferir(token.ToLowerInvariant()).ShouldBe("MED27-7QK4");
    }

    [Fact]
    public void Assinatura_adulterada_ou_ausente_nao_confere()
    {
        var codigos = Codigos();
        var token = codigos.Token("MED27-7QK4");
        var outroCodigo = "MED27-7QK5" + token[10..];

        codigos.Conferir(token[..^1] + (token[^1] == 'A' ? 'B' : 'A')).ShouldBeNull();
        codigos.Conferir(outroCodigo).ShouldBeNull();
        codigos.Conferir("MED27-7QK4").ShouldBeNull();
        codigos.Conferir("").ShouldBeNull();
    }

    /// <summary>O segredo é configuração, não sorteio: outro processo com o mesmo segredo confere o mesmo convite.</summary>
    [Fact]
    public void Convite_sobrevive_ao_reinicio_e_nao_a_troca_do_segredo()
    {
        var token = Codigos().Token("MED27-7QK4");

        Codigos().Conferir(token).ShouldBe("MED27-7QK4");
        Codigos(Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('x', 32)))).Conferir(token).ShouldBeNull();
    }

    [Fact]
    public void Portaria_aceita_codigo_digitado_mas_nao_token_adulterado()
    {
        var codigos = Codigos();
        var token = codigos.Token("MED27-7QK4");

        codigos.ParaPortaria(" med27-7qk4 ").ShouldBe("MED27-7QK4");
        codigos.ParaPortaria(token).ShouldBe("MED27-7QK4");
        codigos.ParaPortaria(token[..^1] + (token[^1] == 'A' ? 'B' : 'A')).ShouldBeNull();
    }

    [Fact]
    public void Sem_segredo_valido_o_codigo_nao_assina()
    {
        Should.Throw<InvalidOperationException>(() => Codigos("curto").Token("MED27-7QK4"));
        new ConviteSettings { SegredoDoConvite = Segredo }
            .SegredoValido()
            .ShouldBeTrue();
    }

    /// <summary>Decisão 5: token adulterado é recusado antes de qualquer consulta.</summary>
    [Fact]
    public async Task Token_adulterado_e_recusado_sem_consultar_o_banco()
    {
        var convites = Substitute.For<IConviteDoEventoRepository>();
        var codigos = Codigos();
        var portaria = new Portaria(
            convites,
            Substitute.For<IEventoDaTurmaRepository>(),
            Substitute.For<IFormaturaRepository>(),
            Substitute.For<IFormaturaAtual>(),
            codigos,
            Substitute.For<IEventoRepository>(),
            Substitute.For<IUnitOfWork>(),
            NullLogger<Portaria>.Instance
        );
        var token = codigos.Token("MED27-7QK4");

        var resultado = await portaria.ValidarEntrada(token[..^1] + (token[^1] == 'A' ? 'B' : 'A'), null, Guid.NewGuid(), Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("festa.convite_nao_encontrado");
        convites.ReceivedCalls().ShouldBeEmpty();
    }

    // ---- Horários (P5 e P7) ----

    [Fact]
    public void Janela_vai_de_seis_horas_antes_a_doze_depois_e_a_lista_fecha_24_horas_antes()
    {
        var evento = new EventoDoConvite(Guid.NewGuid(), TipoDeEvento.Festa, "Festa", new DateOnly(2027, 12, 11), new TimeOnly(22, 0), "Salão");
        var inicio = DataUtils.ParaUtc(new DateTime(2027, 12, 11, 22, 0, 0));

        evento.JanelaAberta(inicio.AddHours(-6).AddMinutes(-1)).ShouldBeFalse();
        evento.JanelaAberta(inicio.AddHours(-6)).ShouldBeTrue();
        evento.JanelaAberta(inicio.AddHours(12)).ShouldBeTrue();
        evento.JanelaAberta(inicio.AddHours(12).AddMinutes(1)).ShouldBeFalse();
        evento.ListaAberta(inicio.AddHours(-24).AddMinutes(-1)).ShouldBeTrue();
        evento.ListaAberta(inicio.AddHours(-24)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(22, "Salão", true)]
    [InlineData(null, "Salão", false)]
    [InlineData(22, " ", false)]
    public void Evento_sem_hora_ou_local_nao_imprime_convite(int? hora, string local, bool completo) =>
        new EventoDoConvite(
            Guid.NewGuid(),
            TipoDeEvento.Festa,
            "Festa",
            new DateOnly(2027, 12, 11),
            hora is { } h ? new TimeOnly(h, 0) : null,
            local
        ).Completo.ShouldBe(completo);

    // ---- Titular (P5.1 e decisão 17) ----

    [Theory]
    [InlineData(TipoDeDocumento.Cpf, "111.111.111-12", false)]
    [InlineData(TipoDeDocumento.Cpf, "529.982.247-25", true)]
    [InlineData(TipoDeDocumento.Rg, "12.345.678-9", true)]
    [InlineData(TipoDeDocumento.Rg, "12-3", false)]
    [InlineData(null, "529.982.247-25", false)]
    public void Documento_e_conferido_pelo_tipo(TipoDeDocumento? tipo, string numero, bool valido) =>
        new DadosDoConvidadoValidator().Validate(new DadosDoConvidado("Maria", tipo, numero, null)).IsValid.ShouldBe(valido);

    [Fact]
    public void Documento_sai_mascarado_e_so_a_lista_o_mostra_inteiro()
    {
        DocumentoDoConvidado.Mascarar(TipoDeDocumento.Rg, "123456789").ShouldBe("RG ••••6789");
        DocumentoDoConvidado.Mascarar(TipoDeDocumento.Cpf, "52998224725").ShouldBe("CPF ••••4725");
        DocumentoDoConvidado.Inteiro(TipoDeDocumento.Cpf, "52998224725").ShouldBe("CPF 529.982.247-25");
        DocumentoDoConvidado.Normalizar(TipoDeDocumento.Rg, "12.345.678-x").ShouldBe("12345678X");
    }

    [Fact]
    public void Primeira_nomeacao_nao_e_transferencia_e_trocar_o_nome_e()
    {
        var convite = ConviteDoEvento.Cortesia(Guid.NewGuid(), "MED27-7QK4", new DadosDoConvidado("Maria", null, null, null));
        var aDefinir = new ConviteDoEvento();

        aDefinir.TrocaDeTitular(new DadosDoConvidado("Maria", TipoDeDocumento.Rg, "1234567", null)).ShouldBeFalse();
        convite.TrocaDeTitular(new DadosDoConvidado("maria ", TipoDeDocumento.Rg, "1234567", null)).ShouldBeFalse();
        convite.TrocaDeTitular(new DadosDoConvidado("João", null, null, null)).ShouldBeTrue();
    }

    [Fact]
    public void Revogar_de_novo_nao_muda_o_motivo()
    {
        var convite = ConviteDoEvento.Cortesia(Guid.NewGuid(), "MED27-7QK4", new DadosDoConvidado("Maria", null, null, null));

        convite.Revogar("pagamento estornado", DateTime.UtcNow).ShouldBeTrue();
        convite.Revogar("outro", DateTime.UtcNow).ShouldBeFalse();
        convite.MotivoDaRevogacao.ShouldBe("pagamento estornado");
    }

    // ---- PDF (decisões 9 e 10) ----

    [Fact]
    public void Pdf_do_convite_e_deterministico_e_traz_o_qr_como_vetor()
    {
        var convite = new ConvitePublico(
            "Medicina 2027",
            "UFPR",
            new EventoDoConvite(
                Guid.NewGuid(),
                TipoDeEvento.Festa,
                "Festa de formatura",
                new DateOnly(2027, 12, 11),
                new TimeOnly(22, 0),
                "Espaço Vitrália"
            ),
            "MED27-7QK4",
            "MED27-7QK4-ABCDEFGH",
            "Maria Silva",
            "RG ••••6789"
        );

        var pdf = ConviteEmPdf.Gerar(convite, "https://kapa.app/ingresso/MED27-7QK4-ABCDEFGH");
        var texto = Encoding.Latin1.GetString(pdf);

        texto.ShouldStartWith("%PDF-1.4");
        texto.ShouldContain("(MED27-7QK4) Tj");
        texto.ShouldContain(" re\n");
        ConviteEmPdf.Gerar(convite, "https://kapa.app/ingresso/MED27-7QK4-ABCDEFGH").ShouldBe(pdf);
    }

    /// <summary>A lista exportada traz o documento inteiro de quem vale — e nenhum de quem deixou de ser convidado.</summary>
    [Fact]
    public void Lista_da_portaria_nao_imprime_o_documento_do_convite_revogado()
    {
        var evento = new EventoDoConvite(Guid.NewGuid(), TipoDeEvento.Festa, "Festa", new DateOnly(2027, 12, 11), new TimeOnly(22, 0), "Salão");
        var valido = ConviteDoEvento.Cortesia(evento.Id, "MED27-AAAA", new DadosDoConvidado("Carlos", TipoDeDocumento.Rg, "7654321", null));
        var transferido = ConviteDoEvento.Cortesia(evento.Id, "MED27-BBBB", new DadosDoConvidado("Maria", TipoDeDocumento.Cpf, "52998224725", null));
        transferido.Revogar("transferido para outro convidado", DateTime.UtcNow);
        var linhas = new[] { new ConviteGravadoNaPortaria(valido, null, null, false), new ConviteGravadoNaPortaria(transferido, null, null, false) };

        var texto = Encoding.Latin1.GetString(ConviteEmPdf.Lista("Medicina 2027", evento, linhas, Portaria.ParaPortaria));

        texto.ShouldContain("RG 7654321");
        texto.ShouldNotContain("529.982.247-25");
        texto.ShouldContain("(Revogado:");
    }

    [Fact]
    public void Matriz_do_qr_e_quadrada_e_tem_a_margem_clara()
    {
        var modulos = ConviteEmPdf.Modulos("https://kapa.app/ingresso/MED27-7QK4-ABCDEFGH");

        modulos.ShouldAllBe(linha => linha.Count == modulos.Count);
        modulos[0].ShouldAllBe(modulo => !modulo);
        modulos.SelectMany(linha => linha).Count(modulo => modulo).ShouldBeGreaterThan(100);
    }
}

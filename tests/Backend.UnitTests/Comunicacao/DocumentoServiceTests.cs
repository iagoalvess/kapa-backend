using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Comunicacao.Services;
using Backend.Business.Comunicacao.Validators;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Comunicacao;

/// <summary>
/// As regras do acervo que não dependem do banco: extensão e teto de tamanho antes de gravar, a
/// substituição audita e só apaga o antigo depois do commit, e o download só emite a URL para o que
/// o papel pode ver.
/// </summary>
/// <remarks>
/// Os primeiros bytes contra a extensão são conferidos pelo <c>ArquivoService</c>, por onde todo
/// envio passa — a garantia está em <c>ArquivoServiceTests</c>.
/// </remarks>
public sealed class DocumentoServiceTests
{
    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid UsuarioId = Guid.CreateVersion7();
    private static readonly Guid NovoArquivoId = Guid.CreateVersion7();

    private static readonly byte[] Pdf = "%PDF-1.7\n%âãÏÓ\n1 0 obj"u8.ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IDocumentoRepository _documentos = Substitute.For<IDocumentoRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IArquivoService _arquivos = Substitute.For<IArquivoService>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public DocumentoServiceTests()
    {
        _vinculos.ObterPapelAtivo(UsuarioId, FormaturaId, Arg.Any<CancellationToken>()).Returns(PapelNaFormatura.Comissao);
        _arquivos
            .Enviar(Arg.Any<NovoArquivo>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Ok(new ArquivoResumo(NovoArquivoId, "contrato.pdf", "application/pdf", Pdf.Length, "documentos", UsuarioId, DateTime.UtcNow))
            );
        _arquivos.Remover(Arg.Any<Guid>(), Arg.Any<SolicitanteDeArquivo>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
        _documentos.Obter(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(chamada => Resumo(chamada.Arg<Guid>()));
    }

    private DocumentoService Servico =>
        new(_documentos, _vinculos, _arquivos, _eventos, new DadosDoDocumentoValidator(), _unitOfWork, NullLogger<DocumentoService>.Instance);

    private static DadosDoDocumento Dados() => new("Contrato do buffet", CategoriaDeDocumento.Contrato, Visibilidade.Turma);

    private static NovoArquivo Arquivo(byte[] bytes, string nome = "contrato.pdf", long? tamanho = null) =>
        new(nome, tamanho ?? bytes.Length, new MemoryStream(bytes), "qualquer");

    private static DocumentoResumo Resumo(Guid id) =>
        new(
            id,
            "Contrato do buffet",
            CategoriaDeDocumento.Contrato,
            Visibilidade.Turma,
            1,
            "contrato.pdf",
            "application/pdf",
            10,
            DateTime.UtcNow,
            "Ana"
        );

    [Fact]
    public async Task Acima_do_teto_do_acervo_e_recusado()
    {
        var resultado = await Servico.Enviar(FormaturaId, UsuarioId, Dados(), Arquivo(Pdf, tamanho: Documento.TamanhoMaximoEmBytes + 1), Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("comunicacao.arquivo_grande");
        await _arquivos.DidNotReceiveWithAnyArgs().Enviar(default!, default, Ct);
    }

    [Theory]
    [InlineData("ata.txt")]
    [InlineData("planilha.csv")]
    [InlineData("script.html")]
    public async Task Extensao_fora_da_lista_do_acervo_e_recusada(string nome)
    {
        var resultado = await Servico.Enviar(FormaturaId, UsuarioId, Dados(), Arquivo(Pdf, nome), Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("comunicacao.tipo_invalido");
    }

    [Fact]
    public async Task Sem_arquivo_e_recusado()
    {
        var resultado = await Servico.Enviar(FormaturaId, UsuarioId, Dados(), arquivo: null, Ct);

        resultado.PrimeiroErro.Codigo.ShouldBe("comunicacao.arquivo_obrigatorio");
    }

    [Fact]
    public async Task Pdf_de_verdade_e_gravado_na_categoria_do_acervo_em_nome_de_quem_envia()
    {
        var resultado = await Servico.Enviar(FormaturaId, UsuarioId, Dados(), Arquivo(Pdf), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _arquivos.Received(1).Enviar(Arg.Is<NovoArquivo>(a => a.Categoria == DocumentoService.CategoriaDoArquivo), UsuarioId, Ct);
        await _documentos.Received(1).Adicionar(Arg.Is<Documento>(d => d.ArquivoId == NovoArquivoId && d.Versao == 1), Ct);
    }

    [Fact]
    public async Task Substituir_soma_a_versao_audita_e_apaga_o_antigo_so_depois_do_commit()
    {
        // Arrange
        var antigoId = Guid.CreateVersion7();
        var remetenteAntigo = Guid.CreateVersion7();
        var documento = Documento.Novo(Dados(), antigoId);
        _documentos.ObterParaEdicao(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);
        _documentos
            .ObterArquivo(documento.Id, PapelNaFormatura.Comissao, Arg.Any<CancellationToken>())
            .Returns(new ArquivoDoDocumento(antigoId, remetenteAntigo, "contrato-v1.pdf"));

        // Act
        var resultado = await Servico.Atualizar(FormaturaId, UsuarioId, documento.Id, Dados(), Arquivo(Pdf, "contrato-v2.pdf"), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        documento.Versao.ShouldBe(2);
        documento.ArquivoId.ShouldBe(NovoArquivoId);
        Received.InOrder(() =>
        {
            _eventos.Adicionar(
                Arg.Is<Evento>(e =>
                    e.Nome == DocumentoService.EventoDeSubstituicao
                    && e.UsuarioId == UsuarioId
                    && e.Dados!.Contains("contrato-v1.pdf")
                    && e.Dados.Contains("contrato-v2.pdf")
                ),
                Ct
            );
            _unitOfWork.SalvarAsync(Ct);
            _arquivos.Remover(antigoId, new SolicitanteDeArquivo(remetenteAntigo, false), Ct);
        });
    }

    [Fact]
    public async Task Corrigir_sem_arquivo_nao_mexe_na_versao_nem_apaga_nada()
    {
        var documento = Documento.Novo(Dados(), Guid.CreateVersion7());
        _documentos.ObterParaEdicao(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);

        var resultado = await Servico.Atualizar(
            FormaturaId,
            UsuarioId,
            documento.Id,
            Dados() with
            {
                Visibilidade = Visibilidade.SomenteComissao,
            },
            null,
            Ct
        );

        resultado.Sucesso.ShouldBeTrue();
        documento.Versao.ShouldBe(1);
        documento.Visibilidade.ShouldBe(Visibilidade.SomenteComissao);
        await _arquivos.DidNotReceiveWithAnyArgs().Remover(default, default, Ct);
        await _eventos.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Excluir_audita_e_apaga_o_arquivo_depois_do_commit()
    {
        var arquivoId = Guid.CreateVersion7();
        var documento = Documento.Novo(Dados(), arquivoId);
        _documentos.ObterParaEdicao(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);
        _documentos
            .ObterArquivo(documento.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ArquivoDoDocumento(arquivoId, UsuarioId, "contrato.pdf"));

        var resultado = await Servico.Excluir(FormaturaId, UsuarioId, documento.Id, Ct);

        resultado.Sucesso.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _documentos.Remover(documento);
            _eventos.Adicionar(Arg.Is<Evento>(e => e.Nome == DocumentoService.EventoDeExclusao && e.UsuarioId == UsuarioId), Ct);
            _unitOfWork.SalvarAsync(Ct);
            _arquivos.Remover(arquivoId, new SolicitanteDeArquivo(UsuarioId, false), Ct);
        });
    }

    /// <summary>O repositório não achou para este papel: nenhuma URL é emitida, e a resposta é a do inexistente.</summary>
    [Fact]
    public async Task Documento_que_o_papel_nao_ve_nao_emite_url()
    {
        _vinculos.ObterPapelAtivo(UsuarioId, FormaturaId, Arg.Any<CancellationToken>()).Returns(PapelNaFormatura.Formando);

        var resultado = await Servico.Baixar(FormaturaId, UsuarioId, Guid.CreateVersion7(), Ct);

        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
        await _arquivos.DidNotReceiveWithAnyArgs().GerarUrlTemporaria(default, default, default, Ct);
    }

    [Fact]
    public async Task O_download_pede_a_url_de_minutos_como_quem_enviou()
    {
        var documentoId = Guid.CreateVersion7();
        var arquivoId = Guid.CreateVersion7();
        var remetente = Guid.CreateVersion7();
        _documentos
            .ObterArquivo(documentoId, PapelNaFormatura.Comissao, Arg.Any<CancellationToken>())
            .Returns(new ArquivoDoDocumento(arquivoId, remetente, "ata.pdf"));
        _arquivos
            .GerarUrlTemporaria(arquivoId, Arg.Any<SolicitanteDeArquivo>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok("/api/v1/arquivos/temporario?chave=x"));

        var resultado = await Servico.Baixar(FormaturaId, UsuarioId, documentoId, Ct);

        resultado.Valor.ShouldBe("/api/v1/arquivos/temporario?chave=x");
        DocumentoService.ValidadeDaUrl.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
        await _arquivos.Received(1).GerarUrlTemporaria(arquivoId, new SolicitanteDeArquivo(remetente, false), DocumentoService.ValidadeDaUrl, Ct);
    }
}

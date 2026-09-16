using System.Text;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Services;
using Backend.Business.Arquivos.Settings;
using Backend.Business.Arquivos.Validators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Arquivos;

/// <summary>
/// Cobre as regras de envio e as fronteiras de acesso.
/// </summary>
public sealed class ArquivoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <remarks>
    /// <c>.xyz</c> está liberado de propósito e não tem tipo conhecido: é o caso de quem
    /// acrescenta uma extensão à configuração e esquece de mapeá-la.
    /// </remarks>
    private static readonly ArmazenamentoSettings Settings = new() { TamanhoMaximoEmMB = 1, ExtensoesPermitidas = [".pdf", ".png", ".txt", ".xyz"] };

    private readonly IArquivoRepository _repositorio = Substitute.For<IArquivoRepository>();
    private readonly IArmazenamentoDeArquivos _armazenamento = Substitute.For<IArmazenamentoDeArquivos>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private ArquivoService Criar() =>
        new(
            _repositorio,
            _armazenamento,
            new NovoArquivoValidator(Options.Create(Settings)),
            Options.Create(Settings),
            new UrlTemporariaLocal(),
            _unitOfWork,
            NullLogger<ArquivoService>.Instance
        );

    /// <summary>Um pedido de envio cujos bytes batem com a extensão do nome.</summary>
    /// <remarks>
    /// Com o conteúdo conferido no envio, um fluxo de texto solto reprovaria em todo teste que só
    /// quer exercitar outra regra. Use <see cref="NovoComBytes"/> para o caso em que o conteúdo
    /// é o assunto.
    /// </remarks>
    private static NovoArquivo Novo(string nome = "relatorio.pdf", long tamanho = 1024, string categoria = "anexos") =>
        NovoComBytes(CabecalhoDe(nome), nome, tamanho, categoria);

    private static NovoArquivo NovoComBytes(byte[] bytes, string nome = "relatorio.pdf", long? tamanho = null, string categoria = "anexos") =>
        new(nome, tamanho ?? bytes.Length, new MemoryStream(bytes), categoria);

    /// <summary>Os primeiros bytes que a extensão promete; texto solto para o que não tem assinatura.</summary>
    private static byte[] CabecalhoDe(string nome) =>
        Path.GetExtension(nome).ToLowerInvariant() switch
        {
            ".pdf" => "%PDF-1.7\n"u8.ToArray(),
            ".png" => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            _ => Encoding.UTF8.GetBytes("conteúdo"),
        };

    private static SolicitanteDeArquivo Dono(Guid id) => new(id, EhAdministrador: false);

    [Fact]
    public async Task Enviar_grava_os_bytes_antes_de_registrar_os_metadados()
    {
        var usuario = Guid.CreateVersion7();

        var resultado = await Criar().Enviar(Novo(), usuario, Ct);

        resultado.Sucesso.ShouldBeTrue();

        await _armazenamento.Received(1).GravarAsync(Arg.Any<string>(), Arg.Any<Stream>(), "application/pdf", Arg.Any<CancellationToken>());
        await _repositorio.Received(1).Adicionar(Arg.Any<Arquivo>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// O tipo gravado sai da extensão, não do cabeçalho do multipart.
    /// </summary>
    /// <remarks>
    /// O valor gravado é o que o download devolve. Se o cliente pudesse escolhê-lo, um
    /// <c>.txt</c> anunciado como <c>text/html</c> voltaria como página executando na origem
    /// da API.
    /// </remarks>
    [Theory]
    [InlineData("relatorio.pdf", "application/pdf")]
    [InlineData("foto.PNG", "image/png")]
    public async Task O_tipo_do_conteudo_vem_da_extensao_e_nao_do_cliente(string nome, string esperado)
    {
        Arquivo? registrado = null;
        await _repositorio.Adicionar(Arg.Do<Arquivo>(a => registrado = a), Arg.Any<CancellationToken>());

        var resultado = await Criar().Enviar(Novo(nome), Guid.CreateVersion7(), Ct);

        resultado.Sucesso.ShouldBeTrue();
        registrado.ShouldNotBeNull();
        registrado.ContentType.ShouldBe(esperado);
    }

    /// <summary>
    /// A cota de espaço barra o envio antes de qualquer byte chegar ao provedor.
    /// </summary>
    /// <remarks>
    /// O teto por arquivo não limita o total: 120 envios por minuto de arquivos válidos enchem o
    /// bucket igual. É a cota que segura.
    /// </remarks>
    [Fact]
    public async Task Envio_que_estoura_a_cota_de_espaco_e_recusado_sem_tocar_no_provedor()
    {
        var usuario = Guid.CreateVersion7();
        _repositorio
            .ObterUsoDoUsuario(usuario, Arg.Any<CancellationToken>())
            .Returns(new UsoDeArmazenamento(3, Settings.CotaPorUsuarioEmBytes - 100));

        var resultado = await Criar().Enviar(Novo(tamanho: 500), usuario, Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("arquivo.cota_excedida");
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Conflito);

        await _armazenamento.DidNotReceive().GravarAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repositorio.DidNotReceive().Adicionar(Arg.Any<Arquivo>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// O limite de quantidade é independente do de espaço: milhões de arquivos de 1 KB cabem
    /// folgados na cota e ainda assim inutilizam a listagem.
    /// </summary>
    [Fact]
    public async Task Envio_acima_do_limite_de_quantidade_e_recusado()
    {
        var usuario = Guid.CreateVersion7();
        _repositorio
            .ObterUsoDoUsuario(usuario, Arg.Any<CancellationToken>())
            .Returns(new UsoDeArmazenamento(Settings.MaximoDeArquivosPorUsuario, 1024));

        var resultado = await Criar().Enviar(Novo(), usuario, Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("arquivo.limite_de_quantidade");

        await _armazenamento.DidNotReceive().GravarAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Dentro da cota, o envio segue normalmente.</summary>
    [Fact]
    public async Task Envio_dentro_da_cota_e_aceito()
    {
        var usuario = Guid.CreateVersion7();
        _repositorio.ObterUsoDoUsuario(usuario, Arg.Any<CancellationToken>()).Returns(new UsoDeArmazenamento(1, 1024));

        var resultado = await Criar().Enviar(Novo(), usuario, Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _armazenamento.Received(1).GravarAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A chave não pode derivar do nome enviado: é o que elimina travessia de diretório, colisão
    /// entre arquivos de mesmo nome e caractere inválido no provedor, de uma vez.
    /// </summary>
    [Fact]
    public async Task A_chave_e_gerada_pela_aplicacao_e_nao_pelo_nome_enviado()
    {
        Arquivo? registrado = null;
        await _repositorio.Adicionar(Arg.Do<Arquivo>(a => registrado = a), Arg.Any<CancellationToken>());

        await Criar().Enviar(Novo("../../etc/senha.pdf"), Guid.CreateVersion7(), Ct);

        registrado.ShouldNotBeNull();
        registrado.Chave.ShouldNotContain("..");
        registrado.Chave.ShouldStartWith("anexos/");
        registrado.Chave.ShouldEndWith(".pdf");
        registrado.Nome.ShouldBe("senha.pdf");
    }

    /// <summary>
    /// Um executável renomeado para <c>.pdf</c> passa pela extensão e pelo <c>Content-Type</c> —
    /// os dois são texto que o cliente escolhe. Quem o barra são os primeiros bytes.
    /// </summary>
    /// <remarks>
    /// A conferência vive aqui, e não em cada feature que envia arquivo: este é o ponto único por
    /// onde comprovante, documento do acervo, foto e envio avulso passam.
    /// </remarks>
    [Fact]
    public async Task Executavel_renomeado_para_pdf_e_recusado_sem_tocar_no_provedor()
    {
        byte[] executavel = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

        var resultado = await Criar().Enviar(NovoComBytes(executavel), Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("arquivo.conteudo_invalido");
        resultado.PrimeiroErro.Campo.ShouldBe("arquivo");

        await _armazenamento.DidNotReceive().GravarAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repositorio.DidNotReceive().Adicionar(Arg.Any<Arquivo>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Texto puro não tem assinatura para contradizer a extensão, então passa.</summary>
    [Fact]
    public async Task Texto_puro_passa_porque_nao_tem_assinatura_a_conferir()
    {
        var resultado = await Criar().Enviar(NovoComBytes(Encoding.UTF8.GetBytes("a;b;c"), "planilha.txt"), Guid.CreateVersion7(), Ct);

        resultado.Sucesso.ShouldBeTrue();
    }

    /// <summary>
    /// Extensão liberada na configuração mas sem assinatura mapeada é recusada — de propósito.
    /// </summary>
    /// <remarks>
    /// Falhar alto no primeiro envio é melhor que aceitar em silêncio um formato que ninguém
    /// consegue conferir. Quem acrescenta uma extensão à lista de permissão acrescenta a
    /// assinatura dela em <c>ConteudoDeArquivo</c> junto.
    /// </remarks>
    [Fact]
    public async Task Extensao_liberada_sem_assinatura_conhecida_e_recusada()
    {
        var resultado = await Criar().Enviar(Novo("desconhecido.xyz"), Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("arquivo.conteudo_invalido");
    }

    [Fact]
    public async Task Extensao_fora_da_lista_de_permissao_e_recusada()
    {
        var resultado = await Criar().Enviar(Novo("script.exe"), Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);

        await _armazenamento.DidNotReceive().GravarAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Arquivo_acima_do_limite_e_recusado_sem_tocar_no_provedor()
    {
        var resultado = await Criar().Enviar(Novo(tamanho: 5 * 1024 * 1024), Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        await _armazenamento.DidNotReceive().GravarAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Categoria_com_caractere_invalido_e_recusada()
    {
        var resultado = await Criar().Enviar(Novo(categoria: "Anexos do Pedido"), Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Campo.ShouldBe("categoria");
    }

    /// <summary>
    /// Arquivo de terceiro responde como inexistente. Devolver 403 confirmaria que o
    /// identificador existe, transformando o endpoint num verificador.
    /// </summary>
    [Fact]
    public async Task Arquivo_de_outro_usuario_responde_como_inexistente()
    {
        var arquivo = new Arquivo { EnviadoPorId = Guid.CreateVersion7(), Chave = "anexos/x.pdf" };
        _repositorio.ObterPorId(arquivo.Id, Arg.Any<CancellationToken>()).Returns(arquivo);

        var resultado = await Criar().Baixar(arquivo.Id, Dono(Guid.CreateVersion7()), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
        resultado.PrimeiroErro.Codigo.ShouldBe("arquivo.nao_encontrado");
    }

    [Fact]
    public async Task O_administrador_acessa_arquivo_de_qualquer_usuario()
    {
        var arquivo = new Arquivo { EnviadoPorId = Guid.CreateVersion7(), Chave = "anexos/x.pdf" };
        _repositorio.ObterPorId(arquivo.Id, Arg.Any<CancellationToken>()).Returns(arquivo);
        _armazenamento.AbrirLeituraAsync(arquivo.Chave, Arg.Any<CancellationToken>()).Returns(new MemoryStream());

        var resultado = await Criar().Baixar(arquivo.Id, new SolicitanteDeArquivo(Guid.CreateVersion7(), EhAdministrador: true), Ct);

        resultado.Sucesso.ShouldBeTrue();
    }

    /// <summary>
    /// Registro existe mas o objeto sumiu do provedor: vira 404 com código próprio, em vez de
    /// escapar como erro 500.
    /// </summary>
    [Fact]
    public async Task Objeto_ausente_no_provedor_vira_conteudo_indisponivel()
    {
        var dono = Guid.CreateVersion7();
        var arquivo = new Arquivo { EnviadoPorId = dono, Chave = "anexos/sumiu.pdf" };
        _repositorio.ObterPorId(arquivo.Id, Arg.Any<CancellationToken>()).Returns(arquivo);
        _armazenamento.AbrirLeituraAsync(arquivo.Chave, Arg.Any<CancellationToken>()).Returns<Stream>(_ => throw new FileNotFoundException("sumiu"));

        var resultado = await Criar().Baixar(arquivo.Id, Dono(dono), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("arquivo.conteudo_indisponivel");
    }

    /// <summary>
    /// Falha ao apagar o objeto não desfaz a remoção do registro: o resultado é um órfão no
    /// provedor, e não um arquivo listado que não abre.
    /// </summary>
    [Fact]
    public async Task Remocao_conclui_mesmo_se_o_provedor_falhar_ao_apagar()
    {
        var dono = Guid.CreateVersion7();
        var arquivo = new Arquivo { EnviadoPorId = dono, Chave = "anexos/x.pdf" };
        _repositorio.ObterPorId(arquivo.Id, Arg.Any<CancellationToken>()).Returns(arquivo);
        _armazenamento.RemoverAsync(arquivo.Chave, Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("s3 fora"));

        var resultado = await Criar().Remover(arquivo.Id, Dono(dono), Ct);

        resultado.Sucesso.ShouldBeTrue();
        _repositorio.Received(1).Remover(arquivo);
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }
}

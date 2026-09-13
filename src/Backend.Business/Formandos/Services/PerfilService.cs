using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Formandos.Services;

/// <summary>
/// Cadastro do formando: o próprio preenche, a comissão consulta e corrige.
/// </summary>
/// <remarks>
/// O cadastro nasce na primeira gravação, não no convite: quem nunca abriu a tela não ocupa
/// linha, e a leitura de quem ainda não preencheu devolve um cadastro vazio.
/// <para>Log só com ids. O cadastro tem CPF e endereço, e nunca é escrito inteiro em log.</para>
/// </remarks>
/// <param name="perfilRepository">Cadastros e membros.</param>
/// <param name="arquivoService">Onde a foto é guardada.</param>
/// <param name="validator">Forma do cadastro.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PerfilService(
    IPerfilRepository perfilRepository,
    IArquivoService arquivoService,
    IValidator<AtualizarPerfil> validator,
    IUnitOfWork unitOfWork,
    ILogger<PerfilService> logger
) : IPerfilService
{
    /// <summary>Categoria da foto no módulo de arquivos — vira diretório no provedor.</summary>
    public const string CategoriaDaFoto = "fotos-de-formandos";

    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("formando.nao_encontrado", "Formando não encontrado nesta formatura.");

    /// <inheritdoc />
    public async Task<Result<PerfilDetalhe>> Obter(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);

        if (membro is null)
            return NaoEncontrado;

        var perfil = await perfilRepository.ObterDoVinculo(membro.VinculoId, ct) ?? new PerfilDoFormando { VinculoId = membro.VinculoId };

        return ParaDetalhe(membro, perfil);
    }

    /// <inheritdoc />
    public Task<Result<PerfilDetalhe>> Atualizar(Guid formaturaId, Guid usuarioId, AtualizarPerfil dados, CancellationToken ct = default) =>
        Gravar(formaturaId, usuarioId, dados, autorDaCorrecao: null, ct);

    /// <inheritdoc />
    /// <remarks>
    /// O registro da correção entra na mesma transação da alteração: ou os dois ficam, ou nenhum.
    /// </remarks>
    public Task<Result<PerfilDetalhe>> Corrigir(
        Guid formaturaId,
        Guid usuarioId,
        Guid autorId,
        AtualizarPerfil dados,
        CancellationToken ct = default
    ) => Gravar(formaturaId, usuarioId, dados, autorId, ct);

    /// <inheritdoc />
    /// <remarks>
    /// A foto nova é gravada antes e a antiga removida depois do commit: falhar no meio deixa, no
    /// pior caso, um arquivo órfão — nunca um cadastro apontando para foto que não existe. Trocar
    /// remove a anterior, então cada formando ocupa uma foto só no armazenamento.
    /// </remarks>
    public async Task<Result<PerfilDetalhe>> EnviarFoto(
        Guid formaturaId,
        Guid usuarioId,
        Stream conteudo,
        long tamanho,
        CancellationToken ct = default
    )
    {
        var jpeg = await FotoDoFormando.Preparar(conteudo, tamanho, ct);
        if (jpeg.Falhou)
            return Result.Falha<PerfilDetalhe>(jpeg.Erros);

        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return NaoEncontrado;

        await using var bytes = new MemoryStream(jpeg.Valor);

        var arquivo = await arquivoService.Enviar(new NovoArquivo("foto.jpg", jpeg.Valor.Length, bytes, CategoriaDaFoto), usuarioId, ct);
        if (arquivo.Falhou)
            return Result.Falha<PerfilDetalhe>(arquivo.Erros);

        var perfil = await ObterOuCriar(membro.VinculoId, ct);
        var anterior = perfil.TrocarFoto(arquivo.Valor.Id);

        await unitOfWork.SalvarAsync(ct);

        if (anterior is { } fotoAntiga)
            await RemoverFotoAntiga(fotoAntiga, usuarioId, ct);

        return ParaDetalhe(membro, perfil);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pede o arquivo ao módulo de arquivos <b>como o dono</b>: lá a regra é "cada um vê os
    /// próprios", e quem pode ver a foto de um formando é decisão daqui — a política de Gestão já
    /// passou, e o formando foi achado nesta turma. A foto só entra pelo <c>/eu</c>, então o dono do
    /// arquivo é sempre o próprio formando.
    /// </remarks>
    public async Task<Result<ArquivoParaDownload>> BaixarFoto(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return NaoEncontrado;

        var perfil = await perfilRepository.ObterDoVinculo(membro.VinculoId, ct);
        if (perfil?.FotoArquivoId is not { } arquivoId)
            return Erro.NaoEncontrado("perfil.sem_foto", "Este formando ainda não enviou foto.");

        return await arquivoService.Baixar(arquivoId, new SolicitanteDeArquivo(membro.UsuarioId, EhAdministrador: false), ct);
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<FormandoResumo>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeFormandos filtro,
        CancellationToken ct = default
    ) => Result.Ok(await perfilRepository.Listar(formaturaId, paginacao.Normalizar(), filtro, ct));

    /// <summary>Validar, achar o membro, aplicar, registrar a correção se houver autor, salvar.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Dono do cadastro.</param>
    /// <param name="dados">Seções a gravar.</param>
    /// <param name="autorDaCorrecao">Quem corrige, quando é a comissão; nulo quando é o próprio.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task<Result<PerfilDetalhe>> Gravar(
        Guid formaturaId,
        Guid usuarioId,
        AtualizarPerfil dados,
        Guid? autorDaCorrecao,
        CancellationToken ct
    )
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PerfilDetalhe>(validacao.Erros);

        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return NaoEncontrado;

        var perfil = await ObterOuCriar(membro.VinculoId, ct);
        perfil.Aplicar(dados);

        if (autorDaCorrecao is { } autor)
        {
            await perfilRepository.RegistrarCorrecao(
                new CorrecaoDePerfil
                {
                    PerfilId = perfil.Id,
                    AutorUsuarioId = autor,
                    CorrigidoEm = DateTime.UtcNow,
                    Secoes = dados.SecoesInformadas(),
                },
                ct
            );

            logger.LogInformation("Cadastro {PerfilId} corrigido por {AutorId}.", perfil.Id, autor);
        }

        await unitOfWork.SalvarAsync(ct);

        return ParaDetalhe(membro, perfil);
    }

    private async Task<PerfilDoFormando> ObterOuCriar(Guid vinculoId, CancellationToken ct)
    {
        if (await perfilRepository.ObterParaEdicao(vinculoId, ct) is { } existente)
            return existente;

        var novo = new PerfilDoFormando { VinculoId = vinculoId };
        await perfilRepository.Adicionar(novo, ct);

        return novo;
    }

    /// <summary>A anterior sai do armazenamento; falhar aqui só deixa um órfão, então vira log.</summary>
    private async Task RemoverFotoAntiga(Guid arquivoId, Guid usuarioId, CancellationToken ct)
    {
        var remocao = await arquivoService.Remover(arquivoId, new SolicitanteDeArquivo(usuarioId, EhAdministrador: false), ct);

        if (remocao.Falhou)
            logger.LogWarning("Foto anterior {ArquivoId} não foi removida: {Codigo}.", arquivoId, remocao.PrimeiroErro.Codigo);
    }

    private static PerfilDetalhe ParaDetalhe(MembroDoPerfil membro, PerfilDoFormando perfil) =>
        new(
            membro.UsuarioId,
            membro.Nome,
            membro.Email,
            membro.Papel,
            perfil.ParaDadosPessoais(),
            perfil.Endereco.ParaDados(),
            perfil.ContatoDeEmergencia.ParaDados(),
            perfil.FotoArquivoId,
            perfil.Completude,
            perfil.Faltando(),
            !perfil.EssencialPreenchido
        );
}

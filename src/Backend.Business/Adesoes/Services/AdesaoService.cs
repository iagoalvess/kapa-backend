using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Usuarios.Models;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Adesoes.Services;

/// <summary>
/// A adesão do formando ao termo da turma e o acompanhamento que a comissão faz dela.
/// </summary>
/// <remarks>
/// Aderir é o <b>único</b> caminho que gera parcela: o aceite, o snapshot do plano, as parcelas e o
/// e-mail entram num só <c>SalvarAsync</c> — ou fica tudo, ou nada. Um caminho alternativo de gerar
/// parcela é como aparece formando devendo sem nunca ter aderido.
/// <para>Log só com ids: a adesão tem nome e CPF.</para>
/// </remarks>
/// <param name="adesaoRepository">Termos e adesões.</param>
/// <param name="planoRepository">Plano vigente.</param>
/// <param name="perfilRepository">Membro e cadastro de quem adere.</param>
/// <param name="formaturaRepository">Nome da turma, para os e-mails.</param>
/// <param name="geracaoDeParcelas">Parcelas do plano no nome do formando.</param>
/// <param name="emails">E-mails da adesão.</param>
/// <param name="userManager">Contas: gera e confere o código de confirmação do aceite.</param>
/// <param name="validator">Forma do pedido de aceite.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AdesaoService(
    IAdesaoRepository adesaoRepository,
    IPlanoDeCobrancaRepository planoRepository,
    IPerfilRepository perfilRepository,
    IFormaturaRepository formaturaRepository,
    IGeracaoDeParcelasService geracaoDeParcelas,
    EmailsDeAdesao emails,
    UserManager<Usuario> userManager,
    IValidator<AderirAoTermo> validator,
    IUnitOfWork unitOfWork,
    ILogger<AdesaoService> logger
) : IAdesaoService
{
    /// <summary>
    /// Finalidade do código no provedor do Identity: o código do aceite não serve para confirmar
    /// e-mail nem para redefinir senha, e vice-versa.
    /// </summary>
    public const string FinalidadeDoCodigo = "adesao";

    /// <summary>
    /// Por quanto tempo o código vale, para o texto do e-mail e da tela.
    /// </summary>
    /// <remarks>
    /// O provedor de e-mail do Identity é TOTP com passo de três minutos e aceita o passo anterior —
    /// na prática, entre três e seis minutos, e o número não é configurável. Três é o que se promete,
    /// porque é o piso. <c>ponytail:</c> se a janela apertar na prática — a fila de e-mail roda a cada
    /// 30 segundos e come parte dela —, o caminho é um provedor próprio com validade configurável, e
    /// não esticar a promessa.
    /// </remarks>
    public const int MinutosDeValidadeDoCodigo = 3;

    /// <summary>Tamanho da coluna de User-Agent; o cabeçalho é escrito pelo cliente e não tem teto.</summary>
    public const int TamanhoMaximoDoUserAgent = 512;

    /// <summary>Idade mínima para aderir pela plataforma (decisão de 14/09/2026).</summary>
    public const int IdadeMinima = 18;

    /// <summary>
    /// O que o cadastro precisa ter para aderir: nome e CPF identificam quem assina; a data de
    /// nascimento diz se a pessoa pode assinar sozinha.
    /// </summary>
    public static readonly IReadOnlyList<string> ExigidosNaAdesao =
    [
        ItensDoCadastro.NomeCompleto,
        ItensDoCadastro.Cpf,
        ItensDoCadastro.DataDeNascimento,
    ];

    private static readonly Erro SemTermo = Erro.Conflito("adesao.sem_termo_publicado", "A comissão ainda não publicou o termo de adesão da turma.");

    private static readonly Erro MembroNaoEncontrado = Erro.NaoEncontrado("membro.nao_encontrado", "Membro não encontrado nesta formatura.");

    private static readonly Erro AdesaoNaoEncontrada = Erro.NaoEncontrado("adesao.nao_encontrada", "Adesão não encontrada.");

    private static readonly Erro SemPlano = Erro.Conflito("adesao.sem_plano_vigente", "A turma ainda não tem plano de cobrança em vigor.");

    /// <inheritdoc />
    /// <remarks>
    /// As conferências seguem a ordem da sprint: termo, plano, adesão repetida, hash, cadastro — e,
    /// pelas decisões de 14/09/2026, idade e CPF repetido na turma.
    /// <para>
    /// O código do e-mail é a <b>última</b> conferência de propósito: ele vale poucos minutos, e
    /// descobrir o cadastro incompleto só depois de pedir o código gastaria a janela inteira.
    /// </para>
    /// </remarks>
    public async Task<Result<AdesaoDetalhe>> Aderir(
        Guid formaturaId,
        Guid usuarioId,
        AderirAoTermo dados,
        OrigemDoAceite origem,
        CancellationToken ct = default
    )
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<AdesaoDetalhe>(validacao.Erros);

        var termo = await adesaoRepository.ObterTermoVigente(ct);
        if (termo is null)
            return SemTermo;

        var plano = await planoRepository.ObterVigente(ct);
        if (plano is null)
            return SemPlano;

        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return MembroNaoEncontrado;

        if (await adesaoRepository.JaAderiu(membro.VinculoId, termo.Id, ct))
            return Erro.Conflito("adesao.ja_aderiu", "Você já aderiu a esta versão do termo.");

        var snapshot = SnapshotDoPlano.De(plano, DataUtils.Hoje());
        var planoJson = snapshot.ParaJson();
        var hash = AdesaoDoFormando.CalcularHash(termo.Conteudo, planoJson);

        if (!string.Equals(hash, dados.HashDoConteudo, StringComparison.Ordinal))
            return Erro.Conflito(
                "adesao.termo_desatualizado",
                "O termo ou o plano mudou enquanto você lia. Confira a versão atual antes de aceitar."
            );

        var perfil = await perfilRepository.ObterDoVinculo(membro.VinculoId, ct);
        var conferencia = await ConferirCadastro(perfil, membro.VinculoId, ct);
        if (conferencia.Falhou)
            return Result.Falha<AdesaoDetalhe>(conferencia.Erros);

        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is null)
            return MembroNaoEncontrado;

        if (!await userManager.VerifyUserTokenAsync(usuario, TokenOptions.DefaultEmailProvider, FinalidadeDoCodigo, dados.Codigo))
        {
            logger.LogWarning("Código de adesão recusado para o vínculo {VinculoId}.", membro.VinculoId);

            return Erro.Conflito(
                "adesao.codigo_invalido",
                "O código não confere ou já expirou. Peça um código novo e use o mais recente que chegou no seu e-mail."
            );
        }

        var adesao = new AdesaoDoFormando
        {
            VinculoId = membro.VinculoId,
            TermoId = termo.Id,
            Versao = termo.Versao,
            HashDoConteudo = hash,
            AceitoEm = DateTime.UtcNow,
            EnderecoIp = origem.EnderecoIp ?? string.Empty,
            UserAgent = TextoUtils.Truncar(origem.UserAgent, TamanhoMaximoDoUserAgent) ?? string.Empty,
            EmailDoAceite = usuario.Email ?? membro.Email,
            NomeCompleto = perfil!.NomeCompleto!,
            Cpf = perfil.Cpf!,
            PlanoAceito = planoJson,
        };

        await adesaoRepository.Adicionar(adesao, ct);

        var parcelas = await geracaoDeParcelas.Gerar(membro.VinculoId, plano, ct);
        if (parcelas.Falhou)
            return Result.Falha<AdesaoDetalhe>(parcelas.Erros);

        var formatura = await formaturaRepository.ObterDetalhe(formaturaId, ct);
        await emails.Confirmacao(membro.Email, formatura?.Nome ?? string.Empty, termo.Versao, snapshot, ct);

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation(
            "Adesão {AdesaoId} do vínculo {VinculoId} à versão {Versao}; {Parcelas} parcelas geradas.",
            adesao.Id,
            membro.VinculoId,
            termo.Versao,
            parcelas.Valor
        );

        return Detalhar(new AdesaoComTermo(adesao, termo.Conteudo));
    }

    /// <inheritdoc />
    /// <remarks>
    /// O código sai do provedor de e-mail do Identity: seis dígitos derivados do <i>security stamp</i>
    /// da conta e do relógio, e por isso nada é gravado — não há tabela de códigos para limpar, nem
    /// código sobrevivendo a uma troca de senha. Pedir de novo devolve o mesmo código dentro do mesmo
    /// passo de três minutos; é o esperado, e o e-mail repetido não invalida o anterior.
    /// </remarks>
    public async Task<Result<CodigoEnviado>> SolicitarCodigo(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return MembroNaoEncontrado;

        var termo = await adesaoRepository.ObterTermoVigente(ct);
        if (termo is null)
            return SemTermo;

        if (await planoRepository.ObterVigente(ct) is null)
            return SemPlano;

        if (await adesaoRepository.JaAderiu(membro.VinculoId, termo.Id, ct))
            return Erro.Conflito("adesao.ja_aderiu", "Você já aderiu a esta versão do termo.");

        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is null)
            return MembroNaoEncontrado;

        var codigo = await userManager.GenerateUserTokenAsync(usuario, TokenOptions.DefaultEmailProvider, FinalidadeDoCodigo);
        var formatura = await formaturaRepository.ObterDetalhe(formaturaId, ct);

        await emails.Codigo(usuario.Email ?? membro.Email, formatura?.Nome ?? string.Empty, codigo, MinutosDeValidadeDoCodigo, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Código de adesão enviado ao vínculo {VinculoId}.", membro.VinculoId);

        return new CodigoEnviado(MascararEmail(usuario.Email ?? membro.Email), MinutosDeValidadeDoCodigo);
    }

    /// <inheritdoc />
    public async Task<Result<MinhaAdesao>> ObterMinha(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        // Do titular: o termo que vigorou continua acessível a quem foi desligado (P5 da Sprint 15).
        var membro = await perfilRepository.ObterTitular(formaturaId, usuarioId, ct);
        if (membro is null)
            return MembroNaoEncontrado;

        var ultima = await adesaoRepository.ObterUltimaDoVinculo(membro.VinculoId, ct);
        var perfil = await perfilRepository.ObterDoVinculo(membro.VinculoId, ct);

        return new MinhaAdesao(
            ultima is null ? null : Detalhar(ultima),
            Pendencias(perfil),
            perfil?.DataDeNascimento is { } nascimento && MenorDeIdade(nascimento)
        );
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<SituacaoDeAdesao>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeAdesoes filtro,
        CancellationToken ct = default
    ) => Result.Ok(await adesaoRepository.ListarSituacoes(formaturaId, paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    public async Task<Result<ResumoDeAdesoes>> Resumir(Guid formaturaId, CancellationToken ct = default)
    {
        var (membros, aderiram) = await adesaoRepository.Contar(formaturaId, ct);
        var termo = await adesaoRepository.ObterTermoVigente(ct);

        return new ResumoDeAdesoes(membros, aderiram, termo?.Versao);
    }

    /// <inheritdoc />
    public async Task<Result<PdfDaAdesao>> ObterPdf(Guid formaturaId, Guid adesaoId, Guid solicitanteId, CancellationToken ct = default)
    {
        var adesao = await adesaoRepository.Obter(adesaoId, ct);
        // Do titular: o PDF do próprio termo acompanha a adesão. O de terceiro continua exigindo Gestão,
        // e quem saiu nunca a tem — o papel dele passou a valer só para ler o que é dele.
        var solicitante = await perfilRepository.ObterTitular(formaturaId, solicitanteId, ct);

        if (adesao is null || solicitante is null)
            return AdesaoNaoEncontrada;

        var propria = adesao.Adesao.VinculoId == solicitante.VinculoId;

        if (!propria && !PapelNaFormatura.Gestao.Contains(solicitante.Papel))
            return AdesaoNaoEncontrada;

        return new PdfDaAdesao(TermoEmPdf.Gerar(adesao, mascararCpf: !propria), $"termo-de-adesao-v{adesao.Adesao.Versao}.pdf");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Quem aderiu a uma versão anterior pode ser lembrado: é assim que a comissão pede re-adesão
    /// depois de publicar uma correção. Quem já aceitou a vigente, não.
    /// </remarks>
    public async Task<Result> Lembrar(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        var membro = await perfilRepository.ObterMembro(formaturaId, usuarioId, ct);
        if (membro is null)
            return Result.Falha(MembroNaoEncontrado);

        var termo = await adesaoRepository.ObterTermoVigente(ct);
        if (termo is null)
            return Result.Falha(SemTermo);

        if (await adesaoRepository.JaAderiu(membro.VinculoId, termo.Id, ct))
            return Result.Falha(Erro.Conflito("adesao.ja_aderiu", "Este membro já aderiu à versão vigente do termo."));

        var formatura = await formaturaRepository.ObterDetalhe(formaturaId, ct);
        await emails.Lembrete(membro.Email, formatura?.Nome ?? string.Empty, ct);

        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <summary>
    /// Nome, CPF e nascimento presentes; maior de idade; CPF sem outra adesão na turma.
    /// </summary>
    /// <param name="perfil">Cadastro de quem adere; nulo se nunca preenchido.</param>
    /// <param name="vinculoId">Vínculo de quem adere.</param>
    private async Task<Result> ConferirCadastro(PerfilDoFormando? perfil, Guid vinculoId, CancellationToken ct)
    {
        if (perfil is null || Pendencias(perfil).Count > 0)
            return Result.Falha(
                Erro.Conflito("adesao.cadastro_incompleto", "Para aderir, informe no seu cadastro o nome completo, o CPF e a data de nascimento.")
            );

        if (MenorDeIdade(perfil.DataDeNascimento!.Value))
            return Result.Falha(
                Erro.Conflito(
                    "adesao.menor_de_idade",
                    "Quem tem menos de 18 anos adere com a comissão, junto com o responsável legal, e não pela plataforma."
                )
            );

        if (await adesaoRepository.CpfEmUsoPorOutro(perfil.Cpf!, vinculoId, ct))
            return Result.Falha(
                Erro.Conflito(
                    "adesao.cpf_em_uso",
                    "Este CPF já está na adesão de outra pessoa da turma. Confira o seu cadastro ou fale com a comissão."
                )
            );

        return Result.Ok();
    }

    private static IReadOnlyList<string> Pendencias(PerfilDoFormando? perfil) =>
        perfil is null ? ExigidosNaAdesao : [.. ExigidosNaAdesao.Intersect(perfil.Faltando())];

    private static bool MenorDeIdade(DateOnly nascimento) => nascimento.AddYears(IdadeMinima) > DataUtils.Hoje();

    /// <summary>
    /// <c>ana.souza@exemplo.com</c> vira <c>an*******@exemplo.com</c>: o bastante para a pessoa
    /// reconhecer a própria caixa de entrada, pouco para quem só tem a tela na frente.
    /// </summary>
    /// <param name="email">E-mail da conta.</param>
    private static string MascararEmail(string email)
    {
        var arroba = email.IndexOf('@', StringComparison.Ordinal);
        if (arroba <= 0)
            return email;

        var inicio = email[..Math.Min(2, arroba)];

        return $"{inicio}{new string('*', arroba - inicio.Length)}{email[arroba..]}";
    }

    private static AdesaoDetalhe Detalhar(AdesaoComTermo adesao) =>
        new(
            adesao.Adesao.Id,
            adesao.Adesao.Versao,
            adesao.Adesao.AceitoEm,
            adesao.Adesao.HashDoConteudo,
            adesao.Adesao.NomeCompleto,
            adesao.Adesao.Cpf,
            adesao.Adesao.EmailDoAceite,
            adesao.ConteudoDoTermo,
            adesao.Adesao.LerPlano()
        );
}

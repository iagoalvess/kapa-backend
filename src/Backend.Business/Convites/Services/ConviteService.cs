using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Convites.Interfaces;
using Backend.Business.Convites.Models;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Usuarios.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Backend.Business.Convites.Services;

/// <summary>
/// Criação, acompanhamento e aceite de convites.
/// </summary>
/// <param name="conviteRepository">Convites e aceites.</param>
/// <param name="vinculoRepository">Vínculos, para o papel do autor e o do convidado.</param>
/// <param name="formaturaRepository">A turma do convite, para o nome e o status.</param>
/// <param name="usuarioRepository">E-mail da conta que aceita o convite nominal.</param>
/// <param name="authService">Emissão da sessão dentro da turma nova.</param>
/// <param name="tokenService">Hash do token, o mesmo do refresh token.</param>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="criarValidator">Validador do pedido de convite.</param>
/// <param name="aplicacao">Identidade da aplicação, que monta o link.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class ConviteService(
    IConviteRepository conviteRepository,
    IVinculoRepository vinculoRepository,
    IFormaturaRepository formaturaRepository,
    IUsuarioRepository usuarioRepository,
    IAuthService authService,
    ITokenService tokenService,
    IEmailService emailService,
    IValidator<CriarConvite> criarValidator,
    IOptions<AplicacaoSettings> aplicacao,
    IUnitOfWork unitOfWork
) : IConviteService
{
    /// <summary>Caminho da tela de aceite no front-end (<c>ROTAS.convite</c>), com o token no fim.</summary>
    private const string CaminhoDoConvite = "/convite/";

    /// <summary>
    /// Teto da listagem.
    /// </summary>
    /// <remarks><c>ponytail:</c> turma de 80 formandos cabe folgada; paginar quando alguma passar disso.</remarks>
    private const int LimiteDaListagem = 200;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// A mesma resposta para inexistente, expirado, revogado e esgotado.
    /// </summary>
    /// <remarks>Diferenciar transformaria o endpoint num oráculo que confirma quais tokens existem.</remarks>
    private static readonly Erro Invalido = Erro.NaoEncontrado("convite.invalido", "Este convite não está mais disponível. Peça um novo à comissão.");

    private static readonly Erro JaVinculado = Erro.Conflito("convite.ja_vinculado", "Você já participa desta formatura.");

    private static readonly Erro PapelRestrito = Erro.Proibido("convite.papel_restrito", "Só o Presidente convida para a comissão e a tesouraria.");

    /// <inheritdoc />
    /// <remarks>
    /// Token de 32 bytes de CSPRNG em Base64Url, e só o SHA-256 vai para o banco — o mesmo padrão do
    /// refresh token. Não é JWT: convite precisa ser revogável, e revogar exige o banco de qualquer forma.
    /// </remarks>
    public async Task<Result<ConviteCriado>> Criar(Guid formaturaId, Guid usuarioId, CriarConvite dados, CancellationToken ct = default)
    {
        var validacao = criarValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ConviteCriado>(validacao.Erros);

        var papel = dados.Papel ?? PapelNaFormatura.Formando;

        if (!await PodeTratarDoPapel(papel, usuarioId, formaturaId, ct))
            return PapelRestrito;

        var formatura = await formaturaRepository.ObterDetalhe(formaturaId, ct);
        if (formatura is null)
            return Erro.NaoEncontrado("formatura.nao_encontrada", "Formatura não encontrada.");

        if (!AceitaEntrada(formatura.Status, papel))
            return Erro.Proibido(
                "convite.formatura_nao_contratada",
                "Contrate um plano para convidar formandos. Antes disso, dá para convidar a comissão."
            );

        var email = dados.Email?.Trim();
        var nominal = email is not null;
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        var convite = new Convite
        {
            TokenHash = tokenService.CalcularHash(token),
            Email = email,
            Papel = papel,
            ExpiraEm = DateTime.UtcNow.AddDays(dados.DiasDeValidade ?? (nominal ? Convite.DiasDeValidadeDoNominal : Convite.DiasDeValidadeDoLink)),
            UsosMaximos = nominal ? 1 : dados.UsosMaximos,
            CriadoPorUsuarioId = usuarioId,
        };

        var link = MontarLink(token);

        await conviteRepository.Adicionar(convite, ct);

        if (email is not null)
            await EnfileirarEmail(email, formatura, convite, link, ct);

        await unitOfWork.SalvarAsync(ct);

        return new ConviteCriado(convite.Id, link, convite.ExpiraEm);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ConviteResumo>>> Listar(CancellationToken ct = default) =>
        Result.Ok(await conviteRepository.ListarRecentes(DateTime.UtcNow, LimiteDaListagem, ct));

    /// <inheritdoc />
    public async Task<Result> Revogar(Guid formaturaId, Guid usuarioId, Guid conviteId, CancellationToken ct = default)
    {
        var convite = await conviteRepository.ObterParaEdicao(conviteId, ct);

        if (convite is null)
            return Result.Falha(Erro.NaoEncontrado("convite.nao_encontrado", "Convite não encontrado nesta formatura."));

        if (!await PodeTratarDoPapel(convite.Papel, usuarioId, formaturaId, ct))
            return Result.Falha(PapelRestrito);

        convite.Revogar(DateTime.UtcNow);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<ConvitePublico>> ObterPublico(string token, CancellationToken ct = default) =>
        await ObterUtilizavel(token, DateTime.UtcNow, ct) is { } utilizavel
            ? new ConvitePublico(
                utilizavel.Formatura.Nome,
                utilizavel.Formatura.Instituicao,
                utilizavel.Convite.Papel,
                utilizavel.Convite.Email is { } email ? TextoUtils.MascararEmail(email) : null
            )
            : Invalido;

    /// <inheritdoc />
    /// <remarks>
    /// O papel vem do convite gravado, nunca da requisição: aceitar com papel no corpo bastaria um
    /// campo trocado no DevTools para virar tesoureiro.
    /// <para>
    /// Uso consumido, vínculo e sessão nascem na mesma transação. O <c>UPDATE</c> condicional do uso
    /// trava a linha do convite, então dois aceites do mesmo convite passam aqui um de cada vez: o
    /// segundo não passa do limite e, se for a mesma pessoa, já encontra o vínculo que o primeiro criou.
    /// </para>
    /// <para>
    /// Vínculo desativado volta a valer com o papel do convite, em vez de ganhar uma segunda linha:
    /// o índice único <c>(usuario, formatura)</c> recusaria, e o histórico continua sendo um só.
    /// Mas <b>só por convite pessoal</b>: pelo link da turma, quem o Presidente removeu voltaria
    /// sozinho enquanto o link valesse, e a remoção não serviria de nada.
    /// </para>
    /// <para>
    /// Convite pessoal exige o e-mail da conta <b>igual e confirmado</b>. Só bater não prova nada:
    /// quem recebe o link encaminhado cria uma conta com o e-mail convidado e entra no lugar do dono.
    /// </para>
    /// </remarks>
    public async Task<Result<ParDeTokens>> Aceitar(
        Guid usuarioId,
        string token,
        string refreshTokenAtual,
        OrigemDoAceite origem,
        CancellationToken ct = default
    )
    {
        var agora = DateTime.UtcNow;

        var utilizavel = await ObterUtilizavel(token, agora, ct);

        if (utilizavel is null)
            return Invalido;

        var convite = utilizavel.Value.Convite;

        if (convite.Email is not null && await ConferirDono(usuarioId, convite.Email, ct) is { } recusa)
            return recusa;

        if (await vinculoRepository.ObterPapelAtivo(usuarioId, convite.FormaturaId, ct) is not null)
            return JaVinculado;

        return await unitOfWork.EmTransacaoAsync<Result<ParDeTokens>>(
            async tentativa =>
            {
                if (!await conviteRepository.ConsumirUsoDeTodasAsFormaturas(convite.Id, agora, tentativa))
                    return Erro.Conflito("convite.esgotado", "Este convite acabou de atingir o limite de entradas. Peça um novo à comissão.");

                var vinculo = await vinculoRepository.ObterParaEdicao(usuarioId, convite.FormaturaId, tentativa);

                if (vinculo is { Ativo: true })
                    return JaVinculado;

                if (vinculo is { Ativo: false } && convite.Email is null)
                    return Erro.Proibido(
                        "convite.vinculo_removido",
                        "Você foi removido desta formatura. Para voltar, peça à comissão um convite pessoal."
                    );

                if (vinculo is null)
                {
                    await vinculoRepository.Adicionar(
                        new VinculoDeFormatura
                        {
                            UsuarioId = usuarioId,
                            FormaturaId = convite.FormaturaId,
                            Papel = convite.Papel,
                        },
                        tentativa
                    );
                }
                else
                {
                    vinculo.Ativo = true;
                    vinculo.Papel = convite.Papel;
                }

                await conviteRepository.RegistrarAceite(
                    new AceiteDeConvite
                    {
                        ConviteId = convite.Id,
                        UsuarioId = usuarioId,
                        AceitoEm = agora,
                        EnderecoIp = origem.EnderecoIp,
                        UserAgent = TextoUtils.Truncar(origem.UserAgent, 512),
                    },
                    tentativa
                );

                return await authService.EmitirSessaoDeFormatura(
                    usuarioId,
                    convite.FormaturaId,
                    convite.Papel,
                    refreshTokenAtual,
                    origem.EnderecoIp,
                    tentativa
                );
            },
            ct
        );
    }

    /// <summary>
    /// O convite do token, se ainda aceitar entrada, com a turma dele.
    /// </summary>
    /// <remarks>
    /// A turma também precisa aceitar a entrada (<see cref="AceitaEntrada"/>): a que foi suspensa
    /// ou encerrada depois não recebe gente nova — nem conta ao convidado por quê.
    /// </remarks>
    /// <param name="token">Token recebido.</param>
    /// <param name="agoraUtc">Momento que decide a validade.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task<(Convite Convite, FormaturaDetalhe Formatura)?> ObterUtilizavel(string token, DateTime agoraUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var convite = await conviteRepository.ObterPorHashDeTodasAsFormaturas(tokenService.CalcularHash(token), ct);

        if (convite is null || convite.StatusEm(agoraUtc) != StatusDoConvite.Pendente)
            return null;

        var formatura = await formaturaRepository.ObterDetalhe(convite.FormaturaId, ct);

        return formatura is not null && AceitaEntrada(formatura.Status, convite.Papel) ? (convite, formatura) : null;
    }

    /// <summary>Se a turma, neste status, recebe alguém com este papel.</summary>
    /// <remarks>
    /// A comissão entra desde o rascunho, para decidir junto a contratação. Formando só com a turma
    /// ativa: é ele que o plano cobra, e antes de pagar não há o que oferecer a ele.
    /// </remarks>
    /// <param name="status">Status da formatura.</param>
    /// <param name="papel">Papel do convite.</param>
    private static bool AceitaEntrada(StatusDaFormatura status, string papel) =>
        status == StatusDaFormatura.Ativa
        || (papel != PapelNaFormatura.Formando && status is StatusDaFormatura.Rascunho or StatusDaFormatura.AguardandoPagamento);

    /// <summary>
    /// Confere que a conta é dona do e-mail do convite pessoal: o mesmo endereço, já confirmado.
    /// </summary>
    /// <remarks>
    /// O e-mail sai mascarado na recusa, para quem entrou com a conta errada saber qual usar.
    /// </remarks>
    /// <param name="usuarioId">Quem está aceitando.</param>
    /// <param name="emailConvidado">E-mail do convite.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O erro, ou <c>null</c> se a conta é dona do e-mail.</returns>
    private async Task<Erro?> ConferirDono(Guid usuarioId, string emailConvidado, CancellationToken ct)
    {
        var conta = await usuarioRepository.ObterDetalhe(usuarioId, ct);
        var mascarado = TextoUtils.MascararEmail(emailConvidado);

        if (!string.Equals(conta?.Email, emailConvidado, StringComparison.OrdinalIgnoreCase))
            return Erro.Proibido(
                "convite.email_divergente",
                $"Este convite é pessoal e foi enviado para {mascarado}. Entre com a conta desse e-mail para aceitar."
            );

        return conta!.EmailConfirmado
            ? null
            : Erro.Proibido(
                "convite.email_nao_confirmado",
                $"Confirme seu e-mail para aceitar este convite pessoal. O link de confirmação foi enviado para {mascarado}."
            );
    }

    /// <summary>Formando qualquer membro da gestão oferece; os demais papéis, só o Presidente.</summary>
    /// <param name="papel">Papel do convite.</param>
    /// <param name="usuarioId">Autor.</param>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private async Task<bool> PodeTratarDoPapel(string papel, Guid usuarioId, Guid formaturaId, CancellationToken ct) =>
        papel == PapelNaFormatura.Formando || await vinculoRepository.ObterPapelAtivo(usuarioId, formaturaId, ct) == PapelNaFormatura.Presidente;

    private string MontarLink(string token) => $"{aplicacao.Value.UrlDoFrontend.TrimEnd('/')}{CaminhoDoConvite}{token}";

    /// <summary>Convite nominal: o link vai por e-mail. Só enfileira — quem salva é <see cref="Criar"/>.</summary>
    private async Task EnfileirarEmail(string email, FormaturaDetalhe formatura, Convite convite, string link, CancellationToken ct)
    {
        var nome = aplicacao.Value.Nome;
        var validade = DataUtils.ParaExibicao(convite.ExpiraEm).ToString("dd/MM/yyyy", PtBr);
        var papel = convite.Papel == PapelNaFormatura.Comissao ? "Comissão" : convite.Papel;

        var corpo = ModeloDeEmail.Montar(
            nome,
            $"Você foi convidado para {formatura.Nome}",
            $"A comissão de <strong>{ModeloDeEmail.Texto(formatura.Nome)}</strong> ({ModeloDeEmail.Texto(formatura.Instituicao)}) "
                + $"convidou você para entrar na turma como {ModeloDeEmail.Texto(papel)}. O convite é pessoal e vale até {validade}.",
            "Aceitar convite",
            link,
            comLogo: true
        );

        await emailService.Enfileirar(new NovoEmail(email, $"Convite para {formatura.Nome} — {nome}", corpo), ct);
    }
}

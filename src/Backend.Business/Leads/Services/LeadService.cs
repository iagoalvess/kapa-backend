using System.Globalization;
using Backend.Business.Abstractions;
using Backend.Business.Common;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Leads.Interfaces;
using Backend.Business.Leads.Models;
using Backend.Business.Leads.Settings;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Legal.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Leads.Services;

/// <summary>
/// O formulário de contato da página institucional: grava o contato e avisa o comercial.
/// </summary>
/// <remarks>
/// É o único endpoint de escrita anônimo do produto fora do cadastro e do aceite de convite, e por
/// isso tem três defesas empilhadas, do mais barato para o mais caro: limite de taxa por IP (na
/// borda), honeypot (um <c>if</c>) e janela de repetição por e-mail (uma consulta). Captcha só
/// entra quando as três deixarem passar spam de verdade.
/// </remarks>
/// <param name="leadRepository">Contatos.</param>
/// <param name="legalRepository">Versão vigente da Política de Privacidade, que é o que se consente.</param>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="validator">Forma do formulário.</param>
/// <param name="aplicacao">Nome da aplicação, no e-mail ao comercial.</param>
/// <param name="settings">Caixa do comercial e janela de repetição.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class LeadService(
    ILeadRepository leadRepository,
    ILegalRepository legalRepository,
    IEmailService emailService,
    IValidator<NovoLead> validator,
    IOptions<AplicacaoSettings> aplicacao,
    IOptions<LeadsSettings> settings,
    IUnitOfWork unitOfWork,
    ILogger<LeadService> logger
) : ILeadService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <inheritdoc />
    public async Task<Result> Registrar(NovoLead dados, OrigemDoContato origem, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(dados.Sobrenome))
        {
            logger.LogInformation("Contato descartado pelo honeypot.");
            return Result.Ok();
        }

        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var agora = DateTime.UtcNow;
        var email = dados.Email.Trim().ToLowerInvariant();

        if (await leadRepository.JaRegistrado(email, agora.AddHours(-settings.Value.HorasParaRepetirOMesmoEmail), ct))
        {
            logger.LogInformation("Contato repetido dentro da janela; ignorado.");
            return Result.Ok();
        }

        var vigentes = await legalRepository.ListarVigentes(agora, ct);
        var privacidade = vigentes.FirstOrDefault(v => v.Tipo == TipoDeDocumento.PoliticaDePrivacidade);

        if (privacidade is null)
            return Result.Falha(
                Erro.Indisponivel("lead.privacidade_indisponivel", "Não foi possível registrar seu contato agora. Tente em instantes.")
            );

        var lead = new Lead
        {
            Nome = dados.Nome.Trim(),
            Email = email,
            Telefone = FormatosBrasileiros.TelefoneE164(dados.Telefone),
            Instituicao = dados.Instituicao.Trim(),
            Curso = dados.Curso.Trim(),
            TamanhoDaTurma = dados.TamanhoDaTurma,
            PrevisaoDeColacao = PrimeiroDiaDoMes(dados.PrevisaoDeColacao),
            Mensagem = string.IsNullOrWhiteSpace(dados.Mensagem) ? null : dados.Mensagem.Trim(),
            Origem = Limpar(dados.Origem),
            Meio = Limpar(dados.Meio),
            Campanha = Limpar(dados.Campanha),
            PrivacidadeVersao = privacidade.Versao,
            ConsentidoEm = agora,
            EnderecoIp = origem.EnderecoIp,
            UserAgent = TextoUtils.Truncar(origem.UserAgent, 512),
        };

        await leadRepository.Adicionar(lead, ct);
        await AvisarComercial(lead, ct);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<LeadResumo>>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default) =>
        Result.Ok(await leadRepository.Listar(paginacao.Normalizar(), busca, ct));

    /// <summary>
    /// Converte <c>aaaa-mm</c> no primeiro dia do mês. Já validado pelo validator.
    /// </summary>
    /// <param name="mes">Mês informado, ou nulo.</param>
    private static DateOnly? PrimeiroDiaDoMes(string? mes) =>
        DateOnly.TryParseExact($"{mes}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data) ? data : null;

    /// <summary>Texto de query string: vazio vira nulo, e o resto é cortado no tamanho da coluna.</summary>
    /// <param name="valor">Valor recebido.</param>
    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : TextoUtils.Truncar(valor.Trim(), 120);

    /// <summary>
    /// Enfileira o aviso ao comercial. Só enfileira — quem salva é <see cref="Registrar"/>.
    /// </summary>
    /// <remarks>
    /// O corpo leva os dados do contato porque é o que faz alguém responder no mesmo dia: abrir o
    /// painel para descobrir quem escreveu é um passo a mais entre o interesse e a ligação. Vai para
    /// uma caixa interna, não para terceiro.
    /// </remarks>
    /// <param name="lead">Contato gravado.</param>
    private Task AvisarComercial(Lead lead, CancellationToken ct)
    {
        var destino = settings.Value.EmailDoComercial;

        if (string.IsNullOrWhiteSpace(destino))
            return Task.CompletedTask;

        var colacao = lead.PrevisaoDeColacao is { } data ? data.ToString("MMMM 'de' yyyy", PtBr) : "não informada";

        var corpo = ModeloDeEmail.Montar(
            aplicacao.Value.Nome,
            "Contato novo pela página",
            $"<strong>{ModeloDeEmail.Texto(lead.Nome)}</strong><br>"
                + $"{ModeloDeEmail.Texto(lead.Curso)} — {ModeloDeEmail.Texto(lead.Instituicao)}<br>"
                + $"{lead.TamanhoDaTurma} formandos · colação em {ModeloDeEmail.Texto(colacao)}<br><br>"
                + $"E-mail: {ModeloDeEmail.Texto(lead.Email)}<br>"
                + $"Telefone: {ModeloDeEmail.Texto(lead.Telefone ?? "não informado")}"
                + (lead.Mensagem is null ? string.Empty : $"<br><br>“{ModeloDeEmail.Texto(lead.Mensagem)}”")
                + (lead.Origem is null ? string.Empty : $"<br><br>Veio de: {ModeloDeEmail.Texto(lead.Origem)}"),
            null,
            null,
            Mascote.Lupa
        );

        return emailService.Enfileirar(new NovoEmail(destino, $"Contato novo: {lead.Instituicao} — {aplicacao.Value.Nome}", corpo), ct);
    }
}

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Services;
using Backend.Business.Adesoes.Settings;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Services;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Services;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Services;
using Backend.Business.Arquivos.Settings;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Services;
using Backend.Business.Auth.Settings;
using Backend.Business.Busca.Interfaces;
using Backend.Business.Busca.Services;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Common;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Services;
using Backend.Business.Convites.Interfaces;
using Backend.Business.Convites.Services;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Services;
using Backend.Business.Emails.Settings;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Services;
using Backend.Business.Festa.Settings;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Services;
using Backend.Business.IA.Interfaces;
using Backend.Business.IA.Services;
using Backend.Business.IA.Settings;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Legal.Services;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Services;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Services;
using Backend.Business.Marketing.Settings;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Services;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Services;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Services;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Services;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Services;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Services;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business;

/// <summary>
/// Registro de tudo o que a camada de negócio expõe.
/// </summary>
/// <remarks>
/// Cada camada registra as próprias dependências. É o que evita o arquivo central de
/// centenas de linhas que toda feature nova precisa editar — e que vira conflito de merge
/// sempre que duas pessoas criam uma feature na mesma semana.
/// <para>Ao criar uma feature, adicione o service dela em <c>AdicionarServices</c>.</para>
/// </remarks>
public static class DependenciasBusiness
{
    /// <summary>Registra services, validadores e configurações da camada de negócio.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>().Bind(configuration.GetSection(JwtSettings.Secao)).ValidateDataAnnotations().ValidateOnStart();

        services.AddOptions<SmtpSettings>().Bind(configuration.GetSection(SmtpSettings.Secao));
        services.AddOptions<ContaSettings>().Bind(configuration.GetSection(ContaSettings.Secao));
        services.AddOptions<AplicacaoSettings>().Bind(configuration.GetSection(AplicacaoSettings.Secao));
        services.AddOptions<ArmazenamentoSettings>().Bind(configuration.GetSection(ArmazenamentoSettings.Secao));
        services.AddOptions<AssinaturaSettings>().Bind(configuration.GetSection(AssinaturaSettings.Secao));
        services.AddOptions<ConviteSettings>().Bind(configuration.GetSection(ConviteSettings.Secao));
        services.AddOptions<IaSettings>().Bind(configuration.GetSection(IaSettings.Secao));
        services.AddOptions<ResumoSettings>().Bind(configuration.GetSection(ResumoSettings.Secao));
        services.AddOptions<MercadoPagoSettings>().Bind(configuration.GetSection(MercadoPagoSettings.Secao));
        services.AddOptions<ComunicacaoDoKapaSettings>().Bind(configuration.GetSection(ComunicacaoDoKapaSettings.Secao));

        services.AddValidatorsFromAssembly(typeof(DependenciasBusiness).Assembly, ServiceLifetime.Singleton);

        return services.AdicionarRemetenteDeEmail(configuration).AdicionarArmazenamento(configuration).AdicionarServices();
    }

    /// <summary>
    /// Registra o remetente de e-mail conforme a configuração.
    /// </summary>
    /// <remarks>
    /// Com <c>Smtp:ApiKeyDaMillionSend</c>, a API HTTP da MillionSend (produção); com <c>Smtp:Host</c>, SMTP (o Gmail de desenvolvimento). Sem nenhum dos dois, entra o remetente que apenas registra a mensagem no log. É o que
    /// permite o projeto subir e o fluxo de e-mail funcionar de ponta a ponta sem um servidor
    /// SMTP à mão — em desenvolvimento e nos testes.
    /// <para>
    /// Singleton, e não scoped: o remetente não guarda estado por requisição, e quem o consome é
    /// um <c>BackgroundService</c> — que é singleton e não pode depender de serviço scoped.
    /// </para>
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    private static IServiceCollection AdicionarRemetenteDeEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var smtp = configuration.GetSection(SmtpSettings.Secao).Get<SmtpSettings>();

        if (!string.IsNullOrWhiteSpace(smtp?.ApiKeyDaMillionSend))
        {
            if (string.IsNullOrWhiteSpace(smtp.UrlDasImagens))
                throw new InvalidOperationException("Smtp:ApiKeyDaMillionSend exige Smtp:UrlDasImagens: a MillionSend não aceita imagem por cid.");

            services.AddSingleton<SmtpEmailSender>();
            services.AddSingleton<IEmailSender>(sp => ActivatorUtilities.CreateInstance<MillionSendEmailSender>(sp, ClienteHttpDaMillionSend));
        }
        else if (smtp?.Configurado == true)
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, EmailSenderDeLog>();

        return services;
    }

    /// <summary>
    /// Registra o provedor de armazenamento conforme a configuração.
    /// </summary>
    /// <remarks>
    /// A escolha acontece uma vez, aqui. Nenhum outro ponto do projeto sabe qual provedor está
    /// ativo — todos falam com <see cref="IArmazenamentoDeArquivos"/>.
    /// <para>
    /// O cliente do S3 é montado à mão em vez de usar <c>AWSSDK.Extensions.NETCore.Setup</c>: são
    /// poucas linhas, e evita mais um pacote só para ler duas chaves de configuração. As
    /// credenciais continuam vindo da cadeia padrão do SDK.
    /// </para>
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    private static IServiceCollection AdicionarArmazenamento(this IServiceCollection services, IConfiguration configuration)
    {
        var armazenamento = configuration.GetSection(ArmazenamentoSettings.Secao).Get<ArmazenamentoSettings>() ?? new ArmazenamentoSettings();

        services.AddSingleton<UrlTemporariaLocal>();

        if (armazenamento.Provedor is not EProvedorDeArmazenamento.S3)
        {
            services.AddSingleton<IArmazenamentoDeArquivos, ArmazenamentoLocal>();

            return services;
        }

        services.AddSingleton<IAmazonS3>(_ => CriarClienteS3(armazenamento.S3));
        services.AddSingleton<IArmazenamentoDeArquivos, ArmazenamentoS3>();

        return services;
    }

    /// <summary>Cliente do S3 ou, com <c>ServiceUrl</c>, de um serviço compatível.</summary>
    /// <remarks>
    /// Em serviço compatível, o checksum só vai quando a operação exige. O SDK passou a mandá-lo em
    /// todo envio, num corpo com trailer (<c>STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER</c>) que o
    /// Cloudflare R2 recusa com "not implemented" — conferido contra o bucket de produção em 25/09/2026.
    /// </remarks>
    /// <param name="s3">Configuração do provedor.</param>
    private static AmazonS3Client CriarClienteS3(S3Settings s3)
    {
        var configuracao = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(s3.Regiao))
            configuracao.RegionEndpoint = RegionEndpoint.GetBySystemName(s3.Regiao);

        if (!string.IsNullOrWhiteSpace(s3.ServiceUrl))
        {
            configuracao.ServiceURL = s3.ServiceUrl;
            configuracao.ForcePathStyle = true;
            configuracao.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            configuracao.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        }

        return new AmazonS3Client(configuracao);
    }

    /// <summary>
    /// O <see cref="HttpClient"/> do <see cref="ClienteDeModelo"/>: um só, com conexões recicladas.
    /// </summary>
    /// <remarks>
    /// Instância única com <c>PooledConnectionLifetime</c> é a alternativa da própria Microsoft ao
    /// <c>IHttpClientFactory</c> — reaproveita conexões e ainda enxerga a troca de DNS, sem trazer
    /// <c>Microsoft.Extensions.Http</c> para o worker. Sem <c>Timeout</c> aqui: o tempo limite é por
    /// chamada, em <c>IA:SegundosDeEspera</c>.
    /// </remarks>
    private static readonly HttpClient ClienteHttpDaIa = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>O <see cref="HttpClient"/> do <see cref="ClienteDoMercadoPago"/>, pelo mesmo motivo do da IA.</summary>
    /// <remarks>Sem <c>Timeout</c> aqui: o tempo limite é por chamada, em <c>MercadoPago:SegundosDeEspera</c>.</remarks>
    private static readonly HttpClient ClienteHttpDoMercadoPago = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>O <see cref="HttpClient"/> do <see cref="MillionSendEmailSender"/>, pelo mesmo motivo do da IA.</summary>
    private static readonly HttpClient ClienteHttpDaMillionSend = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static IServiceCollection AdicionarServices(this IServiceCollection services)
    {
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<TentativasDeSenha>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IContaService, ContaService>();
        services.AddScoped<IEmailsDeConta, EmailsDeConta>();
        services.AddScoped<IFormaturaService, FormaturaService>();
        services.AddScoped<IRetencaoDeFormaturasService, RetencaoDeFormaturasService>();
        services.AddScoped<IMembroService, MembroService>();
        services.AddScoped<EmailsDeDesligamento>();
        services.AddScoped<EmailsDePapel>();
        services.AddScoped<IConviteService, ConviteService>();
        services.AddScoped<IPerfilService, PerfilService>();
        services.AddScoped<ICobrancaService, PlanoDeCobrancaService>();
        services.AddScoped<IConsultaDeParcelasService, ConsultaDeParcelasService>();
        services.AddScoped<IGeracaoDeParcelasService, GeracaoDeParcelasService>();
        services.AddScoped<IOpcionaisService, OpcionaisService>();
        services.AddScoped<PedidoService>();
        services.AddScoped<IPedidoService>(sp => sp.GetRequiredService<PedidoService>());
        services.AddScoped<IQuitacaoDePedidos>(sp => sp.GetRequiredService<PedidoService>());
        services.AddScoped<AberturaDeSolicitacao>();
        services.AddScoped<ISolicitacaoDeCancelamentoService, SolicitacaoDeCancelamentoService>();
        services.AddScoped<ILancamentoAvulsoService, LancamentoAvulsoService>();
        services.AddScoped<EmissaoDeConvites>();
        services.AddScoped<EmailsDoConvite>();
        services.AddScoped<IConviteDoEventoService, ConviteDoEventoService>();
        services.AddScoped<IGestaoDeConvitesService, GestaoDeConvitesService>();
        services.AddScoped<IPortariaService, Portaria>();
        services.AddScoped<IPainelDeConvitesService, PainelDeConvitesService>();
        services.AddScoped<IMesaService, MesaService>();
        services.AddScoped<DonosDeMesa>();
        services.AddSingleton<CodigoDoConvite>();
        services.AddScoped<ILojaService, LojaService>();
        services.AddScoped<IComprasDaLojaService, ComprasDaLojaService>();
        services.AddScoped<ICancelamentoDaCompraService, CancelamentoDaCompra>();
        services.AddScoped<PagamentoDaCompra>();
        services.AddScoped<ExpiracaoDaCompra>();
        services.AddScoped<EmailsDaLoja>();
        services.AddSingleton<LinkDaCompra>();
        services.AddScoped<ITermoService, TermoService>();
        services.AddScoped<IAdesaoService, AdesaoService>();
        services.AddScoped<IAditivoService, AditivoService>();
        services.AddScoped<EmailsDeAdesao>();
        services.AddScoped<IResumoDoTermoService, ResumoDoTermoService>();
        services.AddSingleton<IModeloDeLinguagem>(sp => new ClienteDeModelo(
            ClienteHttpDaIa,
            sp.GetRequiredService<IOptions<IaSettings>>(),
            sp.GetRequiredService<ILogger<ClienteDeModelo>>()
        ));
        services.AddScoped<IContaDeRecebimentoService, ContaDeRecebimentoService>();
        services.AddScoped<EmailsDeRecebimento>();
        services.AddScoped<IItemDaFestaService, ItemDaFestaService>();
        services.AddScoped<IAgendaService, AgendaService>();
        services.AddScoped<IPropostaService, PropostaService>();
        services.AddScoped<IFornecedorService, FornecedorService>();
        services.AddScoped<IDespesaService, DespesaService>();
        services.AddScoped<IOutraReceitaService, OutraReceitaService>();
        services.AddScoped<ICaixaService, CaixaService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IRelatorioService, RelatorioService>();
        services.AddScoped<IGeracaoDeRelatoriosService, GeracaoDeRelatoriosService>();
        services.AddScoped<IAvisoService, AvisoService>();
        services.AddScoped<IDocumentoService, DocumentoService>();
        services.AddScoped<IPagamentoService, PagamentoService>();
        services.AddScoped<IReciboService, ReciboService>();
        services.AddScoped<ITesourariaService, TesourariaService>();
        services.AddScoped<IValoresADevolverService, ValoresADevolverService>();
        services.AddScoped<ValoresADevolver>();
        services.AddScoped<EstornoDaCobranca>();
        services.AddScoped<INotificacaoService, NotificacaoService>();
        services.AddScoped<IReguaService, ReguaService>();
        services.AddScoped<ICanalDeNotificacao, CanalDeEmail>();
        services.AddScoped<BaixaService>();
        services.AddScoped<BaixaAutomatica>();
        services.AddScoped<IAvisoDoMercadoPago, AvisoDoMercadoPago>();
        services.AddScoped<IProvedorDaTurmaService, ProvedorDaTurmaService>();
        services.AddScoped<EmissaoNoMercadoPago>();
        services.TryAddScoped<FormaturaDoProcessamento>();
        services.TryAddSingleton<IFilaDaTurma, SemFila>();
        services.AddSingleton<IMercadoPago>(sp => new ClienteDoMercadoPago(
            ClienteHttpDoMercadoPago,
            sp.GetRequiredService<IOptions<MercadoPagoSettings>>(),
            sp.GetRequiredService<ILogger<ClienteDoMercadoPago>>()
        ));
        services.AddScoped<EmailsDePagamento>();
        services.AddScoped<ILegalService, LegalService>();
        services.AddScoped<IAuditoriaService, AuditoriaService>();
        services.AddScoped<IBuscaService, BuscaService>();
        services.AddScoped<IPrivacidadeService, PrivacidadeService>();
        services.AddScoped<IProcessamentoDePrivacidadeService, ProcessamentoDePrivacidadeService>();
        services.AddScoped<IAnonimizacaoDeTitular, AnonimizacaoDeTitular>();
        services.AddScoped<EmailsDePrivacidade>();
        services.AddScoped<IComunicacaoDoKapaService, ComunicacaoDoKapaService>();
        services.AddScoped<IJornadasDeMarketingService, JornadasDeMarketingService>();
        services.AddScoped<EmailsDeMarketing>();
        services.AddSingleton<LinkDeDescadastro>();
        services.AddSingleton<ConfirmacaoPorEmail>();
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IAmostraDeEmails, AmostraDeEmails>();
        services.AddScoped<IProcessamentoDaFilaDeEmail, ProcessamentoDaFilaDeEmail>();
        services.AddScoped<IArquivoService, ArquivoService>();
        services.AddScoped<IAssinaturaService, AssinaturaService>();
        services.AddScoped<CupomService>();
        services.AddScoped<VagasDoPlano>();
        services.AddScoped<EstornoDaAssinatura>();
        services.AddScoped<IWebhookService, WebhookService>();
        services.AddScoped<EmailsDeAssinatura>();

        return services;
    }
}

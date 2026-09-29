using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Busca.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Convites.Interfaces;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Usuarios.Interfaces;
using Backend.Data.Context;
using Backend.Data.Criptografia;
using Backend.Data.Provedores;
using Backend.Data.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.Data;

/// <summary>
/// Registro do contexto, da unidade de trabalho e dos repositórios.
/// </summary>
/// <remarks>Ao criar um repositório, registre-o em <c>AdicionarRepositorios</c>.</remarks>
public static class DependenciasData
{
    /// <summary>Nome da string de conexão esperada na configuração.</summary>
    public const string NomeDaConexao = "Postgres";

    /// <summary>
    /// Nome que amarra as chaves do DataProtection a esta aplicação, igual na API e no Worker.
    /// </summary>
    /// <remarks>
    /// As chaves vão para o banco (<see cref="AppDbContext.DataProtectionKeys"/>): no disco do contêiner,
    /// cada deploy e cada réplica tinham as suas, e o link de redefinir senha ou confirmar e-mail
    /// enviado por uma parava de valer na outra — ou no deploy seguinte.
    /// </remarks>
    public const string NomeDaAplicacaoNaProtecao = "kapa";

    /// <summary>Registra o acesso a dados.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    /// <exception cref="InvalidOperationException">Se a string de conexão não estiver configurada.</exception>
    public static IServiceCollection AddData(this IServiceCollection services, IConfiguration configuration)
    {
        var conexao =
            configuration.GetConnectionString(NomeDaConexao)
            ?? throw new InvalidOperationException($"A string de conexão '{NomeDaConexao}' não está configurada.");

        services.AddDbContext<AppDbContext>(opcoes =>
            opcoes.UseNpgsql(conexao, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)).UseSnakeCaseNamingConvention()
        );

        services
            .AddOptions<CriptografiaSettings>()
            .Bind(configuration.GetSection(CriptografiaSettings.Secao))
            .Validate(settings => settings.ChaveValida(), $"'{CriptografiaSettings.Secao}:ChaveDeDados' precisa ser uma chave de 32 bytes em Base64.")
            .ValidateOnStart();
        services.AddSingleton<CifraDeCampo>();

        services.AddFormaturaAtualPadrao();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddDataProtection().SetApplicationName(NomeDaAplicacaoNaProtecao).PersistKeysToDbContext<AppDbContext>();

        return services.AdicionarRepositorios().AdicionarProvedorDeAssinatura(configuration);
    }

    /// <summary>
    /// Registra o PSP da licença conforme <c>Assinaturas:Provedor</c>.
    /// </summary>
    /// <remarks>
    /// Singleton: o provedor não guarda estado por requisição (o fake guarda as sessões dele, que
    /// precisam sobreviver entre o checkout e o pagamento).
    /// <para>
    /// Os avisos da conta do Kapa no Mercado Pago acompanham a escolha: com o fake, só são registrados. Mercado Pago
    /// sem o token da conta do Kapa recusa a partida — a primeira turma a contratar descobriria com um 503.
    /// </para>
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">De onde sai <c>Assinaturas:Provedor</c>.</param>
    private static IServiceCollection AdicionarProvedorDeAssinatura(this IServiceCollection services, IConfiguration configuration)
    {
        var provedor = configuration.GetValue($"{AssinaturaSettings.Secao}:{nameof(AssinaturaSettings.Provedor)}", EProvedorDeAssinatura.Fake);

        if (provedor == EProvedorDeAssinatura.MercadoPago)
        {
            if (string.IsNullOrWhiteSpace(configuration[$"{MercadoPagoSettings.Secao}:{nameof(MercadoPagoSettings.AccessTokenDoKapa)}"]))
                throw new InvalidOperationException(
                    "'Assinaturas:Provedor' é MercadoPago, mas 'MercadoPago:AccessTokenDoKapa' está vazio: sem o token da conta do Kapa nenhuma turma contrata."
                );

            services.AddSingleton<ProvedorMercadoPago>();
            services.AddSingleton<IProvedorDeAssinatura>(sp => sp.GetRequiredService<ProvedorMercadoPago>());
            services.AddScoped<IAvisosDaContaDoKapa, AvisosDaContaDoKapa>();

            return services;
        }

        services.AddSingleton<ProvedorFake>();
        services.AddSingleton<IProvedorDeAssinatura>(sp => sp.GetRequiredService<ProvedorFake>());
        services.AddScoped<IAvisosDaContaDoKapa, AvisosDaContaDoKapaSemProvedor>();

        return services;
    }

    /// <summary>
    /// Registra o contexto de formatura padrão: nenhuma selecionada.
    /// </summary>
    /// <remarks>
    /// Serve onde não há requisição HTTP: o <c>Backend.Worker</c>, a CLI do EF Core e as
    /// migrações em tempo de projeto. Sem ele, os três falhariam ao resolver o
    /// <c>AppDbContext</c>. A Api troca este registro pela implementação que lê a claim do token
    /// (<c>Replace</c> em <c>ApiConfig</c>), então a ordem entre <c>AddData</c> e <c>AddApi</c>
    /// não importa.
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    private static IServiceCollection AddFormaturaAtualPadrao(this IServiceCollection services)
    {
        services.TryAddScoped<IFormaturaAtual, SemFormaturaSelecionada>();

        return services;
    }

    private static IServiceCollection AdicionarRepositorios(this IServiceCollection services)
    {
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IEmailFilaRepository, EmailFilaRepository>();
        services.AddScoped<IEventoRepository, EventoRepository>();
        services.AddScoped<IBuscaRepository, BuscaRepository>();
        services.AddScoped<IArquivoRepository, ArquivoRepository>();
        services.AddScoped<IVinculoRepository, VinculoRepository>();
        services.AddScoped<IConviteRepository, ConviteRepository>();
        services.AddScoped<IFormaturaRepository, FormaturaRepository>();
        services.AddScoped<IRetencaoDeFormaturasRepository, RetencaoDeFormaturasRepository>();
        services.AddScoped<ILegalRepository, LegalRepository>();
        services.AddScoped<IAssinaturaRepository, AssinaturaRepository>();
        services.AddScoped<IPerfilRepository, PerfilRepository>();
        services.AddScoped<IPlanoDeCobrancaRepository, PlanoDeCobrancaRepository>();
        services.AddScoped<IParcelaRepository, ParcelaRepository>();
        services.AddScoped<IPedidoRepository, PedidoRepository>();
        services.AddScoped<IConviteDoEventoRepository, ConviteDoEventoRepository>();
        services.AddScoped<ICompraDeConviteRepository, CompraDeConviteRepository>();
        services.AddScoped<IPendenciasDaTurmaRepository, PendenciasDaTurmaRepository>();
        services.AddScoped<IMesaRepository, MesaRepository>();
        services.AddScoped<IAdesaoRepository, AdesaoRepository>();
        services.AddScoped<IContaDeRecebimentoRepository, ContaDeRecebimentoRepository>();
        services.AddScoped<IInformeRepository, InformeRepository>();
        services.AddScoped<IProvedorDaTurmaRepository, ProvedorDaTurmaRepository>();
        services.AddScoped<IRecebimentoRepository, RecebimentoRepository>();
        services.AddScoped<IItemDaFestaRepository, ItemDaFestaRepository>();
        services.AddScoped<IEventoDaTurmaRepository, EventoDaTurmaRepository>();
        services.AddScoped<IPropostaRepository, PropostaRepository>();
        services.AddScoped<IFornecedorRepository, FornecedorRepository>();
        services.AddScoped<IDespesaRepository, DespesaRepository>();
        services.AddScoped<IOutraReceitaRepository, OutraReceitaRepository>();
        services.AddScoped<ICaixaRepository, CaixaRepository>();
        services.AddScoped<IRelatorioRepository, RelatorioRepository>();
        services.AddScoped<ISolicitacaoDeRelatorioRepository, SolicitacaoDeRelatorioRepository>();
        services.AddScoped<IAvisoRepository, AvisoRepository>();
        services.AddScoped<INotificacaoRepository, NotificacaoRepository>();
        services.AddScoped<IDocumentoRepository, DocumentoRepository>();
        services.AddScoped<IPrivacidadeRepository, PrivacidadeRepository>();
        services.AddScoped<IComunicacaoDoKapaRepository, ComunicacaoDoKapaRepository>();

        return services;
    }
}

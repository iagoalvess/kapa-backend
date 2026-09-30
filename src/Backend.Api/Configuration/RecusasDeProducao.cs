using Backend.Business.Assinaturas.Settings;

namespace Backend.Api.Configuration;

/// <summary>
/// Configurações que não podem subir em produção: a API se recusa a iniciar com elas.
/// </summary>
/// <remarks>
/// Recusar na subida, e não ignorar em silêncio, é de propósito: quem deixou a chave ligada precisa descobrir no
/// deploy, não seis meses depois. O que só é perigoso, e não um buraco, fica em <see cref="AvisosDeProducao"/>.
/// </remarks>
public static class RecusasDeProducao
{
    /// <summary>Lança se a configuração de produção abre a plataforma.</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <c>Seed:AoIniciar</c> — seed em produção é a conta de administrador com senha de arquivo de configuração,
    /// e é assim que um produto estreia com credencial conhecida. A conta inicial de produção é criada uma vez, à
    /// mão (checklist da Sprint 16).
    /// </item>
    /// <item>
    /// <c>Assinaturas:Provedor</c> Fake — o provedor fake ativa qualquer turma sem cobrar nada: em produção ele é
    /// um buraco, não um modo de teste (Sprint 37).
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="app">Aplicação web.</param>
    /// <exception cref="InvalidOperationException">Uma das configurações acima está ligada em produção.</exception>
    public static WebApplication RecusarConfiguracaoInseguraDeProducao(this WebApplication app)
    {
        if (!app.Environment.IsProduction())
            return app;

        if (app.Configuration.GetValue<bool>("Seed:AoIniciar"))
            throw new InvalidOperationException(
                "'Seed:AoIniciar' não pode ficar ligado em produção: a conta inicial é criada manualmente. Ver docs/operacao.md."
            );

        if (app.Configuration.GetValue("Assinaturas:Provedor", EProvedorDeAssinatura.Fake) == EProvedorDeAssinatura.Fake)
            throw new InvalidOperationException(
                "'Assinaturas:Provedor' não pode ser Fake em produção: configure MercadoPago e o token da conta do Kapa. Ver docs/deploy.md."
            );

        return app;
    }
}

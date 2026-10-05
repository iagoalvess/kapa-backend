using System.Reflection;
using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;
using Backend.Business.Agenda.Models;
using Backend.Business.Arquivos.Models;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Auth.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Convites.Models;
using Backend.Business.Emails.Models;
using Backend.Business.Eventos.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Loja.Models;
using Backend.Business.Marketing.Models;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Privacidade.Models;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Relatorios.Models;
using Backend.Business.Usuarios.Models;
using Backend.Data.Criptografia;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Backend.Data.Context;

/// <summary>
/// Contexto único da aplicação: Identity e domínio na mesma cadeia de migrations.
/// </summary>
/// <remarks>
/// Dois contextos significariam duas cadeias de migration, duas transações e nenhuma garantia
/// entre elas. A separação só se justificaria com bancos fisicamente separados.
/// <para>
/// O mapeamento das entidades **não** fica aqui: cada uma tem seu
/// <c>IEntityTypeConfiguration</c> em <c>Mappings/</c>, carregado por varredura do assembly.
/// É o que impede este arquivo de virar um <c>OnModelCreating</c> de 800 linhas.
/// </para>
/// <para>
/// O isolamento por formatura também é convenção, não configuração: quem herda de
/// <see cref="EntidadeDaFormatura"/> ganha filtro global e índice sozinho, e recebe o
/// <c>FormaturaId</c> carimbado na gravação.
/// </para>
/// </remarks>
/// <param name="options">Opções de configuração do contexto.</param>
/// <param name="formaturaAtual">Formatura da requisição em curso.</param>
/// <param name="cifra">Cifra das colunas sensíveis, entregue ao mapeamento que a usa.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options, IFormaturaAtual formaturaAtual, CifraDeCampo cifra)
    : IdentityDbContext<Usuario, Perfil, Guid>(options),
        IDataProtectionKeyContext
{
    /// <summary>Chaves do DataProtection, compartilhadas entre réplicas e deploys.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Refresh tokens emitidos.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Fila de e-mails aguardando envio.</summary>
    public DbSet<EmailNaFila> EmailsFila => Set<EmailNaFila>();

    /// <summary>Eventos de uso registrados pela aplicação.</summary>
    public DbSet<Evento> Eventos => Set<Evento>();

    /// <summary>Metadados dos arquivos armazenados.</summary>
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();

    /// <summary>Formaturas cadastradas.</summary>
    public DbSet<Formatura> Formaturas => Set<Formatura>();

    /// <summary>Vínculos entre usuário e formatura.</summary>
    public DbSet<VinculoDeFormatura> Vinculos => Set<VinculoDeFormatura>();

    /// <summary>Convites para entrar numa formatura.</summary>
    public DbSet<Convite> Convites => Set<Convite>();

    /// <summary>Registro de quem entrou por qual convite.</summary>
    public DbSet<AceiteDeConvite> AceitesDeConvite => Set<AceiteDeConvite>();

    /// <summary>Recados do mural de cada formatura.</summary>
    public DbSet<Aviso> Avisos => Set<Aviso>();

    /// <summary>Catálogo de planos da licença.</summary>
    public DbSet<Plano> Planos => Set<Plano>();

    /// <summary>Assinaturas da licença, por formatura.</summary>
    public DbSet<Assinatura> Assinaturas => Set<Assinatura>();

    /// <summary>Eventos recebidos do provedor de assinatura.</summary>
    public DbSet<EventoDeCobranca> EventosDeCobranca => Set<EventoDeCobranca>();

    /// <summary>Pagamentos do plano: o PIX de cada ciclo, a diferença de plano e os débitos do cartão.</summary>
    public DbSet<CobrancaDaAssinatura> CobrancasDaAssinatura => Set<CobrancaDaAssinatura>();

    /// <summary>Versões publicadas dos documentos legais da plataforma.</summary>
    public DbSet<DocumentoLegal> DocumentosLegais => Set<DocumentoLegal>();

    /// <summary>Registros de aceite e revogação dos documentos legais.</summary>
    public DbSet<ConsentimentoRegistrado> Consentimentos => Set<ConsentimentoRegistrado>();

    /// <summary>Cadastros dos formandos, um por vínculo.</summary>
    public DbSet<PerfilDoFormando> PerfisDeFormandos => Set<PerfilDoFormando>();

    /// <summary>Correções feitas pela comissão no cadastro de um formando.</summary>
    public DbSet<CorrecaoDePerfil> CorrecoesDePerfil => Set<CorrecaoDePerfil>();

    /// <summary>Planos de cobrança de cada formatura.</summary>
    public DbSet<PlanoDeCobranca> PlanosDeCobranca => Set<PlanoDeCobranca>();

    /// <summary>Itens dos planos de cobrança.</summary>
    public DbSet<ItemDeCobranca> ItensDeCobranca => Set<ItemDeCobranca>();

    /// <summary>Os pacotes que cada formando escolheu na adesão (Sprint 47).</summary>
    public DbSet<EscolhaDaCesta> EscolhasDaCesta => Set<EscolhaDaCesta>();

    /// <summary>Parcelas devidas pelos formandos, uma por vencimento.</summary>
    public DbSet<Parcela> Parcelas => Set<Parcela>();

    /// <summary>Pedidos dos opcionais: o que cada formando pediu só para ele.</summary>
    public DbSet<Pedido> Pedidos => Set<Pedido>();

    /// <summary>Solicitações de cancelamento de pacote ou pedido, esperando a comissão (Sprint 48, D8).</summary>
    public DbSet<SolicitacaoDeCancelamento> SolicitacoesDeCancelamento => Set<SolicitacaoDeCancelamento>();

    /// <summary>Versões do termo de adesão de cada formatura.</summary>
    public DbSet<TermoDaFormatura> TermosDeAdesao => Set<TermoDaFormatura>();

    /// <summary>Aceites do termo de adesão, com o plano congelado.</summary>
    public DbSet<AdesaoDoFormando> Adesoes => Set<AdesaoDoFormando>();

    /// <summary>Aditivos da adesão: o que o formando acrescentou à cesta depois (Sprint 48, D38).</summary>
    public DbSet<AditivoDaAdesao> AditivosDaAdesao => Set<AditivoDaAdesao>();

    /// <summary>Resumo gerado por IA de cada versão do termo (Sprint 24). Fora do que foi aceito.</summary>
    public DbSet<ResumoDoTermo> ResumosDeTermo => Set<ResumoDoTermo>();

    /// <summary>A chave PIX de cada formatura, uma por turma.</summary>
    public DbSet<ContaDeRecebimento> ContasDeRecebimento => Set<ContaDeRecebimento>();

    /// <summary>A autorização do Mercado Pago de cada turma, com os tokens cifrados (Sprint 25).</summary>
    public DbSet<CredencialDeProvedor> CredenciaisDeProvedor => Set<CredencialDeProvedor>();

    /// <summary>Os PIX dinâmicos emitidos pelo Mercado Pago das turmas (Sprint 25).</summary>
    public DbSet<CobrancaBancaria> CobrancasBancarias => Set<CobrancaBancaria>();

    /// <summary>Avisos de pagamento dos formandos, esperando a tesouraria.</summary>
    public DbSet<InformeDePagamento> Informes => Set<InformeDePagamento>();

    /// <summary>Entradas no caixa: as parcelas baixadas, com o valor e quem baixou.</summary>
    public DbSet<Recebimento> Recebimentos => Set<Recebimento>();

    /// <summary>A lista "a devolver" da tesouraria (Sprint 42).</summary>
    public DbSet<ValorADevolver> ValoresADevolver => Set<ValorADevolver>();

    /// <summary>Fornecedores contratados por cada formatura.</summary>
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();

    /// <summary>O que a turma está comprando: buffet, espaço, fotografia — o combinado, não o vencimento.</summary>
    public DbSet<ItemDaFesta> ItensDaFesta => Set<ItemDaFesta>();

    /// <summary>Candidatas a serem contratadas para um item "a contratar".</summary>
    public DbSet<PropostaDoItem> PropostasDoItem => Set<PropostaDoItem>();

    /// <summary>Em qual proposta cada formando votou — uma linha por formando por item.</summary>
    public DbSet<VotoNaProposta> VotosNasPropostas => Set<VotoNaProposta>();

    /// <summary>
    /// As datas da turma: colação, festa, reunião, prazo.
    /// </summary>
    /// <remarks>
    /// Desde a Sprint 19 é aqui que moram a colação e a festa. A <c>Formatura</c> não as guarda mais
    /// em coluna — o que a leitura dela devolve é projeção destas linhas.
    /// </remarks>
    public DbSet<EventoDaTurma> EventosDaTurma => Set<EventoDaTurma>();

    /// <summary>Convites dos eventos da turma: um por pessoa, com código e QR próprios (Sprint 21).</summary>
    public DbSet<ConviteDoEvento> ConvitesDoEvento => Set<ConviteDoEvento>();

    /// <summary>Entradas validadas na portaria — uma linha por validação, desfeita ou não.</summary>
    public DbSet<CheckIn> CheckIns => Set<CheckIn>();

    /// <summary>Mesas do jantar: nome, lugares e o dono da mesa vendida (Sprint 27).</summary>
    public DbSet<Mesa> Mesas => Set<Mesa>();

    /// <summary>O salão do jantar de cada turma: tamanho, palco, pista e o resto do mapa (28/09/2026).</summary>
    public DbSet<Salao> Saloes => Set<Salao>();

    /// <summary>Saídas do caixa: o que a turma deve e o que já pagou, uma linha por vencimento.</summary>
    public DbSet<Despesa> Despesas => Set<Despesa>();

    /// <summary>Receitas da turma que não vêm de formando (Sprint 28).</summary>
    public DbSet<OutraReceita> OutrasReceitas => Set<OutraReceita>();

    /// <summary>Compras da loja pública (Sprint 26).</summary>
    public DbSet<CompraDeConvite> ComprasDeConvite => Set<CompraDeConvite>();

    /// <summary>Pedidos de cancelamento do comprador da loja (Sprint 38, P1).</summary>
    public DbSet<PedidoDeCancelamento> PedidosDeCancelamento => Set<PedidoDeCancelamento>();

    /// <summary>Acervo de cada formatura: atas, contratos, orçamentos, regulamentos.</summary>
    public DbSet<Documento> Documentos => Set<Documento>();

    /// <summary>Relatórios pesados pedidos pela gestão, à espera do worker.</summary>
    public DbSet<SolicitacaoDeRelatorio> SolicitacoesDeRelatorio => Set<SolicitacaoDeRelatorio>();

    /// <summary>Os degraus da régua de cobrança de cada formatura.</summary>
    public DbSet<RegraDeNotificacao> RegrasDeNotificacao => Set<RegraDeNotificacao>();

    /// <summary>O que a régua já disparou — o histórico, e a trava contra a segunda mensagem.</summary>
    public DbSet<NotificacaoEnviada> NotificacoesEnviadas => Set<NotificacaoEnviada>();

    /// <summary>O que cada membro escolheu não receber.</summary>
    public DbSet<PreferenciaDeNotificacao> PreferenciasDeNotificacao => Set<PreferenciaDeNotificacao>();

    /// <summary>
    /// Pedidos de exportação e de eliminação feitos pelos titulares.
    /// </summary>
    /// <remarks>
    /// Sem <c>formatura_id</c>: o titular é a pessoa, e o pedido dela atravessa as turmas
    /// (decisão 2 da Sprint 14).
    /// </remarks>
    public DbSet<SolicitacaoDePrivacidade> SolicitacoesDePrivacidade => Set<SolicitacaoDePrivacidade>();

    /// <summary>Histórico da preferência "Receber novidades do Kapa" (Sprint 40). Append-only.</summary>
    public DbSet<ConsentimentoDeMarketing> ConsentimentosDeMarketing => Set<ConsentimentoDeMarketing>();

    /// <summary>Os e-mails de marketing que o Kapa mandou, um por pessoa, turma e jornada (Sprint 40).</summary>
    public DbSet<EnvioDeMarketing> EnviosDeMarketing => Set<EnvioDeMarketing>();

    /// <summary>
    /// Formatura que os filtros globais enxergam.
    /// </summary>
    /// <remarks>
    /// Pública e lida <b>a cada consulta</b>: o EF Core reconhece o acesso a membro da instância
    /// do contexto dentro de um filtro global e o reavalia por requisição. Capturar o valor no
    /// <c>OnModelCreating</c> — que roda uma vez por processo — congelaria a primeira formatura
    /// selecionada para todo mundo.
    /// </remarks>
    public Guid? FormaturaAtualId => formaturaAtual.Id;

    /// <inheritdoc />
    /// <remarks>
    /// A extensão <c>unaccent</c> é declarada aqui porque é do banco, não de uma entidade: é ela que faz
    /// a busca por nome achar "Júlia" quem digitou "julia" (ver <c>Repositories/Busca</c>).
    /// <para>
    /// A cifra do CPF (do perfil e da adesão) e do documento e e-mail do convidado da festa é a única configuração que mora aqui: ela precisa da chave, e os mappings
    /// são instanciados pela varredura sem parâmetro. O modelo é montado uma vez por processo,
    /// então o conversor guarda a cifra da primeira instância — a chave é a mesma para o processo
    /// inteiro, e é isso que se quer.
    /// </para>
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("unaccent");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        builder.Entity<PerfilDoFormando>().Property(p => p.Cpf).HasConversion(cifra.Conversor());
        builder.Entity<AdesaoDoFormando>().Property(a => a.Cpf).HasConversion((ValueConverter)cifra.Conversor());
        builder.Entity<ConviteDoEvento>().Property(c => c.NumeroDoDocumento).HasConversion(cifra.Conversor());
        builder.Entity<ConviteDoEvento>().Property(c => c.EmailDoConvidado).HasConversion(cifra.Conversor());
        builder.Entity<CompraDeConvite>().Property(c => c.Cpf).HasConversion(cifra.Conversor());
        builder.Entity<CompraDeConvite>().Property(c => c.Convidados).HasConversion(cifra.Conversor());
        builder.Entity<CompraDeConvite>().Property(c => c.CpfDoPagador).HasConversion(cifra.Conversor());
        builder.Entity<CredencialDeProvedor>().Property(c => c.AccessToken).HasConversion((ValueConverter)cifra.Conversor());
        builder.Entity<CredencialDeProvedor>().Property(c => c.RefreshToken).HasConversion((ValueConverter)cifra.Conversor());

        AplicarIsolamentoPorFormatura(builder);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AtualizadoEm</c> e <c>FormaturaId</c> são carimbados aqui, e não pelos services:
    /// auditoria e isolamento que dependem de alguém lembrar de escrever a linha são auditoria
    /// desatualizada e dado gravado na turma errada.
    /// <para>
    /// Na sobrecarga com <paramref name="acceptAllChangesOnSuccess"/>, e não na só com o token: a outra
    /// termina nesta, e o <c>UnitOfWork</c> chama esta direto — carimbar só na outra deixava a gravação
    /// da transação sem <c>FormaturaId</c>.
    /// </para>
    /// </remarks>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CarimbarAtualizacoes();
        CarimbarFormatura();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Aplica filtro global e índice a toda entidade que pertence a uma formatura.
    /// </summary>
    /// <remarks>
    /// Por reflexão sobre o modelo já construído, e não entidade a entidade: esquecer o
    /// <c>where</c> deixa de ser possível porque ninguém precisa lembrar dele. Quem esquece
    /// passa a ser o EF, e ele não esquece.
    /// </remarks>
    /// <param name="builder">Construtor do modelo.</param>
    private void AplicarIsolamentoPorFormatura(ModelBuilder builder)
    {
        var filtro = typeof(AppDbContext).GetMethod(nameof(FiltroDeFormatura), BindingFlags.Instance | BindingFlags.NonPublic)!;

        var tipos = builder
            .Model.GetEntityTypes()
            .Where(tipo => typeof(EntidadeDaFormatura).IsAssignableFrom(tipo.ClrType))
            .Select(tipo => tipo.ClrType)
            .ToList();

        foreach (var tipo in tipos)
        {
            filtro.MakeGenericMethod(tipo).Invoke(this, [builder]);
            builder.Entity(tipo).HasIndex(nameof(EntidadeDaFormatura.FormaturaId));
        }
    }

    /// <summary>
    /// Amarra a entidade à formatura da sessão.
    /// </summary>
    /// <remarks>
    /// Genérico, e chamado por reflexão, para o <c>lambda</c> ser escrito pelo compilador: é
    /// assim que o EF Core reconhece a referência ao contexto e reavalia
    /// <see cref="FormaturaAtualId"/> a cada consulta, em vez de congelar o valor no modelo.
    /// <para>
    /// Filtro <b>nomeado</b>: o sem nome é único por entidade, e esta convenção roda depois dos
    /// mappings — substituiria em silêncio um filtro declarado lá (soft delete, por exemplo).
    /// </para>
    /// </remarks>
    /// <typeparam name="TEntidade">Entidade que pertence a uma formatura.</typeparam>
    /// <param name="builder">Construtor do modelo.</param>
    private void FiltroDeFormatura<TEntidade>(ModelBuilder builder)
        where TEntidade : EntidadeDaFormatura =>
        builder.Entity<TEntidade>().HasQueryFilter(nameof(EntidadeDaFormatura.FormaturaId), entidade => entidade.FormaturaId == FormaturaAtualId);

    private void CarimbarAtualizacoes()
    {
        var agora = DateTime.UtcNow;

        foreach (var entrada in ChangeTracker.Entries().Where(e => e.State is EntityState.Modified))
        {
            switch (entrada.Entity)
            {
                case Entity entidade:
                    entidade.AtualizadoEm = agora;
                    break;
                case Usuario usuario:
                    usuario.AtualizadoEm = agora;
                    break;
            }
        }
    }

    /// <summary>
    /// Escreve a formatura dona em cada linha nova e recusa alterar ou remover linha de outra.
    /// </summary>
    /// <remarks>
    /// Gravar sem formatura selecionada é erro de programação — a linha iria para
    /// <c>Guid.Empty</c> e sumiria de toda consulta, silenciosamente. Melhor estourar na hora.
    /// <para>
    /// O filtro global só protege o que é <b>lido</b>. <c>Remove(new Aviso { Id = x })</c> ou um
    /// <c>Update</c> de objeto montado à mão viram <c>DELETE</c>/<c>UPDATE</c> por id, sem
    /// <c>where</c> de formatura. Entidade carregada pela consulta filtrada tem o
    /// <c>FormaturaId</c> original da sessão; qualquer outro valor é escrita fora da turma.
    /// Sem formatura na sessão (worker, CLI) a checagem não se aplica — ali a travessia de
    /// formaturas é explícita, via <c>DeTodasAsFormaturas</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Se não houver formatura selecionada ao inserir, ou se a linha alterada for de outra formatura.</exception>
    private void CarimbarFormatura()
    {
        foreach (var entrada in ChangeTracker.Entries<EntidadeDaFormatura>())
        {
            var propriedade = entrada.Property(nameof(EntidadeDaFormatura.FormaturaId));
            var nome = entrada.Entity.GetType().Name;

            switch (entrada.State)
            {
                case EntityState.Added:
                    propriedade.CurrentValue =
                        FormaturaAtualId ?? throw new InvalidOperationException($"Tentativa de gravar {nome} sem formatura selecionada na sessão.");
                    break;

                case EntityState.Modified
                or EntityState.Deleted when FormaturaAtualId is { } atual && !atual.Equals(propriedade.OriginalValue):
                    throw new InvalidOperationException($"Tentativa de alterar {nome} de outra formatura.");
            }
        }
    }
}

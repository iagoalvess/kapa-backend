using System.Reflection;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Auth.Models;
using Backend.Business.Emails.Models;
using Backend.Business.Eventos.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Usuarios.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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
public class AppDbContext(DbContextOptions<AppDbContext> options, IFormaturaAtual formaturaAtual) : IdentityDbContext<Usuario, Perfil, Guid>(options)
{
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

    /// <summary>Recados do mural de cada formatura.</summary>
    public DbSet<Aviso> Avisos => Set<Aviso>();

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
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        AplicarIsolamentoPorFormatura(builder);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AtualizadoEm</c> e <c>FormaturaId</c> são carimbados aqui, e não pelos services:
    /// auditoria e isolamento que dependem de alguém lembrar de escrever a linha são auditoria
    /// desatualizada e dado gravado na turma errada.
    /// </remarks>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CarimbarAtualizacoes();
        CarimbarFormatura();

        return base.SaveChangesAsync(cancellationToken);
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

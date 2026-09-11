using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Auth.Models;
using Backend.Business.Emails.Models;
using Backend.Business.Eventos.Models;
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
/// </remarks>
/// <param name="options">Opções de configuração do contexto.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<Usuario, Perfil, Guid>(options)
{
    /// <summary>Refresh tokens emitidos.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Fila de e-mails aguardando envio.</summary>
    public DbSet<EmailNaFila> EmailsFila => Set<EmailNaFila>();

    /// <summary>Eventos de uso registrados pela aplicação.</summary>
    public DbSet<Evento> Eventos => Set<Evento>();

    /// <summary>Metadados dos arquivos armazenados.</summary>
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AtualizadoEm</c> é carimbado aqui, e não pelos services: auditoria que depende de
    /// alguém lembrar de escrever a linha é auditoria que fica desatualizada.
    /// </remarks>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CarimbarAtualizacoes();

        return base.SaveChangesAsync(cancellationToken);
    }

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
}

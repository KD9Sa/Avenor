using Avenor.Application;
using Avenor.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avenor.Infrastructure;

public sealed class AvenorDbContext(DbContextOptions<AvenorDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public Guid ActiveTenant => tenant.TenantId ?? Guid.Empty;
    public bool Seeding
    {
        get; set;
    }
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<SupportUser> Users => Set<SupportUser>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> Messages => Set<TicketMessage>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Organization>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.Slug).HasMaxLength(64);
            b.Property(x => x.BusinessHours).HasMaxLength(100);
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasQueryFilter(x => x.Id == ActiveTenant);
        });
        model.Entity<SupportUser>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(80);
            b.Property(x => x.Email).HasMaxLength(254);
            b.Property(x => x.PasswordHash).HasMaxLength(512);
            b.Property(x => x.Company).HasMaxLength(100);
            b.Property(x => x.AvatarColor).HasMaxLength(20);
            b.HasOne<Organization>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(x => x.TenantId == ActiveTenant);
        });
        model.Entity<Ticket>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.TenantId, x.Id });
            b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status, x.UpdatedAt });
            b.HasIndex(x => new { x.TenantId, x.CustomerId, x.CreatedAt });
            b.HasIndex(x => new { x.TenantId, x.AssigneeId, x.Status });
            b.Property(x => x.Subject).HasMaxLength(180);
            b.Property(x => x.Description).HasMaxLength(10000);
            b.Property(x => x.Category).HasMaxLength(40);
            b.Property(x => x.TagsJson).HasMaxLength(1024);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => new { x.TenantId, x.CustomerId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => new { x.TenantId, x.AssigneeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(x => x.TenantId == ActiveTenant);
        });
        model.Entity<TicketMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Body).HasMaxLength(10000);
            b.HasOne(x => x.Ticket).WithMany().HasForeignKey(x => new { x.TenantId, x.TicketId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Author).WithMany().HasForeignKey(x => new { x.TenantId, x.AuthorId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.TicketId, x.CreatedAt });
            b.HasQueryFilter(x => x.TenantId == ActiveTenant);
        });
        model.Entity<SlaPolicy>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.Priority }).IsUnique();
            b.HasOne<Organization>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(x => x.TenantId == ActiveTenant);
            b.ToTable(t => { t.HasCheckConstraint("CK_Sla_Response", "ResponseMinutes > 0 AND ResponseMinutes <= 43200"); t.HasCheckConstraint("CK_Sla_Resolution", "ResolutionMinutes >= ResponseMinutes AND ResolutionMinutes <= 43200"); });
        });
        model.Entity<AuditEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(80);
            b.Property(x => x.Description).HasMaxLength(1000);
            b.Property(x => x.ActorName).HasMaxLength(80);
            b.HasOne<Organization>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
            b.HasQueryFilter(x => x.TenantId == ActiveTenant);
        });
        model.Entity<Notification>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(180);
            b.Property(x => x.Body).HasMaxLength(1000);
            b.HasOne<Organization>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
            b.HasQueryFilter(x => x.TenantId == ActiveTenant);
        });
        foreach (var type in model.Model.GetEntityTypes())
            foreach (var property in type.GetProperties().Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
    }

    private void ValidateWrites()
    {
        if (Seeding)
            return;
        foreach (var entry in ChangeTracker.Entries<TenantEntity>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (ActiveTenant == Guid.Empty || entry.Entity.TenantId != ActiveTenant)
                throw new InvalidOperationException("A tenant-scoped write was rejected.");
            if (entry.State == EntityState.Modified && entry.Property(x => x.TenantId).IsModified)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
        foreach (var entry in ChangeTracker.Entries<Organization>().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
            if (entry.Entity.Id != ActiveTenant)
                throw new InvalidOperationException("An organization write was rejected.");
        if (ChangeTracker.Entries<AuditEntry>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit history is immutable.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

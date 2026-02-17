using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<CashflowItem> CashflowItems => Set<CashflowItem>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<CashBox> CashBoxes => Set<CashBox>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<CashOperation> CashOperations => Set<CashOperation>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();

        modelBuilder.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        modelBuilder.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.PermissionId });

        modelBuilder.Entity<CashShift>()
            .HasIndex(x => new { x.CashBoxId, x.Status });

        base.OnModelCreating(modelBuilder);
    }
}

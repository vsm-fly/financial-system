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
        // ---- Security ----
        modelBuilder.Entity<User>()
            .HasIndex(x => x.Email)
            .IsUnique();

        // User -> CashBox (1 user (cashier) = 1 cashbox) через FK
        modelBuilder.Entity<User>()
            .HasOne(u => u.CashBox)              // если у тебя нет навигации CashBox в User — см. примечание ниже
            .WithMany()
            .HasForeignKey(u => u.CashBoxId)
            .OnDelete(DeleteBehavior.Restrict);

        // индекс на CashBoxId для быстрых проверок
        modelBuilder.Entity<User>()
            .HasIndex(u => u.CashBoxId);

        // ✅ Опционально: запретить двум пользователям иметь одну и ту же кассу
        // Работает хорошо, если CashBoxId назначается только кассирам.
        modelBuilder.Entity<User>()
            .HasIndex(u => u.CashBoxId)
            .IsUnique()
            .HasFilter("\"CashBoxId\" IS NOT NULL");

        modelBuilder.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        modelBuilder.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.PermissionId });

        // ---- Cash ----
        modelBuilder.Entity<CashShift>()
            .HasIndex(x => new { x.CashBoxId, x.Status });

        // ---- Ledger ----
        // ✅ DB-level защита от двойного проведения:
        // один и тот же документ не может создать две одинаковые проводки
        modelBuilder.Entity<LedgerEntry>()
            .HasIndex(x => new
            {
                x.SourceDocType,
                x.SourceDocId,
                x.MoneyLocationType,
                x.MoneyLocationId,
                x.Direction
            })
            .IsUnique();

        // (опционально) ускоряет отчеты/реестры
        modelBuilder.Entity<LedgerEntry>()
            .HasIndex(x => x.PostedAt);

        base.OnModelCreating(modelBuilder);
    }
}
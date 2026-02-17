using FinancialSystem.Domain.Entities;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, string adminEmail, string adminPassword)
    {
        // В MVP используем EnsureCreated (быстро). Для продакшена перейдем на миграции.
        await db.Database.EnsureCreatedAsync();

        // Roles
        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "ADMIN");
        if (adminRole is null)
        {
            adminRole = new Role { Code = "ADMIN", Name = "Администратор" };
            db.Roles.Add(adminRole);

            db.Roles.AddRange(
                new Role { Code = "CASHIER", Name = "Кассир" },
                new Role { Code = "FINANCE", Name = "Финансист" },
                new Role { Code = "MANAGER", Name = "Менеджер" },
                new Role { Code = "CEO", Name = "Руководитель" }
            );

            await db.SaveChangesAsync();
        }

        // Permissions
        if (!await db.Permissions.AnyAsync())
        {
            db.Permissions.AddRange(
                new Permission { Code = "LEDGER.READ", Name = "Просмотр леджера" },
                new Permission { Code = "CASH.READ", Name = "Просмотр кассы" },
                new Permission { Code = "CASH.WRITE", Name = "Операции по кассе" },
                new Permission { Code = "CASH.POST", Name = "Проведение кассовых операций" },
                new Permission { Code = "BANK.READ", Name = "Просмотр банков" },
                new Permission { Code = "BANK.IMPORT", Name = "Импорт выписок" },
                new Permission { Code = "ADMIN.ALL", Name = "Администрирование" }
            );
            await db.SaveChangesAsync();
        }

        // Admin user
        var admin = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Email == adminEmail);
        if (admin is null)
        {
            var (hash, salt) = PasswordHasher.HashPassword(adminPassword);
            admin = new User
            {
                Email = adminEmail,
                PasswordHash = hash,
                PasswordSalt = salt,
                IsActive = true
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();

            var adminRoleId = (await db.Roles.FirstAsync(r => r.Code == "ADMIN")).Id;
            db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRoleId });
            await db.SaveChangesAsync();
        }

        // Master data
        if (!await db.Currencies.AnyAsync(c => c.Code == "TJS"))
        {
            db.Currencies.AddRange(
                new Currency { Code = "TJS", Name = "Сомони", Precision = 2 },
                new Currency { Code = "USD", Name = "Доллар США", Precision = 2 },
                new Currency { Code = "RUB", Name = "Российский рубль", Precision = 2 }
            );
        }

        if (!await db.CashflowItems.AnyAsync())
        {
            db.CashflowItems.AddRange(
                new CashflowItem { Code = "CF.SALES", Name = "Поступления от продаж" },
                new CashflowItem { Code = "CF.SUPPLIERS", Name = "Оплаты поставщикам" },
                new CashflowItem { Code = "CF.TRANSFER", Name = "Перемещения/Инкассация" },
                new CashflowItem { Code = "CF.FEES", Name = "Комиссии" }
            );
        }

        await db.SaveChangesAsync();
    }
}

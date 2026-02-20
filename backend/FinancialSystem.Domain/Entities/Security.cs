namespace FinancialSystem.Domain.Entities;

public class User : Entity
{
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public bool IsActive { get; set; } = true;

    // 1 кассир = 1 касса (для CASHIER обязательно, для остальных ролей может быть null)
    public Guid? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class Role : Entity
{
    public required string Code { get; set; }  // ADMIN, CASHIER, FINANCE, MANAGER, CEO
    public required string Name { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission : Entity
{
    public required string Code { get; set; } // e.g. CASH.POST, BANK.IMPORT, LEDGER.READ
    public required string Name { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class UserRole
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }
}
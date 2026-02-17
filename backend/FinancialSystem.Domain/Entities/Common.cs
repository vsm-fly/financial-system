namespace FinancialSystem.Domain.Entities;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public enum MoneyLocationType
{
    CashBox = 1,
    BankAccount = 2
}

public enum Direction
{
    In = 1,
    Out = 2
}

public enum DocumentStatus
{
    Draft = 1,
    Posted = 2,
    Canceled = 3
}

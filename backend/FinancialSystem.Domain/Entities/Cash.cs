namespace FinancialSystem.Domain.Entities;

public class CashBox : Entity
{
    public required string Name { get; set; }
    public Guid? LegalEntityId { get; set; }
    public string CurrencyMode { get; set; } = "SINGLE"; // SINGLE/MULTI
    public bool IsActive { get; set; } = true;
}

public class CashShift : Entity
{
    public Guid CashBoxId { get; set; }
    public Guid CashierUserId { get; set; }

    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public decimal OpeningBalanceDeclared { get; set; }
    public decimal? ClosingBalanceDeclared { get; set; }
    public decimal? ClosingBalanceCalculated { get; set; }

    public string Status { get; set; } = "OPEN"; // OPEN/CLOSED
}

public enum CashOperationType
{
    Income = 1,
    Expense = 2,
    Refund = 3,
    Transfer = 4,
    Collection = 5
}

public class CashOperation : Entity
{
    public Guid CashBoxId { get; set; }
    public Guid? ShiftId { get; set; }
    public CashOperationType Type { get; set; }

    public decimal Amount { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal FxRate { get; set; } = 1m;
    public decimal AmountBase { get; set; }

    public Guid CashflowItemId { get; set; }
    public Guid? CounterpartyId { get; set; }
    public Guid? RelatedSaleId { get; set; }
    public Guid? RelatedBankAccountId { get; set; } // for collection

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public Guid CreatedBy { get; set; }
    public string? Comment { get; set; }
}

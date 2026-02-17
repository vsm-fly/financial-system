namespace FinancialSystem.Domain.Entities;

public class LedgerEntry : Entity
{
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public Direction Direction { get; set; }

    public decimal Amount { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal FxRate { get; set; } = 1m;
    public decimal AmountBase { get; set; }

    public MoneyLocationType MoneyLocationType { get; set; }
    public Guid MoneyLocationId { get; set; }

    public Guid CashflowItemId { get; set; }

    public Guid? LegalEntityId { get; set; }
    public Guid? CounterpartyId { get; set; }

    public Guid? SaleId { get; set; }
    public Guid? TourId { get; set; }
    public Guid? ManagerId { get; set; }
    public string? Channel { get; set; } // A/B

    public bool IsFee { get; set; }
    public bool IsRefund { get; set; }
    public bool IsCorrection { get; set; }

    public required string SourceDocType { get; set; }
    public required string SourceDocId { get; set; }

    public string? Comment { get; set; }
}

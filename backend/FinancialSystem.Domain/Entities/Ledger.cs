namespace FinancialSystem.Domain.Entities;

public class LedgerEntry : Entity
{
    /// <summary>
    /// Дата проведения (ставится сервисом при проведении документа).
    /// </summary>
    public DateTime PostedAt { get; set; }

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

    /// <summary>
    /// Канал продажи (например A/B)
    /// </summary>
    public string? Channel { get; set; }

    public bool IsFee { get; set; } = false;
    public bool IsRefund { get; set; } = false;
    public bool IsCorrection { get; set; } = false;

    /// <summary>
    /// Тип документа-источника (например "CashOperation")
    /// </summary>
    public required string SourceDocType { get; set; }

    /// <summary>
    /// Id документа-источника (обычно Guid.ToString())
    /// </summary>
    public required string SourceDocId { get; set; }

    public string? Comment { get; set; }
}

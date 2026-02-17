namespace FinancialSystem.Domain.Entities;

public static class CashConstants
{
    public const string CurrencyModeSingle = "SINGLE";
    public const string CurrencyModeMulti  = "MULTI";

    public const string ShiftStatusOpen   = "OPEN";
    public const string ShiftStatusClosed = "CLOSED";
}

public class CashBox : Entity
{
    public required string Name { get; set; }

    public Guid? LegalEntityId { get; set; }

    // SINGLE/MULTI
    public string CurrencyMode { get; set; } = CashConstants.CurrencyModeSingle;

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

    // OPEN/CLOSED
    public string Status { get; set; } = CashConstants.ShiftStatusOpen;
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

    // Может быть null для операций "вне смены" (если решишь разрешать).
    // Если НЕ нужно — убери nullable и валидацию делай жесткой.
    public Guid? ShiftId { get; set; }

    public CashOperationType Type { get; set; }

    // Сумма в валюте операции (CurrencyCode)
    public decimal Amount { get; set; }

    public required string CurrencyCode { get; set; }

    // Курс к базовой валюте (если CurrencyMode SINGLE — всегда 1)
    public decimal FxRate { get; set; } = 1m;

    // Сумма в базовой валюте.
    // ВАЖНО: лучше вычислять (Amount * FxRate) в сервисе перед сохранением,
    // чтобы не было рассинхрона.
    public decimal AmountBase { get; set; }

    public Guid CashflowItemId { get; set; }

    public Guid? CounterpartyId { get; set; }
    public Guid? RelatedSaleId { get; set; }

    // Например: для Collection (инкассация в банк) или Transfer (перемещение на банк/между кассами)
    public Guid? RelatedBankAccountId { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public Guid CreatedBy { get; set; }

    public string? Comment { get; set; }
}

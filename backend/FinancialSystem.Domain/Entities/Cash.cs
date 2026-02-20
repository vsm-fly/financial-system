using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Domain.Entities;

public static class CashConstants
{
    public const string CurrencyModeSingle = "SINGLE";
    public const string CurrencyModeMulti  = "MULTI";

    public const string ShiftStatusOpen   = "OPEN";
    public const string ShiftStatusClosed = "CLOSED";
}

[Index(nameof(Name), IsUnique = false)]
public class CashBox : Entity
{
    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    public Guid? LegalEntityId { get; set; }

    /// <summary>SINGLE / MULTI</summary>
    [Required]
    [MaxLength(10)]
    public string CurrencyMode { get; set; } = CashConstants.CurrencyModeSingle;

    public bool IsActive { get; set; } = true;
}

[Index(nameof(CashBoxId), nameof(Status))]
public class CashShift : Entity
{
    public Guid CashBoxId { get; set; }
    public Guid CashierUserId { get; set; }

    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>Фактический остаток на открытии (ввод кассиром)</summary>
    public decimal OpeningBalanceDeclared { get; set; }

    /// <summary>
    /// ✅ Расчетный остаток на момент открытия (по ledger, base).
    /// Нужен для правильного расчёта ClosingBalanceCalculated.
    /// </summary>
    public decimal OpeningBalanceCalculated { get; set; }

    /// <summary>Фактический остаток на закрытии (ввод кассиром)</summary>
    public decimal? ClosingBalanceDeclared { get; set; }

    /// <summary>Расчётный остаток на закрытии (OpeningCalculated + movements)</summary>
    public decimal? ClosingBalanceCalculated { get; set; }

    /// <summary>OPEN / CLOSED</summary>
    [Required]
    [MaxLength(10)]
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

[Index(nameof(CashBoxId), nameof(Status))]
[Index(nameof(ShiftId), nameof(Status))]
public class CashOperation : Entity
{
    public Guid CashBoxId { get; set; }

    /// <summary>
    /// Может быть null для операций "вне смены" (если решишь разрешать).
    /// </summary>
    public Guid? ShiftId { get; set; }

    public CashOperationType Type { get; set; }

    /// <summary>Сумма в валюте операции (CurrencyCode)</summary>
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(3)]
    public required string CurrencyCode { get; set; }

    /// <summary>Курс к базовой валюте (если CurrencyMode SINGLE — обычно 1)</summary>
    public decimal FxRate { get; set; } = 1m;

    /// <summary>Сумма в базовой валюте (Amount * FxRate, округление — в сервисе)</summary>
    public decimal AmountBase { get; set; }

    public Guid CashflowItemId { get; set; }

    public Guid? CounterpartyId { get; set; }
    public Guid? RelatedSaleId { get; set; }

    /// <summary>
    /// Для Collection (инкасация касса -> банк).
    /// Для Transfer между кассами лучше позже добавить отдельное поле RelatedCashBoxId (пока не реализовано).
    /// </summary>
    public Guid? RelatedBankAccountId { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public Guid CreatedBy { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}
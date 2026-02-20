using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Domain.Entities;

public class LegalEntity : Entity
{
    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(32)]
    public string? TaxId { get; set; }

    public bool IsActive { get; set; } = true;
}

[Index(nameof(Code), IsUnique = true)]
public class Currency : Entity
{
    /// <summary>ISO 4217 code: TJS, RUB, USD</summary>
    [Required]
    [MaxLength(3)]
    public required string Code { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Name { get; set; }

    /// <summary>Decimal places (обычно 2, иногда 0/3)</summary>
    [Range(0, 6)]
    public int Precision { get; set; } = 2;
}

[Index(nameof(BaseCurrencyCode), nameof(QuoteCurrencyCode), nameof(Date), IsUnique = true)]
public class ExchangeRate : Entity
{
    /// <summary>Base currency code, e.g. TJS</summary>
    [Required]
    [MaxLength(3)]
    public required string BaseCurrencyCode { get; set; }

    /// <summary>Quote currency code, e.g. USD</summary>
    [Required]
    [MaxLength(3)]
    public required string QuoteCurrencyCode { get; set; }

    /// <summary>1 Quote = Rate Base</summary>
    [Range(typeof(decimal), "0.00000001", "79228162514264337593543950335")]
    public decimal Rate { get; set; }

    public DateOnly Date { get; set; }
}

public enum CounterpartyType
{
    Customer = 1,
    Supplier = 2,
    Agent = 3
}

[Index(nameof(Name))]
public class Counterparty : Entity
{
    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    public CounterpartyType? Type { get; set; }
}

[Index(nameof(Code), IsUnique = true)]
public class CashflowItem : Entity
{
    [Required]
    [MaxLength(64)]
    public required string Code { get; set; }

    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;
}

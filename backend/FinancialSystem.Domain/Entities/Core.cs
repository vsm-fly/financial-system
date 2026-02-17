namespace FinancialSystem.Domain.Entities;

public class LegalEntity : Entity
{
    public required string Name { get; set; }
    public string? TaxId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Currency : Entity
{
    public required string Code { get; set; } // TJS, RUB, USD
    public required string Name { get; set; }
    public int Precision { get; set; } = 2;
}

public class ExchangeRate : Entity
{
    public required string BaseCurrencyCode { get; set; } // e.g. TJS
    public required string QuoteCurrencyCode { get; set; } // e.g. USD
    public decimal Rate { get; set; } // 1 Quote = Rate Base
    public DateOnly Date { get; set; }
}

public class Counterparty : Entity
{
    public required string Name { get; set; }
    public string? Type { get; set; } // Customer/Supplier/Agent
}

public class CashflowItem : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

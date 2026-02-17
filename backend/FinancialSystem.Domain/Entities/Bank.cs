namespace FinancialSystem.Domain.Entities;

public class BankAccount : Entity
{
    public required string BankName { get; set; }
    public required string AccountNo { get; set; }
    public string CurrencyCode { get; set; } = "TJS";
    public Guid? LegalEntityId { get; set; }
    public bool IsActive { get; set; } = true;
}

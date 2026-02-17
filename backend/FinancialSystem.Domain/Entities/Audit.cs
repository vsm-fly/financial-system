namespace FinancialSystem.Domain.Entities;

public class AuditLog : Entity
{
    public required Guid UserId { get; set; }
    public required string Action { get; set; }
    public required string ObjectType { get; set; }
    public required string ObjectId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
}

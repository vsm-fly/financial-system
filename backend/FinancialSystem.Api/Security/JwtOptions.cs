namespace FinancialSystem.Api.Security;

public class JwtOptions
{
    public string Issuer { get; set; } = "FinancialSystem";
    public string Audience { get; set; } = "FinancialSystem";
    public string Key { get; set; } = "super-dev-only-change-me-please-32chars";
    public int ExpMinutes { get; set; } = 60 * 12;
}

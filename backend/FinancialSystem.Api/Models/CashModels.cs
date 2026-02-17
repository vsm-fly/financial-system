using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Api.Models;

public record OpenShiftRequest(Guid CashBoxId, decimal OpeningBalanceDeclared);

public record CloseShiftRequest(decimal ClosingBalanceDeclared);

public record CreateCashOperationRequest(
    Guid CashBoxId,
    Guid? ShiftId,
    CashOperationType Type,
    decimal Amount,
    string CurrencyCode,
    decimal FxRate,
    Guid CashflowItemId,
    Guid? CounterpartyId,
    string? Comment,
    Guid? RelatedBankAccountId // для Collection (инкасация: касса -> банк)
);

// Для Refund можно явно указать направление (In/Out).
// Для остальных типов направление не требуется (Income/Expense определяются автоматически).
public record PostCashOperationRequest(Direction? Direction = null, bool IsRefund = false);

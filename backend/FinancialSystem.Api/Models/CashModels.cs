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
    string? Comment
);

// Для Refund/Transfer/Collection можно указать направление явно
public record PostCashOperationRequest(Direction? Direction, bool IsRefund = false);

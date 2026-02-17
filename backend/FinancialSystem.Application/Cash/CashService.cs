using System.Text.Json;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Cash;

public class CashService
{
    private readonly AppDbContext _db;
    private readonly CashRules _rules;

    public CashService(AppDbContext db, CashRules rules)
    {
        _db = db;
        _rules = rules;
    }

    public async Task<CashShift> OpenShiftAsync(Guid cashBoxId, Guid cashierUserId, decimal openingBalanceDeclared)
    {
        var hasOpen = await _db.CashShifts.AnyAsync(s => s.CashBoxId == cashBoxId && s.Status == "OPEN");
        if (hasOpen) throw new InvalidOperationException("В этой кассе уже есть открытая смена.");

        var shift = new CashShift
        {
            CashBoxId = cashBoxId,
            CashierUserId = cashierUserId,
            OpenedAt = DateTime.UtcNow,
            OpeningBalanceDeclared = openingBalanceDeclared,
            Status = "OPEN"
        };
        _db.CashShifts.Add(shift);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = cashierUserId,
            Action = "CASH.SHIFT.OPEN",
            ObjectType = "CashShift",
            ObjectId = shift.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { shift.Id, shift.CashBoxId, shift.OpenedAt, shift.OpeningBalanceDeclared })
        });

        await _db.SaveChangesAsync();
        return shift;
    }

    public async Task<CashShift> CloseShiftAsync(Guid shiftId, Guid userId, decimal closingBalanceDeclared)
    {
        var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == shiftId);
        if (shift is null) throw new InvalidOperationException("Смена не найдена.");
        if (shift.Status != "OPEN") throw new InvalidOperationException("Смена уже закрыта.");

        var calc = await GetCashBalanceBaseAsync(shift.CashBoxId);

        shift.ClosedAt = DateTime.UtcNow;
        shift.ClosingBalanceDeclared = closingBalanceDeclared;
        shift.ClosingBalanceCalculated = calc;
        shift.Status = "CLOSED";
        shift.UpdatedAt = DateTime.UtcNow;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "CASH.SHIFT.CLOSE",
            ObjectType = "CashShift",
            ObjectId = shift.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { shift.Id, shift.CashBoxId, shift.ClosedAt, shift.ClosingBalanceDeclared, shift.ClosingBalanceCalculated })
        });

        await _db.SaveChangesAsync();
        return shift;
    }

    public async Task<CashOperation> CreateOperationDraftAsync(
        Guid userId,
        Guid cashBoxId,
        Guid? shiftId,
        CashOperationType type,
        decimal amount,
        string currencyCode,
        decimal fxRate,
        Guid cashflowItemId,
        Guid? counterpartyId,
        string? comment)
    {
        if (amount <= 0) throw new InvalidOperationException("Amount должен быть > 0.");
        if (fxRate <= 0) throw new InvalidOperationException("FxRate должен быть > 0.");

        if (shiftId is not null)
        {
            var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == shiftId);
            if (shift is null) throw new InvalidOperationException("Смена не найдена.");
            if (shift.Status != "OPEN") throw new InvalidOperationException("Нельзя добавлять операции в закрытую смену.");
            if (shift.CashBoxId != cashBoxId) throw new InvalidOperationException("Смена не принадлежит этой кассе.");
        }

        var op = new CashOperation
        {
            CashBoxId = cashBoxId,
            ShiftId = shiftId,
            Type = type,
            Amount = amount,
            CurrencyCode = currencyCode,
            FxRate = fxRate,
            AmountBase = Math.Round(amount * fxRate, 2),
            CashflowItemId = cashflowItemId,
            CounterpartyId = counterpartyId,
            Status = DocumentStatus.Draft,
            CreatedBy = userId,
            Comment = comment
        };

        _db.CashOperations.Add(op);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "CASH.OP.CREATE_DRAFT",
            ObjectType = "CashOperation",
            ObjectId = op.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { op.Id, op.CashBoxId, op.Type, op.Amount, op.CurrencyCode, op.FxRate, op.CashflowItemId, op.Status })
        });

        await _db.SaveChangesAsync();
        return op;
    }

    public async Task<LedgerEntry> PostOperationAsync(Guid operationId, Guid userId, Direction? overrideDirection = null, bool isRefund = false)
    {
        var op = await _db.CashOperations.FirstOrDefaultAsync(o => o.Id == operationId);
        if (op is null) throw new InvalidOperationException("Операция не найдена.");
        if (op.Status != DocumentStatus.Draft) throw new InvalidOperationException("Провести можно только черновик.");

        var direction = op.Type switch
        {
            CashOperationType.Income => Direction.In,
            CashOperationType.Expense => Direction.Out,
            CashOperationType.Refund => overrideDirection ?? Direction.Out, // по умолчанию возврат денег клиенту
            _ => overrideDirection ?? throw new InvalidOperationException("Для этого типа операции нужно указать направление.")
        };

        // Запрет минус остатка (считаем по ledger в base currency)
        if (_rules.DisallowNegativeCash && direction == Direction.Out)
        {
            var current = await GetCashBalanceBaseAsync(op.CashBoxId);
            if (current - op.AmountBase < 0)
                throw new InvalidOperationException($"Недостаточно средств в кассе. Остаток={current}, нужно={op.AmountBase}.");
        }

        var entry = new LedgerEntry
        {
            PostedAt = DateTime.UtcNow,
            Direction = direction,
            Amount = op.Amount,
            CurrencyCode = op.CurrencyCode,
            FxRate = op.FxRate,
            AmountBase = op.AmountBase,
            MoneyLocationType = MoneyLocationType.CashBox,
            MoneyLocationId = op.CashBoxId,
            CashflowItemId = op.CashflowItemId,
            CounterpartyId = op.CounterpartyId,
            IsRefund = op.Type == CashOperationType.Refund || isRefund,
            SourceDocType = "CashOperation",
            SourceDocId = op.Id.ToString(),
            Comment = op.Comment
        };

        _db.LedgerEntries.Add(entry);

        op.Status = DocumentStatus.Posted;
        op.UpdatedAt = DateTime.UtcNow;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "CASH.OP.POST",
            ObjectType = "CashOperation",
            ObjectId = op.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { op.Id, op.CashBoxId, op.Type, op.AmountBase, direction, op.Status })
        });

        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<decimal> GetCashBalanceBaseAsync(Guid cashBoxId)
    {
        // Баланс = sum(In) - sum(Out) по AmountBase
        var sumIn = await _db.LedgerEntries
            .Where(e => e.MoneyLocationType == MoneyLocationType.CashBox && e.MoneyLocationId == cashBoxId && e.Direction == Direction.In)
            .SumAsync(e => (decimal?)e.AmountBase) ?? 0m;

        var sumOut = await _db.LedgerEntries
            .Where(e => e.MoneyLocationType == MoneyLocationType.CashBox && e.MoneyLocationId == cashBoxId && e.Direction == Direction.Out)
            .SumAsync(e => (decimal?)e.AmountBase) ?? 0m;

        return Math.Round(sumIn - sumOut, 2);
    }
}

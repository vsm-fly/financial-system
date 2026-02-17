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

    // =========================
    // SHIFT
    // =========================

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
            AfterJson = JsonSerializer.Serialize(new
            {
                shift.Id,
                shift.CashBoxId,
                shift.OpenedAt,
                shift.OpeningBalanceDeclared
            })
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
            AfterJson = JsonSerializer.Serialize(new
            {
                shift.Id,
                shift.CashBoxId,
                shift.ClosedAt,
                shift.ClosingBalanceDeclared,
                shift.ClosingBalanceCalculated
            })
        });

        await _db.SaveChangesAsync();
        return shift;
    }

    // =========================
    // CREATE DRAFT
    // =========================

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
        Guid? relatedBankAccountId,
        string? comment)
    {
        if (amount <= 0) throw new InvalidOperationException("Amount должен быть > 0.");
        if (fxRate <= 0) throw new InvalidOperationException("FxRate должен быть > 0.");

        // Проверка смены
        if (shiftId is not null)
        {
            var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == shiftId);
            if (shift is null) throw new InvalidOperationException("Смена не найдена.");
            if (shift.Status != "OPEN") throw new InvalidOperationException("Нельзя добавлять операции в закрытую смену.");
            if (shift.CashBoxId != cashBoxId) throw new InvalidOperationException("Смена не принадлежит этой кассе.");
        }

        // Валидация инкассации
        if (type == CashOperationType.Collection)
        {
            if (relatedBankAccountId is null || relatedBankAccountId == Guid.Empty)
                throw new InvalidOperationException("Для инкассации нужно указать BankAccountId.");

            var bankExists = await _db.BankAccounts
                .AnyAsync(b => b.Id == relatedBankAccountId && b.IsActive);

            if (!bankExists)
                throw new InvalidOperationException("Банковский счет не найден или не активен.");
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
            RelatedBankAccountId = relatedBankAccountId,
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
            AfterJson = JsonSerializer.Serialize(new
            {
                op.Id,
                op.CashBoxId,
                op.Type,
                op.Amount,
                op.CurrencyCode,
                op.FxRate,
                op.CashflowItemId,
                op.Status
            })
        });

        await _db.SaveChangesAsync();
        return op;
    }

    // =========================
    // POST STANDARD OPERATION
    // =========================

    public async Task<LedgerEntry> PostOperationAsync(
        Guid operationId,
        Guid userId,
        Direction? overrideDirection = null,
        bool isRefund = false)
    {
        var op = await _db.CashOperations.FirstOrDefaultAsync(o => o.Id == operationId);
        if (op is null) throw new InvalidOperationException("Операция не найдена.");

        var direction = op.Type switch
        {
            CashOperationType.Income => Direction.In,
            CashOperationType.Expense => Direction.Out,
            CashOperationType.Refund => overrideDirection ?? Direction.Out,
            _ => overrideDirection ?? throw new InvalidOperationException("Для этого типа операции нужно указать направление.")
        };

        var sourceDocType = "CashOperation";
        var sourceDocId = op.Id.ToString();

        // 1) Идемпотентность: если проводка уже есть — вернуть её
        var existing = await _db.LedgerEntries.FirstOrDefaultAsync(e =>
            e.SourceDocType == sourceDocType &&
            e.SourceDocId == sourceDocId &&
            e.MoneyLocationType == MoneyLocationType.CashBox &&
            e.MoneyLocationId == op.CashBoxId &&
            e.Direction == direction);

        if (existing is not null)
        {
            // На всякий случай синхронизируем статус документа
            if (op.Status != DocumentStatus.Posted)
            {
                op.Status = DocumentStatus.Posted;
                op.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return existing;
        }

        if (op.Status != DocumentStatus.Draft)
            throw new InvalidOperationException("Провести можно только черновик.");

        if (_rules.DisallowNegativeCash && direction == Direction.Out)
        {
            var current = await GetCashBalanceBaseAsync(op.CashBoxId);
            if (current - op.AmountBase < 0)
                throw new InvalidOperationException($"Недостаточно средств в кассе. Остаток={current}, нужно={op.AmountBase}.");
        }

        var now = DateTime.UtcNow;

        var entry = new LedgerEntry
        {
            PostedAt = now,
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
            SourceDocType = sourceDocType,
            SourceDocId = sourceDocId,
            Comment = op.Comment
        };

        _db.LedgerEntries.Add(entry);

        op.Status = DocumentStatus.Posted;
        op.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return entry;
    }

    // =========================
    // POST COLLECTION (CASH → BANK)
    // =========================

    public async Task<(LedgerEntry cashEntry, LedgerEntry bankEntry)> PostCollectionAsync(Guid operationId, Guid userId)
    {
        var op = await _db.CashOperations.FirstOrDefaultAsync(o => o.Id == operationId);
        if (op is null) throw new InvalidOperationException("Операция не найдена.");
        if (op.Type != CashOperationType.Collection) throw new InvalidOperationException("Это не инкассация.");

        if (op.RelatedBankAccountId is null)
            throw new InvalidOperationException("Не указан BankAccountId.");

        var sourceDocType = "CashOperation";
        var sourceDocId = op.Id.ToString();

        // 1) Идемпотентность: если обе проводки уже есть — вернуть их
        var existingCash = await _db.LedgerEntries.FirstOrDefaultAsync(e =>
            e.SourceDocType == sourceDocType &&
            e.SourceDocId == sourceDocId &&
            e.MoneyLocationType == MoneyLocationType.CashBox &&
            e.MoneyLocationId == op.CashBoxId &&
            e.Direction == Direction.Out);

        var existingBank = await _db.LedgerEntries.FirstOrDefaultAsync(e =>
            e.SourceDocType == sourceDocType &&
            e.SourceDocId == sourceDocId &&
            e.MoneyLocationType == MoneyLocationType.BankAccount &&
            e.MoneyLocationId == op.RelatedBankAccountId.Value &&
            e.Direction == Direction.In);

        if (existingCash is not null && existingBank is not null)
        {
            if (op.Status != DocumentStatus.Posted)
            {
                op.Status = DocumentStatus.Posted;
                op.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return (existingCash, existingBank);
        }

        if (op.Status != DocumentStatus.Draft)
            throw new InvalidOperationException("Провести можно только черновик.");

        if (_rules.DisallowNegativeCash)
        {
            var current = await GetCashBalanceBaseAsync(op.CashBoxId);
            if (current - op.AmountBase < 0)
                throw new InvalidOperationException($"Недостаточно средств в кассе. Остаток={current}, нужно={op.AmountBase}.");
        }

        var now = DateTime.UtcNow;

        var cashEntry = existingCash ?? new LedgerEntry
        {
            PostedAt = now,
            Direction = Direction.Out,
            Amount = op.Amount,
            CurrencyCode = op.CurrencyCode,
            FxRate = op.FxRate,
            AmountBase = op.AmountBase,
            MoneyLocationType = MoneyLocationType.CashBox,
            MoneyLocationId = op.CashBoxId,
            CashflowItemId = op.CashflowItemId,
            CounterpartyId = op.CounterpartyId,
            SourceDocType = sourceDocType,
            SourceDocId = sourceDocId,
            Comment = op.Comment
        };

        var bankEntry = existingBank ?? new LedgerEntry
        {
            PostedAt = now,
            Direction = Direction.In,
            Amount = op.Amount,
            CurrencyCode = op.CurrencyCode,
            FxRate = op.FxRate,
            AmountBase = op.AmountBase,
            MoneyLocationType = MoneyLocationType.BankAccount,
            MoneyLocationId = op.RelatedBankAccountId.Value,
            CashflowItemId = op.CashflowItemId,
            CounterpartyId = op.CounterpartyId,
            SourceDocType = sourceDocType,
            SourceDocId = sourceDocId,
            Comment = op.Comment
        };

        if (existingCash is null) _db.LedgerEntries.Add(cashEntry);
        if (existingBank is null) _db.LedgerEntries.Add(bankEntry);

        op.Status = DocumentStatus.Posted;
        op.UpdatedAt = now;

        await _db.SaveChangesAsync();

        return (cashEntry, bankEntry);
    }

    // =========================
    // BALANCE
    // =========================

    public async Task<decimal> GetCashBalanceBaseAsync(Guid cashBoxId)
    {
        var sumIn = await _db.LedgerEntries
            .Where(e => e.MoneyLocationType == MoneyLocationType.CashBox
                        && e.MoneyLocationId == cashBoxId
                        && e.Direction == Direction.In)
            .SumAsync(e => (decimal?)e.AmountBase) ?? 0m;

        var sumOut = await _db.LedgerEntries
            .Where(e => e.MoneyLocationType == MoneyLocationType.CashBox
                        && e.MoneyLocationId == cashBoxId
                        && e.Direction == Direction.Out)
            .SumAsync(e => (decimal?)e.AmountBase) ?? 0m;

        return Math.Round(sumIn - sumOut, 2);
    }
}

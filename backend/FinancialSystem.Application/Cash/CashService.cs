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
        if (cashBoxId == Guid.Empty) throw new InvalidOperationException("CashBoxId пустой.");
        if (cashierUserId == Guid.Empty) throw new InvalidOperationException("CashierUserId пустой.");
        if (openingBalanceDeclared < 0) throw new InvalidOperationException("OpeningBalanceDeclared не может быть отрицательным.");

        var hasOpen = await _db.CashShifts.AnyAsync(s => s.CashBoxId == cashBoxId && s.Status == "OPEN");
        if (hasOpen) throw new InvalidOperationException("В этой кассе уже есть открытая смена.");

        // ✅ Расчетный остаток на момент открытия
        var openingCalculated = await GetCashBalanceBaseAsync(cashBoxId);

        var now = DateTime.UtcNow;

        var shift = new CashShift
        {
            CashBoxId = cashBoxId,
            CashierUserId = cashierUserId,
            OpenedAt = now,
            OpeningBalanceDeclared = openingBalanceDeclared,

            // ⚠️ Нужно добавить поле в CashShift:
            // public decimal OpeningBalanceCalculated { get; set; }
            OpeningBalanceCalculated = openingCalculated,

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
                shift.OpeningBalanceDeclared,
                shift.OpeningBalanceCalculated
            })
        });

        await _db.SaveChangesAsync();
        return shift;
    }

    public async Task<CashShift> CloseShiftAsync(Guid shiftId, Guid userId, decimal closingBalanceDeclared)
    {
        if (shiftId == Guid.Empty) throw new InvalidOperationException("ShiftId пустой.");
        if (userId == Guid.Empty) throw new InvalidOperationException("UserId пустой.");
        if (closingBalanceDeclared < 0) throw new InvalidOperationException("ClosingBalanceDeclared не может быть отрицательным.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == shiftId);
        if (shift is null) throw new InvalidOperationException("Смена не найдена.");
        if (shift.Status != "OPEN") throw new InvalidOperationException("Смена уже закрыта.");

        // (опционально, но полезно) — запрет закрывать смену при наличии Draft
        var hasDrafts = await _db.CashOperations.AnyAsync(o => o.ShiftId == shiftId && o.Status == DocumentStatus.Draft);
        if (hasDrafts)
            throw new InvalidOperationException("Нельзя закрыть смену: есть операции в статусе Draft.");

        // ✅ Движения смены считаем по операциям смены (только Posted)
        var movementBase = await GetShiftMovementsBaseAsync(shiftId);

        // ✅ ClosingCalculated = OpeningCalculated + movements
        var closingCalculated = Math.Round(shift.OpeningBalanceCalculated + movementBase, 2);

        var now = DateTime.UtcNow;

        shift.ClosedAt = now;
        shift.ClosingBalanceDeclared = closingBalanceDeclared;
        shift.ClosingBalanceCalculated = closingCalculated;
        shift.Status = "CLOSED";
        shift.UpdatedAt = now;

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
                shift.OpeningBalanceCalculated,
                MovementBase = movementBase,
                shift.ClosingBalanceDeclared,
                shift.ClosingBalanceCalculated
            })
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

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
        if (userId == Guid.Empty) throw new InvalidOperationException("UserId пустой.");
        if (cashBoxId == Guid.Empty) throw new InvalidOperationException("CashBoxId пустой.");
        if (cashflowItemId == Guid.Empty) throw new InvalidOperationException("CashflowItemId пустой.");

        if (amount <= 0) throw new InvalidOperationException("Amount должен быть > 0.");
        if (fxRate <= 0) throw new InvalidOperationException("FxRate должен быть > 0.");
        if (string.IsNullOrWhiteSpace(currencyCode)) throw new InvalidOperationException("CurrencyCode обязателен.");

        currencyCode = currencyCode.Trim().ToUpperInvariant();

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

            var bankExists = await _db.BankAccounts.AnyAsync(b => b.Id == relatedBankAccountId && b.IsActive);
            if (!bankExists)
                throw new InvalidOperationException("Банковский счет не найден или не активен.");
        }

        var now = DateTime.UtcNow;

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
                op.ShiftId,
                op.Type,
                op.Amount,
                op.CurrencyCode,
                op.FxRate,
                op.CashflowItemId,
                op.RelatedBankAccountId,
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
        if (operationId == Guid.Empty) throw new InvalidOperationException("OperationId пустой.");
        if (userId == Guid.Empty) throw new InvalidOperationException("UserId пустой.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        var op = await _db.CashOperations.FirstOrDefaultAsync(o => o.Id == operationId);
        if (op is null) throw new InvalidOperationException("Операция не найдена.");

        // ✅ Если операция привязана к смене — смена должна быть OPEN и принадлежать кассе
        if (op.ShiftId is not null)
        {
            var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == op.ShiftId.Value);
            if (shift is null) throw new InvalidOperationException("Смена не найдена.");
            if (shift.Status != "OPEN") throw new InvalidOperationException("Нельзя проводить операцию в закрытой смене.");
            if (shift.CashBoxId != op.CashBoxId) throw new InvalidOperationException("Смена не принадлежит этой кассе.");
        }

        var direction = op.Type switch
        {
            CashOperationType.Income => Direction.In,
            CashOperationType.Expense => Direction.Out,
            CashOperationType.Refund => overrideDirection ?? Direction.Out,
            CashOperationType.Collection => throw new InvalidOperationException("Для инкассации используйте PostCollectionAsync."),
            CashOperationType.Transfer => throw new InvalidOperationException("Transfer пока не реализован отдельным методом."),
            _ => overrideDirection ?? throw new InvalidOperationException("Для этого типа операции нужно указать направление.")
        };

        var sourceDocType = "CashOperation";
        var sourceDocId = op.Id.ToString();

        // ✅ Идемпотентность: если уже есть проводка по этому документу в этой локации — вернуть
        var existing = await _db.LedgerEntries.FirstOrDefaultAsync(e =>
            e.SourceDocType == sourceDocType &&
            e.SourceDocId == sourceDocId &&
            e.MoneyLocationType == MoneyLocationType.CashBox &&
            e.MoneyLocationId == op.CashBoxId);

        if (existing is not null)
        {
            if (op.Status != DocumentStatus.Posted)
            {
                op.Status = DocumentStatus.Posted;
                op.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            await tx.CommitAsync();
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

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "CASH.OP.POST",
            ObjectType = "CashOperation",
            ObjectId = op.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new
            {
                op.Id,
                op.CashBoxId,
                op.ShiftId,
                op.Type,
                Direction = direction.ToString(),
                op.AmountBase,
                op.Status
            })
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return entry;
    }

    // =========================
    // POST COLLECTION (CASH → BANK)
    // =========================

    public async Task<(LedgerEntry cashEntry, LedgerEntry bankEntry)> PostCollectionAsync(Guid operationId, Guid userId)
    {
        if (operationId == Guid.Empty) throw new InvalidOperationException("OperationId пустой.");
        if (userId == Guid.Empty) throw new InvalidOperationException("UserId пустой.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        var op = await _db.CashOperations.FirstOrDefaultAsync(o => o.Id == operationId);
        if (op is null) throw new InvalidOperationException("Операция не найдена.");
        if (op.Type != CashOperationType.Collection) throw new InvalidOperationException("Это не инкассация.");

        if (op.RelatedBankAccountId is null || op.RelatedBankAccountId == Guid.Empty)
            throw new InvalidOperationException("Не указан BankAccountId.");

        // ✅ Если операция привязана к смене — смена должна быть OPEN
        if (op.ShiftId is not null)
        {
            var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == op.ShiftId.Value);
            if (shift is null) throw new InvalidOperationException("Смена не найдена.");
            if (shift.Status != "OPEN") throw new InvalidOperationException("Нельзя проводить инкассацию в закрытой смене.");
            if (shift.CashBoxId != op.CashBoxId) throw new InvalidOperationException("Смена не принадлежит этой кассе.");
        }

        var sourceDocType = "CashOperation";
        var sourceDocId = op.Id.ToString();

        // ✅ Идемпотентность: ищем обе стороны
        var existingCash = await _db.LedgerEntries.FirstOrDefaultAsync(e =>
            e.SourceDocType == sourceDocType &&
            e.SourceDocId == sourceDocId &&
            e.MoneyLocationType == MoneyLocationType.CashBox &&
            e.MoneyLocationId == op.CashBoxId);

        var existingBank = await _db.LedgerEntries.FirstOrDefaultAsync(e =>
            e.SourceDocType == sourceDocType &&
            e.SourceDocId == sourceDocId &&
            e.MoneyLocationType == MoneyLocationType.BankAccount &&
            e.MoneyLocationId == op.RelatedBankAccountId.Value);

        if (existingCash is not null && existingBank is not null)
        {
            if (op.Status != DocumentStatus.Posted)
            {
                op.Status = DocumentStatus.Posted;
                op.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            await tx.CommitAsync();
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

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "CASH.OP.POST_COLLECTION",
            ObjectType = "CashOperation",
            ObjectId = op.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new
            {
                op.Id,
                op.CashBoxId,
                op.ShiftId,
                BankAccountId = op.RelatedBankAccountId,
                op.AmountBase,
                op.Status
            })
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return (cashEntry, bankEntry);
    }

    // =========================
    // SHIFT MOVEMENTS (base)
    // =========================

    private async Task<decimal> GetShiftMovementsBaseAsync(Guid shiftId)
    {
        // Берем операции смены, которые реально проведены
        var opIds = await _db.CashOperations
            .Where(o => o.ShiftId == shiftId && o.Status == DocumentStatus.Posted)
            .Select(o => o.Id.ToString())
            .ToListAsync();

        if (opIds.Count == 0) return 0m;

        var sumIn = await _db.LedgerEntries
            .Where(e => e.SourceDocType == "CashOperation"
                        && opIds.Contains(e.SourceDocId)
                        && e.MoneyLocationType == MoneyLocationType.CashBox
                        && e.Direction == Direction.In)
            .SumAsync(e => (decimal?)e.AmountBase) ?? 0m;

        var sumOut = await _db.LedgerEntries
            .Where(e => e.SourceDocType == "CashOperation"
                        && opIds.Contains(e.SourceDocId)
                        && e.MoneyLocationType == MoneyLocationType.CashBox
                        && e.Direction == Direction.Out)
            .SumAsync(e => (decimal?)e.AmountBase) ?? 0m;

        return Math.Round(sumIn - sumOut, 2);
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
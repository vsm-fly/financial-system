using System.Text;
using FinancialSystem.Api.Models;
using FinancialSystem.Api.Security;
using FinancialSystem.Application.Cash;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Infrastructure.Seed;
using FinancialSystem.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

static Guid GetUserId(ClaimsPrincipal user)
{
    var uid = user.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
    return uid is null ? Guid.Empty : Guid.Parse(uid);
}

static bool IsCashier(ClaimsPrincipal user) =>
    user.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "CASHIER");

static async Task<Guid> GetCashierCashBoxIdAsync(AppDbContext db, Guid userId)
{
    var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.IsActive);
    if (u is null) throw new InvalidOperationException("Пользователь не найден или не активен.");
    if (u.CashBoxId is null) throw new InvalidOperationException("Кассиру не назначена касса.");
    return u.CashBoxId.Value;
}

static async Task EnsureCashierShiftAccessAsync(AppDbContext db, Guid cashierCashBoxId, Guid shiftId)
{
    var shift = await db.CashShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shiftId);
    if (shift is null) throw new InvalidOperationException("Смена не найдена.");
    if (shift.CashBoxId != cashierCashBoxId) throw new InvalidOperationException("Нет доступа к этой смене.");
}

var builder = WebApplication.CreateBuilder(args);

// JWT options from env
var jwtOptions = new JwtOptions
{
    Issuer = builder.Configuration["Jwt:Issuer"] ?? builder.Configuration["Jwt__Issuer"] ?? "FinancialSystem",
    Audience = builder.Configuration["Jwt:Audience"] ?? builder.Configuration["Jwt__Audience"] ?? "FinancialSystem",
    Key = builder.Configuration["Jwt:Key"] ?? builder.Configuration["Jwt__Key"] ?? "super-dev-only-change-me-please-32chars",
    ExpMinutes = int.TryParse(builder.Configuration["Jwt:ExpMinutes"], out var m) ? m : 60 * 12
};

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    var cs = builder.Configuration.GetConnectionString("Default")
             ?? builder.Configuration["ConnectionStrings:Default"]
             ?? builder.Configuration["ConnectionStrings__Default"]
             ?? throw new Exception("No connection string configured.");
    opt.UseNpgsql(cs);
});

// Cash rules
var cashRules = new CashRules
{
    DisallowNegativeCash = (builder.Configuration["Rules:DisallowNegativeCash"] ?? builder.Configuration["Rules__DisallowNegativeCash"] ?? "true")
        .Equals("true", StringComparison.OrdinalIgnoreCase)
};
builder.Services.AddSingleton(cashRules);
builder.Services.AddScoped<CashService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.RequireHttpsMetadata = false;
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FinancialSystem API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Введите: Bearer <token>"
    });
    c.AddSecurityRequirement(new()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new List<string>()
        }
    });
});

var app = builder.Build();

// Auto-create + seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var adminEmail = app.Configuration["Seed:AdminEmail"] ?? app.Configuration["Seed__AdminEmail"] ?? "admin@local";
    var adminPassword = app.Configuration["Seed:AdminPassword"] ?? app.Configuration["Seed__AdminPassword"] ?? "Admin123!";

    await DbSeeder.SeedAsync(db, adminEmail, adminPassword);
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Auth
app.MapPost("/auth/login", async (LoginRequest req, AppDbContext db, JwtTokenService jwt) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);
    if (user is null) return Results.Unauthorized();

    if (!PasswordHasher.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
        return Results.Unauthorized();

    var roles = await (from ur in db.UserRoles
                       join r in db.Roles on ur.RoleId equals r.Id
                       where ur.UserId == user.Id
                       select r.Code).ToListAsync();

    var token = jwt.Create(user, roles);
    return Results.Ok(new LoginResponse(token));
});

// Current user
app.MapGet("/me", (ClaimsPrincipal user) =>
{
    var email = user.Claims.FirstOrDefault(c => c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;
    var uid = user.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
    var roles = user.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
    return Results.Ok(new { uid, email, roles });
}).RequireAuthorization();

// Simple demo endpoints (existing)
app.MapGet("/cashboxes", async (AppDbContext db) => await db.CashBoxes.OrderBy(x => x.Name).ToListAsync())
   .RequireAuthorization();

app.MapPost("/cashboxes", async (FinancialSystem.Domain.Entities.CashBox dto, AppDbContext db) =>
{
    db.CashBoxes.Add(dto);
    await db.SaveChangesAsync();
    return Results.Created($"/cashboxes/{dto.Id}", dto);
}).RequireAuthorization();

app.MapGet("/bankaccounts", async (AppDbContext db) => await db.BankAccounts.OrderBy(x => x.BankName).ToListAsync())
   .RequireAuthorization();

app.MapPost("/bankaccounts", async (FinancialSystem.Domain.Entities.BankAccount dto, AppDbContext db) =>
{
    db.BankAccounts.Add(dto);
    await db.SaveChangesAsync();
    return Results.Created($"/bankaccounts/{dto.Id}", dto);
}).RequireAuthorization();

app.MapGet("/ledger", async (AppDbContext db) =>
    await db.LedgerEntries.OrderByDescending(x => x.PostedAt).Take(200).ToListAsync()
).RequireAuthorization();

//
// CASH v1 endpoints
//

app.MapPost("/cash/shifts/open", async (OpenShiftRequest req, ClaimsPrincipal user, AppDbContext db, CashService cash) =>
{
    var uid = GetUserId(user);
    if (uid == Guid.Empty) return Results.Unauthorized();

    try
    {
        var cashBoxId = IsCashier(user)
            ? await GetCashierCashBoxIdAsync(db, uid)
            : req.CashBoxId;

        var shift = await cash.OpenShiftAsync(cashBoxId, uid, req.OpeningBalanceDeclared);
        return Results.Ok(shift);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/cash/shifts/{id:guid}/close", async (Guid id, CloseShiftRequest req, ClaimsPrincipal user, AppDbContext db, CashService cash) =>
{
    var uid = GetUserId(user);
    if (uid == Guid.Empty) return Results.Unauthorized();

    try
    {
        if (IsCashier(user))
        {
            var cashierCashBoxId = await GetCashierCashBoxIdAsync(db, uid);
            await EnsureCashierShiftAccessAsync(db, cashierCashBoxId, id);
        }

        var shift = await cash.CloseShiftAsync(id, uid, req.ClosingBalanceDeclared);
        return Results.Ok(shift);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/cash/operations", async (CreateCashOperationRequest req, ClaimsPrincipal user, AppDbContext db, CashService cash) =>
{
    var uid = GetUserId(user);
    if (uid == Guid.Empty) return Results.Unauthorized();

    try
    {
        var cashBoxId = IsCashier(user)
            ? await GetCashierCashBoxIdAsync(db, uid)
            : req.CashBoxId;

        // кассиру shiftId не даём — сервис сам подхватит открытую смену
        var shiftId = IsCashier(user) ? null : req.ShiftId;

        var op = await cash.CreateOperationDraftAsync(
            userId: uid,
            cashBoxId: cashBoxId,
            shiftId: shiftId,
            type: req.Type,
            amount: req.Amount,
            currencyCode: req.CurrencyCode,
            fxRate: req.FxRate,
            cashflowItemId: req.CashflowItemId,
            counterpartyId: req.CounterpartyId,
            relatedBankAccountId: req.RelatedBankAccountId, // ✅ FIX: прокидываем банк для инкассации
            comment: req.Comment
        );

        return Results.Ok(op);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/cash/operations/{id:guid}/post", async (Guid id, PostCashOperationRequest req, ClaimsPrincipal user, CashService cash) =>
{
    var uid = GetUserId(user);
    if (uid == Guid.Empty) return Results.Unauthorized();

    try
    {
        var entry = await cash.PostOperationAsync(id, uid, req.Direction, req.IsRefund);
        return Results.Ok(entry);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

// ✅ NEW: проведение инкассации (Cash -> Bank) с 2 проводками
app.MapPost("/cash/operations/{id:guid}/post-collection", async (Guid id, ClaimsPrincipal user, CashService cash) =>
{
    var uid = GetUserId(user);
    if (uid == Guid.Empty) return Results.Unauthorized();

    try
    {
        var (cashEntry, bankEntry) = await cash.PostCollectionAsync(id, uid);
        return Results.Ok(new { cashEntry, bankEntry });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/cash/balance", async (Guid? cashBoxId, ClaimsPrincipal user, AppDbContext db, CashService cash) =>
{
    var uid = GetUserId(user);
    if (uid == Guid.Empty) return Results.Unauthorized();

    try
    {
        var effectiveCashBoxId = IsCashier(user)
            ? await GetCashierCashBoxIdAsync(db, uid)
            : (cashBoxId ?? throw new InvalidOperationException("cashBoxId обязателен для этой роли."));

        var bal = await cash.GetCashBalanceBaseAsync(effectiveCashBoxId);
        return Results.Ok(new { cashBoxId = effectiveCashBoxId, balanceBase = bal });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.Run();
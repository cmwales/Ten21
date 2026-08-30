using Microsoft.Extensions.DependencyInjection;
using Ten21.Business.Charges;
using Ten21.Business.Credits;
using Ten21.Business.Deposits;
using Ten21.Business.Directory;
using Ten21.Business.Documents;
using Ten21.Business.Leases;
using Ten21.Business.Payments;
using Ten21.Business.Refunds;
using Ten21.Business.Statements;
using Ten21.Business.UnitGroups;
using Ten21.Business.UnitTiers;
using Ten21.Business.Workspace;

namespace Ten21.Business;

/// <summary>
/// Business-layer refactor: registers this project's concrete service/repository classes.
/// All registered Scoped, matching Ten21DbContext's own lifetime -- one instance per HTTP
/// request, same as every other Scoped dependency in this app.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddBusiness(this IServiceCollection services)
    {
        services.AddScoped<ChargeRepository>();
        services.AddScoped<ChargeService>();
        services.AddScoped<PaymentRepository>();
        services.AddScoped<PaymentService>();
        services.AddScoped<StatementRepository>();
        services.AddScoped<StatementService>();
        services.AddScoped<CreditRepository>();
        services.AddScoped<CreditService>();
        services.AddScoped<RefundService>();
        services.AddScoped<DepositRepository>();
        services.AddScoped<DepositService>();
        services.AddScoped<LeaseService>();
        services.AddScoped<WorkspaceSettingsService>();
        services.AddScoped<WorkspaceLedgerService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<UnitGroupService>();
        services.AddScoped<UnitTierService>();
        services.AddScoped<DirectoryService>();
        return services;
    }
}

using Ten21.Domain.Enums;

namespace Ten21.Application.Ledger;

/// <summary>
/// Business-layer refactor: RefundTransactionResponse/SecurityDepositResponse relocated here
/// (not to Ten21.Business, since neither's own business logic has moved yet -- see
/// CreditsController/DepositsController/RefundsController, which still do their own direct
/// database access) purely because Ten21.Business.Statements.UnitStatementResponse needs to
/// reference both, and Business cannot depend on Api (the wrong direction). Application is
/// the shared ground every later layer can already see, and these are plain data records with
/// no EF Core/ASP.NET Core dependency of their own -- a natural fit here regardless of which
/// layer eventually owns the logic that builds them.
/// </summary>
public record RefundTransactionResponse(
    Guid Id,
    Guid ResidentProfileId,
    string ResidentName,
    Guid PropertyId,
    decimal Amount,
    DateOnly RefundDate,
    RefundTenderType TenderType,
    string? ReferenceNumber,
    RefundReason Reason,
    DateTimeOffset CreatedAt);

public record SecurityDepositResponse(
    Guid Id,
    Guid PropertyId,
    Guid ResidentProfileId,
    string ResidentName,
    decimal OriginalAmount,
    decimal AmountHeld,
    DateOnly CollectedDate,
    SecurityDepositStatus Status);

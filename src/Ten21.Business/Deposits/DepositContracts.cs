using Ten21.Application.Ledger;
using Ten21.Domain.Enums;

namespace Ten21.Business.Deposits;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Deposits so
/// DepositService can accept/return these directly.
///
/// US-39: collects a new security deposit at move-in. ResidentProfileId is optional -- per
/// the sprint's "Dual-Anchor Attribution" rule, if a manager doesn't specify a roommate, the
/// server auto-defaults to the Primary Resident on the unit's active lease (Lease.ResidentId),
/// and only throws a ValidationException if there's no active lease to default from.</summary>
public record CollectDepositRequest(
    decimal Amount,
    DateOnly CollectedDate,
    Guid? ResidentProfileId);

/// <summary>US-39: one line of "Settle Deposit"'s application against a charge -- the
/// deposit-money equivalent of CreditAllocationResponse. See
/// DepositSettlementAllocation's own class comment for why this isn't just CreditAllocation
/// reused.</summary>
public record DepositSettlementAllocationResponse(
    Guid Id,
    Guid SecurityDepositId,
    Guid TargetChargeId,
    string ChargeDescription,
    decimal AppliedAmount,
    DateOnly AppliedDate);

/// <summary>US-39: "Settle Deposit" -- a single atomic action, not two separate steps. TenderType
/// is always captured up front even though it's only used if AmountRefunded on the response
/// ends up being greater than zero (dues might consume the whole deposit).</summary>
public record SettleDepositRequest(
    RefundTenderType TenderType,
    string? ReferenceNumber);

public record SettleDepositResponse(
    SecurityDepositResponse Deposit,
    decimal AmountAppliedToCharges,
    decimal AmountRefunded,
    IReadOnlyList<DepositSettlementAllocationResponse> ChargeAllocations,
    RefundTransactionResponse? Refund);

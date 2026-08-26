using Ten21.Api.Contracts.Charges;
using Ten21.Domain.Enums;

namespace Ten21.Api.Contracts.Credits;

/// <summary>US-37: the result of a PM clicking "Apply Credits to Charges" -- a manual,
/// on-demand action (deliberately not a scheduled background job -- there's no
/// recurring-billing engine to hang a schedule off of yet, and the PM specifically wanted a
/// button, not automation). Draws down every payment on this unit with retained credit
/// (oldest first) against every outstanding charge (same statutory priority order as the
/// waterfall), until either all credit or all outstanding balance is exhausted.</summary>
public record ApplyCreditsResponse(
    decimal TotalApplied,
    IReadOnlyList<CreditAllocationResponse> Allocations);

/// <summary>US-37: "Refund Credit Balance" -- disburses some or all of a resident's available
/// (un-drawn-down) overpayment credit back to them. Draws down oldest-payment-first across
/// that resident's PaymentTransactions on this unit; the resulting RefundTransaction itself
/// doesn't record which specific payment(s) it came from, matching RefundTransaction's own
/// schema.</summary>
public record RefundCreditBalanceRequest(
    Guid ResidentProfileId,
    decimal Amount,
    DateOnly RefundDate,
    RefundTenderType TenderType,
    string? ReferenceNumber);

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

using Ten21.Domain.Enums;

namespace Ten21.Business.Refunds;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Credits so
/// RefundService can accept it directly.
///
/// US-37: "Refund Credit Balance" -- disburses some or all of a resident's available
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

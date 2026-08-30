using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-39: a security deposit collected at move-in, held in escrow separate from operating
/// rental income until move-out. Deliberately NOT modeled as a Charge -- MVP_features.md
/// already calls for "a dedicated liability ledger tracking held security deposits separately
/// from operating rental income," and a deposit settling against a charge must never look like
/// rent actually being paid (see DepositSettlementAllocation's own comment, and
/// UnitStatementResponse's Balance formula, which subtracts settled-deposit amounts as their
/// own term rather than folding them into SumPayments).
///
/// AmountHeld starts equal to OriginalAmount and is drawn down once, atomically, by "Settle
/// Deposit" (DepositsController.SettleDeposit): first applied against the unit's outstanding
/// charges in the same statutory priority order as the payment waterfall, then whatever's left
/// disbursed to the resident via a RefundTransaction (Reason = DepositReturn). If the unit's
/// dues exceed AmountHeld, the whole deposit is applied and AmountHeld lands at 0 with real
/// balance still outstanding -- see UnitStatementResponse.AccountStatus's own comment for how
/// that's surfaced ("TerminatedWithBalance").
/// </summary>
public class SecurityDeposit : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid ResidentProfileId { get; set; }

    public decimal OriginalAmount { get; set; }
    public decimal AmountHeld { get; set; }
    public DateOnly CollectedDate { get; set; }
    public SecurityDepositStatus Status { get; set; } = SecurityDepositStatus.Held;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

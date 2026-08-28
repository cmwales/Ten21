using Ten21.Domain.Entities;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Common;

/// <summary>
/// Audit Refinement Sprint: the single source of truth for "how much does this charge still
/// owe" and "in what order do charges get paid." An audit found the same
/// Amount + netAdjustment - alreadyAllocated (floored at zero) formula independently
/// reimplemented in PaymentsController's waterfall, DepositsController's settlement, and
/// CreditsController's credit draw-down, plus the same AllocationPriority-then-DueDate
/// ordering duplicated identically in all three loops -- a future change to the formula in
/// one copy and not the others would have silently corrupted ledger math. Kept in Domain
/// (framework-free) since this is pure business rule, not a data-access concern.
/// </summary>
public static class ChargeLedgerMath
{
    public static decimal NetAdjustment(IEnumerable<ChargeAdjustment> adjustments) =>
        adjustments.Sum(a => a.AdjustmentType == AdjustmentType.DebitAdjustment ? a.Amount : -a.Amount);

    public static decimal Outstanding(decimal chargeAmount, decimal netAdjustment, decimal alreadyAllocated) =>
        Math.Max(0m, chargeAmount + netAdjustment - alreadyAllocated);

    /// <summary>US-34's statutory waterfall order: lower AllocationPriority number first,
    /// oldest DueDate breaks ties.</summary>
    public static List<Charge> OrderByStatutoryPriority(IEnumerable<Charge> charges) =>
        charges.OrderBy(c => c.AllocationPriority).ThenBy(c => c.DueDate).ToList();
}

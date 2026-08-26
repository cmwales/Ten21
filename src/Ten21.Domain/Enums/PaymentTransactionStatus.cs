namespace Ten21.Domain.Enums;

/// <summary>US-38: Cleared is the default for every normal logged payment. Reversed means a
/// PM ran "Reverse Payment" (NSF/bounced) or "Reallocate Payment" (cross-property posting
/// error) against it -- its PaymentAllocation/CreditAllocation rows have been un-linked and
/// its UnallocatedAmount zeroed, but the row itself is never deleted (audit trail).</summary>
public enum PaymentTransactionStatus
{
    Cleared,
    Reversed,
}

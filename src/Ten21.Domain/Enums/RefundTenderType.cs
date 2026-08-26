namespace Ten21.Domain.Enums;

/// <summary>US-37: how an outbound refund was disbursed. Deliberately a narrower set than
/// TenderType (no Cash/Venmo) -- per spec, a PM-issued refund is a traceable disbursement,
/// not a handshake.</summary>
public enum RefundTenderType
{
    Check,
    DirectDeposit,
    Zelle,
}

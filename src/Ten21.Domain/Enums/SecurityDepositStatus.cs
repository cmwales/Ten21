namespace Ten21.Domain.Enums;

/// <summary>US-39: Held is a deposit's whole life until a PM runs "Settle Deposit" -- at that
/// point it becomes Settled permanently (AmountHeld drops to whatever, if anything, wasn't
/// applied to charges or refunded -- see SecurityDeposit's own class comment). No partial
/// settlement/re-open: settling is a one-shot, move-out-time action.</summary>
public enum SecurityDepositStatus
{
    Held,
    Settled,
}

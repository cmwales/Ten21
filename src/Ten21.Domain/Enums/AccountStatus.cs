namespace Ten21.Domain.Enums;

/// <summary>US-39: a computed (never stored) label on UnitStatementResponse -- see that
/// record's own comment. TerminatedWithBalance means at least one SecurityDeposit on this
/// unit has been Settled and the unit still owes money afterward (dues exceeded what the
/// deposit could cover).</summary>
public enum AccountStatus
{
    Active,
    TerminatedWithBalance,
}

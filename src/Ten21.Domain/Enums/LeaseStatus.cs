namespace Ten21.Domain.Enums;

/// <summary>US-30: FixedTerm is every lease's starting status. Sprint 6's later story
/// (Pro-Rated Move-In Invoice & Lease Expiration Alerts) is what actually transitions a
/// lease to MonthToMonth once EndDate passes with no move-out notice on file, and to Ended
/// once one is -- this enum exists now so that behavior has somewhere to write its
/// result.</summary>
public enum LeaseStatus
{
    FixedTerm,
    MonthToMonth,
    Ended,
}

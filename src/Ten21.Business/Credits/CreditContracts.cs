using Ten21.Business.Statements;

namespace Ten21.Business.Credits;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Credits so
/// CreditService can return it directly.
///
/// US-37: the result of a PM clicking "Apply Credits to Charges" -- a manual, on-demand
/// action (deliberately not a scheduled background job -- there's no recurring-billing
/// engine to hang a schedule off of yet, and the PM specifically wanted a button, not
/// automation). Draws down every payment on this unit with retained credit (oldest first)
/// against every outstanding charge (same statutory priority order as the waterfall), until
/// either all credit or all outstanding balance is exhausted.</summary>
public record ApplyCreditsResponse(
    decimal TotalApplied,
    IReadOnlyList<CreditAllocationResponse> Allocations);

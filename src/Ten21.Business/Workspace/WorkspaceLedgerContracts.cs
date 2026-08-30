namespace Ten21.Business.Workspace;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Workspace.
///
/// US-36: one property's row in the workspace-wide ledger rollup. Balance uses the same
/// formula as UnitStatementResponse (see that record's own comment) -- this is a pure
/// reporting aggregation over the same Charge/PaymentTransaction/ChargeAdjustment rows, no new
/// tables.</summary>
public record PropertyLedgerSummaryResponse(
    Guid PropertyId,
    string PropertyName,
    string? UnitIdentifier,
    decimal Balance);

/// <summary>US-36: the whole workspace's financial rollup -- every property the caller's
/// tenant manages, with its own balance, plus the portfolio-wide TotalBalance. The
/// per-property unit statement (US-33, /properties/:id/ledger) remains the drill-down target;
/// this is the roll-up view above it, not a replacement.</summary>
public record WorkspaceLedgerResponse(
    decimal TotalBalance,
    IReadOnlyList<PropertyLedgerSummaryResponse> Properties);

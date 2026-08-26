namespace Ten21.Domain.Enums;

/// <summary>US-34: how a manually-logged payment was received. Live tokenized payment
/// processing is deferred to Phase 2 -- this enum only describes payments the PM is
/// recording after the fact, never anything this app itself moves money through.</summary>
public enum TenderType
{
    Cash,
    Check,
    Zelle,
    Venmo,
    DirectDeposit,
}

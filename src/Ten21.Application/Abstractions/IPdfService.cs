namespace Ten21.Application.Abstractions;

/// <summary>
/// US-40: renders payment receipts and unit account statements as PDF bytes. Interfaced for
/// the same reason as every other external-library seam in this codebase (IEmailSender,
/// ITurnstileVerificationService) -- controllers depend on the abstraction, not directly on
/// QuestPDF (the library wired up in Infrastructure). Takes plain data records, not API
/// contracts or domain entities, so this interface (and its Infrastructure implementation)
/// never needs a reference to Ten21.Api.
/// </summary>
public interface IPdfService
{
    byte[] GeneratePaymentReceipt(PaymentReceiptPdfData data);

    byte[] GenerateUnitStatement(UnitStatementPdfData data);
}

public record PaymentReceiptChargeLine(string ChargeDescription, decimal AllocatedAmount);

public record PaymentReceiptPdfData(
    string PropertyName,
    string? UnitIdentifier,
    string ResidentName,
    DateOnly PaymentDate,
    decimal AmountPaid,
    string TenderType,
    string? ReferenceNumber,
    IReadOnlyList<PaymentReceiptChargeLine> Allocations);

public record UnitStatementPdfChargeLine(string Description, string Category, DateOnly DueDate, decimal Amount, string PaymentStatus);

public record UnitStatementPdfPaymentLine(DateOnly PaymentDate, string TenderType, string ResidentName, decimal AmountPaid);

/// <summary>DateRangeLabel is already-resolved display text (e.g. "Year-to-Date") -- the date
/// filtering itself happens before this record is built, in the controller. Balance is always
/// the current snapshot regardless of range; Charges/Payments are what's filtered.</summary>
public record UnitStatementPdfData(
    string PropertyName,
    string? UnitIdentifier,
    string DateRangeLabel,
    decimal Balance,
    IReadOnlyList<UnitStatementPdfChargeLine> Charges,
    IReadOnlyList<UnitStatementPdfPaymentLine> Payments);

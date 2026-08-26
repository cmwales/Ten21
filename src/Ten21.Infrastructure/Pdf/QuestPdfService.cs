using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Pdf;

/// <summary>
/// US-40: the QuestPDF-backed implementation of IPdfService. QuestPDF.Settings.License is
/// set once, in PdfServiceCollectionExtensions.AddPdfGeneration, not here -- this class is
/// just document layout.
/// </summary>
public class QuestPdfService : IPdfService
{
    public byte[] GeneratePaymentReceipt(PaymentReceiptPdfData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(column =>
                {
                    column.Item().Text("Ten21").FontSize(20).Bold();
                    column.Item().Text("Payment Receipt").FontSize(14).SemiBold();
                    column.Item().PaddingTop(4).Text(FormatPropertyLine(data.PropertyName, data.UnitIdentifier));
                });

                page.Content().PaddingVertical(20).Column(column =>
                {
                    column.Spacing(6);
                    column.Item().Text($"Paid By: {data.ResidentName}");
                    column.Item().Text($"Payment Date: {data.PaymentDate:MM/dd/yyyy}");
                    column.Item().Text($"Tender Type: {data.TenderType}");
                    if (!string.IsNullOrWhiteSpace(data.ReferenceNumber))
                    {
                        column.Item().Text($"Reference Number: {data.ReferenceNumber}");
                    }

                    column.Item().PaddingTop(16).Text("Allocated Charges").Bold();
                    column.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Charge").Bold();
                            header.Cell().AlignRight().Text("Amount").Bold();
                        });

                        if (data.Allocations.Count == 0)
                        {
                            table.Cell().ColumnSpan(2).PaddingTop(4).Text("(unallocated -- retained as credit)").Italic();
                        }

                        foreach (var line in data.Allocations)
                        {
                            table.Cell().PaddingTop(2).Text(line.ChargeDescription);
                            table.Cell().PaddingTop(2).AlignRight().Text($"${line.AllocatedAmount:0.00}");
                        }
                    });

                    column.Item().PaddingTop(16).AlignRight().Text($"Total Amount Paid: ${data.AmountPaid:0.00}").Bold().FontSize(13);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated ");
                    text.Span(DateTime.UtcNow.ToString("MM/dd/yyyy")).SemiBold();
                });
            });
        });

        return document.GeneratePdf();
    }

    public byte[] GenerateUnitStatement(UnitStatementPdfData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text("Ten21").FontSize(20).Bold();
                    column.Item().Text("Unit Account Statement").FontSize(14).SemiBold();
                    column.Item().PaddingTop(4).Text(FormatPropertyLine(data.PropertyName, data.UnitIdentifier));
                    column.Item().Text($"Period: {data.DateRangeLabel}").FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(20).Column(column =>
                {
                    column.Spacing(4);
                    column.Item().AlignRight().Text($"Current Balance: ${data.Balance:0.00}").Bold().FontSize(14);

                    column.Item().PaddingTop(16).Text("Charges").Bold();
                    column.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Description").Bold();
                            header.Cell().Text("Category").Bold();
                            header.Cell().Text("Due Date").Bold();
                            header.Cell().Text("Status").Bold();
                            header.Cell().AlignRight().Text("Amount").Bold();
                        });

                        if (data.Charges.Count == 0)
                        {
                            table.Cell().ColumnSpan(5).PaddingTop(4).Text("No charges in this period.").Italic();
                        }

                        foreach (var line in data.Charges)
                        {
                            table.Cell().PaddingTop(2).Text(line.Description);
                            table.Cell().PaddingTop(2).Text(line.Category);
                            table.Cell().PaddingTop(2).Text(line.DueDate.ToString("MM/dd/yyyy"));
                            table.Cell().PaddingTop(2).Text(line.PaymentStatus);
                            table.Cell().PaddingTop(2).AlignRight().Text($"${line.Amount:0.00}");
                        }
                    });

                    column.Item().PaddingTop(20).Text("Payments").Bold();
                    column.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Date").Bold();
                            header.Cell().Text("Tender Type").Bold();
                            header.Cell().Text("Paid By").Bold();
                            header.Cell().AlignRight().Text("Amount").Bold();
                        });

                        if (data.Payments.Count == 0)
                        {
                            table.Cell().ColumnSpan(4).PaddingTop(4).Text("No payments in this period.").Italic();
                        }

                        foreach (var line in data.Payments)
                        {
                            table.Cell().PaddingTop(2).Text(line.PaymentDate.ToString("MM/dd/yyyy"));
                            table.Cell().PaddingTop(2).Text(line.TenderType);
                            table.Cell().PaddingTop(2).Text(line.ResidentName);
                            table.Cell().PaddingTop(2).AlignRight().Text($"${line.AmountPaid:0.00}");
                        }
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated ");
                    text.Span(DateTime.UtcNow.ToString("MM/dd/yyyy")).SemiBold();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string FormatPropertyLine(string propertyName, string? unitIdentifier) =>
        string.IsNullOrWhiteSpace(unitIdentifier) ? propertyName : $"{propertyName}, {unitIdentifier}";
}

namespace Ten21.Domain.Common;

/// <summary>
/// US-21: defends against CSV/XLSX formula injection ("CSV injection") -- a cell value that
/// starts with =, +, -, or @ is interpreted as a formula by Excel/Sheets if this data is
/// ever re-exported, which is dangerous if the original text came from an untrusted bulk
/// import file. Prepending a single quote forces spreadsheet software to treat the value as
/// literal text. Pure/dependency-free (Domain stays framework-free), unlike IInputSanitizer
/// (an HtmlSanitizer-backed Infrastructure concern) -- both are applied to imported text
/// fields, but this one has no external library to abstract behind an interface.
/// </summary>
public static class FormulaInjectionGuard
{
    private static readonly char[] DangerousLeadingCharacters = ['=', '+', '-', '@'];

    public static string Sanitize(string value)
    {
        if (value.Length > 0 && DangerousLeadingCharacters.Contains(value[0]))
        {
            return "'" + value;
        }

        return value;
    }
}

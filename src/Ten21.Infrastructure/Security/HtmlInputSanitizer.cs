using Ganss.Xss;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Security;

/// <summary>
/// US-19/US-21: strips all HTML tags/attributes from free-text input via Ganss.Xss'
/// HtmlSanitizer, configured with an empty allow-list -- these fields (property names,
/// addresses, unit identifiers, imported spreadsheet cells) are plain text and should never
/// contain markup at all, so nothing is allowed through rather than a curated safe subset.
/// </summary>
public class HtmlInputSanitizer : IInputSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlInputSanitizer()
    {
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedTags.Clear();
        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedSchemes.Clear();
    }

    public string? Sanitize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return _sanitizer.Sanitize(value).Trim();
    }
}

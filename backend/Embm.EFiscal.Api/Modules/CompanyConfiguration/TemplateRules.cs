using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Embm.EFiscal.Api.Infrastructure;

namespace Embm.EFiscal.Api.Modules.CompanyConfiguration;

internal sealed record RenderedEmail(string Subject, string Body, string Brand, string Signature, string Footer);

internal static partial class TemplateRules
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Fields =
    [
        "CompanyName", "CustomerName", "CustomerEmail", "DocumentType", "DocumentNumber", "DocumentDate", "DocumentTotal",
        "AmountDue", "DueDate", "OriginalInvoiceNumber", "SalesOrderReference", "CompanyContactInformation",
        "CreditMemoNumber", "CreditMemoDate", "CreditAmount", "AdjustmentReason"
    ];
    public static readonly string[] Columns = ["description", "quantity", "unitPrice", "discount", "tax", "amount"];

    public static bool ValidType(string kind, string type) => kind is "email" or "pdf" && type is "invoice" or "credit-memo";

    public static IResult? ValidateSender(SenderProfile? profile)
    {
        if (profile is null || !ApiSupport.Text(profile.SenderName, 100) || profile.SenderName.Any(char.IsControl) ||
            !Mailbox(profile.SenderEmail) || (!string.IsNullOrWhiteSpace(profile.ReplyTo) && !Mailbox(profile.ReplyTo)))
            return ApiSupport.Invalid("profile", "Enter a sender name, sender email, and optional reply-to email.");
        if (!Recipients(profile.Cc) || !Recipients(profile.Bcc))
            return ApiSupport.Invalid("recipients", "Use up to ten distinct email addresses per copy list.");
        switch (profile.Provider)
        {
            case "platform":
            case "google-workspace":
            case "microsoft-365":
                if (profile.SmtpHost is not null || profile.SmtpPort is not null || profile.TlsMode is not null || profile.Authentication is not null)
                    return ApiSupport.Invalid("provider", "Only custom SMTP can include SMTP preferences.");
                return null;
            case "custom-smtp":
                if (!PublicHost(profile.SmtpHost) || !((profile.SmtpPort == 465 && profile.TlsMode == "implicit-tls") ||
                    (profile.SmtpPort == 587 && profile.TlsMode == "starttls")) || profile.Authentication is not ("password" or "oauth2"))
                    return ApiSupport.Invalid("smtp", "Use a public SMTP hostname, port 465 with implicit TLS or 587 with STARTTLS, and an authentication method.");
                return null;
            default:
                return ApiSupport.Invalid("provider", "Select a supported provider preference.");
        }
    }

    public static bool ValidEmail(EmailTemplate? value) => value is not null &&
        ApiSupport.Text(value.Subject, 160) && !value.Subject.Any(char.IsControl) &&
        ApiSupport.Text(value.Body, 6000) && SafeText(value.Subject, 160) && SafeText(value.Body, 6000) &&
        SafeText(value.Brand, 120) && SafeText(value.Signature, 500) && SafeText(value.Footer, 500);

    public static bool ValidLayout(PdfLayout? value) => value is not null &&
        Plain(value.Title, 100) && ApiSupport.Text(value.Title, 100) && Plain(value.Header, 180) &&
        value.BusinessLayout is "stacked" or "split" && value.CustomerLayout is "stacked" or "split" &&
        value.Columns is { Length: 6 } && Columns.ToHashSet().SetEquals(value.Columns) &&
        value.Font is "sans" or "serif" or "mono" && value.AccentColor is not null && Accent().IsMatch(value.AccentColor) &&
        value.MarginMm is >= 12 and <= 32 && value.PageSize is "A4" or "Letter" && value.FontSize is >= 8 and <= 12 &&
        value.SummaryPosition is "left" or "right" && (value.ColumnWeights is null || value.ColumnWeights.Length == 6 &&
            value.ColumnWeights.All(x => x is >= 9 and <= 55) && value.ColumnWeights.Sum() == 100) && ValidLogo(value.Logo) &&
        Plain(value.PaymentInstructions, 500) && Plain(value.Terms, 800) && Plain(value.Footer, 500);

    public static (EmailTemplate Email, PdfLayout Layout) Defaults(string type)
    {
        var title = type == "invoice" ? "Sales Invoice" : "Credit Memo";
        return (
            new EmailTemplate(title + " {{DocumentNumber}}", "Hello {{CustomerName}},\n\nPlease find your {{DocumentType}} {{DocumentNumber}} from {{CompanyName}}.",
                "{{CompanyName}}", "{{CompanyContactInformation}}", null, false),
            new PdfLayout(title, null, "split", "stacked", Columns, "sans", "#14243B", 18, null, null, null, null, false, true)
        );
    }

    public static object RenderSample(EmailTemplate template, string type)
    {
        var invoice = type == "invoice";
        var sample = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CompanyName"] = "Harbor Supply (sample)", ["CustomerName"] = "Sample Customer", ["CustomerEmail"] = "customer@example.test",
            ["DocumentType"] = invoice ? "Sales Invoice" : "Credit Memo", ["DocumentNumber"] = invoice ? "SI-SAMPLE-001" : "CM-SAMPLE-001",
            ["DocumentDate"] = "15 January 2026", ["DocumentTotal"] = "₱1,008.00", ["AmountDue"] = invoice ? "₱1,008.00" : "₱0.00",
            ["DueDate"] = invoice ? "30 January 2026" : "Not applicable", ["OriginalInvoiceNumber"] = invoice ? "Not applicable" : "SI-SAMPLE-001",
            ["SalesOrderReference"] = invoice ? "SO-SAMPLE-001" : "Not applicable", ["CompanyContactInformation"] = "office@example.test"
        };
        sample["CreditMemoNumber"] = invoice ? "Not applicable" : sample["DocumentNumber"];
        sample["CreditMemoDate"] = invoice ? "Not applicable" : sample["DocumentDate"];
        sample["CreditAmount"] = invoice ? "Not applicable" : sample["DocumentTotal"];
        sample["AdjustmentReason"] = invoice ? "Not applicable" : "Sample return";
        var content = Render(template, sample);
        return new { content.Subject, content.Body, content.Brand, content.Signature, content.Footer, recipient = sample["CustomerEmail"],
            configuredCopiesApplied = false };
    }

    internal static RenderedEmail Render(EmailTemplate template, IReadOnlyDictionary<string, string> values)
    {
        string Resolve(string? text) => Variable().Replace(text ?? "", match => values.GetValueOrDefault(match.Groups[1].Value, ""));
        var subject = string.Concat(Resolve(template.Subject).Select(c => char.IsControl(c) ? ' ' : c));
        return new(subject, Resolve(template.Body), Resolve(template.Brand), Resolve(template.Signature), Resolve(template.Footer));
    }

    internal static bool Mailbox(string? email) => email is not null && ApiSupport.Email(email) && !email.Any(char.IsWhiteSpace) &&
        !email.Any(char.IsControl) && !email.Contains(',') && !email.Contains(';') && !email.Contains('<') && !email.Contains('>');

    private static bool Recipients(string[]? list) => list is { Length: <= 10 } && list.All(Mailbox) &&
        list.Distinct(StringComparer.OrdinalIgnoreCase).Count() == list.Length;

    private static bool PublicHost(string? host) => host is { Length: >= 4 and <= 253 } && Host().IsMatch(host) &&
        !IPAddress.TryParse(host, out _) && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
        !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) &&
        !host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".home", StringComparison.OrdinalIgnoreCase);

    private static bool Plain(string? text, int limit) => text is null || (text.Length <= limit && !text.Contains('<') && !text.Contains('>') &&
        text.All(c => !char.IsControl(c) || c is '\r' or '\n' or '\t'));

    private static bool SafeText(string? text, int limit)
    {
        if (!Plain(text, limit) || text is null) return text is null;
        if (Variable().Matches(text).Any(match => !Fields.Contains(match.Groups[1].Value, StringComparer.Ordinal))) return false;
        var remainder = Variable().Replace(text, "");
        return !remainder.Contains("{{") && !remainder.Contains("}}");
    }

    // Only bounded inline PNGs. Remote images and active content are never fetched or accepted.
    private static bool ValidLogo(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        const string prefix = "data:image/png;base64,";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length > 87_410) return false;
        try
        {
            var bytes = Convert.FromBase64String(value[prefix.Length..]);
            if (bytes.Length is < 33 or > 65_536 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return false;
            return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) is > 0 and <= 1600 &&
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) is > 0 and <= 1600;
        }
        catch (FormatException) { return false; }
    }

    [GeneratedRegex(@"\{\{([A-Za-z]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Variable();
    [GeneratedRegex(@"\A#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Accent();
    [GeneratedRegex(@"\A(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Host();
}

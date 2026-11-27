using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal record MasterImportError(int Row, string Field, string Message);
internal sealed class MasterImportProof
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    public string Issue(Guid subscriber, Guid company, Guid actor, string kind, string digest)
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        return expiry.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + Convert.ToHexString(Sign(expiry, subscriber, company, actor, kind, digest));
    }

    public bool Verify(string token, Guid subscriber, Guid company, Guid actor, string kind, string digest)
    {
        if (token.Length > 100) return false;
        var pieces = token.Split('.');
        if (pieces is not [var timestamp, var signature] || !long.TryParse(timestamp, out var expiry)
            || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || signature.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature), Sign(expiry, subscriber, company, actor, kind, digest)); }
        catch (FormatException) { return false; }
    }

    private byte[] Sign(long expiry, Guid subscriber, Guid company, Guid actor, string kind, string digest) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(FormattableString.Invariant($"{expiry}:{subscriber:N}:{company:N}:{actor:N}:{kind}:{digest}")));
}

internal sealed class MasterImport(EfiscalDbContext db, MasterRegistry registry, MasterImportProof proof)
{
    public const int MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxRecords = 1000;

    private static string[] Headers(MasterDefinition definition) => ["code", "name", "active", ..definition.Fields.Select(x => x.Key)];

    public static IResult Download(MasterDefinition definition, MasterRecord[] rows, string format, bool template)
    {
        List<string[]> output = [Headers(definition)];
        foreach (var row in rows)
        {
            output.Add([row.Code, row.Name, row.Active ? "true" : "false", ..definition.Fields.Select(field =>
                row.Data.TryGetProperty(field.Key, out var value) ? FileValue(value) : "")]);
        }
        var type = format == "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv; charset=utf-8";
        return Results.File(MasterSpreadsheet.Write(output, format), type, $"{definition.Key}-{(template ? "template" : "export")}.{format}");
    }

    private static string FileValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        _ => JsonSerializer.Serialize(MasterRegistry.SafeValue(value))
    };

    public async Task<IResult> Handle(Guid subscriber, Guid company, MasterDefinition definition, string action,
        IFormFile file, string previewToken, HttpContext http, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        Need(extension is ".csv" or ".xlsx", "file", "Choose a CSV or XLSX workbook.");
        using var bytes = new MemoryStream();
        await file.CopyToAsync(bytes, ct);
        var contents = bytes.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(contents));
        var confirmation = action == "confirm";
        Need(!confirmation || proof.Verify(previewToken, subscriber, company, http.UserId(), definition.Key, digest), "file",
            "Preview this exact file before confirming.");
        var rows = MasterSpreadsheet.Read(contents, extension[1..]);
        Need(rows.Count is >= 2 and <= MaxRecords + 1, "file", "Include a header and 1 to 1,000 non-empty data rows.");
        var headers = rows[0].Select(x => x.Trim()).ToArray();
        var valid = Headers(definition);
        Need(headers.Length == headers.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            && headers.All(x => valid.Contains(x)) && headers.Contains("code") && headers.Contains("name")
            && definition.Fields.Where(x => x.Required).All(x => headers.Contains(x.Key)), "file",
            "Use unique template column names and include all required columns.");
        var actor = http.UserId();
        var development = http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        await using var transaction = confirmation ? await db.LockSubscriberAsync(subscriber, ct) : null;
        if (confirmation)
        {
            if (await MasterEndpoints.Authorize(subscriber, company, MasterSchema.ImportPermission(definition.Key), http, db, ct) is null)
                return Results.NotFound();
            var previous = await db.MasterImportOperations.SingleOrDefaultAsync(x => x.SubscriberId == subscriber &&
                x.CompanyId == company && x.Kind == definition.Key && x.Digest == digest, ct);
            if (previous is not null) return Results.Ok(new { digest, total = previous.Rows, imported = previous.Rows, replay = true });
        }

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<MasterImportError>();
        for (var index = 1; index < rows.Count; index++)
        {
            try
            {
                var row = rows[index];
                Need(row.Length <= headers.Length, "file", "The row has extra columns.");
                var cell = headers.Select((header, column) => (header, value: column < row.Length ? row[column] : ""))
                    .ToDictionary(x => x.header, x => x.value);
                Need(codes.Add(cell["code"].Trim()), "code", "Duplicate code in this file.");
                var input = await ToInput(subscriber, company, definition, cell, ct);
                await registry.Write(subscriber, company, actor, definition, input, null, development, "Imported", ct);
            }
            catch (MasterValidation error) { errors.Add(new(index + 1, error.Field, error.Message)); }
            catch (JsonException) { errors.Add(new(index + 1, "data", "A structured cell contains invalid JSON.")); }
        }
        var count = rows.Count - 1;
        if (action == "errors")
        {
            List<string[]> output = [["row", "field", "message"]];
            output.AddRange(errors.Select(x => new[] { x.Row.ToString(System.Globalization.CultureInfo.InvariantCulture), x.Field, x.Message }));
            return Results.File(MasterSpreadsheet.Write(output, "csv"), "text/csv; charset=utf-8", $"{definition.Key}-import-errors.csv");
        }
        if (!confirmation)
            return Results.Ok(new { digest, total = count, valid = count - errors.Count, errors,
                previewToken = errors.Count == 0 ? proof.Issue(subscriber, company, actor, definition.Key, digest) : null });
        if (errors.Count > 0)
            return Results.UnprocessableEntity(new { title = "Import contains validation errors.", total = count, imported = 0, errors });

        db.MasterImportOperations.Add(new MasterImportOperation { SubscriberId = subscriber, CompanyId = company, ActorId = actor,
            Kind = definition.Key, Digest = digest, Rows = count, At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        await transaction!.CommitAsync(ct);
        return Results.Ok(new { digest, total = count, imported = count, replay = false });
    }

    private async Task<MasterInput> ToInput(Guid subscriber, Guid company, MasterDefinition definition, Dictionary<string, string> cell, CancellationToken ct)
    {
        var active = true;
        if (cell.TryGetValue("active", out var boolText) && !string.IsNullOrWhiteSpace(boolText))
            Need(bool.TryParse(boolText.Trim(), out active), "active", "Use true or false.");
        var data = new Dictionary<string, JsonElement>();
        foreach (var field in definition.Fields)
        {
            if (!cell.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            Need(value.Length <= 20_000, field.Key, "The cell exceeds the supported length.");
            if (field.Type is "external" or "custom-values" || field.Type == "emails" && value.TrimStart().StartsWith('['))
            {
                using var doc = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 10 });
                data[field.Key] = doc.RootElement.Clone();
                continue;
            }
            if (field.Type == "emails")
            {
                data[field.Key] = JsonSerializer.SerializeToElement(value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                continue;
            }
            if (field.Reference is not null && !Guid.TryParse(value.Trim(), out _))
            {
                var code = value.Trim().ToUpperInvariant();
                var scoped = await registry.Scope(subscriber, company).Where(x => x.Kind == field.Reference && x.Code == code && x.Active)
                    .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
                Need(scoped.HasValue, field.Key, $"The referenced {field.Label.ToLowerInvariant()} code was not found in this company.");
                value = scoped!.Value.ToString();
            }
            data[field.Key] = JsonSerializer.SerializeToElement(value.Trim());
        }
        return new(cell["code"], cell["name"], active, null, data);
    }
}


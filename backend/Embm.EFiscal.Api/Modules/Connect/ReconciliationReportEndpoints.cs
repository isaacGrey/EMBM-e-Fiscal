using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Connect;

internal static partial class ClientConnectEndpoints
{
    private static async Task<IResult> ReconciliationReport(Guid subscriber, Guid company, string format, [AsParameters] ReconciliationFilters filter,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http) || format is not ("csv" or "xlsx")) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        var rows = await Sorted(FilterChecks(db, subscriber, company, filter, now), filter.Sort).Take(5001).ToArrayAsync(ct);
        if (rows.Length > 5000) return ApiSupport.Invalid("range", "Narrow your export to at most 5,000 rows.");
        var revisionIds = rows.Where(x => x.CoverageId.HasValue).Select(x => x.CoverageId!.Value).Distinct().ToArray();
        var policies = await Coverage.For(db, subscriber, company).AsNoTracking().Where(x => revisionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var data = new List<string[]>
        {
            new[] { "eFiscal number", "Accounting number", "Type", "Business date", "Currency", "eFiscal total", "Accounting total", "Difference",
                "Coverage", "Mode", "Coverage start", "Coverage end", "Policy revision", "Connection", "Sync status", "Reconciliation",
                "Retrieval", "Completeness", "Findings", "Verified at", "Fresh until", "Generated at" }
        };
        foreach (var row in rows)
        {
            var coverage = row.CoverageId is Guid policy ? policies.GetValueOrDefault(policy) : null;
            data.Add([row.Number ?? "", row.ExternalNumber ?? "", row.Type, Iso(row.BusinessDate), row.Currency ?? "",
                Numeric(row.ExpectedTotal), Numeric(row.ActualTotal), Numeric(row.ActualTotal - row.ExpectedTotal), row.Coverage, coverage?.Mode ?? "",
                Iso(coverage?.Start), Iso(coverage?.End), coverage?.Number.ToString(CultureInfo.InvariantCulture) ?? "",
                row.ConnectionId.ToString(), row.Synchronization, row.Status, row.Retrieval, row.Completeness, string.Join(", ", row.Categories),
                row.At.ToString("O"), row.FreshUntil.ToString("O"), now.ToString("O")]);
        }
        return format == "csv"
            ? Results.File(Encoding.UTF8.GetBytes(string.Join("\r\n", data.Select(r => string.Join(",", r.Select(Csv)))) + "\r\n"),
                "text/csv; charset=utf-8", "efiscal-reconciliation.csv")
            : Results.File(Workbook(data), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "efiscal-reconciliation.xlsx");
    }

    private static string Iso(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    private static string Numeric(decimal? amount) => amount?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Csv(string value)
    {
        var text = value.AsSpan().TrimStart();
        if (!text.IsEmpty && (text[0] is '=' or '+' or '-' or '@') &&
            !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        if (value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static byte[] Workbook(List<string[]> rows)
    {
        const string spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream = new MemoryStream();
        using (var file = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Add("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Add("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                    xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="Reconciliation" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Add("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            using var worksheet = file.CreateEntry("xl/worksheets/sheet1.xml").Open();
            using var writer = XmlWriter.Create(worksheet);
            writer.WriteStartElement("worksheet", spreadsheet);
            writer.WriteStartElement("sheetData", spreadsheet);
            for (var index = 0; index < rows.Count; index++)
            {
                writer.WriteStartElement("row", spreadsheet);
                writer.WriteAttributeString("r", (index + 1).ToString(CultureInfo.InvariantCulture));
                for (var column = 0; column < rows[index].Length; column++)
                {
                    writer.WriteStartElement("c", spreadsheet);
                    if (index > 0 && column is (5 or 6 or 7) && rows[index][column].Length > 0)
                        writer.WriteElementString("v", spreadsheet, rows[index][column]);
                    else
                    {
                        writer.WriteAttributeString("t", "inlineStr");
                        writer.WriteStartElement("is", spreadsheet);
                        writer.WriteElementString("t", spreadsheet, rows[index][column]);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndElement();

            void Add(string name, string content)
            {
                using var text = new StreamWriter(file.CreateEntry(name).Open(), new UTF8Encoding(false));
                text.Write(content);
            }
        }
        return stream.ToArray();
    }
}


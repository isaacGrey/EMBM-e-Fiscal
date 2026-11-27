using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal static partial class MasterSpreadsheet
{
    private const string SheetType = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Spreadsheet = SheetType;

    public static List<string[]> Read(byte[] bytes, string format)
    {
        try { return format == "xlsx" ? ReadWorkbook(bytes) : ReadCsv(bytes); }
        catch (Exception error) when (error is MalformedLineException or DecoderFallbackException or XmlException
            or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException or FormatException)
        {
            throw new InvalidDataException("The spreadsheet could not be read.", error);
        }
    }

    private static List<string[]> ReadCsv(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new TextFieldParser(stream, new UTF8Encoding(false, true), detectEncoding: true)
        { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        reader.SetDelimiters(",");
        var records = new List<string[]>();
        while (!reader.EndOfData)
        {
            var row = reader.ReadFields() ?? [];
            if (records.Count > MasterImport.MaxRecords || row.Length > 65)
                throw new InvalidDataException("The CSV exceeds the row or column limit.");
            records.Add(row);
        }
        return records;
    }

    private static List<string[]> ReadWorkbook(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 100 || zip.Entries.Sum(x => x.Length) > 16_000_000 ||
            zip.Entries.Any(x => x.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The workbook exceeds the safe ZIP limits.");
        var workbook = Part(zip, "xl/workbook.xml");
        var sheet = workbook.Descendants(Spreadsheet + "sheet").FirstOrDefault() ?? throw new InvalidDataException("Workbook has no sheet.");
        var id = (string?)sheet.Attribute(XName.Get("id", OfficeRel));
        var relations = Part(zip, "xl/_rels/workbook.xml.rels");
        var relation = relations.Descendants(XName.Get("Relationship", PackageRel)).SingleOrDefault(x => (string?)x.Attribute("Id") == id);
        var target = (string?)relation?.Attribute("Target");
        if (target is null || (string?)relation?.Attribute("TargetMode") is not (null or "Internal") ||
            !SheetPath().IsMatch(target) || !((string?)relation?.Attribute("Type") ?? "").EndsWith("/worksheet", StringComparison.Ordinal))
            throw new InvalidDataException("The first tab must be an internal worksheet.");
        var shared = zip.GetEntry("xl/sharedStrings.xml") is null ? [] :
            Part(zip, "xl/sharedStrings.xml").Descendants(Spreadsheet + "si")
                .Select(x => string.Concat(x.Descendants(Spreadsheet + "t").Select(t => t.Value))).ToArray();
        var workbookRows = Part(zip, target.StartsWith('/') ? target[1..] : "xl/" + target)
            .Descendants(Spreadsheet + "row");
        var rows = new List<string[]>();
        foreach (var row in workbookRows)
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(Spreadsheet + "c"))
            {
                if (cell.Element(Spreadsheet + "f") is not null) throw new InvalidDataException("Formula cells must be replaced with values.");
                var address = (string?)cell.Attribute("r");
                var column = Column(address);
                var type = (string?)cell.Attribute("t");
                var value = cell.Element(Spreadsheet + "v")?.Value ?? "";
                var data = type switch
                {
                    "inlineStr" => string.Concat(cell.Descendants(Spreadsheet + "t").Select(x => x.Value)),
                    "s" => shared[int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)],
                    "b" => value == "1" ? "true" : "false",
                    "e" => throw new InvalidDataException("The worksheet contains an Excel error cell."),
                    _ => value
                };
                if (!cells.TryAdd(column, data)) throw new InvalidDataException("The worksheet contains a duplicate cell.");
            }
            if (!cells.Any(x => x.Value.Length > 0)) continue;
            if (rows.Count > MasterImport.MaxRecords) throw new InvalidDataException("The workbook exceeds the row limit.");
            var values = new string[cells.Keys.Last() + 1];
            Array.Fill(values, "");
            foreach (var (index, data) in cells) values[index] = data;
            rows.Add(values);
        }
        return rows;
    }

    private static int Column(string? address)
    {
        if (address is null || !CellAddress().IsMatch(address)) throw new InvalidDataException("Invalid worksheet cell address.");
        var column = 0;
        foreach (var letter in address.TakeWhile(char.IsAsciiLetter)) column = column * 26 + char.ToUpperInvariant(letter) - 'A' + 1;
        if (column is < 1 or > 65) throw new InvalidDataException("The worksheet exceeds the supported column limit.");
        return column - 1;
    }

    private static XDocument Part(ZipArchive zip, string name)
    {
        using var stream = (zip.Entries.SingleOrDefault(x => x.FullName == name) ?? throw new InvalidDataException("A workbook part is missing.")).Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 12_000_000
        });
        return XDocument.Load(reader);
    }

    public static byte[] Write(IReadOnlyList<string[]> table, string format)
    {
        if (format == "csv")
        {
            var lines = table.Select(row => string.Join(",", row.Select(EscapeCsv)));
            return Encoding.UTF8.GetBytes("\uFEFF" + string.Join("\r\n", lines) + "\r\n");
        }
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
                """);
            Entry(zip, "_rels/.rels", $"""
                <Relationships xmlns="{PackageRel}"><Relationship Id="rId1" Type="{OfficeRel}/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Entry(zip, "xl/workbook.xml", $"""
                <workbook xmlns="{SheetType}" xmlns:r="{OfficeRel}"><sheets><sheet name="Master data" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Entry(zip, "xl/_rels/workbook.xml.rels", $"""
                <Relationships xmlns="{PackageRel}"><Relationship Id="rId1" Type="{OfficeRel}/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
                """);
            using var sheet = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            using var writer = XmlWriter.Create(sheet, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
            writer.WriteStartElement("worksheet", SheetType);
            writer.WriteStartElement("sheetData", SheetType);
            for (var index = 0; index < table.Count; index++)
            {
                writer.WriteStartElement("row", SheetType);
                writer.WriteAttributeString("r", (index + 1).ToString(CultureInfo.InvariantCulture));
                for (var cell = 0; cell < table[index].Length; cell++)
                {
                    var a = cell / 26;
                    var column = (a == 0 ? "" : ((char)('A' + a - 1)).ToString()) + (char)('A' + cell % 26);
                    writer.WriteStartElement("c", SheetType);
                    writer.WriteAttributeString("r", column + (index + 1).ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("t", "inlineStr");
                    writer.WriteStartElement("is", SheetType);
                    writer.WriteStartElement("t", SheetType);
                    writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                    writer.WriteString(table[index][cell]);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        return bytes.ToArray();
    }

    private static string EscapeCsv(string value)
    {
        var trimmed = value.TrimStart().TrimStart('\uFEFF').TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' || value.Length > 0 && value[0] is '\t' or '\r' or '\n')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void Entry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    [GeneratedRegex(@"^(?:/xl/)?worksheets/[a-zA-Z0-9_.-]+\.xml$", RegexOptions.CultureInvariant)]
    private static partial Regex SheetPath();
    [GeneratedRegex(@"^[A-Za-z]{1,3}[1-9][0-9]{0,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex CellAddress();
}


using System.Globalization;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal interface IDocumentPdfRenderer
{
    byte[] Render(FiscalDocument doc, bool preview);
}

internal sealed class DocumentPdf : IDocumentPdfRenderer
{
    static DocumentPdf()
    {
        if (OperatingSystem.IsWindows()) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        else GlobalFontSettings.FontResolver = new ContainerFonts();
    }

    public byte[] Render(FiscalDocument doc, bool preview)
    {
        var layout = Read<DocumentTemplates>(doc.Snapshot.Templates).Pdf?.Content ?? throw new InvalidOperationException("No PDF template is pinned.");
        return Render(doc, layout, preview);
    }

    internal byte[] Render(FiscalDocument document, PdfLayout layout, bool preview)
    {
        if (!TemplateRules.ValidLayout(layout)) throw new InvalidDataException("The pinned PDF layout is invalid.");
        using var pdf = new PdfDocument();
        pdf.Info.Title = Name(document.Type) + (preview ? " preview" : " " + document.Number);
        pdf.Info.Creator = "EMBM eFiscal";
        using var canvas = new InvoiceCanvas(pdf, document, layout, preview);
        canvas.Draw();
        canvas.ClosePage();
        using var result = new MemoryStream();
        pdf.Save(result, closeStream: false);
        return result.ToArray();
    }

    private sealed class ContainerFonts : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var name = familyName switch { "Times New Roman" => "DejaVuSerif", "Courier New" => "DejaVuSansMono", _ => "DejaVuSans" };
            return new FontResolverInfo(name + (bold ? "-Bold" : "") + ".ttf", false, italic);
        }

        public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine("/usr/share/fonts/truetype/dejavu", faceName));
    }

    private sealed class InvoiceCanvas : IDisposable
    {
        private readonly FiscalDocument document;
        private readonly PdfDocument pdf;
        private readonly PdfLayout layout;
        private readonly bool preview;
        private readonly DocumentParties parties;
        private readonly DocumentHeader header;
        private readonly XFont body;
        private readonly XFont bold;
        private readonly XFont title;
        private readonly XSolidBrush accent;
        private readonly double margin;
        private readonly int[] weights;
        private PdfPage page = null!;
        private XGraphics? pageGraphics;
        private XGraphics Graphics => pageGraphics!;
        private double y;
        private double right;
        private double limit;
        private double Width => right - margin;
        private double Leading => layout.FontSize + 4;
        private PdfLayout Layout => layout;

        public InvoiceCanvas(PdfDocument pdf, FiscalDocument doc, PdfLayout settings, bool isPreview)
        {
            this.pdf = pdf;
            document = doc;
            layout = settings;
            preview = isPreview;
            parties = Read<DocumentParties>(doc.Snapshot.Parties);
            header = Read<DocumentHeader>(doc.Snapshot.Header);
            var family = layout.Font switch { "serif" => "Times New Roman", "mono" => "Courier New", _ => "Arial" };
            var font = new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset);
            body = new XFont(family, layout.FontSize, XFontStyleEx.Regular, font);
            bold = new XFont(family, layout.FontSize, XFontStyleEx.Bold, font);
            title = new XFont(family, 18, XFontStyleEx.Bold, font);
            var rgb = int.Parse(layout.AccentColor[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            accent = new XSolidBrush(XColor.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255));
            margin = layout.MarginMm * 72d / 25.4;
            weights = layout.ColumnWeights ?? [36, 11, 14, 12, 12, 15];
        }

        public void Draw()
        {
            NewPage(false);
            Block(layout.Header, margin, Width);
            if (layout.Logo is { Length: > 0 } logo)
            {
                using var bytes = new MemoryStream(Convert.FromBase64String(logo["data:image/png;base64,".Length..]));
                using var image = XImage.FromStream(bytes);
                var scale = Math.Min(112d / image.PixelWidth, 48d / image.PixelHeight);
                Graphics.DrawImage(image, margin, y, scale * image.PixelWidth, scale * image.PixelHeight);
                y += scale * image.PixelHeight + 8;
            }
            Party("Seller", parties.Seller, layout.BusinessLayout);
            Block("Branch " + parties.BranchCode + " · " + parties.BranchName + " · " + parties.BranchAddress, margin, Width);
            Party("Customer", parties.Customer, layout.CustomerLayout);
            Block("Date " + document.Date.ToString("yyyy-MM-dd") + "   Due " + document.DueDate.ToString("yyyy-MM-dd")
                + "   Currency " + document.Currency + "   Rate " + Money(document.ExchangeRate), margin, Width);
            if (header.SourceNumber is not null) Block((document.Type == "CreditMemo" ? "Original invoice " : "Sales Order ") + header.SourceNumber, margin, Width);
            if (header.PurchaseOrderReference is not null) Block("Purchase order " + header.PurchaseOrderReference, margin, Width);
            if (header.ExternalReference is not null) Block("External reference " + header.ExternalReference, margin, Width);
            if (header.AdjustmentReason is not null) Block("Adjustment reason " + header.AdjustmentReason, margin, Width);
            if (header.Remarks is not null) Block("Remarks " + header.Remarks, margin, Width);
            y += 10;
            Ensure(Leading + 20);
            TableHeader();
            foreach (var line in document.Lines.OrderBy(x => x.Position)) Row(line);
            DrawTotals();
            Block(layout.PaymentInstructions, margin, Width);
            Block(layout.Terms, margin, Width);
            if (layout.SignatureField)
            {
                Ensure(38);
                y += 20;
                Graphics.DrawLine(XPens.Gray, margin, y, margin + 180, y);
                y += 4;
                Block("Authorized signature", margin, Width);
            }
            Block(layout.Footer, margin, Width);
        }

        private void Party(string heading, PartySnapshot party, string format)
        {
            Ensure(Leading * 2);
            Block(heading + "  ·  " + party.Name, margin, Width, bold);
            var identity = string.Join("   ", new[] { party.TradeName, party.Tin is null ? null : "TIN " + party.Tin,
                party.Rdo is null ? null : "RDO " + party.Rdo, party.VatStatus }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var address = string.Join("   ", new[] { party.RegisteredAddress, party.BillingAddress is null ? null : "Bill to " + party.BillingAddress }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            if (format == "split") Block(string.Join("\n", new[] { identity, address }.Where(x => x.Length > 0)), margin + 12, Width - 12);
            else { Block(identity, margin, Width); Block(address, margin, Width); }
            y += 6;
        }

        private void DrawTotals()
        {
            y += 13;
            var x = Layout.SummaryPosition == "left" ? margin : margin + Width * .45;
            var size = Width * .55;
            (string Label, decimal Value)[] rows = [("Gross sales", document.GrossSales), ("Commercial discounts", document.CommercialDiscount),
                ("Statutory discounts", document.StatutoryDiscount), ("VATable sales", document.VatableSales), ("VAT-exempt sales", document.ExemptSales),
                ("Zero-rated sales", document.ZeroRatedSales), ("Non-VAT sales", document.NonVatSales), ("Output VAT", document.Vat),
                ("Document total", document.Total), ("Less: Creditable withholding tax", document.IncomeWithholding),
                ("Less: Withholding VAT", document.VatWithholding), ("Net amount due", document.AmountDue)];
            foreach (var (name, value) in rows)
            {
                var emphasis = name == "Net amount due";
                var font = emphasis ? bold : body;
                var names = Wrap(name, font, size * .60 - 6);
                var numbers = Wrap(PrintMoney(document, value), font, size * .40 - 6);
                var count = Math.Max(names.Count, numbers.Count);
                Ensure(count * Leading + 4);
                if (emphasis) Graphics.DrawRectangle(XBrushes.LightGray, x, y, size, count * Leading + 4);
                for (var row = 0; row < count; row++)
                {
                    if (row < names.Count) DrawAt(names[row], x + 3, y + 2, size * .60 - 6, font, XBrushes.Black);
                    if (row < numbers.Count) DrawAt(numbers[row], x + size * .60, y + 2, size * .40 - 4, font, XBrushes.Black, true);
                    y += Leading;
                }
                y += 3;
            }
            y += 12;
        }

        private void TableHeader()
        {
            Graphics.DrawRectangle(accent, margin, y, Width, Leading * 1.8);
            var x = margin;
            for (var i = 0; i < layout.Columns.Length; i++)
            {
                var column = layout.Columns[i];
                var width = ColumnWidth(i);
                var label = column switch { "description" => "Item / description", "quantity" => "Qty / UOM", "unitPrice" => "Unit price",
                    "discount" => "Discount", "tax" => "VAT", _ => "Amount" };
                var labels = Wrap(label, bold, width - 8).Take(2);
                var offset = y + 4;
                foreach (var part in labels)
                {
                    DrawAt(part, x + 4, offset, width - 8, bold, XBrushes.White, column != "description");
                    offset += layout.FontSize + 1;
                }
                x += width;
            }
            y += Leading * 1.8;
        }

        private void Row(FiscalDocumentLine line)
        {
            var cells = layout.Columns.Select((column, index) => Wrap(column switch
            {
                "description" => line.ItemCode + " · " + line.Description, "quantity" => Money(line.Quantity) + " " + line.UnitCode,
                "unitPrice" => PrintMoney(document, line.UnitPrice), "discount" => PrintMoney(document, line.CommercialDiscount + line.StatutoryDiscount),
                "tax" => PrintMoney(document, line.Vat), _ => PrintMoney(document, line.Total)
            }, body, ColumnWidth(index) - 8)).ToArray();
            var height = cells.Max(x => x.Count);
            var required = height * Leading + 7;
            var continuationCapacity = limit - margin - Leading * 3.8 - 10;
            if (y + required > limit && required <= continuationCapacity)
            {
                NewPage(true);
                TableHeader();
            }
            // Oversized rows can continue across pages without repeating amounts.
            for (var row = 0; row < height; row++)
            {
                if (y + Leading + 7 > limit) { NewPage(true); TableHeader(); }
                var left = margin;
                for (var cell = 0; cell < cells.Length; cell++)
                {
                    if (row < cells[cell].Count) DrawAt(cells[cell][row], left + 4, y + 3, ColumnWidth(cell) - 8, body,
                        XBrushes.Black, layout.Columns[cell] != "description");
                    left += ColumnWidth(cell);
                }
                y += Leading;
            }
            Graphics.DrawLine(new XPen(XColors.LightGray, .5), margin, y + 3, right, y + 3);
            y += 6;
        }

        private double ColumnWidth(int index) => Width * weights[index] / 100d;

        private void NewPage(bool continuation)
        {
            ClosePage();
            page = pdf.AddPage();
            page.Size = layout.PageSize == "Letter" ? PageSize.Letter : PageSize.A4;
            pageGraphics = XGraphics.FromPdfPage(page);
            right = page.Width.Point - margin;
            limit = page.Height.Point - margin - 28;
            y = margin;
            Block(preview ? "PREVIEW — NOT ISSUED" : "DEVELOPMENT FIXTURE — NOT FOR TAX USE", margin, Width, bold, accent);
            if (!continuation) Block(layout.Title, margin, Width, title, accent);
            Block(Name(document.Type) + "  ·  " + (preview ? "Number assigned on commitment" : document.Number), margin, Width, bold);
            y += 10;
        }

        public void ClosePage()
        {
            if (pageGraphics is null) return;
            if (layout.PageNumbers)
                DrawAt("Page " + pdf.PageCount, margin, page.Height.Point - margin - 11, Width, body, XBrushes.Black, true);
            pageGraphics.Dispose();
            pageGraphics = null;
        }

        private void Ensure(double height)
        {
            if (y + height > limit) NewPage(true);
        }

        private void Block(string? text, double x, double width, XFont? font = null, XBrush? brush = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            font ??= body;
            var step = font == title ? 25 : Leading;
            foreach (var line in Wrap(text, font, width))
            {
                Ensure(step);
                DrawAt(line, x, y, width, font, brush ?? XBrushes.Black);
                y += step;
            }
        }

        private void DrawAt(string text, double x, double top, double width, XFont font, XBrush brush, bool alignRight = false) =>
            Graphics.DrawString(text, font, brush, new XRect(x, top, width, font.Size + 2),
                alignRight ? XStringFormats.TopRight : XStringFormats.TopLeft);

        private List<string> Wrap(string text, XFont font, double width)
        {
            List<string> lines = [];
            foreach (var paragraph in text.Replace("\r", "").Replace("\t", " ").Split('\n'))
            {
                var line = "";
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (Graphics.MeasureString(candidate, font).Width <= width) { line = candidate; continue; }
                    if (line.Length > 0) { lines.Add(line); line = ""; }
                    foreach (var character in word.EnumerateRunes())
                    {
                        var chunk = line + character;
                        if (line.Length > 0 && Graphics.MeasureString(chunk, font).Width > width)
                        { lines.Add(line); line = character.ToString(); }
                        else line = chunk;
                    }
                }
                lines.Add(line);
            }
            return lines;
        }

        public void Dispose() => ClosePage();
    }
}


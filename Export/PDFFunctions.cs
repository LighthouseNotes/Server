using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Syncfusion.Drawing;
using Syncfusion.HtmlConverter;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.HtmlToPdf;

namespace Server.Export;

public class PDFFuctions
{
    public async Task<MemoryStream> GeneratePDFCoverPage(string html, string caseDisplayName)
    {
        // Initialize HTML to PDF converter
        HtmlToPdfConverter htmlConverter = new();

        // Set temp location
        string baseUrl = Environment.CurrentDirectory;

        //Initialize Blink Converter Settings
        BlinkConverterSettings blinkConverterSettings = new();
        blinkConverterSettings.CommandLineArguments.Add("--no-sandbox");
        blinkConverterSettings.CommandLineArguments.Add("--disable-setuid-sandbox");

        // If running on Windows then blink binaries are at %SystemDrive%}/Program Files (x86)/Syncfusion/HTMLConverter/x.x.x/BlinkBinaries/
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Get Syncfusion version
            string? version = Assembly.GetAssembly(typeof(HtmlToPdfConverter))?.GetName().Version?.ToString(3);

            // If version is null then throw an exception
            if (version == null) throw new InvalidOperationException("Unable to determine the Syncfusion version!");

            // Set the blink path
            blinkConverterSettings.BlinkPath =
                @$"{Path.GetPathRoot(Environment.SystemDirectory)}/Program Files (x86)/Syncfusion/HTMLConverter/{version}/BlinkBinaries/";
        }
        // Else BlinkBinaries are in the executing directory 
        else
        {
            blinkConverterSettings.BlinkPath = "BlinkBinaries/";
        }

        // Read MudBlazor css file and set custom CSS
        using StreamReader streamReader = new(@"./Export/MudBlazor.min.css",
            Encoding.UTF8);
        string mudBlazorCSS = await streamReader.ReadToEndAsync();
        blinkConverterSettings.Css = mudBlazorCSS;

        // Margin
        blinkConverterSettings.Margin.All = 25;

        // Header and footer
        blinkConverterSettings.PdfHeader = Header(caseDisplayName, blinkConverterSettings.PdfPageSize.Width);

        // Set HTML converter settings
        htmlConverter.ConverterSettings = blinkConverterSettings;

        // Convert HTML to PDF
        PdfDocument document = htmlConverter.Convert(html, baseUrl);

        // Create memory stream to store the PDF
        MemoryStream inputPDFMemoryStream = new();

        // Save and close the PDF document.
        document.Save(inputPDFMemoryStream);
        document.Close(true);

        inputPDFMemoryStream.Flush();
        inputPDFMemoryStream.Position = 0;

        return inputPDFMemoryStream;
    }

    public async Task<MemoryStream> GeneratePDF(string html, string caseDisplayName, string timeZone)
    {
        // Initialize HTML to PDF converter
        HtmlToPdfConverter htmlConverter = new();

        // Set temp location
        string baseUrl = Environment.CurrentDirectory;

        //Initialize Blink Converter Settings
        BlinkConverterSettings blinkConverterSettings = new();
        blinkConverterSettings.CommandLineArguments.Add("--no-sandbox");
        blinkConverterSettings.CommandLineArguments.Add("--disable-setuid-sandbox");

        // If running on Windows then blink binaries are at %SystemDrive%}/Program Files (x86)/Syncfusion/HTMLConverter/x.x.x/BlinkBinaries/
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Get Syncfusion version
            string? version = Assembly.GetAssembly(typeof(HtmlToPdfConverter))?.GetName().Version?.ToString(3);

            // If version is null then throw an exception
            if (version == null) throw new InvalidOperationException("Unable to determine the Syncfusion version!");

            // Set the blink path
            blinkConverterSettings.BlinkPath =
                @$"{Path.GetPathRoot(Environment.SystemDirectory)}/Program Files (x86)/Syncfusion/HTMLConverter/{version}/BlinkBinaries/";
        }
        // Else BlinkBinaries are in the executing directory 
        else
        {
            blinkConverterSettings.BlinkPath = "BlinkBinaries/";
        }

        // Read MudBlazor css file and set custom CSS
        using StreamReader streamReader = new(@"./Export/MudBlazor.min.css",
            Encoding.UTF8);
        string mudBlazorCSS = await streamReader.ReadToEndAsync();
        blinkConverterSettings.Css = mudBlazorCSS;

        // Margin
        blinkConverterSettings.Margin.All = 25;

        // Header and footer
        blinkConverterSettings.PdfHeader = Header(caseDisplayName, blinkConverterSettings.PdfPageSize.Width);
        blinkConverterSettings.PdfFooter = Footer(timeZone, blinkConverterSettings.PdfPageSize.Width);

        // Table of contents
        blinkConverterSettings.EnableToc = true;
        blinkConverterSettings.Toc.TitleStyle = new HtmlToPdfTocStyle()
        {
            Font = new PdfStandardFont(PdfFontFamily.Helvetica, 20),
            ForeColor = new PdfSolidBrush(Color.Black)
        };
        blinkConverterSettings.Toc.SetItemStyle(2, new HtmlToPdfTocStyle()
        {
            Font = new PdfStandardFont(PdfFontFamily.Helvetica, 12),
            ForeColor = new PdfSolidBrush(Color.Black)
        });
        blinkConverterSettings.Toc.SetItemStyle(3, new HtmlToPdfTocStyle()
        {
            Font = new PdfStandardFont(PdfFontFamily.Helvetica, 10),
            ForeColor = new PdfSolidBrush(Color.Black)
        });
        blinkConverterSettings.Toc.MaximumHeaderLevel = 3;

        // Set HTML converter settings
        htmlConverter.ConverterSettings = blinkConverterSettings;

        // Convert HTML to PDF
        PdfDocument document = htmlConverter.Convert(html, baseUrl);

        // Create memory stream to store the PDF
        MemoryStream inputPDFMemoryStream = new();

        // Save and close the PDF document.
        document.Save(inputPDFMemoryStream);
        document.Close(true);

        inputPDFMemoryStream.Flush();
        inputPDFMemoryStream.Position = 0;

        return inputPDFMemoryStream;
    }

    private PdfPageTemplateElement Header(string displayName, float pageWidth)
    {
        // Header //
        //Create PDF page template element for header with bounds.
        PdfPageTemplateElement header = new(new RectangleF(0, 0, pageWidth, 50));

        PdfFont bigFont = new PdfStandardFont(PdfFontFamily.Helvetica, 11);
        PdfBrush brush = new PdfSolidBrush(Color.Black);
        // Draw the header case name element
        string caseText = displayName;
        SizeF caseTextSize = bigFont.MeasureString(caseText);
        header.Graphics.DrawString(caseText, bigFont, brush,
            new RectangleF(new PointF(pageWidth - caseTextSize.Width, 25), caseTextSize));

        // Draw header logo element
        FileStream imageStream = new("./Export/logo-black.png", FileMode.Open, FileAccess.Read);
        PdfBitmap image = new(imageStream);
        header.Graphics.DrawImage(image, new PointF(1, 1), new SizeF(50, 50));

        return header;
    }

    private PdfPageTemplateElement Footer(string timeZone, float pageWidth)
    {
        // Footer //
        //Create PDF page template element for footer with bounds.
        PdfPageTemplateElement footer = new(new RectangleF(0, 0, pageWidth, 50));

        PdfFont smallFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
        PdfBrush brush = new PdfSolidBrush(Color.Black);

        // Page number
        PdfPageNumberField pageNumber = new(smallFont, PdfBrushes.Black);
        PdfPageCountField count = new(smallFont, PdfBrushes.Black);
        PdfCompositeField compositeField = new(smallFont, PdfBrushes.Black, "Page {0} of {1}", pageNumber, count);
        compositeField.Draw(footer.Graphics, new PointF(0, 25));

        // Timezone text
        string timeZoneText = timeZone;
        SizeF timeZoneTextSize = smallFont.MeasureString(timeZoneText);
        footer.Graphics.DrawString(timeZoneText, smallFont, brush,
            new RectangleF(new PointF(pageWidth - timeZoneTextSize.Width, 25), timeZoneTextSize));

        return footer;
    }
}
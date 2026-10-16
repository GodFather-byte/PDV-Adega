using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text;

namespace PDV_Adega;

public static class EscPosPrinter
{
    private static readonly byte[] Initialize = [0x1B, 0x40];
    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];
    private static readonly byte[] DrawerPulse = [0x1B, 0x70, 0x00, 0x19, 0xFA];
    private static readonly byte[] Cut = [0x1D, 0x56, 0x01];

    public static IReadOnlyList<string> GetInstalledPrinterNames() =>
        new LocalPrintServer().GetPrintQueues()
            .Select(queue => queue.Name)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static void PrintTestReceipt(PrinterOptions options)
    {
        var width = GetLineWidth(options);
        using var output = new MemoryStream();
        output.Write(Initialize);
        output.Write(AlignCenter);
        output.Write(BoldOn);
        WriteLine(output, "VINHA - TESTE DE IMPRESSAO");
        output.Write(BoldOff);
        WriteLine(output, "PDV Adega");
        output.Write(AlignLeft);
        WriteLine(output, new string('-', width));
        WriteLine(output, Fit("Impressora ESC/POS configurada", width));
        WriteLine(output, DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
        WriteLine(output, "Teste concluido.");
        FinishReceipt(output, options.CutPaper);
        PrintRaw(options.PrinterName, output.ToArray());
    }

    public static void PrintSale(PrinterOptions options, SaleRecord sale)
    {
        var width = GetLineWidth(options);
        using var output = new MemoryStream();
        output.Write(Initialize);
        output.Write(AlignCenter);
        output.Write(BoldOn);
        WriteLine(output, "ADEGA SANTA RITA");
        output.Write(BoldOff);
        WriteLine(output, "COMPROVANTE NAO FISCAL");
        output.Write(AlignLeft);
        WriteLine(output, new string('-', width));
        WriteLine(output, $"Venda: {sale.Number:00000}");
        WriteLine(output, sale.SoldAt.ToString("dd/MM/yyyy HH:mm"));
        WriteLine(output, Fit($"Cliente: {sale.Customer}", width));
        WriteLine(output, new string('-', width));

        foreach (var item in sale.Items)
        {
            foreach (var part in Wrap($"{item.Quantity}x {item.ProductName}", width)) WriteLine(output, part);
            WriteLine(output, FormatColumns($"  {item.UnitPrice:C}", item.Total.ToString("C"), width));
        }

        WriteLine(output, new string('-', width));
        WriteLine(output, FormatColumns("Subtotal", sale.Subtotal.ToString("C"), width));
        if (sale.Discount > 0) WriteLine(output, FormatColumns("Desconto", $"-{sale.Discount:C}", width));
        output.Write(BoldOn);
        WriteLine(output, FormatColumns("TOTAL", sale.Total.ToString("C"), width));
        output.Write(BoldOff);
        WriteLine(output, $"Pagamento: {Normalize(sale.PaymentMethod)}");
        output.Write(AlignCenter);
        WriteLine(output, "Obrigado pela preferencia!");
        output.Write(AlignLeft);
        FinishReceipt(output, options.CutPaper);
        PrintRaw(options.PrinterName, output.ToArray());
        if (options.OpenCashDrawer) PrintRaw(options.PrinterName, DrawerPulse);
    }

    private static int GetLineWidth(PrinterOptions options) => options.PaperWidthMm <= 58 ? 32 : 48;

    private static void FinishReceipt(MemoryStream output, bool cutPaper)
    {
        output.Write([0x1B, 0x64, 0x04]);
        if (cutPaper) output.Write(Cut);
    }

    private static string FormatColumns(string left, string right, int width)
    {
        left = Normalize(left);
        right = Normalize(right);
        if (right.Length >= width) return right[^width..];
        var availableLeft = width - right.Length - 1;
        if (left.Length > availableLeft) left = left[..availableLeft];
        return left.PadRight(availableLeft) + " " + right;
    }

    private static string Fit(string value, int width)
    {
        value = Normalize(value);
        return value.Length <= width ? value : value[..width];
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        value = Normalize(value);
        for (var offset = 0; offset < value.Length; offset += width)
            yield return value.Substring(offset, Math.Min(width, value.Length - offset));
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(character <= 0x7F ? character : '?');
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void WriteLine(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(Normalize(text));
        stream.Write(bytes);
        stream.WriteByte(0x0A);
    }

    private static void PrintRaw(string printerName, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException("Selecione uma impressora antes de imprimir.");
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Nao foi possivel abrir a impressora '{printerName}'.");

        var documentStarted = false;
        try
        {
            var document = new DocumentInfo { DocumentName = "PDV Adega - ESC/POS", DataType = "RAW" };
            if (StartDocPrinter(printer, 1, ref document) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Nao foi possivel iniciar o trabalho de impressao.");
            documentStarted = true;
            if (!StartPagePrinter(printer))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Nao foi possivel iniciar a pagina de impressao.");

            try
            {
                if (!WritePrinter(printer, bytes, bytes.Length, out var written) || written != bytes.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "A impressora nao recebeu todos os dados do cupom.");
            }
            finally
            {
                EndPagePrinter(printer);
            }
        }
        finally
        {
            if (documentStarted) EndDocPrinter(printer);
            ClosePrinter(printer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocumentInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr printer, int level, ref DocumentInfo documentInfo);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printer, byte[] bytes, int count, out int written);
}
namespace PDV_Adega;

public sealed class PrinterOptions
{
    public string PrinterName { get; set; } = "";
    public int PaperWidthMm { get; set; } = 80;
    public bool PrintAfterSale { get; set; }
    public bool CutPaper { get; set; } = true;
    public bool OpenCashDrawer { get; set; }
}
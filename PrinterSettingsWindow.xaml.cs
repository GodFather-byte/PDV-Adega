using System.Windows;

namespace PDV_Adega;

public partial class PrinterSettingsWindow : Window
{
    private readonly LocalStore store = new();

    public PrinterSettingsWindow(PrinterOptions options)
    {
        InitializeComponent();
        PaperWidthCombo.SelectedIndex = options.PaperWidthMm <= 58 ? 0 : 1;
        PrintAfterSaleBox.IsChecked = options.PrintAfterSale;
        CutPaperBox.IsChecked = options.CutPaper;
        OpenDrawerBox.IsChecked = options.OpenCashDrawer;

        try
        {
            var printers = EscPosPrinter.GetInstalledPrinterNames();
            PrinterCombo.ItemsSource = printers;
            PrinterCombo.SelectedItem = printers.FirstOrDefault(name =>
                string.Equals(name, options.PrinterName, StringComparison.OrdinalIgnoreCase));
            if (PrinterCombo.SelectedIndex < 0 && printers.Count > 0) PrinterCombo.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Não foi possível listar as impressoras do Windows.\n{exception.Message}",
                "Impressoras indisponíveis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private PrinterOptions? ReadOptions()
    {
        if (PrinterCombo.SelectedItem is not string printerName)
        {
            MessageBox.Show(this, "Instale ou selecione uma impressora antes de continuar.",
                "Impressora não selecionada", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        return new PrinterOptions
        {
            PrinterName = printerName,
            PaperWidthMm = PaperWidthCombo.SelectedIndex == 0 ? 58 : 80,
            PrintAfterSale = PrintAfterSaleBox.IsChecked == true,
            CutPaper = CutPaperBox.IsChecked == true,
            OpenCashDrawer = OpenDrawerBox.IsChecked == true
        };
    }

    private void TestPrint_Click(object sender, RoutedEventArgs e)
    {
        var options = ReadOptions();
        if (options is null) return;

        try
        {
            EscPosPrinter.PrintTestReceipt(options);
            MessageBox.Show(this, "Cupom de teste enviado para a fila de impressão.",
                "Teste enviado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Não foi possível imprimir o teste.\n{exception.Message}",
                "Falha na impressão", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var options = ReadOptions();
        if (options is null) return;

        try
        {
            store.SavePrinterOptions(options);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Não foi possível salvar as configurações.\n{exception.Message}",
                "Falha ao salvar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
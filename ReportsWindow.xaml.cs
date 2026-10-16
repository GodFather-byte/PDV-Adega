using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace PDV_Adega;

public partial class ReportsWindow : Window
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private readonly List<SaleRecord> sales;
    private readonly List<CashSession> sessions;
    private readonly List<Product> products;
    private List<SaleRecord> filteredSales = [];
    private List<CashSession> filteredSessions = [];
    private List<DailyReportRow> dailyRows = [];
    private List<PaymentReportRow> paymentRows = [];
    private List<SaleItemReportRow> itemRows = [];
    private List<ProductReportRow> productRows = [];
    private List<CategoryReportRow> categoryRows = [];
    private List<InventoryReportRow> inventoryRows = [];
    private List<SaleReportRow> saleRows = [];
    private List<CashSessionReportRow> sessionRows = [];
    private List<CashMovementReportRow> movementRows = [];

    public ReportsWindow(IEnumerable<SaleRecord> sales, IEnumerable<CashSession> sessions, IEnumerable<Product> products)
    {
        this.sales = sales.ToList();
        this.sessions = sessions.ToList();
        this.products = products.ToList();
        InitializeComponent();
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-29);
        ToDatePicker.SelectedDate = DateTime.Today;
        RefreshReports();
    }

    private void ApplyFilter_Click(object sender, RoutedEventArgs e)
    {
        if (FromDatePicker.SelectedDate is null || ToDatePicker.SelectedDate is null)
        {
            MessageBox.Show(this, "Selecione as duas datas do período.", "Período inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (FromDatePicker.SelectedDate.Value.Date > ToDatePicker.SelectedDate.Value.Date)
        {
            MessageBox.Show(this, "A data inicial não pode ser posterior à data final.", "Período inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RefreshReports();
    }

    private void RefreshReports()
    {
        var from = (FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-29)).Date;
        var toExclusive = (ToDatePicker.SelectedDate ?? DateTime.Today).Date.AddDays(1);
        filteredSales = sales.Where(sale => sale.SoldAt >= from && sale.SoldAt < toExclusive)
            .OrderByDescending(sale => sale.SoldAt).ToList();
        filteredSessions = sessions.Where(session => session.OpenedAt < toExclusive &&
            (session.ClosedAt is null || session.ClosedAt.Value >= from)).OrderByDescending(session => session.OpenedAt).ToList();

        var subtotal = filteredSales.Sum(sale => sale.Subtotal);
        var discount = filteredSales.Sum(sale => sale.Discount);
        var total = filteredSales.Sum(sale => sale.Total);
        SaleCountLabel.Text = filteredSales.Count.ToString(BrazilianCulture);
        SubtotalLabel.Text = subtotal.ToString("C", BrazilianCulture);
        DiscountLabel.Text = discount.ToString("C", BrazilianCulture);
        TotalLabel.Text = total.ToString("C", BrazilianCulture);
        AverageTicketLabel.Text = (filteredSales.Count == 0 ? 0 : total / filteredSales.Count).ToString("C", BrazilianCulture);
        PeriodLabel.Text = $"Período: {from:dd/MM/yyyy} a {toExclusive.AddDays(-1):dd/MM/yyyy} · {filteredSales.Count} venda(s) · {filteredSessions.Count} caixa(s)";

        dailyRows = filteredSales.GroupBy(sale => sale.SoldAt.Date).OrderByDescending(group => group.Key)
            .Select(group => new DailyReportRow(group.Key, group.Count(), group.Sum(sale => sale.Subtotal), group.Sum(sale => sale.Discount), group.Sum(sale => sale.Total))).ToList();
        paymentRows = filteredSales.GroupBy(sale => sale.PaymentMethod).OrderBy(group => group.Key)
            .Select(group => new PaymentReportRow(group.Key, group.Count(), group.Sum(sale => sale.Total))).ToList();
        saleRows = filteredSales.Select(sale => new SaleReportRow(sale.Number, sale.SoldAt, sale.Customer,
            sale.PaymentMethod, sale.Items.Sum(item => item.Quantity), sale.Subtotal, sale.Discount, sale.Total, sale.CashSessionNumber)).ToList();
        itemRows = BuildItemRows(filteredSales);
        productRows = BuildProductRows(itemRows);
        categoryRows = BuildCategoryRows(itemRows);
        inventoryRows = products.Select(product => new InventoryReportRow(product.Barcode, product.Name,
                product.Category, product.Price, product.Stock, product.Price * product.Stock))
            .OrderBy(row => row.Category).ThenBy(row => row.ProductName).ToList();
        sessionRows = BuildSessionRows(filteredSessions);
        movementRows = filteredSessions.SelectMany(session => session.Movements
                .Where(movement => movement.CreatedAt >= from && movement.CreatedAt < toExclusive)
                .Select(movement => new CashMovementReportRow(session.Number, movement.CreatedAt, movement.Type, movement.Note, movement.Amount)))
            .OrderByDescending(movement => movement.CreatedAt).ToList();

        DailyGrid.ItemsSource = dailyRows;
        PaymentGrid.ItemsSource = paymentRows;
        SalesGrid.ItemsSource = saleRows;
        ItemsGrid.ItemsSource = itemRows;
        ProductsGrid.ItemsSource = productRows;
        CategoriesGrid.ItemsSource = categoryRows;
        InventoryGrid.ItemsSource = inventoryRows;
        SessionsGrid.ItemsSource = sessionRows;
        MovementsGrid.ItemsSource = movementRows;
    }

    private static List<SaleItemReportRow> BuildItemRows(IEnumerable<SaleRecord> periodSales)
    {
        var rows = new List<SaleItemReportRow>();
        foreach (var sale in periodSales)
        {
            var items = (sale.Items ?? []).ToList();
            var itemSubtotal = items.Sum(item => item.Total);
            var discountBase = sale.Subtotal > 0 ? sale.Subtotal : itemSubtotal;
            var allocatedTotal = 0m;
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var gross = item.Total;
                var allocatedDiscount = index == items.Count - 1
                    ? sale.Discount - allocatedTotal
                    : discountBase > 0
                        ? Math.Min(sale.Discount - allocatedTotal,
                            Math.Round(sale.Discount * gross / discountBase, 2, MidpointRounding.AwayFromZero))
                        : 0;
                allocatedTotal += allocatedDiscount;
                rows.Add(new SaleItemReportRow(sale.Number, sale.SoldAt, sale.Customer, sale.PaymentMethod,
                    item.ProductName, item.Category, item.Quantity, item.UnitPrice, gross, allocatedDiscount,
                    gross - allocatedDiscount, sale.CashSessionNumber));
            }
        }

        return rows.OrderByDescending(row => row.SoldAt).ThenBy(row => row.SaleNumber).ToList();
    }

    private static List<ProductReportRow> BuildProductRows(IEnumerable<SaleItemReportRow> itemLines) =>
        itemLines.GroupBy(row => row.ProductName)
            .Select(group => new ProductReportRow(group.Key, group.Sum(row => row.Quantity), group.Sum(row => row.Gross),
                group.Sum(row => row.Discount), group.Sum(row => row.Net)))
            .OrderByDescending(row => row.Quantity).ThenBy(row => row.ProductName).ToList();

    private static List<CategoryReportRow> BuildCategoryRows(IEnumerable<SaleItemReportRow> itemLines) =>
        itemLines.GroupBy(row => string.IsNullOrWhiteSpace(row.Category) ? "Sem categoria" : row.Category)
            .Select(group => new CategoryReportRow(group.Key, group.Sum(row => row.Quantity), group.Sum(row => row.Gross),
                group.Sum(row => row.Discount), group.Sum(row => row.Net)))
            .OrderByDescending(row => row.Net).ThenBy(row => row.Category).ToList();

    private List<CashSessionReportRow> BuildSessionRows(IEnumerable<CashSession> periodSessions)
    {
        return periodSessions.Select(session =>
        {
            var cashSales = sales.Where(sale => sale.CashSessionNumber == session.Number && sale.PaymentMethod == "Dinheiro")
                .Sum(sale => sale.Total);
            var supplies = session.Movements.Where(movement => movement.IsEntry).Sum(movement => movement.Amount);
            var withdrawals = session.Movements.Where(movement => !movement.IsEntry).Sum(movement => movement.Amount);
            var expected = session.OpeningCash + cashSales + supplies - withdrawals;
            var difference = session.CountedCash is null ? (decimal?)null : session.CountedCash.Value - expected;
            return new CashSessionReportRow(session.Number, session.OpenedAt, session.ClosedAt, session.OpeningCash,
                cashSales, supplies, withdrawals, expected, session.CountedCash, difference,
                session.ClosedAt is null ? "Aberto" : "Fechado");
        }).ToList();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".csv",
            Filter = "Arquivo CSV (*.csv)|*.csv",
            FileName = $"relatorio-{ReportTabs.SelectedIndex + 1}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Title = "Exportar relatório"
        };
        if (dialog.ShowDialog(this) != true) return;

        var lines = ReportTabs.SelectedIndex switch
        {
            0 => ExportOverview(),
            1 => ExportSales(),
            2 => ExportItems(),
            3 => ExportProducts(),
            4 => ExportCategories(),
            5 => ExportInventory(),
            6 => ExportSessions(),
            7 => ExportMovements(),
            _ => []
        };
        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
        MessageBox.Show(this, $"Relatório exportado para:\n{dialog.FileName}", "Exportação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private IEnumerable<string> ExportOverview()
    {
        yield return "Data;Vendas;Subtotal;Descontos;Total";
        foreach (var row in dailyRows)
            yield return JoinCsv(row.Date.ToString("yyyy-MM-dd"), row.SaleCount.ToString(BrazilianCulture),
                row.Subtotal.ToString("0.00", CultureInfo.InvariantCulture), row.Discount.ToString("0.00", CultureInfo.InvariantCulture), row.Total.ToString("0.00", CultureInfo.InvariantCulture));
        yield return "";
        yield return "Forma de pagamento;Vendas;Total";
        foreach (var row in paymentRows)
            yield return JoinCsv(row.PaymentMethod, row.SaleCount.ToString(BrazilianCulture), row.Total.ToString("0.00", CultureInfo.InvariantCulture));
    }

    private IEnumerable<string> ExportSales()
    {
        yield return "Venda;Data;Cliente;Pagamento;Itens;Subtotal;Desconto;Total;Caixa";
        foreach (var row in saleRows)
            yield return JoinCsv(row.Number.ToString(BrazilianCulture), row.SoldAt.ToString("yyyy-MM-dd HH:mm:ss"), row.Customer, row.PaymentMethod,
                row.ItemCount.ToString(BrazilianCulture), row.Subtotal.ToString("0.00", CultureInfo.InvariantCulture),
                row.Discount.ToString("0.00", CultureInfo.InvariantCulture), row.Total.ToString("0.00", CultureInfo.InvariantCulture),
                row.CashSessionNumber?.ToString(BrazilianCulture) ?? "");
    }

    private IEnumerable<string> ExportProducts()
    {
        yield return "Produto;Unidades;Venda bruta;Desconto rateado;Venda líquida";
        foreach (var row in productRows)
            yield return JoinCsv(row.ProductName, row.Quantity.ToString(BrazilianCulture), row.Gross.ToString("0.00", CultureInfo.InvariantCulture),
                row.Discount.ToString("0.00", CultureInfo.InvariantCulture), row.Net.ToString("0.00", CultureInfo.InvariantCulture));
    }

    private IEnumerable<string> ExportItems()
    {
        yield return "Venda;Data;Cliente;Pagamento;Produto;Categoria;Unidades;Preço unitário;Bruto;Desconto rateado;Líquido;Caixa";
        foreach (var row in itemRows)
            yield return JoinCsv(row.SaleNumber.ToString(BrazilianCulture), row.SoldAt.ToString("yyyy-MM-dd HH:mm:ss"),
                row.Customer, row.PaymentMethod, row.ProductName, row.Category, row.Quantity.ToString(BrazilianCulture),
                row.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture), row.Gross.ToString("0.00", CultureInfo.InvariantCulture),
                row.Discount.ToString("0.00", CultureInfo.InvariantCulture), row.Net.ToString("0.00", CultureInfo.InvariantCulture),
                row.CashSessionNumber?.ToString(BrazilianCulture) ?? "");
    }

    private IEnumerable<string> ExportCategories()
    {
        yield return "Categoria;Unidades;Venda bruta;Desconto rateado;Venda líquida";
        foreach (var row in categoryRows)
            yield return JoinCsv(row.Category, row.Quantity.ToString(BrazilianCulture), row.Gross.ToString("0.00", CultureInfo.InvariantCulture),
                row.Discount.ToString("0.00", CultureInfo.InvariantCulture), row.Net.ToString("0.00", CultureInfo.InvariantCulture));
    }

    private IEnumerable<string> ExportInventory()
    {
        yield return "Código de barras;Produto;Categoria;Preço;Saldo;Valor a preço de venda";
        foreach (var row in inventoryRows)
            yield return JoinCsv(row.Barcode, row.ProductName, row.Category, row.Price.ToString("0.00", CultureInfo.InvariantCulture),
                row.Stock.ToString(BrazilianCulture), row.InventoryValue.ToString("0.00", CultureInfo.InvariantCulture));
    }

    private IEnumerable<string> ExportSessions()
    {
        yield return "Caixa;Abertura;Fechamento;Fundo inicial;Vendas em dinheiro;Suprimentos;Sangrias;Esperado;Contado;Diferença;Situação";
        foreach (var row in sessionRows)
            yield return JoinCsv(row.Number.ToString(BrazilianCulture), row.OpenedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                row.ClosedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", row.OpeningCash.ToString("0.00", CultureInfo.InvariantCulture),
                row.CashSales.ToString("0.00", CultureInfo.InvariantCulture), row.Supplies.ToString("0.00", CultureInfo.InvariantCulture),
                row.Withdrawals.ToString("0.00", CultureInfo.InvariantCulture), row.Expected.ToString("0.00", CultureInfo.InvariantCulture),
                row.Counted?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", row.Difference?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", row.Status);
    }

    private IEnumerable<string> ExportMovements()
    {
        yield return "Caixa;Data;Tipo;Descrição;Valor";
        foreach (var row in movementRows)
            yield return JoinCsv(row.SessionNumber.ToString(BrazilianCulture), row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), row.Type,
                row.Note, row.Amount.ToString("0.00", CultureInfo.InvariantCulture));
    }

    private static string JoinCsv(params string[] values) => string.Join(';', values.Select(CsvField));

    private static string CsvField(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

public sealed record DailyReportRow(DateTime Date, int SaleCount, decimal Subtotal, decimal Discount, decimal Total);
public sealed record PaymentReportRow(string PaymentMethod, int SaleCount, decimal Total);
public sealed record SaleReportRow(int Number, DateTime SoldAt, string Customer, string PaymentMethod, int ItemCount,
    decimal Subtotal, decimal Discount, decimal Total, int? CashSessionNumber);
public sealed record SaleItemReportRow(int SaleNumber, DateTime SoldAt, string Customer, string PaymentMethod,
    string ProductName, string Category, int Quantity, decimal UnitPrice, decimal Gross, decimal Discount, decimal Net,
    int? CashSessionNumber);
public sealed record ProductReportRow(string ProductName, int Quantity, decimal Gross, decimal Discount, decimal Net);
public sealed record CategoryReportRow(string Category, int Quantity, decimal Gross, decimal Discount, decimal Net);
public sealed record InventoryReportRow(string Barcode, string ProductName, string Category, decimal Price, int Stock,
    decimal InventoryValue);
public sealed record CashSessionReportRow(int Number, DateTime OpenedAt, DateTime? ClosedAt, decimal OpeningCash,
    decimal CashSales, decimal Supplies, decimal Withdrawals, decimal Expected, decimal? Counted, decimal? Difference, string Status);
public sealed record CashMovementReportRow(int SessionNumber, DateTime CreatedAt, string Type, string Note, decimal Amount);

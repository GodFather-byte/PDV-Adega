using System.IO;
using System.Text.Json;

namespace PDV_Adega;

public sealed class LocalStore
{
    private readonly string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PDV-Adega");
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public List<Product> LoadProducts()
    {
        var path = Path.Combine(directory, "products.json");
        if (File.Exists(path))
        {
            try
            {
                return JsonSerializer.Deserialize<List<Product>>(File.ReadAllText(path)) ?? SeedProducts();
            }
            catch (JsonException)
            {
                return SeedProducts();
            }
        }

        var products = SeedProducts();
        SaveProducts(products);
        return products;
    }

    public List<SaleRecord> LoadSales()
    {
        var path = Path.Combine(directory, "sales.json");
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<SaleRecord>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public List<CashSession> LoadCashSessions()
    {
        var path = Path.Combine(directory, "cash-sessions.json");
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<CashSession>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public PrinterOptions LoadPrinterOptions()
    {
        var path = Path.Combine(directory, "printer-settings.json");
        if (!File.Exists(path)) return new PrinterOptions();

        try
        {
            return JsonSerializer.Deserialize<PrinterOptions>(File.ReadAllText(path)) ?? new PrinterOptions();
        }
        catch (JsonException)
        {
            return new PrinterOptions();
        }
    }

    public void SaveProducts(IEnumerable<Product> products) => Save("products.json", products);

    public void SaveSales(IEnumerable<SaleRecord> sales) => Save("sales.json", sales);

    public void SaveCashSessions(IEnumerable<CashSession> sessions) => Save("cash-sessions.json", sessions);

    public void SavePrinterOptions(PrinterOptions options) => Save("printer-settings.json", options);

    private void Save<T>(string fileName, T value)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), JsonSerializer.Serialize(value, jsonOptions));
    }

    private static List<Product> SeedProducts() =>
    [
        new() { Id = 1, Barcode = "7891234500010", Name = "Cordero con Piel de Lobo Malbec", Category = "Vinhos", Origin = "Argentina", Price = 89.90m, Stock = 12 },
        new() { Id = 2, Barcode = "7891234500027", Name = "Casillero del Diablo Reserva", Category = "Vinhos", Origin = "Chile", Price = 74.50m, Stock = 8 },
        new() { Id = 3, Barcode = "7891234500034", Name = "Casa Perini Moscatel", Category = "Espumantes", Origin = "Brasil", Price = 56.90m, Stock = 5 },
        new() { Id = 4, Barcode = "7891234500041", Name = "Gin Beefeater London Dry", Category = "Destilados", Origin = "Inglaterra", Price = 119.90m, Stock = 7 },
        new() { Id = 5, Barcode = "7891234500058", Name = "Chivas Regal 12 anos", Category = "Destilados", Origin = "Escócia", Price = 189.90m, Stock = 4 },
        new() { Id = 6, Barcode = "7891234500065", Name = "Santa Helena Sauvignon Blanc", Category = "Vinhos", Origin = "Chile", Price = 42.90m, Stock = 16 },
        new() { Id = 7, Barcode = "7891234500072", Name = "Água tônica Prata 269 ml", Category = "Sem álcool", Origin = "Brasil", Price = 5.90m, Stock = 24 },
        new() { Id = 8, Barcode = "7891234500089", Name = "Colorado Ribeirão Lager", Category = "Cervejas", Origin = "Brasil", Price = 8.90m, Stock = 18 },
        new() { Id = 9, Barcode = "7891234500096", Name = "Licor 43 Original", Category = "Destilados", Origin = "Espanha", Price = 139.90m, Stock = 3 }
    ];
}

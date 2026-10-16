namespace PDV_Adega;

public sealed class SaleRecord
{
    public int Number { get; set; }
    public DateTime SoldAt { get; set; }
    public string PaymentMethod { get; set; } = "";
    public string Customer { get; set; } = "Consumidor final";
    public int? CashSessionNumber { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public List<SaleItem> Items { get; set; } = [];
}

public sealed class SaleItem
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Category { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total => Quantity * UnitPrice;
}

namespace PDV_Adega;

public sealed class CashSession
{
    public int Number { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? ExpectedCash { get; set; }
    public List<CashMovement> Movements { get; set; } = [];
}

public sealed class CashMovement
{
    public DateTime CreatedAt { get; set; }
    public string Type { get; set; } = "";
    public string Note { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsEntry { get; set; }
}
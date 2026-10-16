using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PDV_Adega;

public sealed class Product : INotifyPropertyChanged
{
    private int stock;

    public int Id { get; set; }
    public string Barcode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Origin { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock
    {
        get => stock;
        set
        {
            if (stock == value) return;
            stock = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

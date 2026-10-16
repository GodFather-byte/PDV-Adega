using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PDV_Adega;

public sealed class CartLine : INotifyPropertyChanged
{
    private int quantity;

    public CartLine(Product product, int quantity = 1)
    {
        Product = product;
        this.quantity = quantity;
    }

    public Product Product { get; }
    public int Quantity
    {
        get => quantity;
        set
        {
            if (quantity == value) return;
            quantity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LineTotal));
        }
    }
    public decimal LineTotal => Product.Price * Quantity;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

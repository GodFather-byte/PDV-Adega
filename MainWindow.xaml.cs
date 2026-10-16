using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PDV_Adega;

public partial class MainWindow : Window
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private readonly LocalStore store = new();
    private readonly ObservableCollection<Product> visibleProducts = [];
    private readonly ObservableCollection<CartLine> cart = [];
    private List<Product> products = [];
    private List<SaleRecord> sales = [];
    private string selectedCategory = "Todos";

    public MainWindow()
    {
        CultureInfo.DefaultThreadCurrentCulture = BrazilianCulture;
        CultureInfo.DefaultThreadCurrentUICulture = BrazilianCulture;
        InitializeComponent();

        DateLabel.Text = BrazilianCulture.TextInfo.ToTitleCase(
            DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy", BrazilianCulture));
        products = store.LoadProducts();
        sales = store.LoadSales();
        CategoriesList.ItemsSource = new[] { "Todos" }.Concat(products.Select(product => product.Category).Distinct()).ToList();
        CategoriesList.Loaded += (_, _) => UpdateCategoryStyles();
        ProductsList.ItemsSource = visibleProducts;
        CartList.ItemsSource = cart;
        RefreshProducts();
        RefreshCart();
    }

    private void RefreshProducts()
    {
        var query = SearchBox.Text.Trim();
        var filtered = products.Where(product =>
                (selectedCategory == "Todos" || product.Category == selectedCategory) &&
                (query.Length == 0 || product.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 product.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 product.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        visibleProducts.Clear();
        foreach (var product in filtered) visibleProducts.Add(product);
        ProductCountLabel.Text = $"{filtered.Count} {(filtered.Count == 1 ? "produto" : "produtos")}";
    }

    private void RefreshCart()
    {
        EmptyCartLabel.Visibility = cart.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CheckoutButton.IsEnabled = cart.Count > 0;
        ClearCartButton.IsEnabled = cart.Count > 0;
        SaleNumberLabel.Text = $"Venda #{sales.Count + 1:00000}";
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var subtotal = cart.Sum(line => line.LineTotal);
        var requestedDiscount = decimal.TryParse(DiscountBox.Text, NumberStyles.Number, BrazilianCulture, out var parsedDiscount)
            ? Math.Max(0, parsedDiscount)
            : 0;
        var discount = Math.Min(requestedDiscount, subtotal);
        SubtotalLabel.Text = subtotal.ToString("C", BrazilianCulture);
        DiscountLabel.Text = discount == 0 ? "R$ 0,00" : $"− {discount.ToString("C", BrazilianCulture)}";
        TotalLabel.Text = (subtotal - discount).ToString("C", BrazilianCulture);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ProductsList is not null) RefreshProducts();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (visibleProducts.Count == 1) AddProduct(visibleProducts[0]);
        SearchBox.Clear();
        e.Handled = true;
    }

    private void FocusSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string category) return;
        selectedCategory = category;
        UpdateCategoryStyles();
        RefreshProducts();
    }

    private void UpdateCategoryStyles()
    {
        foreach (var categoryButton in FindVisualChildren<Button>(CategoriesList))
        {
            var active = Equals(categoryButton.Tag, selectedCategory);
            categoryButton.Background = active ? new SolidColorBrush(Color.FromRgb(32, 43, 37)) : Brushes.Transparent;
            categoryButton.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(94, 104, 97));
            categoryButton.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(32, 43, 37)) : new SolidColorBrush(Color.FromRgb(229, 233, 227));
        }
    }

    private void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Product product }) AddProduct(product);
    }

    private void AddProduct(Product product)
    {
        var line = cart.FirstOrDefault(item => item.Product.Id == product.Id);
        if ((line?.Quantity ?? 0) >= product.Stock)
        {
            MessageBox.Show("Não há mais unidades disponíveis deste produto.", "Estoque insuficiente", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (line is null) cart.Add(new CartLine(product));
        else line.Quantity++;
        RefreshCart();
    }

    private void IncreaseQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CartLine line }) return;
        if (line.Quantity >= line.Product.Stock)
        {
            MessageBox.Show("A quantidade no carrinho atingiu o estoque disponível.", "Estoque insuficiente", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        line.Quantity++;
        RefreshCart();
    }

    private void DecreaseQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CartLine line }) return;
        if (line.Quantity <= 1) cart.Remove(line);
        else line.Quantity--;
        RefreshCart();
    }

    private void DiscountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) UpdateSummary();
    }

    private void ClearCart_Click(object sender, RoutedEventArgs e)
    {
        if (cart.Count == 0) return;
        var answer = MessageBox.Show("Deseja remover todos os itens desta venda?", "Limpar venda", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        cart.Clear();
        DiscountBox.Text = "0";
        RefreshCart();
    }

    private void Checkout_Click(object sender, RoutedEventArgs e)
    {
        if (cart.Count == 0) return;
        var subtotal = cart.Sum(line => line.LineTotal);
        var requestedDiscount = decimal.TryParse(DiscountBox.Text, NumberStyles.Number, BrazilianCulture, out var parsedDiscount)
            ? Math.Max(0, parsedDiscount)
            : 0;
        var discount = Math.Min(requestedDiscount, subtotal);
        var total = subtotal - discount;
        var payment = new PaymentWindow(total) { Owner = this };
        if (payment.ShowDialog() != true) return;

        var sale = new SaleRecord
        {
            Number = sales.Count + 1,
            SoldAt = DateTime.Now,
            PaymentMethod = payment.SelectedMethod,
            Customer = string.IsNullOrWhiteSpace(CustomerBox.Text) ? "Consumidor final" : CustomerBox.Text.Trim(),
            Subtotal = subtotal,
            Discount = discount,
            Total = total,
            Items = cart.Select(line => new SaleItem
            {
                ProductId = line.Product.Id,
                ProductName = line.Product.Name,
                Quantity = line.Quantity,
                UnitPrice = line.Product.Price
            }).ToList()
        };

        foreach (var line in cart) line.Product.Stock -= line.Quantity;
        sales.Add(sale);
        store.SaveProducts(products);
        store.SaveSales(sales);

        MessageBox.Show(
            $"Venda #{sale.Number:00000} concluída.\nTotal: {sale.Total.ToString("C", BrazilianCulture)}\nPagamento: {sale.PaymentMethod}",
            "Venda concluída", MessageBoxButton.OK, MessageBoxImage.Information);

        cart.Clear();
        DiscountBox.Text = "0";
        CustomerBox.Text = "Consumidor final";
        RefreshProducts();
        RefreshCart();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}

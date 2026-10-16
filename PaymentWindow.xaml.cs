using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PDV_Adega;

public partial class PaymentWindow : Window
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private readonly decimal total;

    public PaymentWindow(decimal total)
    {
        this.total = total;
        InitializeComponent();
        TotalLabel.Text = total.ToString("C", BrazilianCulture);
    }

    public string SelectedMethod => CashOption.IsChecked == true
        ? "Dinheiro"
        : CardOption.IsChecked == true ? "Cartão" : "Pix";

    private void PaymentOption_Checked(object sender, RoutedEventArgs e)
    {
        if (CashPanel is null) return;

        CashPanel.Visibility = CashOption.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SetOptionStyle(PixBorder, PixOption.IsChecked == true);
        SetOptionStyle(CardBorder, CardOption.IsChecked == true);
        SetOptionStyle(CashBorder, CashOption.IsChecked == true);
        UpdateChange();
    }

    private static void SetOptionStyle(Border border, bool selected)
    {
        border.Background = selected ? new SolidColorBrush(Color.FromRgb(234, 241, 235)) : Brushes.White;
        border.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(52, 93, 71)) : new SolidColorBrush(Color.FromRgb(229, 233, 227));
    }

    private void CashReceivedBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateChange();

    private void UpdateChange()
    {
        if (CashReceivedBox is null || ChangeLabel is null) return;
        if (decimal.TryParse(CashReceivedBox.Text, NumberStyles.Currency, BrazilianCulture, out var received))
        {
            ChangeLabel.Text = $"Troco: {Math.Max(0, received - total).ToString("C", BrazilianCulture)}";
        }
        else
        {
            ChangeLabel.Text = $"Troco: {0m.ToString("C", BrazilianCulture)}";
        }
    }

    private void ConfirmPayment_Click(object sender, RoutedEventArgs e)
    {
        if (CashOption.IsChecked == true &&
            (!decimal.TryParse(CashReceivedBox.Text, NumberStyles.Currency, BrazilianCulture, out var received) || received < total))
        {
            MessageBox.Show("O valor recebido precisa ser igual ou maior que o total.", "Valor inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            CashReceivedBox.Focus();
            return;
        }

        DialogResult = true;
    }
}

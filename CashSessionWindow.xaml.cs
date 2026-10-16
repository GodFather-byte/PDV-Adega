using System.Globalization;
using System.Windows;

namespace PDV_Adega;

public partial class CashSessionWindow : Window
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private readonly LocalStore store = new();
    private readonly List<SaleRecord> sales;
    private readonly List<CashSession> sessions;
    private CashSession? activeSession;

    public CashSessionWindow(IEnumerable<SaleRecord> sales)
    {
        this.sales = sales.ToList();
        sessions = store.LoadCashSessions();
        activeSession = sessions.LastOrDefault(session => session.ClosedAt is null);
        InitializeComponent();

        if (activeSession is null)
        {
            SubtitleLabel.Text = "Abra o caixa antes de iniciar as vendas.";
            OpenPanel.Visibility = Visibility.Visible;
            SessionPanel.Visibility = Visibility.Collapsed;
            PrimaryButton.Content = "Abrir caixa";
        }
        else
        {
            SubtitleLabel.Text = "Acompanhe as entradas, saídas e o saldo do turno.";
            OpenPanel.Visibility = Visibility.Collapsed;
            SessionPanel.Visibility = Visibility.Visible;
            PrimaryButton.Content = "Fechar caixa";
            UpdateSessionSummary();
        }
    }

    private decimal CashSales => activeSession is null
        ? 0
        : sales.Where(sale => sale.CashSessionNumber == activeSession.Number && sale.PaymentMethod == "Dinheiro")
            .Sum(sale => sale.Total);

    private decimal ExpectedCash => activeSession is null
        ? 0
        : activeSession.OpeningCash + CashSales + activeSession.Movements.Sum(movement =>
            movement.IsEntry ? movement.Amount : -movement.Amount);

    private void UpdateSessionSummary()
    {
        if (activeSession is null) return;
        SessionNumberLabel.Text = $"Caixa #{activeSession.Number:00000}";
        OpenedAtLabel.Text = $"Aberto em {activeSession.OpenedAt.ToString("dd/MM/yyyy HH:mm", BrazilianCulture)}";
        OpeningCashLabel.Text = activeSession.OpeningCash.ToString("C", BrazilianCulture);
        CashSalesLabel.Text = CashSales.ToString("C", BrazilianCulture);
        MovementsTotalLabel.Text = (ExpectedCash - activeSession.OpeningCash - CashSales).ToString("C", BrazilianCulture);
        ExpectedCashLabel.Text = ExpectedCash.ToString("C", BrazilianCulture);
        MovementFeedbackLabel.Text = $"{activeSession.Movements.Count} movimento(s) registrado(s).";
        UpdateDifference();
    }

    private void UpdateDifference()
    {
        if (activeSession is null || DifferenceLabel is null) return;
        if (!decimal.TryParse(CountedCashBox.Text, NumberStyles.Number, BrazilianCulture, out var counted))
        {
            DifferenceLabel.Text = "Diferença: —";
            return;
        }

        var difference = counted - ExpectedCash;
        DifferenceLabel.Text = $"Diferença: {difference.ToString("C", BrazilianCulture)}";
        DifferenceLabel.Foreground = difference == 0
            ? System.Windows.Media.Brushes.ForestGreen
            : System.Windows.Media.Brushes.Firebrick;
    }

    private void CountedCashBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateDifference();

    private void RegisterMovement_Click(object sender, RoutedEventArgs e)
    {
        if (activeSession is null) return;
        if (!decimal.TryParse(MovementAmountBox.Text, NumberStyles.Number, BrazilianCulture, out var amount) || amount <= 0)
        {
            MessageBox.Show(this, "Informe um valor maior que zero.", "Valor inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isEntry = MovementTypeCombo.SelectedIndex == 0;
        var note = MovementNoteBox.Text.Trim();
        if (note.Length == 0)
        {
            MessageBox.Show(this, "Descreva o motivo do movimento para manter o histórico do caixa.", "Motivo obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            MovementNoteBox.Focus();
            return;
        }
        if (!isEntry && amount > ExpectedCash)
        {
            MessageBox.Show(this, "A sangria não pode ser maior que o saldo esperado em caixa.", "Saldo insuficiente", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        activeSession.Movements.Add(new CashMovement
        {
            CreatedAt = DateTime.Now,
            Type = isEntry ? "Suprimento" : "Sangria",
            Note = note,
            Amount = amount,
            IsEntry = isEntry
        });
        store.SaveCashSessions(sessions);
        MovementAmountBox.Clear();
        MovementNoteBox.Clear();
        UpdateSessionSummary();
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (activeSession is null)
        {
            if (!decimal.TryParse(OpeningCashBox.Text, NumberStyles.Number, BrazilianCulture, out var openingCash) || openingCash < 0)
            {
                MessageBox.Show(this, "Informe um fundo inicial válido.", "Valor inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            activeSession = new CashSession
            {
                Number = sessions.Count + 1,
                OpenedAt = DateTime.Now,
                OpeningCash = openingCash
            };
            sessions.Add(activeSession);
            store.SaveCashSessions(sessions);
            DialogResult = true;
            return;
        }

        if (!decimal.TryParse(CountedCashBox.Text, NumberStyles.Number, BrazilianCulture, out var countedCash) || countedCash < 0)
        {
            MessageBox.Show(this, "Informe o valor contado para fechar o caixa.", "Conferência necessária", MessageBoxButton.OK, MessageBoxImage.Warning);
            CountedCashBox.Focus();
            return;
        }

        var expectedCash = ExpectedCash;
        var difference = countedCash - expectedCash;
        var confirmation = MessageBox.Show(this,
            $"Saldo esperado: {expectedCash.ToString("C", BrazilianCulture)}\nValor contado: {countedCash.ToString("C", BrazilianCulture)}\nDiferença: {difference.ToString("C", BrazilianCulture)}\n\nConfirmar fechamento?",
            "Fechar caixa", MessageBoxButton.YesNo, difference == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        activeSession.ClosedAt = DateTime.Now;
        activeSession.CountedCash = countedCash;
        activeSession.ExpectedCash = expectedCash;
        store.SaveCashSessions(sessions);
        DialogResult = true;
    }
}
using System.Windows;

namespace PDV_Adega;

public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();
    }

    public LicenseClient.LicenseState? LicenseState { get; private set; }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(ServerUrlBox.Text.Trim(), UriKind.Absolute, out var serverUri) ||
            serverUri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(this, "Informe um endereço HTTPS válido do servidor de licenças.", "Endereço inválido",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var licenseKey = LicenseKeyBox.Text.Trim();
        if (licenseKey.Length < 16)
        {
            MessageBox.Show(this, "Informe uma chave de licença válida.", "Chave inválida",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LicenseState = new LicenseClient.LicenseState
        {
            ServerUrl = serverUri.ToString().TrimEnd('/'),
            LicenseKey = licenseKey
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

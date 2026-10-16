using System.Windows;

namespace PDV_Adega;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var licenseClient = new LicenseClient();
            if (!await licenseClient.EnsureLicensedAsync())
            {
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Não foi possível validar a licença do PDV.\n\n{exception.Message}",
                "Erro de licença",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}

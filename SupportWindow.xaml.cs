using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace PDV_Adega;

public partial class SupportWindow : Window
{
    private readonly SupportClient client;
    private readonly ObservableCollection<SupportClient.SupportTicketSummary> tickets = [];

    public SupportWindow(SupportClient client)
    {
        this.client = client;
        InitializeComponent();
        TicketsList.ItemsSource = tickets;
        Loaded += async (_, _) => await LoadTicketsAsync();
    }

    private async Task LoadTicketsAsync()
    {
        try
        {
            var selectedId = (TicketsList.SelectedItem as SupportClient.SupportTicketSummary)?.Id;
            var loaded = await client.GetTicketsAsync();
            tickets.Clear();
            foreach (var ticket in loaded) tickets.Add(ticket);
            TicketsList.SelectedItem = tickets.FirstOrDefault(ticket => ticket.Id == selectedId) ?? tickets.FirstOrDefault();
            TicketsStatusLabel.Text = "";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            TicketsStatusLabel.Text = $"Não foi possível carregar os chamados: {exception.Message}";
        }
    }

    private async void CreateTicket_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SubjectBox.Text) || string.IsNullOrWhiteSpace(DescriptionBox.Text))
        {
            CreateStatusLabel.Text = "Preencha o assunto e a descrição do problema.";
            return;
        }

        var includeDiagnostics = DiagnosticsConsentCheck.IsChecked == true;
        var requestRemoteSupport = RemoteConsentCheck.IsChecked == true;
        var confirmation = "O chamado enviará o assunto, a descrição, a versão do aplicativo, o nome deste computador e o identificador do dispositivo.";
        if (includeDiagnostics)
            confirmation += "\n\nVocê autorizou incluir o pacote compactado de diagnóstico (logs de erro e informações técnicas; sem dados de vendas/clientes).";
        else
            confirmation += "\n\nNenhum arquivo de diagnóstico será enviado.";
        if (requestRemoteSupport)
            confirmation += "\n\nVocê solicitou e autorizou que a equipe combine uma sessão temporária por ferramenta externa. O PDV não dará acesso remoto por conta própria.";
        if (MessageBox.Show(this, confirmation + "\n\nEnviar o chamado?", "Confirmar envio", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
            return;

        CreateStatusLabel.Text = "Enviando chamado…";
        try
        {
            var ticket = await client.CreateTicketAsync(
                SubjectBox.Text.Trim(),
                DescriptionBox.Text.Trim(),
                includeDiagnostics,
                requestRemoteSupport);
            SubjectBox.Clear();
            DescriptionBox.Clear();
            DiagnosticsConsentCheck.IsChecked = false;
            RemoteConsentCheck.IsChecked = false;
            CreateStatusLabel.Text = $"Chamado #{ticket.Id[..8]} enviado. Acompanhe as respostas na aba “Meus chamados”.";
            await LoadTicketsAsync();
            TicketsList.SelectedItem = tickets.FirstOrDefault(item => item.Id == ticket.Id);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            CreateStatusLabel.Text = $"Não foi possível enviar o chamado: {exception.Message}";
        }
    }

    private async void RefreshTickets_Click(object sender, RoutedEventArgs e) => await LoadTicketsAsync();

    private void TicketsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TicketsList.SelectedItem is not SupportClient.SupportTicketSummary ticket)
        {
            TicketDetailsLabel.Text = "Selecione um chamado para ver o histórico.";
            TicketMessagesLabel.Text = "";
            ReplyBox.IsEnabled = false;
            ReplyButton.IsEnabled = false;
            return;
        }

        var remoteConsent = ticket.RemoteSessionRequested
            ? $"Solicitação de sessão remota externa autorizada em {ticket.RemoteConsentAt?.ToLocalTime():g}"
            : "Sem solicitação de sessão remota.";
        var status = ticket.Status switch
        {
            "Open" => "Aberto",
            "InProgress" => "Em atendimento",
            "WaitingForCustomer" => "Aguardando cliente",
            "Resolved" => "Resolvido",
            _ => ticket.Status
        };
        var diagnosticStatus = ticket.HasDiagnostics
            ? $"Pacote de diagnóstico enviado com consentimento em {ticket.DiagnosticConsentAt?.ToLocalTime():g}."
            : ticket.DiagnosticExpired ? "Pacote de diagnóstico expirado e apagado." : "Sem pacote de diagnóstico.";
        TicketDetailsLabel.Text =
            $"#{ticket.Id[..8]} · {status} · atualizado {ticket.UpdatedAt.ToLocalTime():g}\n{ticket.Description}\nPDV {ticket.AppVersion} · {ticket.DeviceName}\n{diagnosticStatus}\n{remoteConsent}";
        TicketMessagesLabel.Text = string.Join(
            "\n\n",
            ticket.Messages.Select(message =>
                $"{message.CreatedAt.ToLocalTime():g} · {message.Author}\n{message.Message}"));
        ReplyBox.IsEnabled = ticket.Status != "Resolved";
        ReplyButton.IsEnabled = ticket.Status != "Resolved";
    }

    private async void Reply_Click(object sender, RoutedEventArgs e)
    {
        if (TicketsList.SelectedItem is not SupportClient.SupportTicketSummary ticket ||
            string.IsNullOrWhiteSpace(ReplyBox.Text))
            return;

        try
        {
            await client.ReplyAsync(ticket.Id, ReplyBox.Text.Trim());
            ReplyBox.Clear();
            TicketsStatusLabel.Text = "Resposta enviada.";
            await LoadTicketsAsync();
            TicketsList.SelectedItem = tickets.FirstOrDefault(item => item.Id == ticket.Id);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            TicketsStatusLabel.Text = $"Não foi possível enviar a resposta: {exception.Message}";
        }
    }
}

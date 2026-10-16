using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.IO;
using System.Text.Json;

namespace PDV_Adega;

public sealed class SupportClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly LicenseClient.LicenseState license;

    public SupportClient(LicenseClient.LicenseState license)
    {
        this.license = license;
    }

    public async Task<IReadOnlyList<SupportTicketSummary>> GetTicketsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"{license.ServerUrl.TrimEnd('/')}/api/support/tickets/search",
            new TicketSearchRequest(license.LicenseKey, license.DeviceId),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<SupportTicketSummary>>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("O servidor retornou uma lista de chamados inválida.");
    }

    public async Task<SupportTicketSummary> CreateTicketAsync(
        string subject,
        string description,
        bool includeDiagnostics,
        bool remoteSessionRequested,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSupportTicketRequest
        {
            LicenseKey = license.LicenseKey,
            DeviceId = license.DeviceId,
            DeviceName = Environment.MachineName,
            AppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            Subject = subject,
            Description = description,
            DiagnosticPackageBase64 = includeDiagnostics
                ? Convert.ToBase64String(SupportDiagnostics.CreatePackage(license.DeviceId))
                : null,
            DiagnosticConsent = includeDiagnostics,
            RemoteSessionRequested = remoteSessionRequested
        };

        using var response = await httpClient.PostAsJsonAsync(
            $"{license.ServerUrl.TrimEnd('/')}/api/support/tickets",
            request,
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SupportTicketSummary>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("O servidor não retornou o chamado criado.");
    }

    public async Task ReplyAsync(string ticketId, string message, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"{license.ServerUrl.TrimEnd('/')}/api/support/tickets/{Uri.EscapeDataString(ticketId)}/messages",
            new CustomerTicketMessageRequest(license.LicenseKey, license.DeviceId, message),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = $"O servidor de suporte retornou o erro {(int)response.StatusCode}.";
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(JsonOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(problem?.Detail)) detail = problem.Detail;
        }
        catch (JsonException)
        {
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("A licença/dispositivo não está autorizado a acessar este chamado.");
        throw new HttpRequestException(detail, null, response.StatusCode);
    }

    private sealed record TicketSearchRequest(string LicenseKey, string DeviceId);

    private sealed record CustomerTicketMessageRequest(string LicenseKey, string DeviceId, string Message);

    private sealed record ProblemResponse(string? Detail);

    public sealed record SupportTicketSummary(
        string Id,
        string CustomerName,
        string Subject,
        string Description,
        string Status,
        string DeviceName,
        string AppVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        bool HasDiagnostics,
        bool DiagnosticExpired,
        DateTimeOffset? DiagnosticConsentAt,
        bool RemoteSessionRequested,
        DateTimeOffset? RemoteConsentAt,
        IReadOnlyList<SupportTicketMessage> Messages);

    public sealed record SupportTicketMessage(
        string Author,
        string Message,
        DateTimeOffset CreatedAt);

    private sealed record CreateSupportTicketRequest
    {
        public required string LicenseKey { get; init; }
        public required string DeviceId { get; init; }
        public required string DeviceName { get; init; }
        public required string AppVersion { get; init; }
        public required string Subject { get; init; }
        public required string Description { get; init; }
        public string? DiagnosticPackageBase64 { get; init; }
        public bool DiagnosticConsent { get; init; }
        public bool RemoteSessionRequested { get; init; }
    }
}

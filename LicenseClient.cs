using System.Net;
using System.Net.Http;
using System.IO;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace PDV_Adega;

public sealed class LicenseClient
{
    private static readonly TimeSpan OfflineGracePeriod = TimeSpan.FromDays(7);
    private static readonly byte[] Entropy = "PDV-Adega-license-v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string statePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PDV-Adega",
        "license.dat");
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<bool> EnsureLicensedAsync()
    {
        var state = LoadState();
        if (state is not null)
        {
            try
            {
                await ValidateOnlineAsync(state);
                SaveState(state);
                return true;
            }
            catch (HttpRequestException)
            {
                if (IsWithinOfflineGrace(state))
                {
                    state.LastObservedAt = DateTimeOffset.UtcNow;
                    SaveState(state);
                    return true;
                }
                MessageBox.Show(
                    "Não foi possível alcançar o servidor de licenças e o período offline de 7 dias terminou.",
                    "Licença não validada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
            catch (TaskCanceledException) when (!httpClient.Timeout.Equals(Timeout.InfiniteTimeSpan))
            {
                if (IsWithinOfflineGrace(state))
                {
                    state.LastObservedAt = DateTimeOffset.UtcNow;
                    SaveState(state);
                    return true;
                }
                MessageBox.Show(
                    "O servidor de licenças não respondeu e o período offline de 7 dias terminou.",
                    "Licença não validada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
            catch (LicenseRejectedException exception)
            {
                MessageBox.Show(
                    $"{exception.Message}\n\nInforme uma nova chave para substituir a ativação local.",
                    "Licença inválida",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        while (true)
        {
            var activationWindow = new LicenseWindow();
            if (activationWindow.ShowDialog() != true) return false;

            var newState = activationWindow.LicenseState!;
            try
            {
                await ValidateOnlineAsync(newState);
                SaveState(newState);
                return true;
            }
            catch (HttpRequestException)
            {
                MessageBox.Show(
                    "Não foi possível conectar ao servidor. Confira o endereço e a conexão com a internet.",
                    "Falha na ativação",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (TaskCanceledException)
            {
                MessageBox.Show(
                    "O servidor de licenças não respondeu. Tente novamente.",
                    "Falha na ativação",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (LicenseRejectedException exception)
            {
                MessageBox.Show(exception.Message, "Falha na ativação", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public LicenseState? GetCurrentLicense() => LoadState();

    private async Task ValidateOnlineAsync(LicenseState state)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"{state.ServerUrl.TrimEnd('/')}/api/licenses/validate",
            new LicenseValidationRequest(state.LicenseKey, state.DeviceId, Environment.MachineName),
            JsonOptions);

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new LicenseRejectedException(
                "A licença está expirada, revogada ou não está autorizada para este dispositivo.");

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode >= 500)
                throw new HttpRequestException($"Servidor de licenças indisponível ({(int)response.StatusCode}).");
            throw new LicenseRejectedException(
                $"O servidor de licenças retornou o erro {(int)response.StatusCode}.");
        }

        var result = await response.Content.ReadFromJsonAsync<LicenseValidationResponse>(JsonOptions)
            ?? throw new LicenseRejectedException("O servidor retornou uma resposta de licença inválida.");

        if (!result.IsValid || result.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new LicenseRejectedException("A licença está expirada ou não está autorizada.");

        state.LastSuccessfulValidationAt = result.CheckedAt;
        state.LastObservedAt = result.CheckedAt;
        state.ExpiresAt = result.ExpiresAt;
    }

    private bool IsWithinOfflineGrace(LicenseState state)
    {
        var now = DateTimeOffset.UtcNow;
        return state.LastSuccessfulValidationAt is { } lastChecked &&
            (state.LastObservedAt is null || now >= state.LastObservedAt) &&
            now >= lastChecked &&
            now - lastChecked <= OfflineGracePeriod &&
            state.ExpiresAt is { } expiresAt &&
            now < expiresAt;
    }

    private LicenseState? LoadState()
    {
        if (!File.Exists(statePath)) return null;
        var protectedBytes = File.ReadAllBytes(statePath);
        var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<LicenseState>(bytes, JsonOptions)
            ?? throw new InvalidDataException("O arquivo local de licença está vazio ou inválido.");
    }

    private void SaveState(LicenseState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(statePath, protectedBytes);
    }

    private sealed record LicenseValidationRequest(string LicenseKey, string DeviceId, string DeviceName);

    private sealed record LicenseValidationResponse(bool IsValid, DateTimeOffset ExpiresAt, DateTimeOffset CheckedAt);

    public sealed class LicenseState
    {
        public string ServerUrl { get; set; } = "";
        public string LicenseKey { get; set; } = "";
        public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset? LastSuccessfulValidationAt { get; set; }
        public DateTimeOffset? LastObservedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private sealed class LicenseRejectedException(string message) : Exception(message);
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using PDV_Adega.LicensingServer;

namespace PDV_Adega.LicensingServer.Services;

/// <summary>Gerencia licenças em arquivo JSON com escrita atômica.</summary>
public sealed class LicenseService : ILicenseService
{
    private readonly SemaphoreSlim fileLock = new(1, 1);
    private readonly string dataPath;
    private readonly string diagnosticsDirectory;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Inicializa o armazenamento persistente do servidor.</summary>
    public LicenseService(IWebHostEnvironment environment)
    {
        dataPath = Environment.GetEnvironmentVariable("PDV_LICENSE_DATA_PATH") ??
            Path.Combine(environment.ContentRootPath, "App_Data", "licenses.json");
        diagnosticsDirectory = Environment.GetEnvironmentVariable("PDV_SUPPORT_FILES_PATH") ??
            Path.Combine(Path.GetDirectoryName(dataPath)!, "support-diagnostics");
    }

    /// <summary>Lista licenças sem expor suas chaves completas.</summary>
    public async Task<IReadOnlyList<LicenseResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            return (await ReadAsync(cancellationToken)).Licenses
                .OrderByDescending(license => license.CreatedAt)
                .Select(ToResponse)
                .ToList();
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Cria uma licença e gera sua chave de ativação.</summary>
    public async Task<CreatedLicenseResponse> CreateAsync(CreateLicenseRequest request, CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            var licenseKey = $"PDV-{Convert.ToHexString(RandomNumberGenerator.GetBytes(20))}";
            var now = DateTimeOffset.UtcNow;
            var license = new LicenseDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                CustomerName = request.CustomerName.Trim(),
                KeyHash = HashKey(licenseKey),
                KeyPrefix = licenseKey[..12],
                CreatedAt = now,
                ExpiresAt = request.ExpiresAt.ToUniversalTime(),
                MaxDevices = request.MaxDevices
            };
            database.Licenses.Add(license);
            await WriteAsync(database, cancellationToken);
            return new CreatedLicenseResponse(
                license.Id,
                license.CustomerName,
                licenseKey,
                license.CreatedAt,
                license.ExpiresAt,
                license.MaxDevices);
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Atualiza vencimento e limite de dispositivos.</summary>
    public async Task<LicenseResponse?> UpdateAsync(string id, UpdateLicenseRequest request, CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            var license = database.Licenses.SingleOrDefault(item => item.Id == id);
            if (license is null) return null;
            license.ExpiresAt = request.ExpiresAt.ToUniversalTime();
            license.MaxDevices = request.MaxDevices;
            await WriteAsync(database, cancellationToken);
            return ToResponse(license);
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Revoga permanentemente uma licença.</summary>
    public async Task<bool> RevokeAsync(string id, CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            var license = database.Licenses.SingleOrDefault(item => item.Id == id);
            if (license is null) return false;
            license.IsRevoked = true;
            await WriteAsync(database, cancellationToken);
            return true;
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Remove um dispositivo e libera uma ativação.</summary>
    public async Task<bool> RemoveDeviceAsync(string id, string deviceId, CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            var license = database.Licenses.SingleOrDefault(item => item.Id == id);
            if (license is null) return false;
            var device = license.Devices.SingleOrDefault(item => item.DeviceId == deviceId);
            if (device is null) return false;
            license.Devices.Remove(device);
            await WriteAsync(database, cancellationToken);
            return true;
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Valida chave e dispositivo e registra a última utilização.</summary>
    public async Task<LicenseValidationResponse?> ValidateAsync(ValidateLicenseRequest request, CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            var keyHash = HashKey(request.LicenseKey.Trim().ToUpperInvariant());
            var license = database.Licenses.FirstOrDefault(item =>
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(item.KeyHash),
                    Convert.FromHexString(keyHash)));
            var now = DateTimeOffset.UtcNow;

            if (license is null || license.IsRevoked || license.ExpiresAt <= now)
                return null;

            var device = license.Devices.SingleOrDefault(item => item.DeviceId == request.DeviceId);
            if (device is null)
            {
                if (license.Devices.Count >= license.MaxDevices) return null;
                device = new DeviceDocument
                {
                    DeviceId = request.DeviceId,
                    DeviceName = request.DeviceName.Trim(),
                    FirstActivatedAt = now,
                    LastSeenAt = now
                };
                license.Devices.Add(device);
            }
            else
            {
                device.DeviceName = request.DeviceName.Trim();
                device.LastSeenAt = now;
            }

            await WriteAsync(database, cancellationToken);
            return new LicenseValidationResponse(true, license.ExpiresAt, now);
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Abre um chamado autenticado e salva somente diagnóstico enviado pelo usuário.</summary>
    public async Task<SupportTicketResponse?> CreateSupportTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            if (RemoveExpiredDiagnostics(database, DateTimeOffset.UtcNow))
                await WriteAsync(database, cancellationToken);
            var license = FindLicense(database, request.LicenseKey);
            if (license is null || license.IsRevoked ||
                license.Devices.All(device => device.DeviceId != request.DeviceId))
                return null;

            byte[]? diagnosticBytes = null;
            if (request.DiagnosticConsent != !string.IsNullOrWhiteSpace(request.DiagnosticPackageBase64))
                return null;
            if (!string.IsNullOrWhiteSpace(request.DiagnosticPackageBase64))
            {
                try
                {
                    diagnosticBytes = Convert.FromBase64String(request.DiagnosticPackageBase64);
                    using var archiveStream = new MemoryStream(diagnosticBytes);
                    using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
                    if (archive.Entries.Count is < 1 or > 5 ||
                        archive.Entries.Any(entry => entry.FullName.Contains("..", StringComparison.Ordinal) ||
                            Path.IsPathRooted(entry.FullName)))
                        return null;
                }
                catch (FormatException)
                {
                    return null;
                }
                catch (InvalidDataException)
                {
                    return null;
                }
            }

            var now = DateTimeOffset.UtcNow;
            var ticket = new SupportTicketDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                LicenseId = license.Id,
                CustomerName = license.CustomerName,
                DeviceId = request.DeviceId,
                DeviceName = request.DeviceName.Trim(),
                AppVersion = request.AppVersion.Trim(),
                Subject = request.Subject.Trim(),
                Description = request.Description.Trim(),
                Status = SupportTicketStatus.Open,
                CreatedAt = now,
                UpdatedAt = now,
                HasDiagnostics = diagnosticBytes is not null,
                DiagnosticFileName = diagnosticBytes is null ? null : $"{Guid.NewGuid():N}.zip",
                DiagnosticExpiresAt = diagnosticBytes is null ? null : now.AddDays(30),
                DiagnosticConsentAt = diagnosticBytes is null ? null : now,
                RemoteSessionRequested = request.RemoteSessionRequested,
                RemoteConsentAt = request.RemoteSessionRequested ? now : null
            };
            ticket.Messages.Add(new SupportTicketMessageDocument
            {
                Author = "Cliente",
                Message = ticket.Description,
                CreatedAt = now
            });

            string? diagnosticPath = null;
            if (diagnosticBytes is not null)
            {
                Directory.CreateDirectory(diagnosticsDirectory);
                diagnosticPath = Path.Combine(diagnosticsDirectory, ticket.DiagnosticFileName!);
                await File.WriteAllBytesAsync(diagnosticPath, diagnosticBytes, cancellationToken);
            }

            database.SupportTickets.Add(ticket);
            try
            {
                await WriteAsync(database, cancellationToken);
            }
            catch
            {
                if (diagnosticPath is not null && File.Exists(diagnosticPath)) File.Delete(diagnosticPath);
                throw;
            }

            return ToResponse(ticket);
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Lista chamados de uma instalação após conferir licença e dispositivo.</summary>
    public async Task<IReadOnlyList<SupportTicketResponse>?> ListCustomerTicketsAsync(
        SupportTicketSearchRequest request,
        CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            if (RemoveExpiredDiagnostics(database, DateTimeOffset.UtcNow))
                await WriteAsync(database, cancellationToken);
            var license = FindLicense(database, request.LicenseKey);
            if (license is null || license.IsRevoked ||
                license.Devices.All(device => device.DeviceId != request.DeviceId))
                return null;

            return database.SupportTickets
                .Where(ticket => ticket.LicenseId == license.Id && ticket.DeviceId == request.DeviceId)
                .OrderByDescending(ticket => ticket.UpdatedAt)
                .Select(ToResponse)
                .ToList();
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Adiciona uma resposta do cliente a um chamado pertencente à instalação.</summary>
    public async Task<SupportTicketResponse?> AddCustomerMessageAsync(
        string ticketId,
        CustomerTicketMessageRequest request,
        CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            var license = FindLicense(database, request.LicenseKey);
            if (license is null || license.IsRevoked ||
                license.Devices.All(device => device.DeviceId != request.DeviceId))
                return null;

            var ticket = database.SupportTickets.SingleOrDefault(item =>
                item.Id == ticketId && item.LicenseId == license.Id && item.DeviceId == request.DeviceId);
            if (ticket is null || ticket.Status == SupportTicketStatus.Resolved) return null;

            var now = DateTimeOffset.UtcNow;
            ticket.Messages.Add(new SupportTicketMessageDocument
            {
                Author = "Cliente",
                Message = request.Message.Trim(),
                CreatedAt = now
            });
            ticket.UpdatedAt = now;
            ticket.Status = SupportTicketStatus.Open;
            await WriteAsync(database, cancellationToken);
            return ToResponse(ticket);
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Lista chamados e histórico visíveis à equipe administrativa.</summary>
    public async Task<IReadOnlyList<SupportTicketResponse>> ListSupportTicketsAsync(CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            if (RemoveExpiredDiagnostics(database, DateTimeOffset.UtcNow))
                await WriteAsync(database, cancellationToken);
            return database.SupportTickets
                .OrderByDescending(ticket => ticket.UpdatedAt)
                .Select(ToResponse)
                .ToList();
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Atualiza estado e registra a resposta administrativa no histórico.</summary>
    public async Task<SupportTicketResponse?> UpdateSupportTicketAsync(
        string ticketId,
        UpdateSupportTicketRequest request,
        CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            var ticket = database.SupportTickets.SingleOrDefault(item => item.Id == ticketId);
            if (ticket is null) return null;

            var now = DateTimeOffset.UtcNow;
            ticket.Status = request.Status;
            ticket.UpdatedAt = now;
            ticket.Messages.Add(new SupportTicketMessageDocument
            {
                Author = "Suporte",
                Message = request.Message.Trim(),
                CreatedAt = now
            });
            await WriteAsync(database, cancellationToken);
            return ToResponse(ticket);
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>Retorna o arquivo de diagnóstico associado a um chamado.</summary>
    public async Task<SupportDiagnosticFile?> GetDiagnosticPackageAsync(
        string ticketId,
        CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var database = await ReadAsync(cancellationToken);
            if (RemoveExpiredDiagnostics(database, DateTimeOffset.UtcNow))
                await WriteAsync(database, cancellationToken);
            var ticket = database.SupportTickets
                .SingleOrDefault(item => item.Id == ticketId && item.DiagnosticFileName is not null);
            if (ticket?.DiagnosticFileName is null) return null;
            var path = Path.Combine(diagnosticsDirectory, ticket.DiagnosticFileName);
            return File.Exists(path) ? new SupportDiagnosticFile(path, $"PDV-Adega-diagnosticos-{ticket.Id[..8]}.zip") : null;
        }
        finally
        {
            fileLock.Release();
        }
    }

    private async Task<LicenseDatabase> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(dataPath)) return new LicenseDatabase();
        await using var stream = File.OpenRead(dataPath);
        return await JsonSerializer.DeserializeAsync<LicenseDatabase>(stream, jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("O armazenamento de licenças está inválido.");
    }

    private async Task WriteAsync(LicenseDatabase database, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(dataPath)
            ?? throw new InvalidOperationException("O caminho de dados das licenças não possui diretório.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{dataPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, database, jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, dataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static LicenseDocument? FindLicense(LicenseDatabase database, string licenseKey)
    {
        var keyHash = HashKey(licenseKey.Trim().ToUpperInvariant());
        return database.Licenses.FirstOrDefault(license =>
            CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(license.KeyHash),
                Convert.FromHexString(keyHash)));
    }

    private bool RemoveExpiredDiagnostics(LicenseDatabase database, DateTimeOffset now)
    {
        var changed = false;
        foreach (var ticket in database.SupportTickets.Where(ticket =>
                     ticket.HasDiagnostics &&
                     ticket.DiagnosticExpiresAt is { } expiresAt &&
                     expiresAt <= now))
        {
            if (ticket.DiagnosticFileName is { } fileName)
            {
                var path = Path.Combine(diagnosticsDirectory, fileName);
                if (File.Exists(path)) File.Delete(path);
            }

            ticket.HasDiagnostics = false;
            ticket.DiagnosticExpired = true;
            changed = true;
        }

        return changed;
    }

    private static SupportTicketResponse ToResponse(SupportTicketDocument ticket) => new(
        ticket.Id,
        ticket.CustomerName,
        ticket.Subject,
        ticket.Description,
        ticket.Status.ToString(),
        ticket.DeviceName,
        ticket.AppVersion,
        ticket.CreatedAt,
        ticket.UpdatedAt,
        ticket.HasDiagnostics,
        ticket.DiagnosticExpired,
        ticket.DiagnosticConsentAt,
        ticket.RemoteSessionRequested,
        ticket.RemoteConsentAt,
        ticket.Messages.Select(message =>
            new SupportTicketMessageResponse(message.Author, message.Message, message.CreatedAt)).ToList());

    private static LicenseResponse ToResponse(LicenseDocument license) => new(
        license.Id,
        license.CustomerName,
        license.KeyPrefix,
        license.CreatedAt,
        license.ExpiresAt,
        license.MaxDevices,
        license.IsRevoked,
        license.Devices.Select(device => new LicensedDeviceResponse(
            device.DeviceId,
            device.DeviceName,
            device.FirstActivatedAt,
            device.LastSeenAt)).ToList());

    private sealed class LicenseDatabase
    {
        public List<LicenseDocument> Licenses { get; init; } = [];
        public List<SupportTicketDocument> SupportTickets { get; init; } = [];
    }

    private sealed class LicenseDocument
    {
        public string Id { get; init; } = "";
        public string CustomerName { get; set; } = "";
        public string KeyHash { get; init; } = "";
        public string KeyPrefix { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; set; }
        public int MaxDevices { get; set; }
        public bool IsRevoked { get; set; }
        public List<DeviceDocument> Devices { get; init; } = [];
    }

    private sealed class DeviceDocument
    {
        public string DeviceId { get; init; } = "";
        public string DeviceName { get; set; } = "";
        public DateTimeOffset FirstActivatedAt { get; init; }
        public DateTimeOffset LastSeenAt { get; set; }
    }

    private sealed class SupportTicketDocument
    {
        public string Id { get; init; } = "";
        public string LicenseId { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string DeviceId { get; init; } = "";
        public string DeviceName { get; init; } = "";
        public string AppVersion { get; init; } = "";
        public string Subject { get; init; } = "";
        public string Description { get; init; } = "";
        public SupportTicketStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool HasDiagnostics { get; set; }
        public bool DiagnosticExpired { get; set; }
        public string? DiagnosticFileName { get; init; }
        public DateTimeOffset? DiagnosticExpiresAt { get; init; }
        public DateTimeOffset? DiagnosticConsentAt { get; init; }
        public bool RemoteSessionRequested { get; init; }
        public DateTimeOffset? RemoteConsentAt { get; init; }
        public List<SupportTicketMessageDocument> Messages { get; init; } = [];
    }

    private sealed class SupportTicketMessageDocument
    {
        public string Author { get; init; } = "";
        public string Message { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
    }
}

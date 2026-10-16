using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PDV_Adega.LicensingServer;

namespace PDV_Adega.LicensingServer.Services;

/// <summary>Gerencia licenças em arquivo JSON com escrita atômica.</summary>
public sealed class LicenseService : ILicenseService
{
    private readonly SemaphoreSlim fileLock = new(1, 1);
    private readonly string dataPath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Inicializa o armazenamento persistente do servidor.</summary>
    public LicenseService(IWebHostEnvironment environment)
    {
        dataPath = Environment.GetEnvironmentVariable("PDV_LICENSE_DATA_PATH") ??
            Path.Combine(environment.ContentRootPath, "App_Data", "licenses.json");
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
}

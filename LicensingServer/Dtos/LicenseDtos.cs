using System.ComponentModel.DataAnnotations;

namespace PDV_Adega.LicensingServer;

/// <summary>Dados para criar uma licença de assinatura.</summary>
public sealed record CreateLicenseRequest
{
    /// <summary>Nome do cliente ou estabelecimento associado à licença.</summary>
    [Required, StringLength(160, MinimumLength = 2)]
    public required string CustomerName { get; init; }

    /// <summary>Data e hora UTC de expiração da assinatura.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Quantidade máxima de computadores ativados simultaneamente.</summary>
    [Range(1, 100)]
    public int MaxDevices { get; init; } = 1;
}

/// <summary>Dados para alterar vencimento e limite de dispositivos.</summary>
public sealed record UpdateLicenseRequest
{
    /// <summary>Nova data e hora UTC de expiração.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Nova quantidade máxima de computadores ativados.</summary>
    [Range(1, 100)]
    public int MaxDevices { get; init; } = 1;
}

/// <summary>Chave e dados iniciais da licença criada; a chave é exibida uma única vez.</summary>
public sealed record CreatedLicenseResponse(
    string Id,
    string CustomerName,
    string LicenseKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int MaxDevices);

/// <summary>Computador autorizado a utilizar a licença.</summary>
public sealed record LicensedDeviceResponse(
    string DeviceId,
    string DeviceName,
    DateTimeOffset FirstActivatedAt,
    DateTimeOffset LastSeenAt);

/// <summary>Resumo de uma licença no painel de administração.</summary>
public sealed record LicenseResponse(
    string Id,
    string CustomerName,
    string KeyPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int MaxDevices,
    bool IsRevoked,
    IReadOnlyList<LicensedDeviceResponse> Devices);

/// <summary>Dados enviados pelo PDV ao ativar ou renovar sua validação.</summary>
public sealed record ValidateLicenseRequest
{
    /// <summary>Chave de licença fornecida pelo administrador.</summary>
    [Required, StringLength(100, MinimumLength = 16)]
    public required string LicenseKey { get; init; }

    /// <summary>Identificador persistente desta instalação do PDV.</summary>
    [Required, StringLength(80, MinimumLength = 16)]
    public required string DeviceId { get; init; }

    /// <summary>Nome do computador para exibição no painel.</summary>
    [Required, StringLength(160, MinimumLength = 1)]
    public required string DeviceName { get; init; }
}

/// <summary>Resultado da validação online do PDV.</summary>
public sealed record LicenseValidationResponse(
    bool IsValid,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CheckedAt);

/// <summary>Credenciais para autenticar no painel.</summary>
public sealed record AdminLoginRequest
{
    /// <summary>Senha administrativa.</summary>
    public string? Password { get; init; }
}

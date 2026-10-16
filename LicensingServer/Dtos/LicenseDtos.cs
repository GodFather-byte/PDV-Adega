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

/// <summary>Estados possíveis para um chamado de suporte.</summary>
public enum SupportTicketStatus
{
    /// <summary>Chamado recém-aberto.</summary>
    Open,
    /// <summary>Equipe de suporte está atendendo.</summary>
    InProgress,
    /// <summary>A equipe aguarda retorno do cliente.</summary>
    WaitingForCustomer,
    /// <summary>Atendimento concluído.</summary>
    Resolved
}

/// <summary>Credenciais para consultar chamados da instalação autenticada.</summary>
public sealed record SupportTicketSearchRequest
{
    /// <summary>Chave de licença da instalação.</summary>
    [Required, StringLength(100, MinimumLength = 16)]
    public required string LicenseKey { get; init; }

    /// <summary>Identificador da instalação do PDV.</summary>
    [Required, StringLength(80, MinimumLength = 16)]
    public required string DeviceId { get; init; }
}

/// <summary>Dados para abertura de chamado pelo PDV.</summary>
public sealed record CreateSupportTicketRequest
{
    /// <summary>Chave da licença vinculada ao chamado.</summary>
    [Required, StringLength(100, MinimumLength = 16)]
    public required string LicenseKey { get; init; }

    /// <summary>Identificador do computador vinculado à licença.</summary>
    [Required, StringLength(80, MinimumLength = 16)]
    public required string DeviceId { get; init; }

    /// <summary>Nome do computador para triagem.</summary>
    [Required, StringLength(160, MinimumLength = 1)]
    public required string DeviceName { get; init; }

    /// <summary>Versão do aplicativo instalado.</summary>
    [Required, StringLength(40, MinimumLength = 1)]
    public required string AppVersion { get; init; }

    /// <summary>Resumo do problema.</summary>
    [Required, StringLength(120, MinimumLength = 3)]
    public required string Subject { get; init; }

    /// <summary>Descrição do problema; não incluir dados pessoais, senhas ou informações de pagamento.</summary>
    [Required, StringLength(4000, MinimumLength = 5)]
    public required string Description { get; init; }

    /// <summary>Pacote ZIP de diagnóstico enviado após consentimento explícito.</summary>
    [StringLength(4_200_000)]
    public string? DiagnosticPackageBase64 { get; init; }

    /// <summary>Confirma consentimento explícito para enviar o pacote de diagnóstico.</summary>
    public bool DiagnosticConsent { get; init; }

    /// <summary>Indica que o usuário pediu suporte remoto por ferramenta externa.</summary>
    public bool RemoteSessionRequested { get; init; }
}

/// <summary>Credenciais e mensagem de resposta do cliente.</summary>
public sealed record CustomerTicketMessageRequest
{
    /// <summary>Chave da licença vinculada ao chamado.</summary>
    [Required, StringLength(100, MinimumLength = 16)]
    public required string LicenseKey { get; init; }

    /// <summary>Identificador do computador vinculado à licença.</summary>
    [Required, StringLength(80, MinimumLength = 16)]
    public required string DeviceId { get; init; }

    /// <summary>Mensagem para a equipe de suporte.</summary>
    [Required, StringLength(2000, MinimumLength = 1)]
    public required string Message { get; init; }
}

/// <summary>Atualização administrativa com histórico auditável.</summary>
public sealed record UpdateSupportTicketRequest
{
    /// <summary>Novo estado do chamado.</summary>
    public required SupportTicketStatus Status { get; init; }

    /// <summary>Resposta ou nota administrativa adicionada ao histórico.</summary>
    [Required, StringLength(2000, MinimumLength = 1)]
    public required string Message { get; init; }
}

/// <summary>Entrada individual no histórico do chamado.</summary>
public sealed record SupportTicketMessageResponse(
    string Author,
    string Message,
    DateTimeOffset CreatedAt);

/// <summary>Resumo do chamado apresentado ao cliente e ao painel.</summary>
public sealed record SupportTicketResponse(
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
    IReadOnlyList<SupportTicketMessageResponse> Messages);

/// <summary>Arquivo de diagnóstico disponível para download autorizado.</summary>
public sealed record SupportDiagnosticFile(string Path, string DownloadName);

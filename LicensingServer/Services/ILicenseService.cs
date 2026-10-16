using PDV_Adega.LicensingServer;

namespace PDV_Adega.LicensingServer.Services;

/// <summary>Operações de persistência e validação de licenças.</summary>
public interface ILicenseService
{
    /// <summary>Lista as licenças cadastradas.</summary>
    Task<IReadOnlyList<LicenseResponse>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Cria uma nova licença e sua chave secreta.</summary>
    Task<CreatedLicenseResponse> CreateAsync(CreateLicenseRequest request, CancellationToken cancellationToken);
    /// <summary>Atualiza vencimento e quantidade de dispositivos.</summary>
    Task<LicenseResponse?> UpdateAsync(string id, UpdateLicenseRequest request, CancellationToken cancellationToken);
    /// <summary>Revoga uma licença existente.</summary>
    Task<bool> RevokeAsync(string id, CancellationToken cancellationToken);
    /// <summary>Remove um dispositivo autorizado.</summary>
    Task<bool> RemoveDeviceAsync(string id, string deviceId, CancellationToken cancellationToken);
    /// <summary>Ativa ou valida um dispositivo.</summary>
    Task<LicenseValidationResponse?> ValidateAsync(ValidateLicenseRequest request, CancellationToken cancellationToken);
    /// <summary>Abre um chamado autenticado por licença e instalação.</summary>
    Task<SupportTicketResponse?> CreateSupportTicketAsync(CreateSupportTicketRequest request, CancellationToken cancellationToken);
    /// <summary>Lista chamados pertencentes à instalação autenticada.</summary>
    Task<IReadOnlyList<SupportTicketResponse>?> ListCustomerTicketsAsync(SupportTicketSearchRequest request, CancellationToken cancellationToken);
    /// <summary>Adiciona uma mensagem do cliente ao chamado.</summary>
    Task<SupportTicketResponse?> AddCustomerMessageAsync(string ticketId, CustomerTicketMessageRequest request, CancellationToken cancellationToken);
    /// <summary>Lista chamados para a equipe administrativa.</summary>
    Task<IReadOnlyList<SupportTicketResponse>> ListSupportTicketsAsync(CancellationToken cancellationToken);
    /// <summary>Atualiza estado e registra uma mensagem administrativa.</summary>
    Task<SupportTicketResponse?> UpdateSupportTicketAsync(string ticketId, UpdateSupportTicketRequest request, CancellationToken cancellationToken);
    /// <summary>Obtém um pacote de diagnóstico sem expor seu caminho à API.</summary>
    Task<SupportDiagnosticFile?> GetDiagnosticPackageAsync(string ticketId, CancellationToken cancellationToken);
}

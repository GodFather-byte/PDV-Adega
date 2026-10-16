using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PDV_Adega.LicensingServer.Services;

namespace PDV_Adega.LicensingServer;

/// <summary>Rotas para chamados de suporte com autenticação da instalação e do painel.</summary>
public static class SupportEndpoints
{
    /// <summary>Registra as rotas de suporte do cliente e da equipe administrativa.</summary>
    public static void MapSupport(this WebApplication app)
    {
        app.MapPost("/api/support/tickets", async Task<Results<Created<SupportTicketResponse>, ForbidHttpResult, ValidationProblem>> (
            CreateSupportTicketRequest request,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var errors = Validate(request);
            if (request.DiagnosticConsent != !string.IsNullOrWhiteSpace(request.DiagnosticPackageBase64))
                errors[nameof(request.DiagnosticConsent)] = ["O consentimento de diagnóstico deve corresponder ao envio do pacote."];
            if (!string.IsNullOrWhiteSpace(request.DiagnosticPackageBase64))
            {
                try
                {
                    var package = Convert.FromBase64String(request.DiagnosticPackageBase64);
                    if (package.Length > 3 * 1024 * 1024)
                        errors[nameof(request.DiagnosticPackageBase64)] = ["O pacote deve ter até 3 MB."];
                    else if (package.Length < 4 || package[0] != 0x50 || package[1] != 0x4B)
                        errors[nameof(request.DiagnosticPackageBase64)] = ["O pacote de diagnóstico deve ser um ZIP válido."];
                }
                catch (FormatException)
                {
                    errors[nameof(request.DiagnosticPackageBase64)] = ["O pacote de diagnóstico está em formato inválido."];
                }
            }

            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var ticket = await service.CreateSupportTicketAsync(request, cancellationToken);
            return ticket is null
                ? TypedResults.Forbid()
                : TypedResults.Created($"/api/support/tickets/{ticket.Id}", ticket);
        })
            .WithName("CreateSupportTicket")
            .WithSummary("Abre um chamado associado à licença e ao dispositivo")
            .Produces<SupportTicketResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem()
            .WithMetadata(new RequestSizeLimitAttribute(5 * 1024 * 1024))
            .RequireRateLimiting("support-write")
            .WithMetadata(new ResponseCacheAttribute { NoStore = true });

        app.MapGet("/api/support/tickets/{ticketId}", async Task<Results<Ok<SupportTicketResponse>, ForbidHttpResult>> (
            string ticketId,
            [FromHeader(Name = "X-License-Key")] string licenseKey,
            [FromHeader(Name = "X-Device-Id")] string deviceId,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var tickets = await service.ListCustomerTicketsAsync(
                new SupportTicketSearchRequest { LicenseKey = licenseKey, DeviceId = deviceId },
                cancellationToken);
            var ticket = tickets?.SingleOrDefault(item => item.Id == ticketId);
            return ticket is null ? TypedResults.Forbid() : TypedResults.Ok(ticket);
        })
            .WithName("GetCustomerSupportTicket")
            .WithSummary("Obtém um chamado da instalação autenticada")
            .Produces<SupportTicketResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .WithMetadata(new ResponseCacheAttribute { NoStore = true });

        app.MapPost("/api/support/tickets/search", async Task<Results<Ok<IReadOnlyList<SupportTicketResponse>>, ForbidHttpResult, ValidationProblem>> (
            SupportTicketSearchRequest request,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var tickets = await service.ListCustomerTicketsAsync(request, cancellationToken);
            return tickets is null ? TypedResults.Forbid() : TypedResults.Ok(tickets);
        })
            .WithName("ListCustomerSupportTickets")
            .WithSummary("Lista chamados da instalação autenticada")
            .Produces<IReadOnlyList<SupportTicketResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem()
            .RequireRateLimiting("support-write")
            .WithMetadata(new ResponseCacheAttribute { NoStore = true });

        app.MapPost("/api/support/tickets/{ticketId}/messages", async Task<Results<NoContent, ForbidHttpResult, ValidationProblem>> (
            string ticketId,
            CustomerTicketMessageRequest request,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var ticket = await service.AddCustomerMessageAsync(ticketId, request, cancellationToken);
            return ticket is null ? TypedResults.Forbid() : TypedResults.NoContent();
        })
            .WithName("AddCustomerSupportTicketMessage")
            .WithSummary("Adiciona uma resposta ao chamado da instalação autenticada")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem()
            .RequireRateLimiting("support-write")
            .WithMetadata(new ResponseCacheAttribute { NoStore = true });

        var admin = app.MapGroup("/api/admin/support/tickets").RequireAuthorization();
        admin.MapGet("/", async Task<Ok<IReadOnlyList<SupportTicketResponse>>> (
            ILicenseService service,
            CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.ListSupportTicketsAsync(cancellationToken)))
            .WithName("ListSupportTickets")
            .WithSummary("Lista chamados e histórico para a equipe administrativa")
            .Produces<IReadOnlyList<SupportTicketResponse>>(StatusCodes.Status200OK)
            .WithMetadata(new ResponseCacheAttribute { NoStore = true });

        admin.MapPut("/{ticketId}", async Task<Results<Ok<SupportTicketResponse>, NotFound, ValidationProblem>> (
            string ticketId,
            UpdateSupportTicketRequest request,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var errors = Validate(request);
            if (!Enum.IsDefined(request.Status))
                errors[nameof(request.Status)] = ["O estado do chamado é inválido."];
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var ticket = await service.UpdateSupportTicketAsync(ticketId, request, cancellationToken);
            return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
        })
            .WithName("UpdateSupportTicket")
            .WithSummary("Responde ao cliente, registra o histórico e atualiza o estado")
            .Produces<SupportTicketResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        admin.MapGet("/{ticketId}/diagnostics", async Task<Results<PhysicalFileHttpResult, NotFound>> (
            string ticketId,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var file = await service.GetDiagnosticPackageAsync(ticketId, cancellationToken);
            return file is null
                ? TypedResults.NotFound()
                : TypedResults.PhysicalFile(file.Path, "application/zip", file.DownloadName);
        })
            .WithName("DownloadSupportDiagnostics")
            .WithSummary("Baixa o pacote de diagnóstico consentido associado ao chamado")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .Produces(StatusCodes.Status404NotFound);
    }

    private static Dictionary<string, string[]> Validate<T>(T value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value!, new ValidationContext(value!), results, validateAllProperties: true);
        return results
            .SelectMany(result => result.MemberNames.Select(member => new { member, result.ErrorMessage }))
            .GroupBy(item => item.member)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ErrorMessage ?? "Valor inválido.").ToArray());
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using PDV_Adega.LicensingServer.Services;

namespace PDV_Adega.LicensingServer;

/// <summary>Rotas HTTP para administração e validação de licenças.</summary>
public static class LicensesEndpoints
{
    /// <summary>Registra endpoints de gerenciamento e validação de licenças.</summary>
    public static void MapLicenses(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/licenses").RequireAuthorization();

        admin.MapGet("/", async Task<Ok<IReadOnlyList<LicenseResponse>>> (
            ILicenseService service,
            CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListLicenses")
            .WithSummary("Lista licenças, validade e dispositivos ativados")
            .Produces<IReadOnlyList<LicenseResponse>>(StatusCodes.Status200OK);

        admin.MapPost("/", async Task<Results<Created<CreatedLicenseResponse>, ValidationProblem>> (
            CreateLicenseRequest request,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var errors = Validate(request);
            if (request.ExpiresAt <= DateTimeOffset.UtcNow)
                errors[nameof(request.ExpiresAt)] = ["O vencimento deve estar no futuro."];
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var created = await service.CreateAsync(request, cancellationToken);
            return TypedResults.Created($"/api/admin/licenses/{created.Id}", created);
        })
            .WithName("CreateLicense")
            .WithSummary("Cria uma licença de assinatura e gera sua chave de ativação")
            .Produces<CreatedLicenseResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        admin.MapPut("/{id}", async Task<Results<Ok<LicenseResponse>, NotFound, ValidationProblem>> (
            string id,
            UpdateLicenseRequest request,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var errors = Validate(request);
            if (request.ExpiresAt <= DateTimeOffset.UtcNow)
                errors[nameof(request.ExpiresAt)] = ["O vencimento deve estar no futuro."];
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var updated = await service.UpdateAsync(id, request, cancellationToken);
            return updated is null ? TypedResults.NotFound() : TypedResults.Ok(updated);
        })
            .WithName("UpdateLicense")
            .WithSummary("Altera vencimento ou limite de dispositivos")
            .Produces<LicenseResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        admin.MapPost("/{id}/revoke", async Task<Results<NoContent, NotFound>> (
            string id,
            ILicenseService service,
            CancellationToken cancellationToken) =>
                await service.RevokeAsync(id, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("RevokeLicense")
            .WithSummary("Revoga imediatamente uma licença")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        admin.MapDelete("/{id}/devices/{deviceId}", async Task<Results<NoContent, NotFound>> (
            string id,
            string deviceId,
            ILicenseService service,
            CancellationToken cancellationToken) =>
                await service.RemoveDeviceAsync(id, deviceId, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("RemoveLicensedDevice")
            .WithSummary("Desativa um computador para liberar uma vaga de ativação")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/licenses/validate", async Task<Results<Ok<LicenseValidationResponse>, ForbidHttpResult>> (
            ValidateLicenseRequest request,
            ILicenseService service,
            CancellationToken cancellationToken) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return TypedResults.Forbid();
            var validation = await service.ValidateAsync(request, cancellationToken);
            return validation is null ? TypedResults.Forbid() : TypedResults.Ok(validation);
        })
            .WithName("ValidateLicense")
            .WithSummary("Ativa ou renova a validação de um dispositivo do PDV")
            .Produces<LicenseValidationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);
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

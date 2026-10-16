using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using PDV_Adega.LicensingServer;
using PDV_Adega.LicensingServer.Middleware;
using PDV_Adega.LicensingServer.Services;

var builder = WebApplication.CreateBuilder(args);
var adminPassword = builder.Configuration["PDV_LICENSE_ADMIN_PASSWORD"];
if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 16)
    throw new InvalidOperationException("Configure PDV_LICENSE_ADMIN_PASSWORD com pelo menos 16 caracteres.");
var keyRingPath = builder.Configuration["PDV_LICENSE_KEY_RING_PATH"] ??
    Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keyRingPath);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("PDV-Adega-Licensing");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlPath = Path.Combine(AppContext.BaseDirectory, "LicensingServer.xml");
    options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadHttpRequestExceptionHandler>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-PDVAdegaAdmin";
        options.Cookie.HttpOnly = true;
        options.Cookie.Path = "/";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("admin-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddSingleton<ILicenseService, LicenseService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/admin") &&
        (HttpMethods.IsPost(context.Request.Method) ||
         HttpMethods.IsPut(context.Request.Method) ||
         HttpMethods.IsDelete(context.Request.Method)))
    {
        var origin = context.Request.Headers.Origin.ToString();
        var requestOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) ||
            !string.Equals(parsedOrigin.GetLeftPart(UriPartial.Authority), requestOrigin, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    await next();
});

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }))
    .WithName("Health")
    .WithSummary("Verifica a disponibilidade do serviço de licenças");

app.MapPost("/api/admin/login", async Task<Results<Ok, UnauthorizedHttpResult>> (
    AdminLoginRequest request,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Password ?? ""));
    var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(adminPassword));
    if (!CryptographicOperations.FixedTimeEquals(providedHash, expectedHash))
        return TypedResults.Unauthorized();

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "admin")],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));
    return TypedResults.Ok();
})
    .RequireRateLimiting("admin-login")
    .WithName("AdminLogin")
    .WithSummary("Autentica a conta administradora do painel")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

app.MapPost("/api/admin/logout", async Task<NoContent> (HttpContext context, CancellationToken cancellationToken) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return TypedResults.NoContent();
})
    .RequireAuthorization()
    .WithName("AdminLogout")
    .WithSummary("Encerra a sessão administrativa")
    .Produces(StatusCodes.Status204NoContent);

app.MapLicenses();

app.UseSwagger();
app.UseSwaggerUI();
app.Run();

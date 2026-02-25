using System.Security.Cryptography.X509Certificates;
using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Handlers;
using DonetickCalDav.CalDav.Middleware;
using DonetickCalDav.Configuration;
using DonetickCalDav.Donetick;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Server.Kestrel.Core;

const string Version = "0.5.0";

var builder = WebApplication.CreateBuilder(args);

// -- Configuration --
var configSection = builder.Configuration.Get<AppSettings>() ?? new AppSettings();
builder.Services.Configure<AppSettings>(builder.Configuration);

// -- HTTP client for the Donetick External API --
builder.Services.AddHttpClient<IDonetickApiClient, DonetickApiClient>(client =>
{
    client.BaseAddress = new Uri(configSection.Donetick.BaseUrl);
    client.DefaultRequestHeaders.Add("secretkey", configSection.Donetick.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// -- Cache, calendar resolver, client tracking, and background sync --
builder.Services.AddSingleton<ChoreCache>();
builder.Services.AddSingleton<CalendarResolver>();
builder.Services.AddSingleton<ClientTracker>();
builder.Services.AddHostedService<ChoreSyncService>();

// -- Status page (singleton — uses cache, client tracker + settings) --
builder.Services.AddSingleton<StatusPageHandler>(sp =>
    new StatusPageHandler(
        sp.GetRequiredService<ChoreCache>(),
        sp.GetRequiredService<ClientTracker>(),
        sp.GetRequiredService<IOptions<AppSettings>>(),
        Version));

// -- CalDAV handlers (scoped so they are resolved per-request via middleware injection) --
builder.Services.AddScoped<PropFindHandler>();
builder.Services.AddScoped<ReportHandler>();
builder.Services.AddScoped<GetHandler>();
builder.Services.AddScoped<PutHandler>();
builder.Services.AddScoped<DeleteHandler>();
builder.Services.AddScoped<PropPatchHandler>();

// -- Authentication --
// Note: we do NOT call app.UseAuthentication() — the CalDavMiddleware handles
// auth manually so it can exempt .well-known and OPTIONS from the auth requirement.
builder.Services.AddAuthentication("BasicAuth")
    .AddScheme<AuthenticationSchemeOptions, CalDavAuthenticationHandler>("BasicAuth", null);

// -- Logging: suppress noisy framework logs --
// Apple CalDAV clients always probe without credentials first (getting 401), then retry with
// credentials. Both the ASP.NET Core auth framework and our own CalDavAuthenticationHandler
// log each 401 at Info level, flooding the logs with "Missing Authorization header" messages.
// Similarly, the HttpClient factory logs every request/response at Info level.
builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Warning);
builder.Logging.AddFilter("DonetickCalDav.CalDav.Middleware.CalDavAuthenticationHandler", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// -- Kestrel configuration (HTTP or HTTPS) --
var port = configSection.CalDav.ListenPort;
var tls = configSection.CalDav.Tls;

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(port, listenOptions =>
    {
        var cert = LoadCertificate(tls);
        if (cert != null)
        {
            listenOptions.UseHttps(cert);
        }
    });
});

var app = builder.Build();

// -- Middleware pipeline --
// No app.UseAuthentication() — CalDavMiddleware calls AuthenticateAsync manually
app.UseMiddleware<CalDavMiddleware>();

var scheme = tls != null ? "https" : "http";
app.Logger.LogInformation("Donetick CalDAV Bridge v{Version} starting on {Scheme}://0.0.0.0:{Port}",
    Version, scheme, port);
app.Logger.LogInformation("Donetick API: {BaseUrl}", configSection.Donetick.BaseUrl);
app.Logger.LogInformation("CalDAV user: {User}", configSection.CalDav.Username);
if (tls != null)
    app.Logger.LogInformation("TLS enabled — Apple Calendar/Reminders requires HTTPS for Basic Auth");

app.Run();

/// <summary>
/// Loads a TLS certificate from the configured PFX or PEM files.
/// Returns null when TLS is not configured (plain HTTP mode).
/// </summary>
static X509Certificate2? LoadCertificate(TlsSettings? tls)
{
    if (tls == null)
        return null;

    // Option 1: PFX file
    if (!string.IsNullOrEmpty(tls.PfxPath))
        return new X509Certificate2(tls.PfxPath, tls.PfxPassword);

    // Option 2: PEM cert + key pair (e.g. from Tailscale or Let's Encrypt)
    if (!string.IsNullOrEmpty(tls.CertPath) && !string.IsNullOrEmpty(tls.KeyPath))
        return X509Certificate2.CreateFromPemFile(tls.CertPath, tls.KeyPath);

    throw new InvalidOperationException(
        "TLS is configured but no certificate provided. " +
        "Set either CalDav__Tls__PfxPath or both CalDav__Tls__CertPath and CalDav__Tls__KeyPath.");
}

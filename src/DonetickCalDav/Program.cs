using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Handlers;
using DonetickCalDav.CalDav.Middleware;
using DonetickCalDav.Configuration;
using DonetickCalDav.Donetick;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// -- Configuration --
var configSection = builder.Configuration.Get<AppSettings>() ?? new AppSettings();
builder.Services.Configure<AppSettings>(builder.Configuration);

// -- HTTP client for the Donetick External API --
builder.Services.AddHttpClient<DonetickApiClient>(client =>
{
    client.BaseAddress = new Uri(configSection.Donetick.BaseUrl);
    client.DefaultRequestHeaders.Add("secretkey", configSection.Donetick.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// -- Cache and background sync --
builder.Services.AddSingleton<ChoreCache>();
builder.Services.AddHostedService<ChoreSyncService>();

// -- CalDAV handlers (scoped so they are resolved per-request via middleware injection) --
builder.Services.AddScoped<PropFindHandler>();
builder.Services.AddScoped<ReportHandler>();
builder.Services.AddScoped<GetHandler>();
builder.Services.AddScoped<PutHandler>();
builder.Services.AddScoped<DeleteHandler>();
builder.Services.AddScoped<PropPatchHandler>();

// -- Authentication --
builder.Services.AddAuthentication("BasicAuth")
    .AddScheme<AuthenticationSchemeOptions, CalDavAuthenticationHandler>("BasicAuth", null);

// -- Kestrel configuration --
var port = configSection.CalDav.ListenPort;
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// -- Middleware pipeline --
app.UseAuthentication();
app.UseMiddleware<CalDavMiddleware>();

app.Logger.LogInformation("Donetick CalDAV Bridge starting on port {Port}", port);
app.Logger.LogInformation("Donetick API: {BaseUrl}", configSection.Donetick.BaseUrl);
app.Logger.LogInformation("CalDAV user: {User}", configSection.CalDav.Username);

app.Run();

using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using DonetickCalDav.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.CalDav.Middleware;

/// <summary>
/// Handles HTTP Basic Authentication for CalDAV endpoints.
/// Validates credentials against the configured CalDAV username and password.
/// </summary>
public sealed class CalDavAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly CalDavSettings _calDavSettings;

    public CalDavAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AppSettings> appSettings)
        : base(options, logger, encoder)
    {
        _calDavSettings = appSettings.Value.CalDav;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header"));

        var authValue = authHeader.ToString();
        if (!authValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Invalid authentication scheme"));

        byte[] decodedBytes;
        try
        {
            decodedBytes = Convert.FromBase64String(authValue["Basic ".Length..]);
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Base64 in Authorization header"));
        }

        var decoded = Encoding.UTF8.GetString(decodedBytes);
        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
            return Task.FromResult(AuthenticateResult.Fail("Invalid credentials format"));

        var username = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];

        if (username != _calDavSettings.Username || password != _calDavSettings.Password)
            return Task.FromResult(AuthenticateResult.Fail("Invalid credentials"));

        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers["WWW-Authenticate"] = "Basic realm=\"DonetickCalDAV\"";
        return Task.CompletedTask;
    }
}

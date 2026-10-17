using System.Security.Claims;
using System.Security.Cryptography;
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

        // Constant-time comparison. A naive != short-circuits on the first mismatching
        // character, leaking the prefix length through timing. FixedTimeEquals processes
        // every byte regardless, so attackers cannot probe the password one byte at a time.
        if (!FixedTimeStringEquals(username, _calDavSettings.Username) ||
            !FixedTimeStringEquals(password, _calDavSettings.Password))
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

    /// <summary>
    /// Timing-safe comparison of two strings as UTF-8 byte sequences.
    /// Note: FixedTimeEquals still returns early when lengths differ, so the length of
    /// the configured credentials is not fully hidden. The guarantee is content-timing,
    /// which is what matters for byte-at-a-time probing attacks.
    /// </summary>
    private static bool FixedTimeStringEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}

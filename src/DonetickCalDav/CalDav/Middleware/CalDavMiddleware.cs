using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Handlers;
using Microsoft.AspNetCore.Authentication;

namespace DonetickCalDav.CalDav.Middleware;

/// <summary>
/// ASP.NET Core middleware that routes incoming HTTP requests to the appropriate CalDAV handler.
/// Handles authentication, DAV header injection, and method-based dispatch.
/// </summary>
public sealed class CalDavMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CalDavMiddleware> _logger;
    private readonly StatusPageHandler _statusPage;
    private readonly ClientTracker _clientTracker;

    private const string DavCapabilities = "1, 3, access-control, calendar-access";
    private const string AllowedMethods = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, REPORT";

    public CalDavMiddleware(RequestDelegate next, ILogger<CalDavMiddleware> logger,
        StatusPageHandler statusPage, ClientTracker clientTracker)
    {
        _next = next;
        _logger = logger;
        _statusPage = statusPage;
        _clientTracker = clientTracker;
    }

    public async Task InvokeAsync(
        HttpContext context,
        PropFindHandler propFindHandler,
        ReportHandler reportHandler,
        GetHandler getHandler,
        PutHandler putHandler,
        DeleteHandler deleteHandler,
        PropPatchHandler propPatchHandler)
    {
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method.ToUpperInvariant();

        // Log every incoming request with key headers for debugging
        _logger.LogDebug(">>> {Method} {Scheme}://{Host}{Path} Auth:{HasAuth} UA:{UserAgent}",
            method,
            context.Request.Scheme,
            context.Request.Host,
            path,
            context.Request.Headers.Authorization.Count > 0 ? "yes" : "no",
            context.Request.Headers.UserAgent.FirstOrDefault() ?? "-");

        // .well-known discovery endpoint — no auth, but include DAV header so Apple
        // recognizes this as a CalDAV server before following the redirect
        if (path.StartsWith("/.well-known/caldav", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers["DAV"] = DavCapabilities;
            WellKnownHandler.Handle(context);
            return;
        }

        // Status / health check endpoint
        if (path is "/" or "/health")
        {
            await _statusPage.HandleAsync(context);
            return;
        }

        // Only handle /caldav paths; pass everything else to the next middleware
        if (!path.StartsWith("/caldav", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // DAV header must be on ALL CalDAV responses — including 401 challenges.
        // Apple will not send credentials unless it recognizes this as a DAV server.
        context.Response.Headers["DAV"] = DavCapabilities;

        // OPTIONS must work without auth — Apple checks capabilities before authenticating
        if (method == "OPTIONS")
        {
            OptionsHandler.Handle(context);
            return;
        }

        // Enforce Basic Auth for all other CalDAV paths
        var authResult = await context.AuthenticateAsync("BasicAuth");
        if (!authResult.Succeeded)
        {
            _logger.LogDebug("<<< 401 Challenge for {Method} {Path} — reason: {Reason}",
                method, path, authResult.Failure?.Message ?? "unknown");
            context.Response.StatusCode = 401;
            context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"DonetickCalDAV\"";
            return;
        }

        _logger.LogDebug("CalDAV {Method} {Path} Depth:{Depth} (authenticated)",
            method, path, context.Request.Headers["Depth"].FirstOrDefault() ?? "-");

        // Track client activity for status page
        _clientTracker.RecordRequest(
            context.Request.Headers.UserAgent.FirstOrDefault(), method);

        await DispatchAsync(context, method, propFindHandler, reportHandler,
            getHandler, putHandler, deleteHandler, propPatchHandler);
    }

    /// <summary>
    /// Routes the request to the correct handler based on the HTTP method.
    /// </summary>
    private async Task DispatchAsync(
        HttpContext context,
        string method,
        PropFindHandler propFindHandler,
        ReportHandler reportHandler,
        GetHandler getHandler,
        PutHandler putHandler,
        DeleteHandler deleteHandler,
        PropPatchHandler propPatchHandler)
    {
        switch (method)
        {
            case "PROPFIND":
                await propFindHandler.HandleAsync(context);
                break;

            case "REPORT":
                await reportHandler.HandleAsync(context);
                break;

            case "GET" or "HEAD":
                await getHandler.HandleAsync(context);
                break;

            case "PUT":
                await putHandler.HandleAsync(context);
                break;

            case "DELETE":
                await deleteHandler.HandleAsync(context);
                break;

            case "PROPPATCH":
                await propPatchHandler.HandleAsync(context);
                break;

            case "MKCALENDAR" or "MKCOL":
                // Apple may attempt these but they are not supported
                context.Response.StatusCode = 403;
                break;

            default:
                context.Response.StatusCode = 405;
                context.Response.Headers["Allow"] = AllowedMethods;
                break;
        }
    }
}

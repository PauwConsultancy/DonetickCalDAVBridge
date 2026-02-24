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

    private const string AllowedMethods = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, REPORT";

    public CalDavMiddleware(RequestDelegate next, ILogger<CalDavMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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

        // .well-known discovery endpoint does not require authentication
        if (path.StartsWith("/.well-known/caldav", StringComparison.OrdinalIgnoreCase))
        {
            WellKnownHandler.Handle(context);
            return;
        }

        // Health check endpoint
        if (path is "/" or "/health")
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("Donetick CalDAV Bridge is running");
            return;
        }

        // Only handle /caldav paths; pass everything else to the next middleware
        if (!path.StartsWith("/caldav", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Enforce Basic Auth for all CalDAV paths
        var authResult = await context.AuthenticateAsync("BasicAuth");
        if (!authResult.Succeeded)
        {
            context.Response.StatusCode = 401;
            context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"DonetickCalDAV\"";
            return;
        }

        // Apple Calendar requires the DAV header on every CalDAV response
        context.Response.Headers["DAV"] = "1, 3, access-control, calendar-access";

        _logger.LogDebug("CalDAV {Method} {Path} Depth:{Depth}",
            method, path, context.Request.Headers["Depth"].FirstOrDefault() ?? "-");

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
            case "OPTIONS":
                OptionsHandler.Handle(context);
                break;

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

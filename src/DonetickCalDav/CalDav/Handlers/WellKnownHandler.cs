namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Handles /.well-known/caldav by redirecting to the CalDAV root.
/// This is the entry point for RFC 6764 CalDAV service discovery.
/// Apple Calendar always starts here when configuring a new account.
/// </summary>
public static class WellKnownHandler
{
    public static void Handle(HttpContext context)
    {
        context.Response.StatusCode = 301;
        context.Response.Headers["Location"] = "/caldav/";
    }
}

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Handles OPTIONS requests by returning the DAV capabilities and allowed methods.
/// Apple Calendar sends this during discovery to check server capabilities.
/// </summary>
public static class OptionsHandler
{
    private const string AllowedMethods = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, REPORT";

    public static void Handle(HttpContext context)
    {
        context.Response.StatusCode = 200;
        context.Response.Headers["Allow"] = AllowedMethods;
    }
}

using System.Text;
using Microsoft.AspNetCore.Http;

namespace DonetickCalDav.Tests.Helpers;

/// <summary>
/// Factory methods for creating DefaultHttpContext instances in handler tests.
/// </summary>
public static class HttpContextHelper
{
    /// <summary>
    /// Creates a DefaultHttpContext with the given method, path, and optional request body.
    /// The response body is a writable MemoryStream.
    /// </summary>
    public static DefaultHttpContext Create(string method, string path, string? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (body != null)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bodyBytes);
            context.Request.ContentLength = bodyBytes.Length;
            context.Request.ContentType = "text/calendar; charset=utf-8";
        }

        return context;
    }

    /// <summary>Reads the response body as a string (seeks back to start first).</summary>
    public static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }
}

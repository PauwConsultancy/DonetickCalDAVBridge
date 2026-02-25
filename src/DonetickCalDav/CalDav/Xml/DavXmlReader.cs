using System.Xml.Linq;

namespace DonetickCalDav.CalDav.Xml;

/// <summary>
/// Parses incoming CalDAV/WebDAV XML request bodies (PROPFIND, REPORT).
/// All methods are null-safe — returns empty collections when parsing fails.
/// </summary>
public static class DavXmlReader
{
    /// <summary>
    /// Reads and parses the XML body from a CalDAV request.
    /// Returns null if the body is empty or cannot be parsed.
    /// </summary>
    public static async Task<XDocument?> ReadRequestBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0)
            return null;

        using var reader = new StreamReader(context.Request.Body);
        var xml = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            return XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the list of requested property names from a PROPFIND or REPORT body.
    /// Returns an empty list if no properties are specified (allprop).
    /// </summary>
    public static List<XName> GetRequestedProperties(XDocument doc)
    {
        var prop = doc.Descendants(DavNamespaces.D + "prop").FirstOrDefault();
        return prop?.Elements().Select(e => e.Name).ToList() ?? [];
    }

    /// <summary>
    /// Extracts href values from a calendar-multiget REPORT body.
    /// These are the specific resources the client wants to fetch.
    /// </summary>
    public static List<string> GetMultigetHrefs(XDocument doc)
    {
        return doc.Descendants(DavNamespaces.D + "href")
            .Select(e => e.Value.Trim())
            .ToList();
    }

    /// <summary>Checks if the REPORT body is a calendar-query request.</summary>
    public static bool IsCalendarQuery(XDocument doc) =>
        doc.Root?.Name == DavNamespaces.C + "calendar-query";

    /// <summary>Checks if the REPORT body is a calendar-multiget request.</summary>
    public static bool IsCalendarMultiget(XDocument doc) =>
        doc.Root?.Name == DavNamespaces.C + "calendar-multiget";

    /// <summary>Checks if the REPORT body is a DAV sync-collection request.</summary>
    public static bool IsSyncCollection(XDocument doc) =>
        doc.Root?.Name == DavNamespaces.D + "sync-collection";
}

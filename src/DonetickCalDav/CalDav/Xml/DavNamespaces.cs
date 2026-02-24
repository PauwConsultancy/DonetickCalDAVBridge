using System.Xml.Linq;

namespace DonetickCalDav.CalDav.Xml;

/// <summary>
/// Central registry of all XML namespaces used in CalDAV/WebDAV responses.
/// Apple Calendar requires specific namespace prefixes for proper discovery.
/// </summary>
public static class DavNamespaces
{
    // URI strings — used in XAttribute declarations
    public const string DavUri = "DAV:";
    public const string CalDavUri = "urn:ietf:params:xml:ns:caldav";
    public const string CalendarServerUri = "http://calendarserver.org/ns/";
    public const string AppleUri = "http://apple.com/ns/ical/";

    // Typed namespaces — used in XElement construction
    public static readonly XNamespace D = DavUri;
    public static readonly XNamespace C = CalDavUri;
    public static readonly XNamespace CS = CalendarServerUri;
    public static readonly XNamespace Apple = AppleUri;
}

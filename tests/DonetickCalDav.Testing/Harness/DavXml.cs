using System.Xml.Linq;

namespace DonetickCalDav.Testing.Harness;

/// <summary>
/// XML namespaces and request bodies for WebDAV and CalDAV. Defined here instead of reusing the
/// production constants, so a wrong namespace in the bridge cannot hide behind the same mistake in a test.
/// </summary>
public static class Dav
{
    public static readonly XNamespace D = "DAV:";
    public static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";
    public static readonly XNamespace CS = "http://calendarserver.org/ns/";
    public static readonly XNamespace Apple = "http://apple.com/ns/ical/";

    /// <summary>The properties Apple Reminders asks for on the calendar home (depth 1), abridged.</summary>
    public static readonly XName[] CollectionProperties =
    [
        D + "resourcetype", D + "displayname", CS + "getctag", D + "sync-token",
        C + "supported-calendar-component-set", Apple + "calendar-color", Apple + "calendar-order",
    ];

    /// <summary>PROPFIND body asking for the given properties, or <c>allprop</c> when none are given.</summary>
    public static string PropFind(params XName[] properties) =>
        Serialize(new XElement(D + "propfind", Namespaces(),
            properties.Length == 0 ? new XElement(D + "allprop") : Prop(properties)));

    /// <summary>calendar-query REPORT for all VTODOs.</summary>
    public static string CalendarQuery(params XName[] properties) =>
        Serialize(new XElement(C + "calendar-query", Namespaces(),
            Prop(DefaultResourceProperties(properties)),
            new XElement(C + "filter",
                new XElement(C + "comp-filter", new XAttribute("name", "VCALENDAR"),
                    new XElement(C + "comp-filter", new XAttribute("name", "VTODO"))))));

    /// <summary>calendar-multiget REPORT for the given hrefs.</summary>
    public static string CalendarMultiget(IEnumerable<string> hrefs, params XName[] properties) =>
        Serialize(new XElement(C + "calendar-multiget", Namespaces(),
            Prop(DefaultResourceProperties(properties)),
            hrefs.Select(h => new XElement(D + "href", h))));

    /// <summary>sync-collection REPORT, with an optional token from an earlier response.</summary>
    public static string SyncCollection(string? syncToken = null, params XName[] properties) =>
        Serialize(new XElement(D + "sync-collection", Namespaces(),
            new XElement(D + "sync-token", syncToken ?? ""),
            new XElement(D + "sync-level", "1"),
            Prop(properties.Length == 0 ? [D + "getetag"] : properties)));

    /// <summary>PROPPATCH body that sets the given properties to the given values.</summary>
    public static string PropPatch(params (XName Name, string Value)[] set) =>
        Serialize(new XElement(D + "propertyupdate", Namespaces(),
            new XElement(D + "set", new XElement(D + "prop", set.Select(p => new XElement(p.Name, p.Value))))));

    private static XName[] DefaultResourceProperties(XName[] properties) =>
        properties.Length == 0 ? [D + "getetag", C + "calendar-data"] : properties;

    private static XElement Prop(IEnumerable<XName> properties) =>
        new(D + "prop", properties.Select(p => new XElement(p)));

    private static object[] Namespaces() =>
    [
        new XAttribute(XNamespace.Xmlns + "d", D.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "cs", CS.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "ical", Apple.NamespaceName),
    ];

    private static string Serialize(XElement root) =>
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Declaration + "\n" + root;
}

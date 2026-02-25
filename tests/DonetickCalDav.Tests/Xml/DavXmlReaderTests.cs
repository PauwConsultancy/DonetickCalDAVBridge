using System.Xml.Linq;
using DonetickCalDav.CalDav.Xml;
using FluentAssertions;

namespace DonetickCalDav.Tests.Xml;

public class DavXmlReaderTests
{
    // ── GetRequestedProperties ──────────────────────────────────────────────

    [Fact]
    public void GetRequestedProperties_WithSpecificProps_ReturnsPropNames()
    {
        var xml = XDocument.Parse("""
            <d:propfind xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
                <d:prop>
                    <d:displayname/>
                    <d:resourcetype/>
                    <cal:calendar-home-set/>
                </d:prop>
            </d:propfind>
            """);

        var props = DavXmlReader.GetRequestedProperties(xml);

        props.Should().HaveCount(3);
        props.Should().Contain(DavNamespaces.D + "displayname");
        props.Should().Contain(DavNamespaces.D + "resourcetype");
        props.Should().Contain(DavNamespaces.C + "calendar-home-set");
    }

    [Fact]
    public void GetRequestedProperties_AllProp_ReturnsEmptyList()
    {
        var xml = XDocument.Parse("""
            <d:propfind xmlns:d="DAV:">
                <d:allprop/>
            </d:propfind>
            """);

        var props = DavXmlReader.GetRequestedProperties(xml);

        props.Should().BeEmpty();
    }

    [Fact]
    public void GetRequestedProperties_EmptyProp_ReturnsEmptyList()
    {
        var xml = XDocument.Parse("""
            <d:propfind xmlns:d="DAV:">
                <d:prop/>
            </d:propfind>
            """);

        var props = DavXmlReader.GetRequestedProperties(xml);

        props.Should().BeEmpty();
    }

    // ── GetMultigetHrefs ────────────────────────────────────────────────────

    [Fact]
    public void GetMultigetHrefs_MultipleHrefs_ReturnsAll()
    {
        var xml = XDocument.Parse("""
            <cal:calendar-multiget xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
                <d:prop>
                    <d:getetag/>
                    <cal:calendar-data/>
                </d:prop>
                <d:href>/caldav/calendars/user/tasks/donetick-1.ics</d:href>
                <d:href>/caldav/calendars/user/tasks/donetick-2.ics</d:href>
                <d:href>/caldav/calendars/user/tasks/donetick-3.ics</d:href>
            </cal:calendar-multiget>
            """);

        var hrefs = DavXmlReader.GetMultigetHrefs(xml);

        hrefs.Should().HaveCount(3);
        hrefs.Should().Contain("/caldav/calendars/user/tasks/donetick-1.ics");
        hrefs.Should().Contain("/caldav/calendars/user/tasks/donetick-2.ics");
        hrefs.Should().Contain("/caldav/calendars/user/tasks/donetick-3.ics");
    }

    [Fact]
    public void GetMultigetHrefs_TrimsWhitespace()
    {
        var xml = XDocument.Parse("""
            <cal:calendar-multiget xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
                <d:href>  /caldav/calendars/user/tasks/donetick-1.ics  </d:href>
            </cal:calendar-multiget>
            """);

        var hrefs = DavXmlReader.GetMultigetHrefs(xml);

        hrefs.Should().ContainSingle()
            .Which.Should().Be("/caldav/calendars/user/tasks/donetick-1.ics");
    }

    // ── Report type detection ───────────────────────────────────────────────

    [Fact]
    public void IsCalendarQuery_CalendarQueryRoot_ReturnsTrue()
    {
        var xml = XDocument.Parse("""
            <cal:calendar-query xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
                <d:prop><d:getetag/></d:prop>
            </cal:calendar-query>
            """);

        DavXmlReader.IsCalendarQuery(xml).Should().BeTrue();
        DavXmlReader.IsCalendarMultiget(xml).Should().BeFalse();
        DavXmlReader.IsSyncCollection(xml).Should().BeFalse();
    }

    [Fact]
    public void IsCalendarMultiget_MultigetRoot_ReturnsTrue()
    {
        var xml = XDocument.Parse("""
            <cal:calendar-multiget xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
                <d:prop><d:getetag/></d:prop>
            </cal:calendar-multiget>
            """);

        DavXmlReader.IsCalendarMultiget(xml).Should().BeTrue();
        DavXmlReader.IsCalendarQuery(xml).Should().BeFalse();
        DavXmlReader.IsSyncCollection(xml).Should().BeFalse();
    }

    [Fact]
    public void IsSyncCollection_SyncCollectionRoot_ReturnsTrue()
    {
        var xml = XDocument.Parse("""
            <d:sync-collection xmlns:d="DAV:">
                <d:sync-token/>
                <d:prop><d:getetag/></d:prop>
            </d:sync-collection>
            """);

        DavXmlReader.IsSyncCollection(xml).Should().BeTrue();
        DavXmlReader.IsCalendarQuery(xml).Should().BeFalse();
        DavXmlReader.IsCalendarMultiget(xml).Should().BeFalse();
    }

    [Fact]
    public void IsCalendarQuery_PropfindRoot_ReturnsFalse()
    {
        var xml = XDocument.Parse("""
            <d:propfind xmlns:d="DAV:">
                <d:prop><d:displayname/></d:prop>
            </d:propfind>
            """);

        DavXmlReader.IsCalendarQuery(xml).Should().BeFalse();
        DavXmlReader.IsCalendarMultiget(xml).Should().BeFalse();
        DavXmlReader.IsSyncCollection(xml).Should().BeFalse();
    }
}

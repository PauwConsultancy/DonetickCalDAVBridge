namespace DonetickCalDav.Testing.Harness;

/// <summary>
/// Request bodies modelled on what Apple Reminders and Calendar.app send during account setup and
/// sync: the same methods, depths and property sets, including the many properties the bridge does not
/// support. They are reconstructions, not recordings; recorded sessions (tests/fixtures/sessions) replace
/// them as the reference once captured.
/// </summary>
public static class AppleRequests
{
    public const string UserAgentReminders = "Mac+OS+X/26.0 (25A354) remindd/1.0";
    public const string UserAgentCalendar = "Mac+OS+X/26.0 (25A354) CalendarAgent/1000";
    public const string UserAgentIos = "iOS/26.0 (23A341) dataaccessd/1.0";

    /// <summary>PROPFIND depth 0 on the URL from .well-known: who is the current principal?</summary>
    public const string CurrentUserPrincipal = """
        <?xml version="1.0" encoding="UTF-8"?>
        <A:propfind xmlns:A="DAV:">
          <A:prop>
            <A:current-user-principal/>
            <A:principal-URL/>
            <A:resourcetype/>
          </A:prop>
        </A:propfind>
        """;

    /// <summary>PROPFIND depth 0 on the principal.</summary>
    public const string PrincipalProperties = """
        <?xml version="1.0" encoding="UTF-8"?>
        <A:propfind xmlns:A="DAV:" xmlns:B="urn:ietf:params:xml:ns:caldav" xmlns:C="http://calendarserver.org/ns/">
          <A:prop>
            <B:calendar-home-set/>
            <B:calendar-user-address-set/>
            <A:current-user-principal/>
            <A:displayname/>
            <C:dropbox-home-URL/>
            <C:email-address-set/>
            <C:notification-URL/>
            <A:principal-collection-set/>
            <A:principal-URL/>
            <A:resource-id/>
            <B:schedule-inbox-URL/>
            <B:schedule-outbox-URL/>
            <A:supported-report-set/>
          </A:prop>
        </A:propfind>
        """;

    /// <summary>PROPFIND depth 1 on the calendar home: the list of calendars with everything Apple shows.</summary>
    public const string HomeListing = """
        <?xml version="1.0" encoding="UTF-8"?>
        <A:propfind xmlns:A="DAV:" xmlns:B="urn:ietf:params:xml:ns:caldav" xmlns:C="http://calendarserver.org/ns/" xmlns:D="http://apple.com/ns/ical/">
          <A:prop>
            <A:add-member/>
            <C:allowed-sharing-modes/>
            <B:calendar-alarm/>
            <D:calendar-color/>
            <B:calendar-description/>
            <B:calendar-free-busy-set/>
            <D:calendar-order/>
            <B:calendar-timezone/>
            <A:current-user-privilege-set/>
            <B:default-alarm-vevent-date/>
            <A:displayname/>
            <C:getctag/>
            <C:invite/>
            <A:owner/>
            <C:pushkey/>
            <A:quota-available-bytes/>
            <A:quota-used-bytes/>
            <D:refreshrate/>
            <A:resource-id/>
            <A:resourcetype/>
            <B:schedule-calendar-transp/>
            <C:source/>
            <B:supported-calendar-component-set/>
            <A:supported-report-set/>
            <A:sync-token/>
          </A:prop>
        </A:propfind>
        """;

    /// <summary>PROPFIND depth 0 on a calendar: has anything changed?</summary>
    public const string CollectionChangeCheck = """
        <?xml version="1.0" encoding="UTF-8"?>
        <A:propfind xmlns:A="DAV:" xmlns:C="http://calendarserver.org/ns/">
          <A:prop>
            <C:getctag/>
            <A:sync-token/>
          </A:prop>
        </A:propfind>
        """;

    /// <summary>PROPFIND depth 1 on a calendar: the members and their ETags.</summary>
    public const string CollectionMembers = """
        <?xml version="1.0" encoding="UTF-8"?>
        <A:propfind xmlns:A="DAV:">
          <A:prop>
            <A:getcontenttype/>
            <A:getetag/>
          </A:prop>
        </A:propfind>
        """;

    /// <summary>PROPFIND depth 0 on one resource.</summary>
    public const string ResourceProperties = """
        <?xml version="1.0" encoding="UTF-8"?>
        <A:propfind xmlns:A="DAV:" xmlns:B="urn:ietf:params:xml:ns:caldav">
          <A:prop>
            <A:getetag/>
            <A:getcontenttype/>
            <A:resourcetype/>
            <B:calendar-data/>
          </A:prop>
        </A:propfind>
        """;

    /// <summary>REPORT calendar-multiget for the given hrefs, with ETag and data.</summary>
    public static string Multiget(params string[] hrefs) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <B:calendar-multiget xmlns:A="DAV:" xmlns:B="urn:ietf:params:xml:ns:caldav">
          <A:prop>
            <A:getetag/>
            <B:calendar-data/>
          </A:prop>
        {string.Join("\n", hrefs.Select(h => $"  <A:href>{h}</A:href>"))}
        </B:calendar-multiget>
        """;

    /// <summary>REPORT sync-collection, initial (empty token) or incremental.</summary>
    public static string SyncCollection(string token = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <A:sync-collection xmlns:A="DAV:">
          <A:sync-token>{token}</A:sync-token>
          <A:sync-level>1</A:sync-level>
          <A:prop>
            <A:getcontenttype/>
            <A:getetag/>
          </A:prop>
        </A:sync-collection>
        """;

    /// <summary>REPORT calendar-query for all VTODOs that are not completed, as Reminders asks after setup.</summary>
    public const string OpenTodosQuery = """
        <?xml version="1.0" encoding="UTF-8"?>
        <B:calendar-query xmlns:A="DAV:" xmlns:B="urn:ietf:params:xml:ns:caldav">
          <A:prop>
            <A:getetag/>
            <A:getcontenttype/>
          </A:prop>
          <B:filter>
            <B:comp-filter name="VCALENDAR">
              <B:comp-filter name="VTODO">
                <B:prop-filter name="COMPLETED">
                  <B:is-not-defined/>
                </B:prop-filter>
              </B:comp-filter>
            </B:comp-filter>
          </B:filter>
        </B:calendar-query>
        """;
}

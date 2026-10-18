using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Calendars;

/// <summary>IT-03: the calendar home and its collections, with GroupByLabel off and on.</summary>
[Trait("Scenario", "IT-03")]
[Trait("Covers", "UC-02")]
[Trait("Covers", "UC-07")]
public class CalendarCollectionTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    public async Task Home_GroupByLabelOff_ListsOneCalendarWithConfiguredNameAndColour()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.WithLabels(4));
        var bridge = await StartBridgeAsync(o => { o.CalendarName = "Household"; o.CalendarColor = "#FF8800FF"; });
        var calendar = bridge.CalendarPath();

        var home = await bridge.CreateClient().PropFindAsync(bridge.HomePath, 1,
        [
            .. Dav.CollectionProperties, Dav.D + "current-user-privilege-set", Dav.C + "calendar-description", Dav.D + "owner",
        ]);

        home.Hrefs.Should().Equal(bridge.HomePath, calendar);
        home.Property(calendar, Dav.D + "displayname")!.Value.Should().Be("Household");
        home.Property(calendar, Dav.Apple + "calendar-color")!.Value.Should().Be("#FF8800FF");
        home.Property(calendar, Dav.Apple + "calendar-order")!.Value.Should().Be("1");
        home.Property(calendar, Dav.C + "calendar-description")!.Value.Should().Be("Tasks from Donetick");
        home.Property(calendar, Dav.D + "owner")!.Value.Should().Be(bridge.PrincipalPath);

        var resourceType = home.Property(calendar, Dav.D + "resourcetype")!;
        resourceType.Element(Dav.D + "collection").Should().NotBeNull();
        resourceType.Element(Dav.C + "calendar").Should().NotBeNull();

        var privileges = home.Property(calendar, Dav.D + "current-user-privilege-set")!
            .Elements(Dav.D + "privilege").SelectMany(p => p.Elements()).Select(e => e.Name).ToList();
        privileges.Should().Contain([Dav.D + "read", Dav.D + "write"]);

        var ctag = home.Property(calendar, Dav.CS + "getctag")!.Value;
        home.Property(calendar, Dav.D + "sync-token")!.Value.Should().Be($"http://donetick-caldav/sync/{ctag}");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Home_UnknownProperty_IsReportedAsNotFound()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var home = await bridge.CreateClient().PropFindAsync(bridge.HomePath, 1, Dav.D + "displayname", Dav.D + "quota-used-bytes");

        home.IsNotFound(bridge.HomePath, Dav.D + "quota-used-bytes").Should().BeTrue();
        home.IsNotFound(bridge.CalendarPath(), Dav.D + "quota-used-bytes").Should().BeTrue();
        home.Property(bridge.HomePath, Dav.D + "displayname")!.Value.Should().Be($"{BridgeHost.Username} Calendars");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Collection_GroupByLabelOff_ContainsEveryChore()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.WithDueDate(2), TestChoreFactory.WithLabels(4),
            TestChoreFactory.Inactive(5));
        var bridge = await StartBridgeAsync();
        var calendar = bridge.CalendarPath();

        var depth0 = await bridge.CreateClient().PropFindAsync(calendar, 0, Dav.D + "getetag");
        var depth1 = await bridge.CreateClient().PropFindAsync(calendar, 1, Dav.D + "getetag", Dav.D + "getcontenttype");

        depth0.Hrefs.Should().Equal(calendar);
        depth1.Hrefs.Should().BeEquivalentTo(calendar,
            bridge.ResourcePath(1), bridge.ResourcePath(2), bridge.ResourcePath(4), bridge.ResourcePath(5));
        depth1.Property(bridge.ResourcePath(1), Dav.D + "getcontenttype")!.Value.Should().Be("text/calendar; component=vtodo");
        depth1.GetETag(bridge.ResourcePath(1)).Should().MatchRegex("^\"[0-9A-F]{16}\"$");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Home_GroupByLabelOn_ListsDefaultCalendarThenOneCalendarPerSingleLabel()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = true);

        var home = await bridge.CreateClient().PropFindAsync(bridge.HomePath, 1, Dav.CollectionProperties);

        home.Hrefs.Should().Equal(bridge.HomePath, bridge.CalendarPath("tasks"), bridge.CalendarPath("finance"),
            bridge.CalendarPath("home"));
        home.Property(bridge.CalendarPath("tasks"), Dav.D + "displayname")!.Value.Should().Be("General");
        home.Property(bridge.CalendarPath("finance"), Dav.D + "displayname")!.Value.Should().Be("Finance");
        home.Property(bridge.CalendarPath("home"), Dav.D + "displayname")!.Value.Should().Be("Home");
        home.Property(bridge.CalendarPath("home"), Dav.Apple + "calendar-order")!.Value.Should().Be("3");
        home.Property(bridge.CalendarPath("home"), Dav.Apple + "calendar-color")!.Value.Should().MatchRegex("^#[0-9A-F]{6}FF$");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Collections_GroupByLabelOn_ContainChoresBySingleLabelAndTheRestInTheDefault()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = true);
        var client = bridge.CreateClient();

        async Task<IEnumerable<string>> Members(string slug) =>
            (await client.PropFindAsync(bridge.CalendarPath(slug), 1, Dav.D + "getetag")).Hrefs.Skip(1);

        (await Members("tasks")).Should().BeEquivalentTo(
            bridge.ResourcePath(ReferenceData.WaterThePlants, "tasks"),
            bridge.ResourcePath(ReferenceData.CleanTheGutters, "tasks"),
            bridge.ResourcePath(ReferenceData.CallTheDentist, "tasks"));
        (await Members("finance")).Should().BeEquivalentTo(bridge.ResourcePath(ReferenceData.PayRent, "finance"));
        (await Members("home")).Should().BeEquivalentTo(
            bridge.ResourcePath(ReferenceData.BuyGroceries, "home"),
            bridge.ResourcePath(ReferenceData.TakeOutTheBins, "home"));
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Colours_GroupByLabelOn_AreTheSameAfterARestart()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = true);
        var first = await bridge.CreateClient().PropFindAsync(bridge.HomePath, 1, Dav.Apple + "calendar-color");

        await using var restarted = await BridgeHost.StartAsync(Donetick, o => o.GroupByLabel = true);
        var second = await restarted.CreateClient().PropFindAsync(restarted.HomePath, 1, Dav.Apple + "calendar-color");

        foreach (var slug in new[] { "finance", "home" })
        {
            second.Property(bridge.CalendarPath(slug), Dav.Apple + "calendar-color")!.Value
                .Should().Be(first.Property(bridge.CalendarPath(slug), Dav.Apple + "calendar-color")!.Value);
        }
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task CTag_GroupByLabelOn_ChangesOnlyForTheListWhoseChoreChanged()
    {
        var groceries = TestChoreFactory.Labeled(1, "Buy groceries", "Home");
        var rent = TestChoreFactory.Labeled(2, "Pay rent", "Finance");
        var dentist = TestChoreFactory.Simple(3, "Call the dentist");
        Donetick.WithChores(groceries, rent, dentist);
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = true);
        var client = bridge.CreateClient();
        var before = await client.PropFindAsync(bridge.HomePath, 1, Dav.CS + "getctag");

        var renamed = TestChoreFactory.Labeled(1, "Buy groceries and flowers", "Home");
        renamed.UpdatedAt = groceries.UpdatedAt.AddMinutes(5);
        Donetick.WithChores(renamed, rent, dentist);
        await bridge.RunPollAsync();
        var after = await client.PropFindAsync(bridge.HomePath, 1, Dav.CS + "getctag");

        string CTag(DavResponse response, string slug) => response.Property(bridge.CalendarPath(slug), Dav.CS + "getctag")!.Value;
        CTag(after, "home").Should().NotBe(CTag(before, "home"));
        CTag(after, "finance").Should().Be(CTag(before, "finance"));
        CTag(after, "tasks").Should().Be(CTag(before, "tasks"));
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task CTag_GroupByLabelOff_ChangesWhenAnyChoreChangesAndNotOnAnUnchangedPoll()
    {
        var chore = TestChoreFactory.Simple(1);
        Donetick.WithChores(chore, TestChoreFactory.Simple(2, "Other"));
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var calendar = bridge.CalendarPath();
        var initial = (await client.PropFindAsync(calendar, 0, Dav.CS + "getctag")).Property(calendar, Dav.CS + "getctag")!.Value;

        await bridge.RunPollAsync();
        var unchanged = (await client.PropFindAsync(calendar, 0, Dav.CS + "getctag")).Property(calendar, Dav.CS + "getctag")!.Value;

        Donetick.WithChores(TestChoreFactory.Simple(2, "Other"));
        await bridge.RunPollAsync();
        var afterRemoval = (await client.PropFindAsync(calendar, 0, Dav.CS + "getctag")).Property(calendar, Dav.CS + "getctag")!.Value;

        unchanged.Should().Be(initial, "a poll without changes keeps the CTag, so clients skip the refresh");
        afterRemoval.Should().NotBe(initial, "a chore removed in Donetick must change the CTag");
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData(false, "home")]
    [InlineData(true, "garden")]
    public async Task Collection_UnknownCalendar_Returns404(bool groupByLabel, string slug)
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = groupByLabel);

        var response = await bridge.CreateClient().PropFindAsync(bridge.CalendarPath(slug), 1, Dav.D + "getetag");

        response.Status.Should().Be(HttpStatusCode.NotFound);
    }
}

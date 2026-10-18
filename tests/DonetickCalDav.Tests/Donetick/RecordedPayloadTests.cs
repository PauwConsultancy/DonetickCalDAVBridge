using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DonetickCalDav.CalDav.VTodo;
using DonetickCalDav.Donetick;
using DonetickCalDav.Donetick.Models;
using DonetickCalDav.Testing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DonetickCalDav.Tests.Donetick;

/// <summary>
/// U-01: deserialization and mapping of payloads recorded from a real Donetick v0.1.82
/// (tests/fixtures/donetick/recorded). Hand-written JSON in the other tests misses real-world shapes.
/// </summary>
[Trait("Scenario", "U-01")]
public class RecordedPayloadTests
{
    private const string Version = "v0.1.82";

    [Fact]
    public async Task GetAllChoresAsync_RecordedList_ParsesEveryChoreAndTheFieldsTheBridgeUses()
    {
        var chores = await Client(Recorded("list-chores")).GetAllChoresAsync(TestContext.Current.CancellationToken);

        chores.Select(c => c.Name).Should().BeEquivalentTo(
            "Water the plants", "Pay rent", "Buy groceries", "Clean the gutters", "Call the dentist", "Take out the bins");

        var rent = chores.Single(c => c.Name == "Pay rent");
        rent.LabelsV2!.Select(l => l.Name).Should().Equal("Finance");
        rent.Priority.Should().Be(ChorePriority.High);
        rent.FrequencyMetadata!.Days.Should().Equal("1");

        var dentist = chores.Single(c => c.Name == "Call the dentist");
        dentist.NextDueDate.Should().BeNull();
        dentist.Description.Should().Be("Ask about the check-up");

        var plants = chores.Single(c => c.Name == "Water the plants");
        plants.NextDueDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
        plants.FrequencyMetadata!.Timezone.Should().Be("Europe/Amsterdam");
        chores.Single(c => c.Name == "Clean the gutters").LabelsV2.Should().HaveCount(2);
    }

    [Fact]
    [Trait("KnownIssue", "F-23")]
    public async Task AdjustToScheduledTime_RecordedRfc3339Time_LeavesTheDueDateUnchanged()
    {
        var chores = await Client(Recorded("list-chores")).GetAllChoresAsync(TestContext.Current.CancellationToken);
        var metadata = chores.Single(c => c.Name == "Water the plants").FrequencyMetadata!;
        var drifted = new DateTime(2026, 10, 20, 17, 45, 0, DateTimeKind.Utc);

        var adjusted = VTodoMapper.AdjustToScheduledTime(drifted, metadata);

        metadata.Time.Should().Contain("T", "Donetick stores the scheduled time as an RFC3339 timestamp");
        adjusted.Should().Be(drifted, "TimeOnly.TryParse rejects the timestamp, so nothing is adjusted");
    }

    [Fact]
    [Trait("KnownIssue", "F-24")]
    public async Task ToIcsString_RecordedUpdatedAtWithOffset_LabelsTheLocalWallTimeAsUtc()
    {
        var chores = await Client(Recorded("list-chores")).GetAllChoresAsync(TestContext.Current.CancellationToken);
        var chore = chores.Single(c => c.Name == "Pay rent");

        var ics = VTodoMapper.ToIcsString(chore);

        // Donetick sends updatedAt with the server's offset (+02:00); System.Text.Json converts it to the
        // local time of this process, and the mapper writes that wall time with a Z. Correct only in UTC.
        chore.UpdatedAt.Kind.Should().Be(DateTimeKind.Local);
        var lastModified = ics.Split("\r\n").Single(l => l.StartsWith("LAST-MODIFIED:", StringComparison.Ordinal));
        lastModified.Should().Be("LAST-MODIFIED:" + chore.UpdatedAt.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CreateChoreAsync_RecordedCreateResponse_ReturnsTheChoreWithTheRequestedDate()
    {
        var created = await Client(Recorded("create-chore-date-only"), HttpStatusCode.Created)
            .CreateChoreAsync(new ChoreLiteRequest { Name = "Created by the recorder", DueDate = "2026-11-02" },
                TestContext.Current.CancellationToken);

        created!.Name.Should().Be("Created by the recorder");
        created.Description.Should().Be("Recorded payload");
        DateOnly.FromDateTime(created.NextDueDate!.Value).Should().Be(new DateOnly(2026, 11, 2));
        created.NextDueDate.Value.TimeOfDay.Should().NotBe(TimeSpan.Zero, "Donetick adds the current UTC time of day");
    }

    [Fact]
    public async Task GetAllChoresAsync_RecordedWrongKeyResponse_ThrowsWithStatus401()
    {
        var act = () => Client(Recorded("wrong-api-key"), HttpStatusCode.Unauthorized)
            .GetAllChoresAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("KnownIssue", "F-26")]
    public async Task GetAllChoresAsync_LowerCaseResWrapper_ReturnsAnEmptyList()
    {
        var list = Recorded("list-chores");

        var chores = await Client($"{{\"res\":{list}}}").GetAllChoresAsync(TestContext.Current.CancellationToken);

        chores.Should().BeEmpty("the wrapper property is matched case-sensitively as 'Res'");
    }

    private static string Recorded(string name)
    {
        var path = Path.Combine(TestPaths.Fixtures, "donetick", "recorded", Version, name + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("body").GetRawText();
    }

    private static DonetickApiClient Client(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new FixedResponseHandler(body, status)) { BaseAddress = new Uri("http://donetick.test") },
            NullLogger<DonetickApiClient>.Instance);

    private sealed class FixedResponseHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

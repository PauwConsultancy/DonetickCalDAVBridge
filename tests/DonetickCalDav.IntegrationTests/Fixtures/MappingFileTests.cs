using System.Net;
using System.Text.Json;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Fixtures;

/// <summary>
/// The WireMock mapping files in tests/fixtures/donetick/mappings are also mounted into the L4 stub
/// container. These tests keep them loadable and consistent with <see cref="ReferenceData"/>.
/// </summary>
public class MappingFileTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    public async Task ReferenceMappings_ServeAllReferenceChoresToTheBridge()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();

        bridge.Cache.GetAllChores().Select(c => c.Chore.Id).Should().BeEquivalentTo(ReferenceData.AllIds);
        bridge.Cache.GetChore(ReferenceData.PayRent)!.Chore.LabelsV2!.Single().Name.Should().Be("Finance");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task ReferenceMappings_EchoEditsAndAcceptCompleteAndDelete()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var groceries = bridge.ResourcePath(ReferenceData.BuyGroceries);

        var edit = await client.PutAsync(groceries, Ics.VTodo(Ics.ChoreUid(ReferenceData.BuyGroceries), "Buy groceries today",
            description: "Milk", dueDate: new DateOnly(2025, 6, 17)));
        var get = await client.GetAsync(groceries);
        var complete = await client.PutAsync(bridge.ResourcePath(ReferenceData.WaterThePlants),
            Ics.VTodo(Ics.ChoreUid(ReferenceData.WaterThePlants), "Water the plants", status: "COMPLETED"));
        var delete = await client.DeleteAsync(bridge.ResourcePath(ReferenceData.CallTheDentist));

        edit.Status.Should().Be(HttpStatusCode.NoContent);
        Ics.Value(get.Body, "SUMMARY").Should().Be("Buy groceries today");
        Ics.Value(get.Body, "DESCRIPTION").Should().Be("Milk");
        Ics.Line(get.Body, "DUE").Should().Be("DUE:20250617T090000Z");
        complete.Status.Should().Be(HttpStatusCode.NoContent);
        delete.Status.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public void MappingFiles_AreValidJsonWithATitleAndTheTestApiKey()
    {
        var files = Directory.GetFiles(ReferenceData.MappingsFolder, "*.json");

        files.Should().HaveCountGreaterThanOrEqualTo(5);
        foreach (var file in files)
        {
            var mapping = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
            mapping.GetProperty("Title").GetString().Should().NotBeNullOrEmpty(file);
            mapping.GetProperty("Request").GetProperty("Headers")[0].GetProperty("Matchers")[0]
                .GetProperty("Pattern").GetString().Should().Be(DonetickStub.ApiKey, file);
        }
    }
}

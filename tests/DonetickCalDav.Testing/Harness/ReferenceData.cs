namespace DonetickCalDav.Testing.Harness;

/// <summary>
/// The chores in <c>tests/fixtures/donetick/mappings/list-chores.json</c>, the reference data set
/// shared by the integration tests (L2) and the container stub (L4). Field shapes are copied from
/// payloads recorded from Donetick v0.1.82; dates are fixed.
/// </summary>
public static class ReferenceData
{
    /// <summary>Daily, no labels, scheduled time 06:00Z in Europe/Amsterdam (RFC3339 in metadata).</summary>
    public const int WaterThePlants = 101;

    /// <summary>Monthly on the 1st, label Finance, priority 3.</summary>
    public const int PayRent = 102;

    /// <summary>One-off, label Home, description.</summary>
    public const int BuyGroceries = 103;

    /// <summary>One-off, labels Home and Outdoor, so it stays in the default list.</summary>
    public const int CleanTheGutters = 104;

    /// <summary>One-off, no due date, description.</summary>
    public const int CallTheDentist = 105;

    /// <summary>Monday and Thursday, label Home.</summary>
    public const int TakeOutTheBins = 106;

    /// <summary>The id the create mapping gives every new chore.</summary>
    public const int CreatedChoreId = 900;

    public static readonly int[] AllIds = [WaterThePlants, PayRent, BuyGroceries, CleanTheGutters, CallTheDentist, TakeOutTheBins];

    /// <summary>Folder with the WireMock mapping files.</summary>
    public static string MappingsFolder => Path.Combine(TestPaths.Fixtures, "donetick", "mappings");
}

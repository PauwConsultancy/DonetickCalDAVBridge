using System.Text.Json;
using System.Text.Json.Serialization;

namespace DonetickCalDav.Testing.Harness;

/// <summary>
/// A recorded (or reconstructed) client session for the L3 replay tests, stored as JSON under
/// <c>tests/fixtures/sessions/&lt;client&gt;/&lt;scenario&gt;.json</c>. Format: tests/fixtures/sessions/README.md.
/// </summary>
public sealed class ReplaySession
{
    public const string UserPlaceholder = "{user}";

    /// <summary>Scenario id from the testing strategy, for example RP-05.</summary>
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    /// <summary><c>recorded</c> (captured from a real client) or <c>synthetic</c> (reconstructed by hand).</summary>
    public string Source { get; init; } = "";

    /// <summary>The client and version, for example "Reminders, macOS 26.0".</summary>
    public string Client { get; init; } = "";

    public string? Recorded { get; init; }

    public string? Notes { get; init; }

    /// <summary>Bridge settings for the replay; only GroupByLabel matters today.</summary>
    public ReplayBridgeSettings Bridge { get; init; } = new();

    public List<ReplayExchange> Exchanges { get; init; } = [];

    /// <summary>Donetick calls the session must cause, checked after the replay.</summary>
    public List<ReplayDonetickExpectation> ExpectDonetick { get; init; } = [];

    /// <summary>Path of the file relative to the sessions folder, without extension (client/scenario).</summary>
    [JsonIgnore]
    public string Name { get; private set; } = "";

    public static string Folder => Path.Combine(TestPaths.Fixtures, "sessions");

    public static IEnumerable<string> All() =>
        Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "*.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(Folder, f)[..^".json".Length].Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
            : [];

    public static ReplaySession Load(string name)
    {
        var json = File.ReadAllText(Path.Combine(Folder, name + ".json"));
        var session = JsonSerializer.Deserialize<ReplaySession>(json, Options)
                      ?? throw new InvalidDataException($"Empty session file {name}.");
        session.Name = name;
        return session;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}

public sealed class ReplayBridgeSettings
{
    public bool GroupByLabel { get; init; }
}

public sealed class ReplayExchange
{
    public ReplayRequest Request { get; init; } = new();

    public ReplayResponse Response { get; init; } = new();
}

public sealed class ReplayRequest
{
    public string Method { get; init; } = "GET";

    /// <summary>Path with <c>{user}</c> for the CalDAV user name.</summary>
    public string Path { get; init; } = "/";

    /// <summary>Request headers without Authorization, Host and Content-Length.</summary>
    public Dictionary<string, string> Headers { get; init; } = new();

    /// <summary>Whether the client sent credentials; the runner adds the test credentials.</summary>
    public bool Authenticated { get; init; } = true;

    public string? Body { get; init; }
}

public sealed class ReplayResponse
{
    /// <summary>The status the client received when the session was recorded.</summary>
    public int Status { get; init; }
}

public sealed class ReplayDonetickExpectation
{
    public string Method { get; init; } = "";

    public string Path { get; init; } = "";

    public int Count { get; init; }
}

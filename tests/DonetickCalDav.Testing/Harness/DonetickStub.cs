using System.Text.Json;
using DonetickCalDav.Donetick.Models;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DonetickCalDav.Testing.Harness;

/// <summary>
/// The Donetick eAPI as a WireMock.Net server on a random local port. Every mapping requires the
/// <c>secretkey</c> header, so a request without the right key gets WireMock's 404 and fails the test.
/// </summary>
/// <remarks>
/// Use one instance per test class (<c>IClassFixture</c>) and call <see cref="Reset"/> before each
/// test. Mappings registered here have priority <see cref="NormalPriority"/>; faults are registered
/// with a higher priority, so they win while active and fall through to the normal mapping afterwards.
/// </remarks>
public sealed class DonetickStub : IDisposable
{
    public const string ApiKey = "test-api-key";
    public const string ChoresPath = "/eapi/v1/chore";

    private const int FaultPriority = 1;
    private const int NormalPriority = 10;

    // Fixed GUIDs, so a later call replaces the earlier mapping instead of adding a second one.
    private static readonly Guid ChoreListMapping = new("6f1d7a52-0c4e-4b8e-9d55-2f1c3a9e0001");
    private static readonly Guid CreateMapping = new("6f1d7a52-0c4e-4b8e-9d55-2f1c3a9e0002");

    private int _scenarioCounter;

    public DonetickStub()
    {
        Server = WireMockServer.Start();
    }

    public WireMockServer Server { get; }

    /// <summary>Base address of the stub, for <c>Donetick:BaseUrl</c>.</summary>
    public string Url => Server.Url!;

    /// <summary>Removes all mappings, scenarios and logged requests.</summary>
    public void Reset()
    {
        Server.Reset();
        Server.ResetScenarios();
    }

    /// <summary>Loads WireMock mapping files (JSON), as the L4 stub container does.</summary>
    public DonetickStub LoadMappings(string folder)
    {
        Server.ReadStaticMappings(folder);
        return this;
    }

    // ── Responses ───────────────────────────────────────────────────────────

    /// <summary><c>GET /eapi/v1/chore</c> returns these chores as a JSON array.</summary>
    public DonetickStub WithChores(params DonetickChore[] chores) =>
        WithChoreListJson(JsonSerializer.Serialize(chores));

    /// <summary><c>GET /eapi/v1/chore</c> returns this body, for wrapper or malformed shapes.</summary>
    public DonetickStub WithChoreListJson(string json, int statusCode = 200)
    {
        Server.Given(Request.Create().WithPath(ChoresPath).UsingGet().WithHeader("secretkey", ApiKey))
            .WithGuid(ChoreListMapping)
            .AtPriority(NormalPriority)
            .RespondWith(Json(statusCode, json));
        return this;
    }

    /// <summary><c>POST /eapi/v1/chore</c> returns <paramref name="created"/>.</summary>
    public DonetickStub OnCreate(DonetickChore created)
    {
        Server.Given(Request.Create().WithPath(ChoresPath).UsingPost().WithHeader("secretkey", ApiKey))
            .WithGuid(CreateMapping)
            .AtPriority(NormalPriority)
            .RespondWith(Json(200, JsonSerializer.Serialize(created)));
        return this;
    }

    /// <summary><c>PUT /eapi/v1/chore/{id}</c> returns <paramref name="updated"/>.</summary>
    public DonetickStub OnUpdate(DonetickChore updated)
    {
        Server.Given(Request.Create().WithPath($"{ChoresPath}/{updated.Id}").UsingPut().WithHeader("secretkey", ApiKey))
            .AtPriority(NormalPriority)
            .RespondWith(Json(200, JsonSerializer.Serialize(updated)));
        return this;
    }

    /// <summary><c>DELETE /eapi/v1/chore/{id}</c> succeeds.</summary>
    public DonetickStub OnDelete(int id)
    {
        Server.Given(Request.Create().WithPath($"{ChoresPath}/{id}").UsingDelete().WithHeader("secretkey", ApiKey))
            .AtPriority(NormalPriority)
            .RespondWith(Json(200, """{"message":"Chore deleted successfully"}"""));
        return this;
    }

    /// <summary><c>POST /eapi/v1/chore/{id}/complete</c> succeeds.</summary>
    public DonetickStub OnComplete(int id)
    {
        Server.Given(Request.Create().WithPath($"{ChoresPath}/{id}/complete").UsingPost().WithHeader("secretkey", ApiKey))
            .AtPriority(NormalPriority)
            .RespondWith(Json(200, """{"res":{}}"""));
        return this;
    }

    // ── Faults ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The next <paramref name="times"/> requests to <paramref name="method"/> <paramref name="path"/>
    /// get <paramref name="statusCode"/>; later requests fall through to the normal mapping.
    /// </summary>
    public DonetickStub FailNext(string method, string path, int times, int statusCode = 500)
    {
        var scenario = $"fault-{Interlocked.Increment(ref _scenarioCounter)}";
        for (var i = 0; i < times; i++)
        {
            var mapping = Server.Given(Request.Create().WithPath(path).UsingMethod(method))
                .AtPriority(FaultPriority)
                .InScenario(scenario);
            if (i > 0)
                mapping = mapping.WhenStateIs($"failed-{i}");
            mapping.WillSetStateTo($"failed-{i + 1}")
                .RespondWith(Response.Create().WithStatusCode(statusCode));
        }

        return this;
    }

    /// <summary>Every request to <paramref name="method"/> <paramref name="path"/> gets <paramref name="statusCode"/>.</summary>
    public DonetickStub FailAlways(string method, string path, int statusCode = 500)
    {
        Server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .AtPriority(FaultPriority)
            .RespondWith(Response.Create().WithStatusCode(statusCode));
        return this;
    }

    // ── Verification ────────────────────────────────────────────────────────

    /// <summary>Requests the stub received for <paramref name="method"/> <paramref name="path"/>, oldest first.</summary>
    public IReadOnlyList<IRequestMessage> Received(string method, string path) =>
        AllRequests
            .Where(r => r.Method.Equals(method, StringComparison.OrdinalIgnoreCase) && r.Path == path)
            .ToList();

    /// <summary>Number of requests the stub received for <paramref name="method"/> <paramref name="path"/>.</summary>
    public int Count(string method, string path) => Received(method, path).Count;

    /// <summary>All requests the stub received, oldest first.</summary>
    public IReadOnlyList<IRequestMessage> AllRequests =>
        Server.LogEntries
            .Select(e => e.RequestMessage)
            .OfType<IRequestMessage>()
            .OrderBy(r => r.DateTime)
            .ToList();

    public void Dispose() => Server.Stop();

    private static IResponseBuilder Json(int statusCode, string body) =>
        Response.Create()
            .WithStatusCode(statusCode)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithBody(body);
}

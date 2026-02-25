using System.Net;
using System.Text;
using System.Text.Json;
using DonetickCalDav.Donetick;
using DonetickCalDav.Donetick.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DonetickCalDav.Tests.Donetick;

public class DonetickApiClientTests
{
    // ── GetAllChoresAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAllChoresAsync_ArrayFormat_ReturnsChores()
    {
        var chores = new[] { new DonetickChore { Id = 1, Name = "Task 1" } };
        var json = JsonSerializer.Serialize(chores);
        var client = CreateClient(json);

        var result = await client.GetAllChoresAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Task 1");
    }

    [Fact]
    public async Task GetAllChoresAsync_WrapperFormat_ReturnsChores()
    {
        // Note: ChoreResponseWrapper.Res is Pascal-cased and System.Text.Json is case-sensitive
        var wrapper = new { Res = new[] { new DonetickChore { Id = 1, Name = "Wrapped" } } };
        var json = JsonSerializer.Serialize(wrapper);
        var client = CreateClient(json);

        var result = await client.GetAllChoresAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Wrapped");
    }

    [Fact]
    public async Task GetAllChoresAsync_EmptyArray_ReturnsEmptyList()
    {
        var client = CreateClient("[]");

        var result = await client.GetAllChoresAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllChoresAsync_HttpError_ThrowsHttpRequestException()
    {
        var client = CreateClient("", HttpStatusCode.InternalServerError);

        var act = () => client.GetAllChoresAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── CompleteChoreAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CompleteChoreAsync_Success_DoesNotThrow()
    {
        var client = CreateClient("", HttpStatusCode.OK);

        var act = () => client.CompleteChoreAsync(42);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CompleteChoreAsync_HttpError_ThrowsHttpRequestException()
    {
        var client = CreateClient("", HttpStatusCode.NotFound);

        var act = () => client.CompleteChoreAsync(999);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DonetickApiClient CreateClient(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(responseBody, statusCode);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return new DonetickApiClient(httpClient, NullLogger<DonetickApiClient>.Instance);
    }

    /// <summary>
    /// Minimal HttpMessageHandler that returns a fixed response for all requests.
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public FakeHttpMessageHandler(string responseBody, HttpStatusCode statusCode)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}

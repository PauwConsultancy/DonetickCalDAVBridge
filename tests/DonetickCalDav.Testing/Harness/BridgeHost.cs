using DonetickCalDav.Cache;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DonetickCalDav.Testing.Harness;

/// <summary>Settings for one bridge instance under test. Defaults match <c>appsettings.json</c>.</summary>
public sealed class BridgeOptions
{
    public string Username { get; set; } = BridgeHost.Username;
    public string Password { get; set; } = BridgeHost.Password;
    public string CalendarName { get; set; } = "DonetickTasks";
    public string CalendarColor { get; set; } = "#4A90D9FF";
    public string DefaultCalendarName { get; set; } = "General";
    public bool GroupByLabel { get; set; }
    public bool AllDayEvents { get; set; }
    public bool PreserveScheduledTime { get; set; }
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Wait for the background sync to finish its startup load before the test starts. Turn off to
    /// test startup behaviour itself.
    /// </summary>
    public bool WaitForInitialSync { get; set; } = true;

    /// <summary>Raw configuration overrides, applied last (for example an empty API key).</summary>
    public Dictionary<string, string?> Settings { get; } = new();

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
}

/// <summary>
/// The real application, hosted in the test process with <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// Donetick is replaced by a <see cref="DonetickStub"/> and the clock by a <see cref="TestTimeProvider"/>;
/// everything else (middleware, handlers, cache, HTTP client with its retry pipeline) is production code.
/// </summary>
public sealed class BridgeHost : IAsyncDisposable
{
    public const string Username = "tester";
    public const string Password = "correct-horse-battery";

    /// <summary>Delay before the first sync (<c>ChoreSyncService</c>).</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);

    /// <summary>Delays of the Polly retry pipeline for Donetick calls (<c>Program.cs</c>).</summary>
    public static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(40)];

    private readonly WebApplicationFactory<Program> _factory;

    private BridgeHost(DonetickStub donetick, BridgeOptions options)
    {
        Donetick = donetick;
        Options = options;
        Time = new TestTimeProvider();
        Logs = new LogCollector();
        var output = TestContext.Current.TestOutputHelper;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // A dedicated environment, so a developer's appsettings.Development.json or user secrets
            // cannot leak into the tests.
            builder.UseEnvironment("IntegrationTest");

            // UseSetting, not ConfigureAppConfiguration: Program.cs reads the Donetick address and
            // the listen port before Build(), and only UseSetting reaches those reads.
            foreach (var (key, value) in BuildSettings(donetick, options))
                builder.UseSetting(key, value);

            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new CollectingLoggerProvider(Logs, output));
            });

            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
        });
    }

    public DonetickStub Donetick { get; }

    public BridgeOptions Options { get; }

    public TestTimeProvider Time { get; }

    public LogCollector Logs { get; }

    public IServiceProvider Services => _factory.Services;

    public ChoreCache Cache => Services.GetRequiredService<ChoreCache>();

    /// <summary>
    /// Builds and starts the bridge. With <see cref="BridgeOptions.WaitForInitialSync"/> (the default)
    /// it returns once the startup load from Donetick has completed and the first poll is scheduled.
    /// </summary>
    public static async Task<BridgeHost> StartAsync(DonetickStub donetick, Action<BridgeOptions>? configure = null)
    {
        var options = new BridgeOptions();
        configure?.Invoke(options);

        var host = new BridgeHost(donetick, options);
        try
        {
            host.Start();
            if (options.WaitForInitialSync)
                await host.CompleteStartupAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>Starts the host without waiting for anything. Throws when the host fails to start.</summary>
    public void Start() => _ = _factory.Server;

    /// <summary>
    /// Moves the background sync through its startup delay and waits until the startup load and the
    /// first loop iteration are done, which is when the poll delay gets scheduled.
    /// </summary>
    public async Task CompleteStartupAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await Time.AdvanceWhenScheduledAsync(StartupDelay, ct: ct);
        await Time.WaitForTimerAsync(Options.PollInterval, ct: ct);
    }

    /// <summary>Lets one poll interval pass and waits until the next poll is scheduled.</summary>
    public async Task RunPollAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await Time.AdvanceWhenScheduledAsync(Options.PollInterval, ct: ct);
        await Time.WaitForTimerAsync(Options.PollInterval, ct: ct);
    }

    /// <summary>A CalDAV client for the configured user. Redirects are not followed.</summary>
    public CalDavClient CreateClient() =>
        new(CreateHttpClient(), Options.Username, Options.Password);

    /// <summary>A raw HTTP client on the test server. Redirects are not followed.</summary>
    public HttpClient CreateHttpClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public string ResourcePath(int choreId, string calendar = "tasks") =>
        Ics.ResourcePath(Options.Username, choreId, calendar);

    public string CalendarPath(string calendar = "tasks") =>
        $"/caldav/calendars/{Options.Username}/{calendar}/";

    public string HomePath => $"/caldav/calendars/{Options.Username}/";

    public string PrincipalPath => $"/caldav/principals/{Options.Username}/";

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static Dictionary<string, string?> BuildSettings(DonetickStub donetick, BridgeOptions options)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Donetick:BaseUrl"] = donetick.Url,
            ["Donetick:ApiKey"] = DonetickStub.ApiKey,
            ["Donetick:PollIntervalSeconds"] = options.PollIntervalSeconds.ToString(),
            ["CalDav:Username"] = options.Username,
            ["CalDav:Password"] = options.Password,
            ["CalDav:CalendarName"] = options.CalendarName,
            ["CalDav:CalendarColor"] = options.CalendarColor,
            ["CalDav:DefaultCalendarName"] = options.DefaultCalendarName,
            ["CalDav:GroupByLabel"] = options.GroupByLabel.ToString(),
            ["CalDav:AllDayEvents"] = options.AllDayEvents.ToString(),
            ["CalDav:PreserveScheduledTime"] = options.PreserveScheduledTime.ToString(),
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Microsoft"] = "Warning",
            ["Logging:LogLevel:Polly"] = "Warning",
            ["Logging:LogLevel:DonetickCalDav"] = "Debug",
        };

        foreach (var (key, value) in options.Settings)
            settings[key] = value;

        return settings;
    }
}

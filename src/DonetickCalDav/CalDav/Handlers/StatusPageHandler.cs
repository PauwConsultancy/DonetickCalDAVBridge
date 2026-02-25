using System.Text;
using System.Web;
using DonetickCalDav.Cache;
using DonetickCalDav.Configuration;
using Microsoft.Extensions.Options;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Renders a lightweight HTML status page for the health check endpoint.
/// Shows version, uptime, cache statistics, client activity, and connection info.
/// </summary>
public sealed class StatusPageHandler
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    private readonly ChoreCache _cache;
    private readonly ClientTracker _clientTracker;
    private readonly AppSettings _settings;
    private readonly string _version;

    public StatusPageHandler(ChoreCache cache, ClientTracker clientTracker,
        IOptions<AppSettings> settings, string version)
    {
        _cache = cache;
        _clientTracker = clientTracker;
        _settings = settings.Value;
        _version = version;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var uptime = DateTime.UtcNow - StartTime;
        var chores = _cache.GetAllChores();
        var activeCount = chores.Count(c => c.Chore.IsActive);
        var inactiveCount = chores.Count - activeCount;
        var withDueDate = chores.Count(c => c.Chore.NextDueDate.HasValue);
        var recurringCount = chores.Count(c => c.Chore.FrequencyType != "no_repeat"
                                               && c.Chore.FrequencyType != "once"
                                               && c.Chore.FrequencyType != "trigger");
        var scheme = _settings.CalDav.Tls != null ? "HTTPS" : "HTTP";
        var activeClients = _clientTracker.GetActiveClients(TimeSpan.FromHours(1));
        var totalRequests = _clientTracker.TotalRequests;

        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Donetick CalDAV Bridge</title>
                <style>
                    :root { --bg: #0f1117; --card: #1a1d27; --border: #2a2d3a; --text: #e4e4e7;
                            --muted: #71717a; --accent: #6366f1; --green: #22c55e; --amber: #f59e0b; }
                    * { margin: 0; padding: 0; box-sizing: border-box; }
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
                           background: var(--bg); color: var(--text); min-height: 100vh;
                           display: flex; align-items: center; justify-content: center; padding: 1.5rem; }
                    .container { width: 100%; max-width: 540px; }
                    .header { text-align: center; margin-bottom: 2rem; }
                    .header h1 { font-size: 1.5rem; font-weight: 600; letter-spacing: -0.025em; }
                    .header .version { color: var(--muted); font-size: 0.875rem; margin-top: 0.25rem; }
                    .status-badge { display: inline-flex; align-items: center; gap: 0.5rem;
                                    background: rgba(34,197,94,0.1); border: 1px solid rgba(34,197,94,0.2);
                                    border-radius: 9999px; padding: 0.375rem 1rem; margin-top: 1rem;
                                    font-size: 0.8125rem; color: var(--green); }
                    .status-badge .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--green);
                                         animation: pulse 2s ease-in-out infinite; }
                    @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
                    .card { background: var(--card); border: 1px solid var(--border); border-radius: 0.75rem;
                            margin-bottom: 1rem; overflow: hidden; }
                    .card-title { font-size: 0.75rem; font-weight: 500; text-transform: uppercase;
                                  letter-spacing: 0.05em; color: var(--muted); padding: 1rem 1.25rem 0.5rem; }
                    .row { display: flex; justify-content: space-between; align-items: center;
                           padding: 0.625rem 1.25rem; border-top: 1px solid var(--border); }
                    .row:first-of-type { border-top: none; }
                    .row .label { color: var(--muted); font-size: 0.875rem; }
                    .row .value { font-size: 0.875rem; font-variant-numeric: tabular-nums; }
                    .stats-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem;
                                  padding: 1rem 1.25rem; }
                    .stat { text-align: center; }
                    .stat .number { font-size: 1.75rem; font-weight: 700; letter-spacing: -0.05em;
                                    color: var(--accent); }
                    .stat .desc { font-size: 0.75rem; color: var(--muted); margin-top: 0.125rem; }
                    .client { padding: 0.75rem 1.25rem; border-top: 1px solid var(--border); }
                    .client:first-of-type { border-top: none; }
                    .client-header { display: flex; justify-content: space-between; align-items: center; }
                    .client-name { font-size: 0.875rem; font-weight: 500; }
                    .client-badge { font-size: 0.6875rem; padding: 0.125rem 0.5rem; border-radius: 9999px;
                                    font-variant-numeric: tabular-nums; }
                    .client-badge.active { background: rgba(34,197,94,0.1); color: var(--green); }
                    .client-badge.idle { background: rgba(245,158,11,0.1); color: var(--amber); }
                    .client-meta { font-size: 0.75rem; color: var(--muted); margin-top: 0.25rem; }
                    .no-clients { padding: 1.25rem; text-align: center; color: var(--muted); font-size: 0.875rem; }
                    .footer { text-align: center; color: var(--muted); font-size: 0.75rem; margin-top: 1.5rem; }
                    .footer a { color: var(--accent); text-decoration: none; }
                    .footer a:hover { text-decoration: underline; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="header">
                        <h1>Donetick CalDAV Bridge</h1>
                        <div class="version">v{{_version}}</div>
                        <div class="status-badge"><span class="dot"></span> Running</div>
                    </div>

                    <div class="card">
                        <div class="card-title">Cache</div>
                        <div class="stats-grid">
                            <div class="stat">
                                <div class="number">{{chores.Count}}</div>
                                <div class="desc">Total chores</div>
                            </div>
                            <div class="stat">
                                <div class="number">{{activeCount}}</div>
                                <div class="desc">Active</div>
                            </div>
                            <div class="stat">
                                <div class="number">{{recurringCount}}</div>
                                <div class="desc">Recurring</div>
                            </div>
                            <div class="stat">
                                <div class="number">{{withDueDate}}</div>
                                <div class="desc">With due date</div>
                            </div>
                        </div>
                    </div>

                    <div class="card">
                        <div class="card-title">Server</div>
                        <div class="row">
                            <span class="label">Uptime</span>
                            <span class="value">{{FormatUptime(uptime)}}</span>
                        </div>
                        <div class="row">
                            <span class="label">Protocol</span>
                            <span class="value">{{scheme}}</span>
                        </div>
                        <div class="row">
                            <span class="label">Port</span>
                            <span class="value">{{_settings.CalDav.ListenPort}}</span>
                        </div>
                        <div class="row">
                            <span class="label">Poll interval</span>
                            <span class="value">{{_settings.Donetick.PollIntervalSeconds}}s</span>
                        </div>
                    </div>

                    <div class="card">
                        <div class="card-title">Clients (last hour)</div>
                        {{RenderClients(activeClients, totalRequests)}}
                    </div>

                    <div class="card">
                        <div class="card-title">Connection</div>
                        <div class="row">
                            <span class="label">Donetick API</span>
                            <span class="value">{{_settings.Donetick.BaseUrl}}</span>
                        </div>
                        <div class="row">
                            <span class="label">CalDAV user</span>
                            <span class="value">{{_settings.CalDav.Username}}</span>
                        </div>
                        <div class="row">
                            <span class="label">Calendar</span>
                            <span class="value">{{_settings.CalDav.CalendarName}}</span>
                        </div>
                    </div>

                    <div class="footer">
                        Donetick CalDAV Bridge &mdash;
                        <a href="https://donetick.com" target="_blank" rel="noopener">donetick.com</a>
                    </div>
                </div>
            </body>
            </html>
            """;

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync(html, Encoding.UTF8);
    }

    private static string RenderClients(List<ClientSnapshot> clients, long totalRequests)
    {
        if (clients.Count == 0)
            return """<div class="no-clients">No clients connected in the last hour</div>""";

        var sb = new StringBuilder();
        foreach (var client in clients)
        {
            var ago = FormatTimeAgo(DateTime.UtcNow - client.LastSeen);
            var isActive = (DateTime.UtcNow - client.LastSeen).TotalMinutes < 5;
            var badgeClass = isActive ? "active" : "idle";
            var badgeText = isActive ? "active" : "idle";
            var name = HttpUtility.HtmlEncode(client.DisplayName);

            sb.AppendLine($"""
                <div class="client">
                    <div class="client-header">
                        <span class="client-name">{name}</span>
                        <span class="client-badge {badgeClass}">{badgeText}</span>
                    </div>
                    <div class="client-meta">{client.RequestCount:N0} requests &middot; last seen {ago}</div>
                </div>
            """);
        }

        sb.AppendLine($"""
            <div class="row" style="border-top: 1px solid var(--border);">
                <span class="label">Total requests</span>
                <span class="value">{totalRequests:N0}</span>
            </div>
        """);

        return sb.ToString();
    }

    private static string FormatUptime(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1)
            return $"{ts.Hours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    private static string FormatTimeAgo(TimeSpan ts)
    {
        if (ts.TotalSeconds < 10)
            return "just now";
        if (ts.TotalMinutes < 1)
            return $"{(int)ts.TotalSeconds}s ago";
        if (ts.TotalHours < 1)
            return $"{(int)ts.TotalMinutes}m ago";
        return $"{(int)ts.TotalHours}h {ts.Minutes}m ago";
    }
}

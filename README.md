# Donetick CalDAV Bridge

A lightweight bridge that exposes [Donetick](https://donetick.com) tasks as CalDAV VTODO items, making them accessible in Apple Reminders, Thunderbird, and other CalDAV-compatible clients.

## Vibe code warming
I let Claude code write the whole project. I did some checks and balances, but I wouldn't trust exposing this to the internet. If you only run it internal or via something like Tailscale you should be kinda fine.
Make backups! Don't come complaining to me if something gets f'ed up.
I made it for my own needs and if you see benefits from it feel free to use it also.
If you have improvements please share them.

## Features

- **Read & write** — view, create, update, complete, and delete tasks from your CalDAV client
- **Labels as lists** — optionally split Donetick labels into separate CalDAV calendars (see [Group by Label](#group-by-label))
- **Background sync** — periodically polls the Donetick API and keeps an in-memory cache
- **Apple-compatible** — follows Apple's CalDAV discovery flow (`.well-known`, PROPFIND at multiple depths)
- **Optional HTTPS** — built-in TLS support for Apple Calendar/Reminders (requires HTTPS for Basic Auth)
- **Docker-ready** — multi-stage Dockerfile included, designed for Unraid / self-hosted setups

## How It Works

```
┌──────────────┐       CalDAV        ┌──────────────────┐      REST API      ┌──────────┐
│ Apple         │ ◄──────────────► │ Donetick CalDAV   │ ◄──────────────► │ Donetick │
│ Reminders     │   VTODO/PROPFIND  │ Bridge            │   /eapi/v1/chore  │ Server   │
└──────────────┘                    └──────────────────┘                    └──────────┘
```

Tasks appear in **Apple Reminders** (not Calendar.app) because VTODO items are handled by the Reminders app on macOS/iOS.

> **Important:** Apple Calendar and Reminders require HTTPS to send Basic Auth credentials. See the [HTTPS Setup](#https-setup) section below.

## Quick Start (Docker Compose)

1. Copy the example compose file and edit the configuration:

```bash
cp docker-compose.yml.example docker-compose.yml
```

2. Edit `docker-compose.yml` with your values:

```yaml
services:
  donetick-caldav:
    environment:
      - Donetick__BaseUrl=http://your-donetick-instance:8080
      - Donetick__ApiKey=your-api-key-here
      - CalDav__Username=your-username
      - CalDav__Password=a-strong-password
```

3. Start the container:

```bash
docker compose up -d
```

4. Add the CalDAV account on macOS:

   - Go to **System Settings → Internet Accounts → Add Account → Other → CalDAV**
   - Choose **Manual** setup and enter:

   | Field    | Value                                        |
   |----------|----------------------------------------------|
   | Type     | CalDAV (Manual)                              |
   | Server   | `https://your-server`                        |
   | Username | Value of `CalDav__Username`                  |
   | Password | Value of `CalDav__Password`                  |

5. Enable Reminders for the account:

   - Go to **System Settings → Internet Accounts** and select the newly created account
   - **Enable "Reminders"** — this is required, since Donetick tasks are VTODO items
   - You can **disable "Calendars"** — this bridge does not serve calendar events, and Calendar.app may show an error if left enabled

   Your Donetick tasks will appear in the **Reminders** app. Tasks with a due date will also show up in **Calendar.app** automatically.

## Group by Label

By default, all Donetick tasks appear in a single CalDAV list. If you use labels in Donetick to organise your tasks, you can enable **Group by Label** mode to create a separate list per label in Apple Reminders.

### Why separate lists instead of tags?

Apple Reminders supports tags (hashtags) natively, but **only for iCloud-based lists**. For third-party CalDAV accounts, Apple ignores both the standard iCalendar `CATEGORIES` property and the proprietary `X-APPLE-HASHTAGS` extension. This is an Apple limitation that affects all CalDAV servers, not just this bridge.

Splitting tasks into separate CalDAV calendar collections per label is the only reliable way to visually group tasks by label in Apple Reminders and Calendar.app.

> **Note:** The bridge still emits both `CATEGORIES` and `X-APPLE-HASHTAGS` in the VTODO output. Non-Apple clients like Thunderbird, DAVx5, and Tasks.org do render these as tags/categories.

### How it works

| Situation | List |
|-----------|------|
| Task with **exactly 1 label** | Appears in that label's list (e.g. "Werk") |
| Task with **no labels** | Appears in the default list |
| Task with **2+ labels** | Appears in the default list (to avoid duplicates) |

Tasks never appear in more than one list, so there are no confusing duplicates.

### Configuration

Enable with two environment variables:

```yaml
environment:
  - CalDav__GroupByLabel=true
  - CalDav__DefaultCalendarName=Algemeen   # optional, default: "Algemeen"
```

- `GroupByLabel` — set to `true` to enable separate lists per label
- `DefaultCalendarName` — display name for the catch-all list (tasks with 0 or 2+ labels)

When `GroupByLabel` is `false` (default), all tasks appear in a single list named after `CalDav__CalendarName`.

### Label to list mapping

Each label becomes a CalDAV calendar with:
- **Slug** — URL-safe version of the label name (e.g. "Privé taken" → `prive-taken`)
- **Display name** — the original label name as shown in Apple Reminders
- **Colour** — automatically generated per label (deterministic, based on label name)

The default list keeps the configured `CalDav__CalendarColor`.

### Limitations

- **One-way** — labels are read from Donetick; you cannot assign labels from Apple Reminders (the Donetick eAPI does not support label management)
- **New tasks** — tasks created via Apple Reminders always appear in the default list, because the eAPI cannot set labels on create

## HTTPS Setup

Apple Calendar and Reminders **will not send Basic Auth credentials over plain HTTP**. You must enable HTTPS using one of the methods below.

### Option 1: Tailscale (recommended for self-hosted)

If you run Tailscale

```bash
tailscale serve --bg http://localhost:5232
```
Then use `https://your-hostname.ts.net` as the server URL.

or you can generate trusted certs for your machine:

```bash
tailscale cert your-hostname.ts.net
```

This creates `your-hostname.ts.net.crt` and `your-hostname.ts.net.key`. Mount them into the container:

```yaml
services:
  donetick-caldav:
    volumes:
      - /path/to/certs:/certs:ro
    environment:
      - CalDav__Tls__CertPath=/certs/your-hostname.ts.net.crt
      - CalDav__Tls__KeyPath=/certs/your-hostname.ts.net.key
```

Then use `https://your-hostname.ts.net:5232` as the server URL.

### Option 2: PFX certificate

If you have a `.pfx` file (e.g. from Let's Encrypt via Certbot):

```yaml
environment:
  - CalDav__Tls__PfxPath=/certs/certificate.pfx
  - CalDav__Tls__PfxPassword=optional-password
```

### Option 3: Reverse proxy

Place a reverse proxy (Caddy, nginx, Traefik) in front of the bridge to handle TLS termination. In this case, leave TLS disabled in the bridge and configure the proxy to forward traffic to `http://donetick-caldav:5232`.

### Running without HTTPS

For non-Apple clients (Thunderbird, GNOME Calendar, etc.) that support Basic Auth over HTTP, HTTPS is not required. The bridge works on plain HTTP by default when no TLS settings are configured.

## Configuration

All settings are configured via environment variables:

| Variable                        | Default                | Description                              |
|---------------------------------|------------------------|------------------------------------------|
| `Donetick__BaseUrl`             | `http://localhost:8080`| URL of your Donetick instance            |
| `Donetick__ApiKey`              | *(required)*           | External API key from Donetick settings  |
| `Donetick__PollIntervalSeconds` | `30`                   | How often to poll Donetick for changes   |
| `CalDav__Username`              | `user`                 | Username for CalDAV authentication       |
| `CalDav__Password`              | `pass`                 | Password for CalDAV authentication       |
| `CalDav__CalendarName`          | `DonetickTasks`        | Display name of the calendar             |
| `CalDav__CalendarColor`         | `#4A90D9FF`            | Calendar color (RGBA hex)                |
| `CalDav__GroupByLabel`          | `false`                | Split labels into separate lists         |
| `CalDav__DefaultCalendarName`   | `Algemeen`             | Default list name (when GroupByLabel=true)|
| `CalDav__ListenPort`            | `5232`                 | Port the bridge listens on               |
| `CalDav__Tls__CertPath`         | *(none)*               | PEM certificate file path (enables HTTPS)|
| `CalDav__Tls__KeyPath`          | *(none)*               | PEM private key file path                |
| `CalDav__Tls__PfxPath`          | *(none)*               | PFX certificate file path (alternative)  |
| `CalDav__Tls__PfxPassword`      | *(none)*               | PFX file password (if encrypted)         |
| `Logging__LogLevel__DonetickCalDav` | `Information`     | Log level for the bridge (`Debug` for verbose) |

## Logging

By default the bridge logs business-level events (task created, completed, deleted) at **Information** level. Protocol-level detail (every CalDAV request, auth challenges, XML parsing) is logged at **Debug** level and hidden by default.

To enable verbose logging for troubleshooting, set the log level via environment variable:

```yaml
environment:
  # Show all debug logs from the bridge
  - Logging__LogLevel__DonetickCalDav=Debug
  # Or enable debug for everything (very noisy)
  - Logging__LogLevel__Default=Debug
```

### Docker log rotation

Docker's default `json-file` log driver does **not** rotate logs. Without configuration, container logs grow indefinitely. Add a logging section to your `docker-compose.yml`:

```yaml
services:
  donetick-caldav:
    logging:
      driver: json-file
      options:
        max-size: "10m"    # Rotate after 10 MB
        max-file: "3"      # Keep 3 files (30 MB total)
```

Alternatively, configure log rotation globally in `/etc/docker/daemon.json`:

```json
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  }
}
```

## Building from Source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
cd src/DonetickCalDav
dotnet run
```

Or build and run the Docker image directly:

```bash
docker build -t donetick-caldav .
docker run -p 5232:5232 \
  -e Donetick__BaseUrl=http://your-donetick:8080 \
  -e Donetick__ApiKey=your-key \
  -e CalDav__Username=user \
  -e CalDav__Password=changeme \
  donetick-caldav
```

## Project Structure

```
src/DonetickCalDav/
├── Configuration/       # Strongly-typed settings with validation annotations
├── Donetick/
│   ├── Models/          # Donetick API data models
│   └── DonetickApiClient.cs
├── Cache/
│   ├── ChoreCache.cs    # Thread-safe in-memory cache with CTag/ETag
│   └── ChoreSyncService.cs  # Background polling service
└── CalDav/
    ├── Xml/             # DAV/CalDAV XML reading and writing
    ├── VTodo/           # VTODO ↔ Donetick mapping (status, priority, recurrence)
    ├── Handlers/        # One handler per HTTP method (PROPFIND, REPORT, GET, PUT, DELETE, etc.)
    │   └── CalendarResolver.cs  # Virtual calendar logic for GroupByLabel mode
    └── Middleware/       # Request routing and Basic Auth
```

## Limitations

- **In-memory cache only** — there is no persistent storage; the cache rebuilds on restart
- **External API constraints** — Donetick's eAPI only supports setting name, description, and due date on create/update. Priority, labels, and recurrence must be managed in Donetick directly.
- **Single-user** — designed for a single Donetick account; all CalDAV clients share the same task list
- **Apple tag limitation** — Apple Reminders does not display `CATEGORIES` or `X-APPLE-HASHTAGS` from CalDAV accounts (only iCloud). The `GroupByLabel` feature works around this by using separate calendar collections.

## Backlog

Planned features and improvements (contributions welcome):

- [ ] **All-day events** — option to emit tasks without a specific time (DATE instead of DATE-TIME), so they appear as all-day items in Calendar.app instead of at a specific hour
- [ ] **Preserve original scheduled time** — option to keep the originally configured due time on recurring tasks after completion. Currently, when you complete a recurring task (e.g. "Shave" scheduled daily at 08:00) at a different time (e.g. 10:00), Donetick advances the next due date using the completion time, causing all future occurrences to show at 10:00 instead of the intended 08:00
- [ ] **Unraid template** — create an Unraid Community Applications XML template for easy installation via the Unraid app store

## License

MIT

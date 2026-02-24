# Donetick CalDAV Bridge

A lightweight bridge that exposes [Donetick](https://donetick.com) tasks as CalDAV VTODO items, making them accessible in Apple Reminders, Thunderbird, and other CalDAV-compatible clients.

## Features

- **Read & write** — view, create, update, complete, and delete tasks from your CalDAV client
- **Recurring tasks** — Donetick recurrence patterns are mapped to iCalendar RRULE
- **Background sync** — periodically polls the Donetick API and keeps an in-memory cache
- **Apple-compatible** — follows Apple's CalDAV discovery flow (`.well-known`, PROPFIND at multiple depths)
- **Docker-ready** — multi-stage Dockerfile included, designed for Unraid / self-hosted setups

## How It Works

```
┌──────────────┐       CalDAV        ┌──────────────────┐      REST API      ┌──────────┐
│ Apple         │ ◄──────────────► │ Donetick CalDAV   │ ◄──────────────► │ Donetick │
│ Reminders     │   VTODO/PROPFIND  │ Bridge            │   /eapi/v1/chore  │ Server   │
└──────────────┘                    └──────────────────┘                    └──────────┘
```

Tasks appear in **Apple Reminders** (not Calendar.app) because VTODO items are handled by the Reminders app on macOS/iOS.

## Quick Start (Docker Compose)

1. Copy the example compose file and edit the configuration:

```bash
cp docker-compose.yml docker-compose.override.yml
```

2. Edit `docker-compose.override.yml` with your values:

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

4. Add the account in Apple Reminders (or any CalDAV client):

| Field    | Value                                    |
|----------|------------------------------------------|
| Type     | CalDAV (Manual)                          |
| Server   | `http://your-server:5232`                |
| Username | Value of `CalDav__Username`              |
| Password | Value of `CalDav__Password`              |

## Configuration

All settings are configured via environment variables:

| Variable                       | Default           | Description                              |
|--------------------------------|-------------------|------------------------------------------|
| `Donetick__BaseUrl`            | `http://localhost:8080` | URL of your Donetick instance       |
| `Donetick__ApiKey`             | *(required)*      | External API key from Donetick settings  |
| `Donetick__PollIntervalSeconds`| `30`              | How often to poll Donetick for changes   |
| `CalDav__Username`             | `user`            | Username for CalDAV authentication       |
| `CalDav__Password`             | `pass`            | Password for CalDAV authentication       |
| `CalDav__CalendarName`         | `Donetick Tasks`  | Display name of the calendar             |
| `CalDav__CalendarColor`        | `#4A90D9FF`       | Calendar color (RGBA hex)                |
| `CalDav__ListenPort`           | `5232`            | Port the bridge listens on               |

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
    └── Middleware/       # Request routing and Basic Auth
```

## Limitations

- **In-memory cache only** — there is no persistent storage; the cache rebuilds on restart
- **External API constraints** — Donetick's eAPI only supports setting name, description, and due date on create/update. Priority, labels, and recurrence must be managed in Donetick directly.
- **Single-user** — designed for a single Donetick account; all CalDAV clients share the same task list

## License

MIT

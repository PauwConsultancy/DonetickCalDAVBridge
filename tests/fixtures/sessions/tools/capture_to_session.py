"""Converts a capture of CalDAV traffic into a sanitised replay session (tests/fixtures/sessions).

    python tests/fixtures/sessions/tools/capture_to_session.py capture.flows \
        --user alice --id RP-05 --title "Complete a recurring task" \
        --client "Reminders, macOS 26.1" --output tests/fixtures/sessions/apple-reminders-macos/rp-05.json

Record with mitmproxy as a reverse proxy in front of the bridge (docs/testing/apple-recording.md) and
pass the flow file it writes with -w (needs the mitmproxy package). A HAR file works too, but
mitmproxy's HAR export leaves out the bodies of PROPFIND and REPORT requests, so the converter refuses
a HAR whose bodies are missing. The converter keeps only CalDAV requests (/.well-known/caldav and
/caldav/...), in order, and sanitises them:

- drops Authorization, cookies, Host, connection and proxy headers; records only whether credentials were sent
- replaces the recorded user name with {user} in paths, headers and bodies
- reduces absolute URLs in Destination headers to paths
- replaces client-generated UUIDs with stable fake ones (first seen = ...0001), keeping the UUID shape the
  bridge's UID map recognises
- maps chore ids with --id-map 17=101 when the recording was not made against the reference data

Donetick traffic is not in the capture, so add "expectDonetick" entries by hand after converting.
Review the output before committing: it must not contain personal data.
"""

from __future__ import annotations

import argparse
import base64
import json
import re
import sys
from datetime import date
from pathlib import Path
from urllib.parse import unquote, urlsplit

DROP_HEADERS = {
    "authorization", "cookie", "host", "content-length", "connection", "keep-alive", "accept-encoding",
    "proxy-connection", "proxy-authorization", "te", "upgrade", "transfer-encoding",
}
UUID = re.compile(r"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")
CHORE = re.compile(r"donetick-(\d+)")


class Sanitiser:
    def __init__(self, user: str, id_map: dict[str, str]):
        self.user = user
        self.id_map = id_map
        self.uuids: dict[str, str] = {}

    def text(self, value: str) -> str:
        value = value.replace(self.user, "{user}")
        value = UUID.sub(self._uuid, value)
        return CHORE.sub(lambda m: f"donetick-{self.id_map.get(m.group(1), m.group(1))}", value)

    def _uuid(self, match: re.Match) -> str:
        key = match.group(0).upper()
        if key not in self.uuids:
            self.uuids[key] = f"00000000-0000-4000-8000-{len(self.uuids) + 1:012d}"
        return self.uuids[key]


def read_har(path: Path) -> list[tuple[str, str, list[tuple[str, str]], str | None, int]]:
    """(method, url, headers, body, status) per request in a HAR file."""
    exchanges = []
    for entry in json.loads(path.read_text(encoding="utf-8"))["log"]["entries"]:
        request = entry["request"]
        post = request.get("postData") or {}
        body = post.get("text") or None
        if body is not None and post.get("encoding") == "base64":
            body = base64.b64decode(body).decode("utf-8")
        if body is None and request.get("bodySize", 0) > 0:
            raise SystemExit(f"{request['method']} {request['url']} has a body that the HAR file does not contain; "
                             "convert the mitmproxy flow file (-w) instead.")
        headers = [(h["name"], h["value"]) for h in request["headers"]]
        exchanges.append((request["method"], request["url"], headers, body, entry["response"]["status"]))
    return exchanges


def read_flows(path: Path) -> list[tuple[str, str, list[tuple[str, str]], str | None, int]]:
    """(method, url, headers, body, status) per request in a mitmproxy flow file."""
    from mitmproxy import http, io  # only needed for flow files

    exchanges = []
    with path.open("rb") as file:
        for flow in io.FlowReader(file).stream():
            if not isinstance(flow, http.HTTPFlow) or flow.response is None:
                continue
            body = flow.request.get_text(strict=False) or None
            headers = list(flow.request.headers.items(multi=True))
            exchanges.append((flow.request.method, flow.request.url, headers, body, flow.response.status_code))
    return exchanges


def convert(captured: list[tuple[str, str, list[tuple[str, str]], str | None, int]], args: argparse.Namespace) -> dict:
    sanitiser = Sanitiser(args.user, dict(pair.split("=", 1) for pair in args.id_map))
    exchanges = []
    for method, raw_url, raw_headers, body, status in captured:
        url = urlsplit(raw_url)
        path = unquote(url.path) + (f"?{url.query}" if url.query else "")
        if not (path.startswith("/caldav") or path.startswith("/.well-known/caldav")):
            continue

        headers = {}
        authenticated = False
        for name, value in raw_headers:
            lower = name.lower()
            if lower == "authorization":
                authenticated = True
            if lower in DROP_HEADERS or lower.startswith((":", "x-forwarded", "proxy-")):
                continue
            if lower == "destination":
                value = urlsplit(value).path
            headers[canonical(name)] = sanitiser.text(value)

        exchanges.append({
            "request": {
                "method": method,
                "path": sanitiser.text(path),
                "headers": headers,
                "authenticated": authenticated,
                "body": sanitiser.text(body) if body is not None else None,
            },
            "response": {"status": status},
        })

    if not exchanges:
        raise SystemExit("No CalDAV requests found in the capture.")

    return {
        "id": args.id,
        "title": args.title,
        "source": "recorded",
        "client": args.client,
        "recorded": args.recorded or date.today().isoformat(),
        "notes": args.notes,
        "bridge": {"groupByLabel": args.group_by_label},
        "exchanges": exchanges,
        "expectDonetick": [],
    }


def canonical(name: str) -> str:
    """HTTP/2 captures have lower-case header names; restore the usual spelling."""
    return "-".join(part[:1].upper() + part[1:] for part in name.split("-")) if name.islower() else name


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("capture", type=Path, help="mitmproxy flow file (-w) or HAR file (.har)")
    parser.add_argument("--user", required=True, help="CalDAV user name used during the recording")
    parser.add_argument("--id", required=True, help="scenario id, for example RP-05")
    parser.add_argument("--title", required=True)
    parser.add_argument("--client", required=True, help="client and OS version, for example 'Reminders, macOS 26.1'")
    parser.add_argument("--recorded", help="recording date (default: today)")
    parser.add_argument("--notes")
    parser.add_argument("--group-by-label", action="store_true", help="the bridge ran with GroupByLabel=true")
    parser.add_argument("--id-map", nargs="*", default=[], metavar="RECORDED=REFERENCE", help="chore id mapping")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    captured = read_har(args.capture) if args.capture.suffix.lower() == ".har" else read_flows(args.capture)
    session = convert(captured, args)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(session, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(session['exchanges'])} exchanges written to {args.output}", file=sys.stderr)


if __name__ == "__main__":
    main()

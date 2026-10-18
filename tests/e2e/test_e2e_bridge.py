"""L4 end-to-end tests (E2E-01 to E2E-11): the bridge as deployed, against the Donetick stub with the
reference data, driven by the Python caldav library as an independent CalDAV client over HTTPS.

The stub serves tests/fixtures/donetick/mappings: chores 101 to 106, every create returns chore 900.
Tests that restart or stop services come last in this file.
"""

from __future__ import annotations

import json
import time
import xml.etree.ElementTree as ET
from datetime import date, datetime, timezone

import pytest

import environment as env

pytestmark = pytest.mark.e2e

DAV = "{DAV:}"
REFERENCE_IDS = [101, 102, 103, 104, 105, 106]
PROPFIND_ETAGS = '<?xml version="1.0"?><d:propfind xmlns:d="DAV:"><d:prop><d:getetag/></d:prop></d:propfind>'


def todos_by_uid(calendar) -> dict[str, object]:
    return {str(t.icalendar_component["uid"]): t for t in calendar.todos(include_completed=True)}


def etags(bridge) -> dict[str, str]:
    response = bridge.request("PROPFIND", bridge.calendar(), data=PROPFIND_ETAGS, headers={"Depth": "1"})
    assert response.status_code == 207, response.text
    root = ET.fromstring(response.content)
    result = {}
    for item in root.iter(f"{DAV}response"):
        etag = item.find(f".//{DAV}getetag")
        if etag is not None and etag.text:  # the collection reports getetag as not found
            result[item.findtext(f"{DAV}href")] = etag.text
    return result


def wait_for_reference_data(bridge, timeout: float = 30) -> dict[str, str]:
    """Waits until the cache holds exactly the reference chores again (earlier tests create and delete)."""
    expected = {bridge.resource(i) for i in REFERENCE_IDS}
    found: dict[str, str] = {}

    def ready() -> bool:
        nonlocal found
        found = etags(bridge)
        return set(found) == expected

    env.wait_until(ready, timeout, "reference data in the bridge cache")
    return found


# -- E2E-01 to E2E-07 -------------------------------------------------------

def test_e2e_01_discovery_over_https_with_basic_auth(bridge, caldav_client):
    anonymous = bridge.request("PROPFIND", "/caldav/", auth=False, headers={"Depth": "0"})
    assert anonymous.status_code == 401
    assert anonymous.headers["WWW-Authenticate"].startswith("Basic")
    assert "calendar-access" in anonymous.headers["DAV"]

    well_known = bridge.request("GET", "/.well-known/caldav", auth=False, allow_redirects=False)
    assert well_known.status_code == 301 and well_known.headers["Location"] == "/caldav/"

    principal = caldav_client.principal()
    calendars = principal.calendars()
    assert [c.get_display_name() for c in calendars] == ["DonetickTasks"]
    assert str(calendars[0].url).endswith(bridge.calendar())


def test_e2e_02_todos_match_the_seeded_donetick_data(bridge, caldav_client):
    wait_for_reference_data(bridge)
    calendar = caldav_client.principal().calendars()[0]
    todos = todos_by_uid(calendar)

    assert set(todos) == {f"donetick-{i}@donetick" for i in REFERENCE_IDS}
    groceries = todos["donetick-103@donetick"].icalendar_component
    assert str(groceries["summary"]) == "Buy groceries"
    assert str(groceries["description"]) == "Milk, bread"
    assert groceries["due"].dt == datetime(2025, 6, 16, 6, 0, tzinfo=timezone.utc)
    dentist = todos["donetick-105@donetick"].icalendar_component
    assert "due" not in dentist
    assert str(dentist["status"]) == "NEEDS-ACTION"


def test_e2e_03_create_reaches_donetick_with_the_expected_request(bridge, stub, caldav_client):
    calendar = caldav_client.principal().calendars()[0]

    todo = calendar.save_todo(summary="Repaint the fence", description="Before summer", due=date(2025, 6, 20),
                              uid="0B1C2D3E-4F50-4617-8293-A4B5C6D7E8F9")

    posts = stub.requests("POST", "/eapi/v1/chore")
    assert len(posts) == 1
    assert json.loads(posts[0]["Body"]) == {"name": "Repaint the fence", "description": "Before summer", "dueDate": "2025-06-20"}
    assert posts[0]["Headers"]["secretkey"] == [env.API_KEY]
    todo.load()
    assert str(todo.icalendar_component["uid"]) == "donetick-900@donetick"


def test_e2e_04_edit_reaches_donetick(bridge, stub, caldav_client):
    wait_for_reference_data(bridge)
    todo = todos_by_uid(caldav_client.principal().calendars()[0])["donetick-103@donetick"]

    todo.icalendar_component["summary"] = "Buy groceries and flowers"
    todo.save()

    puts = stub.requests("PUT", "/eapi/v1/chore/103")
    assert len(puts) == 1
    assert json.loads(puts[0]["Body"])["name"] == "Buy groceries and flowers"


def test_e2e_05_complete_reaches_donetick_once(bridge, stub, caldav_client):
    wait_for_reference_data(bridge)
    todo = todos_by_uid(caldav_client.principal().calendars()[0])["donetick-101@donetick"]

    todo.complete()

    assert len(stub.requests("POST", "/eapi/v1/chore/101/complete")) == 1
    assert stub.requests("POST", "/eapi/v1/chore") == [], "a completion must not create a chore"


def test_e2e_06_delete_reaches_donetick(bridge, stub, caldav_client):
    wait_for_reference_data(bridge)
    todo = todos_by_uid(caldav_client.principal().calendars()[0])["donetick-105@donetick"]

    todo.delete()

    assert len(stub.requests("DELETE", "/eapi/v1/chore/105")) == 1
    assert bridge.request("GET", bridge.resource(105)).status_code == 404


def test_e2e_07_group_by_label_gives_one_calendar_per_single_label(labels_bridge):
    client = env.dav_client("bridge-labels")

    calendars = {c.get_display_name(): c for c in client.principal().calendars()}

    assert set(calendars) == {"General", "Finance", "Home"}
    home = {str(t.icalendar_component["summary"]) for t in calendars["Home"].todos()}
    assert home == {"Buy groceries", "Take out the bins"}
    general = {str(t.icalendar_component["summary"]) for t in calendars["General"].todos()}
    assert "Clean the gutters" in general, "a chore with two labels stays in the default list"


def test_e2e_08_invalid_configuration_exits_non_zero_with_a_clear_log_line(stub_stack):
    result = stub_stack.run_bridge({"Donetick__ApiKey": ""})

    assert result.exit_code != 0
    assert "ApiKey" in result.output and "required" in result.output, result.output[-2000:]


def test_e2e_10_health_endpoint(bridge):
    wait_for_reference_data(bridge)

    response = bridge.request("GET", "/health/json", auth=False)

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "healthy"
    assert body["cache"]["total_chores"] == 6
    assert body["version"]


def test_e2e_10_docker_healthcheck(stub_stack):
    pytest.skip("The image has no HEALTHCHECK yet (finding F-18); check the container health status once it has")


# -- restarts and outages (change the shared environment, so they run last) ------

def test_e2e_09_restart_rebuilds_the_cache_with_unchanged_etags(bridge, stub_stack):
    before = wait_for_reference_data(bridge)

    stub_stack.restart("bridge")
    env.wait_for_bridge("bridge")
    after = wait_for_reference_data(bridge)

    assert after == before, "ETags derive from Donetick data only, so clients do not refetch after a restart"


@pytest.mark.slow
def test_e2e_11_stub_outage_reads_keep_working_writes_fail_and_service_recovers(bridge, stub_stack):
    wait_for_reference_data(bridge)
    edit = ("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:e2e\r\nBEGIN:VTODO\r\nUID:donetick-102@donetick\r\n"
            "SUMMARY:Pay rent today\r\nSTATUS:NEEDS-ACTION\r\nEND:VTODO\r\nEND:VCALENDAR\r\n")
    headers = {"Content-Type": "text/calendar"}

    stub_stack.stop("donetick-stub")
    try:
        assert bridge.request("GET", bridge.resource(102)).status_code == 200, "reads come from the cache"
        started = time.monotonic()
        failed = bridge.request("PUT", bridge.resource(102), data=edit, headers=headers)
        elapsed = time.monotonic() - started
        assert failed.status_code == 502
        assert elapsed >= 55, f"the retry pipeline waits 5 + 15 + 40 s before giving up, took {elapsed:.0f}s"
    finally:
        stub_stack.start("donetick-stub")

    session = env.http()
    env.wait_until(lambda: session.get(f"{env.STUB_URL}/__admin/health", timeout=2).status_code == 200, 60, "stub")
    recovered = bridge.request("PUT", bridge.resource(102), data=edit, headers=headers)
    assert recovered.status_code == 204

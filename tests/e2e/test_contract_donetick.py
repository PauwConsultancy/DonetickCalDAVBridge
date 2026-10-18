"""L5 contract tests (CT-01 to CT-07) against a real, freshly provisioned Donetick, plus two checks of
the bridge against that Donetick (CT-08, CT-09).

They verify the assumptions the bridge makes about the external API (/eapi/v1). Each check states the
assumption it protects; a failure after a Donetick upgrade means the bridge or the stub data has to follow.
"""

from __future__ import annotations

import json
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

import environment as env

pytestmark = pytest.mark.contract

RECORDED = Path(__file__).resolve().parents[1] / "fixtures" / "donetick" / "recorded"

# Fields the bridge reads (DonetickChore) with the JSON types it can handle.
BRIDGE_FIELDS = {
    "id": (int,),
    "name": (str,),
    "frequencyType": (str,),
    "frequency": (int,),
    "frequencyMetadata": (dict, type(None)),
    "nextDueDate": (str, type(None)),
    "isRolling": (bool,),
    "assignedTo": (int, type(None)),
    "isActive": (bool,),
    "status": (int,),
    "priority": (int,),
    "createdAt": (str,),
    "updatedAt": (str,),
    "createdBy": (int,),
    "circleId": (int,),
    "labelsV2": (list, type(None)),
}


def parse_time(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def create(donetick, **body) -> dict:
    response = donetick.api.eapi(donetick.account, "POST", "/chore", json=body)
    assert response.status_code in (200, 201), response.text
    return response.json()


def test_ct_01_chore_list_has_the_shape_the_bridge_reads(donetick):
    response = donetick.api.eapi(donetick.account, "GET", "/chore")

    assert response.status_code == 200
    body = response.json()
    assert isinstance(body, list), "the bridge expects a plain JSON array (finding F-26 for the wrapper fallback)"
    assert body, "the seeded chores must be listed"
    for chore in body:
        for name, types in BRIDGE_FIELDS.items():
            assert name in chore, f"chore {chore.get('id')} lacks '{name}'"
            assert isinstance(chore[name], types), f"'{name}' is {type(chore[name]).__name__} in chore {chore['id']}"
        for label in chore["labelsV2"] or []:
            assert isinstance(label["id"], int) and isinstance(label["name"], str)

    # Report fields that appeared or disappeared since the recorded fixtures, without failing on new ones.
    recorded_files = sorted(RECORDED.glob("*/list-chores.json"))
    if recorded_files:
        recorded = json.loads(recorded_files[-1].read_text(encoding="utf-8"))["body"]
        recorded_keys = set().union(*(c.keys() for c in recorded))
        live_keys = set().union(*(c.keys() for c in body))
        missing = recorded_keys - live_keys - {"description"}  # description is omitted when empty
        assert not missing, f"fields gone since {recorded_files[-1].parent.name}: {sorted(missing)}"
        print(f"new fields since {recorded_files[-1].parent.name}: {sorted(live_keys - recorded_keys)}")


def test_ct_01_scheduled_time_is_an_rfc3339_timestamp(donetick):
    chore = donetick.api.chore(donetick.account, donetick.ids["Water the plants"])

    time_value = chore["frequencyMetadata"]["time"]
    parse_time(time_value)  # raises when it is not a timestamp
    assert "T" in time_value, "finding F-23: the bridge parses this as a time of day, which fails"


def test_ct_02_create_stores_what_was_sent_and_adds_the_current_time_to_a_date(donetick):
    before = datetime.now(timezone.utc).replace(microsecond=0)

    created = create(donetick, name="Contract create", description="Created by CT-02", dueDate="2030-03-04")

    after = datetime.now(timezone.utc) + timedelta(seconds=1)
    stored = donetick.api.chore(donetick.account, created["id"])
    assert stored["name"] == "Contract create"
    assert stored["description"] == "Created by CT-02"
    due = parse_time(stored["nextDueDate"])
    assert due.date().isoformat() == "2030-03-04"
    # Donetick fills in the current UTC time of day for a date-only due date (L-08 in the technical design).
    assert before.time() <= due.time().replace(tzinfo=None) <= after.time() or before.date() != after.date()


def test_ct_03_completing_a_recurring_chore_advances_the_due_date(donetick):
    chore_id = donetick.ids["Water the plants"]
    before = donetick.api.chore(donetick.account, chore_id)

    response = donetick.api.eapi(donetick.account, "POST", f"/chore/{chore_id}/complete")

    assert response.status_code == 200, response.text
    after = donetick.api.chore(donetick.account, chore_id)
    assert parse_time(after["nextDueDate"]) == parse_time(before["nextDueDate"]) + timedelta(days=1)
    assert after["status"] == 0
    assert after["updatedAt"] != before["updatedAt"]


def test_ct_03_a_completed_one_off_chore_leaves_the_list(donetick):
    created = create(donetick, name="Contract one-off", dueDate="2030-03-05")

    response = donetick.api.eapi(donetick.account, "POST", f"/chore/{created['id']}/complete")

    assert response.status_code == 200
    assert donetick.api.chore(donetick.account, created["id"]) is None


def test_ct_04_an_update_without_description_or_due_date_keeps_both(donetick):
    created = create(donetick, name="Contract update", description="Keep me", dueDate="2030-03-06")
    due_before = donetick.api.chore(donetick.account, created["id"])["nextDueDate"]

    response = donetick.api.eapi(donetick.account, "PUT", f"/chore/{created['id']}", json={"name": "Contract update renamed"})

    assert response.status_code == 200, response.text
    stored = donetick.api.chore(donetick.account, created["id"])
    assert stored["name"] == "Contract update renamed"
    assert stored["description"] == "Keep me", "the bridge omits null fields and relies on this (finding F-09)"
    assert stored["nextDueDate"] == due_before


def test_ct_05_updated_at_changes_with_description_and_priority(donetick):
    created = create(donetick, name="Contract updatedAt")
    first = donetick.api.chore(donetick.account, created["id"])["updatedAt"]
    time.sleep(1.1)

    donetick.api.eapi(donetick.account, "PUT", f"/chore/{created['id']}",
                      json={"name": "Contract updatedAt", "description": "changed"}).raise_for_status()
    second = donetick.api.chore(donetick.account, created["id"])["updatedAt"]
    time.sleep(1.1)
    donetick.api.api(donetick.account, "PUT", f"/chores/{created['id']}/priority", json={"priority": 3}).raise_for_status()
    third = donetick.api.chore(donetick.account, created["id"])

    assert second != first, "a description change must change updatedAt, or the ETag misses it (finding F-04)"
    assert third["priority"] == 3
    assert third["updatedAt"] != second, "a priority change must change updatedAt, or the ETag misses it (finding F-04)"


def test_ct_06_delete_removes_the_chore(donetick):
    created = create(donetick, name="Contract delete")

    response = donetick.api.eapi(donetick.account, "DELETE", f"/chore/{created['id']}")

    assert response.status_code == 200
    assert donetick.api.chore(donetick.account, created["id"]) is None
    again = donetick.api.eapi(donetick.account, "DELETE", f"/chore/{created['id']}")
    assert again.status_code >= 400, "deleting twice fails, which the bridge reports as 502 (finding F-25)"


@pytest.mark.parametrize("headers", [{"secretkey": "dt_wrong"}, {}], ids=["wrong key", "no key"])
def test_ct_07_requests_without_a_valid_key_are_rejected(donetick, headers):
    response = donetick.api.session.get(f"{env.DONETICK_URL}/eapi/v1/chore", headers=headers, timeout=10)

    assert response.status_code == 401


# -- the bridge against the real Donetick -------------------------------------

def test_ct_08_bridge_serves_the_real_chores(contract_bridge, donetick):
    response = contract_bridge.request("GET", contract_bridge.resource(donetick.ids["Pay rent"]))

    assert response.status_code == 200, response.text
    assert "SUMMARY:Pay rent" in response.text
    health = contract_bridge.request("GET", "/health/json", auth=False).json()
    assert health["cache"]["total_chores"] >= 5


def test_ct_09_bridge_create_and_complete_reach_the_real_donetick(contract_bridge, donetick):
    uid = "5D2C1B0A-9F8E-4D7C-8B6A-504132231405"
    ics = ("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:contract\r\nBEGIN:VTODO\r\nUID:{uid}\r\nSUMMARY:{summary}\r\n"
           "DUE;VALUE=DATE:20300307\r\nSTATUS:{status}\r\nEND:VTODO\r\nEND:VCALENDAR\r\n")
    headers = {"Content-Type": "text/calendar"}

    created = contract_bridge.request("PUT", f"{contract_bridge.calendar()}{uid}.ics",
                                      data=ics.format(uid=uid, summary="Created through the bridge", status="NEEDS-ACTION"),
                                      headers=headers)
    assert created.status_code == 201, created.text
    chore_id = int(created.headers["Location"].rsplit("donetick-", 1)[1].removesuffix(".ics"))
    stored = donetick.api.chore(donetick.account, chore_id)
    assert stored["name"] == "Created through the bridge"
    assert parse_time(stored["nextDueDate"]).date().isoformat() == "2030-03-07"

    completed = contract_bridge.request("PUT", contract_bridge.resource(chore_id),
                                        data=ics.format(uid=f"donetick-{chore_id}@donetick", summary="Created through the bridge",
                                                        status="COMPLETED"), headers=headers)
    assert completed.status_code == 204, completed.text
    assert donetick.api.chore(donetick.account, chore_id) is None, "a completed one-off chore leaves the list"

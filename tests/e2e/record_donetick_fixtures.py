"""Records real Donetick eAPI payloads as fixtures (test data, chapter 6 of the testing strategy).

Run against a fresh, disposable Donetick (never a production instance):

    python tests/e2e/record_donetick_fixtures.py http://localhost:2021 v0.1.82

It provisions a test user, seeds the reference chores, performs each eAPI call the bridge makes and
writes the raw responses to tests/fixtures/donetick/recorded/<version>/. The chore names are the
synthetic reference data from donetick_api.seed_reference_data, so no personal data is recorded.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from donetick_api import DonetickApi, seed_reference_data

ROOT = Path(__file__).resolve().parents[2]


def main(base_url: str, version: str) -> None:
    out = ROOT / "tests" / "fixtures" / "donetick" / "recorded" / version
    out.mkdir(parents=True, exist_ok=True)

    api = DonetickApi(base_url)
    api.wait_until_healthy()
    account = api.provision()
    ids = seed_reference_data(api, account)

    def save(name: str, response) -> None:
        body = response.json() if response.content else None
        record = {"status": response.status_code, "body": body}
        (out / f"{name}.json").write_text(json.dumps(record, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"{name}: {response.status_code}")

    save("list-chores", api.eapi(account, "GET", "/chore"))
    save("create-chore-date-only", api.eapi(account, "POST", "/chore", json={
        "name": "Created by the recorder", "description": "Recorded payload", "dueDate": "2026-11-02"}))
    save("update-chore-name-only", api.eapi(account, "PUT", f"/chore/{ids['Call the dentist']}",
                                            json={"name": "Call the dentist today"}))
    save("complete-recurring-chore", api.eapi(account, "POST", f"/chore/{ids['Water the plants']}/complete"))
    save("complete-one-off-chore", api.eapi(account, "POST", f"/chore/{ids['Buy groceries']}/complete"))
    save("delete-chore", api.eapi(account, "DELETE", f"/chore/{ids['Take out the bins']}"))
    save("list-chores-after-changes", api.eapi(account, "GET", "/chore"))
    save("wrong-api-key", api.session.get(f"{base_url.rstrip('/')}/eapi/v1/chore", headers={"secretkey": "dt_wrong"}))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])

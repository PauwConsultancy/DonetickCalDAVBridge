"""Small client for a real Donetick instance, used by the contract tests (L5) and the fixture recorder.

Provisioning needs no web UI (testing strategy 5.6): sign up, log in for a JWT, create an API token.
The token is the ``secretkey`` for the external API (``/eapi/v1``) that the bridge uses. The internal
API (``/api/v1``, JWT) is only used to create test data the external API cannot express, such as
labels and recurring chores.
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone

import requests


@dataclass
class DonetickAccount:
    base_url: str
    username: str
    jwt: str
    api_key: str


class DonetickApi:
    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")
        self.session = requests.Session()
        # Local test instances must not go through a corporate or sandbox proxy.
        self.session.trust_env = False

    # -- lifecycle --------------------------------------------------------

    def wait_until_healthy(self, timeout: float = 60.0) -> None:
        deadline = time.monotonic() + timeout
        last_error: Exception | None = None
        while time.monotonic() < deadline:
            try:
                if self.session.get(f"{self.base_url}/api/v1/health", timeout=2).status_code == 200:
                    return
            except requests.RequestException as error:  # not listening yet
                last_error = error
            time.sleep(0.5)
        raise TimeoutError(f"Donetick at {self.base_url} not healthy within {timeout}s: {last_error}")

    def provision(self, username: str = "bridgetest", password: str = "bridge-test-pass") -> DonetickAccount:
        """Creates a user (or reuses it) and returns a JWT and a fresh API token."""
        signup = self.session.post(f"{self.base_url}/api/v1/auth/", json={
            "username": username,
            "password": password,
            "email": f"{username}@example.com",
            "displayName": "Bridge Test",
        }, timeout=10)
        if signup.status_code not in (200, 201, 409):
            # An existing user is fine (re-run against the same database); anything else is not.
            if "exist" not in signup.text.lower():
                signup.raise_for_status()

        login = self.session.post(f"{self.base_url}/api/v1/auth/login",
                                  json={"username": username, "password": password}, timeout=10)
        login.raise_for_status()
        jwt = login.json()["access_token"]

        token = self.session.post(f"{self.base_url}/api/v1/users/tokens", json={"name": "caldav-bridge-tests"},
                                  headers={"Authorization": f"Bearer {jwt}"}, timeout=10)
        token.raise_for_status()
        return DonetickAccount(self.base_url, username, jwt, token.json()["res"]["token"])

    # -- external API (what the bridge uses) ------------------------------

    def eapi(self, account: DonetickAccount, method: str, path: str, **kwargs) -> requests.Response:
        headers = {"secretkey": account.api_key, **kwargs.pop("headers", {})}
        return self.session.request(method, f"{self.base_url}/eapi/v1{path}", headers=headers, timeout=10, **kwargs)

    def list_chores(self, account: DonetickAccount) -> list[dict]:
        response = self.eapi(account, "GET", "/chore")
        response.raise_for_status()
        body = response.json()
        return body["res"] if isinstance(body, dict) else body

    def chore(self, account: DonetickAccount, chore_id: int) -> dict | None:
        return next((c for c in self.list_chores(account) if c["id"] == chore_id), None)

    # -- internal API (test data only) ------------------------------------

    def api(self, account: DonetickAccount, method: str, path: str, **kwargs) -> requests.Response:
        headers = {"Authorization": f"Bearer {account.jwt}", **kwargs.pop("headers", {})}
        return self.session.request(method, f"{self.base_url}/api/v1{path}", headers=headers, timeout=10, **kwargs)

    def create_label(self, account: DonetickAccount, name: str, color: str = "#4A90D9") -> int:
        response = self.api(account, "POST", "/labels", json={"name": name, "color": color})
        response.raise_for_status()
        body = response.json()
        return (body.get("res") or body)["id"]

    def create_chore(self, account: DonetickAccount, **fields) -> int:
        payload = {"assignStrategy": "no_assignee", "assignees": [], "isRolling": False, **fields}
        response = self.api(account, "POST", "/chores/", json=payload)
        if response.status_code >= 400:
            raise RuntimeError(f"create chore failed: {response.status_code} {response.text}")
        body = response.json()
        return body["res"] if isinstance(body.get("res"), int) else body["res"]["id"]


def seed_reference_data(api: DonetickApi, account: DonetickAccount, now: datetime | None = None) -> dict[str, int]:
    """Creates the reference chores the contract tests and recorded fixtures use. Returns name -> id."""
    now = (now or datetime.now(timezone.utc)).replace(microsecond=0)
    tomorrow_six = (now + timedelta(days=1)).replace(hour=6, minute=0, second=0)
    labels = {name: api.create_label(account, name) for name in ("Home", "Finance", "Outdoor")}

    def iso(value: datetime) -> str:
        return value.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    ids = {
        "Water the plants": api.create_chore(
            account, name="Water the plants", frequencyType="daily", frequency=1,
            nextDueDate=iso(tomorrow_six),
            frequencyMetadata={"time": tomorrow_six.isoformat().replace("+00:00", "Z"), "timezone": "Europe/Amsterdam"}),
        "Pay rent": api.create_chore(
            account, name="Pay rent", frequencyType="day_of_the_month", frequency=1,
            nextDueDate=iso(tomorrow_six + timedelta(days=10)), frequencyMetadata={"days": ["1"]},
            labelsV2=[{"id": labels["Finance"]}], priority=3),
        "Buy groceries": api.create_chore(
            account, name="Buy groceries", frequencyType="once", nextDueDate=iso(tomorrow_six),
            labelsV2=[{"id": labels["Home"]}], description="Milk, bread"),
        "Clean the gutters": api.create_chore(
            account, name="Clean the gutters", frequencyType="once", nextDueDate=iso(tomorrow_six + timedelta(days=3)),
            labelsV2=[{"id": labels["Home"]}, {"id": labels["Outdoor"]}]),
        "Call the dentist": api.create_chore(
            account, name="Call the dentist", frequencyType="once", description="Ask about the check-up"),
        "Take out the bins": api.create_chore(
            account, name="Take out the bins", frequencyType="days_of_the_week", frequency=1,
            nextDueDate=iso(tomorrow_six), frequencyMetadata={"days": ["monday", "thursday"]}),
    }
    return ids

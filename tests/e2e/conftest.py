"""Fixtures for the L4 end-to-end (marker e2e) and L5 contract (marker contract) tests."""

from __future__ import annotations

import sys
from pathlib import Path

import caldav
import pytest
import requests

sys.path.insert(0, str(Path(__file__).resolve().parent))

import environment as env  # noqa: E402
from donetick_api import DonetickAccount, DonetickApi, seed_reference_data  # noqa: E402


@pytest.fixture(scope="session")
def system():
    """The environment backend (compose or local processes); see environment.py."""
    backend = env.create_environment()
    yield backend
    backend.close()


@pytest.fixture(scope="session")
def stub_stack(system):
    """Stub, bridge and bridge with GroupByLabel, started once per run."""
    system.start_stub_stack()
    return system


class Stub:
    """WireMock.Net admin API of the Donetick stub."""

    def __init__(self) -> None:
        self.session = requests.Session()
        self.session.trust_env = False

    def reset_requests(self) -> None:
        self.session.delete(f"{env.STUB_URL}/__admin/requests", timeout=5).raise_for_status()

    def requests(self, method: str | None = None, path: str | None = None) -> list[dict]:
        entries = self.session.get(f"{env.STUB_URL}/__admin/requests", timeout=5).json()
        found = [e["Request"] for e in entries]
        if method:
            found = [r for r in found if r["Method"].upper() == method.upper()]
        if path:
            found = [r for r in found if r["Path"] == path]
        return sorted(found, key=lambda r: r["DateTime"])


@pytest.fixture
def stub(stub_stack) -> Stub:
    stub = Stub()
    stub.reset_requests()
    return stub


class Bridge:
    """Raw HTTP access to a bridge instance, for assertions on status codes and headers."""

    def __init__(self, service: str = "bridge") -> None:
        self.service = service
        self.base = env.bridge_url(service)
        self.session = env.http()
        self.session.auth = (env.USERNAME, env.PASSWORD)

    def url(self, path: str) -> str:
        return self.base + path

    def request(self, method: str, path: str, auth: bool = True, **kwargs) -> requests.Response:
        kwargs.setdefault("timeout", 120)
        if not auth:
            kwargs["auth"] = None
            return env.http().request(method, self.url(path), **kwargs)
        return self.session.request(method, self.url(path), **kwargs)

    def calendar(self, slug: str = "tasks") -> str:
        return f"/caldav/calendars/{env.USERNAME}/{slug}/"

    def resource(self, chore_id: int, slug: str = "tasks") -> str:
        return f"{self.calendar(slug)}donetick-{chore_id}.ics"


@pytest.fixture
def bridge(stub_stack) -> Bridge:
    return Bridge("bridge")


@pytest.fixture
def labels_bridge(stub_stack) -> Bridge:
    return Bridge("bridge-labels")


@pytest.fixture
def caldav_client(stub_stack) -> caldav.DAVClient:
    return env.dav_client("bridge")


# -- contract (L5) ---------------------------------------------------------

@pytest.fixture(scope="session")
def donetick(system):
    """A fresh real Donetick, provisioned with a user, an API token and the reference chores."""
    system.start_donetick()
    api = DonetickApi(env.DONETICK_URL)
    api.wait_until_healthy()
    account = api.provision()
    ids = seed_reference_data(api, account)
    return DonetickHandle(api, account, ids)


class DonetickHandle:
    def __init__(self, api: DonetickApi, account: DonetickAccount, ids: dict[str, int]) -> None:
        self.api = api
        self.account = account
        self.ids = ids


@pytest.fixture(scope="session")
def contract_bridge(system, donetick) -> Bridge:
    """The bridge container pointed at the real Donetick with the provisioned API key."""
    system.start_contract_bridge(donetick.account.api_key)
    return Bridge("bridge-contract")

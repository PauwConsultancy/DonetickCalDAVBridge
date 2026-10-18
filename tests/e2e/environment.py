"""Starts, stops and inspects the system under test for the L4 and L5 tests.

Two backends with the same interface:

- ComposeEnvironment (E2E_ENV=compose, the default): the containers from docker-compose.e2e.yml.
  This is what CI runs, and the only way to test the image itself.
- LocalEnvironment (E2E_ENV=local): the published bridge (dotnet DonetickCalDav.dll), the
  dotnet-wiremock tool and a Donetick binary as local processes, for machines without Docker.

Ports are the same in both: stub 9091, bridge 5232, bridge with GroupByLabel 5233, real Donetick 2021,
bridge against real Donetick 5234.
"""

from __future__ import annotations

import os
import shutil
import signal
import subprocess
import tempfile
import time
from dataclasses import dataclass, field
from pathlib import Path

import caldav
import requests

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
COMPOSE_FILE = HERE / "docker-compose.e2e.yml"
CERTS = HERE / "certs" / "out"
MAPPINGS = ROOT / "tests" / "fixtures" / "donetick" / "mappings"

USERNAME = "e2e"
PASSWORD = "e2e-password"
API_KEY = "test-api-key"

BRIDGE_PORTS = {"bridge": 5232, "bridge-labels": 5233, "bridge-contract": 5234}
STUB_URL = "http://localhost:9091"
DONETICK_URL = "http://localhost:2021"

BRIDGE_ENVIRONMENT = {
    "CalDav__Username": USERNAME,
    "CalDav__Password": PASSWORD,
    "Donetick__PollIntervalSeconds": "5",
    "Logging__LogLevel__DonetickCalDav": "Debug",
}


def bridge_url(service: str = "bridge") -> str:
    return f"https://localhost:{BRIDGE_PORTS[service]}"


def http() -> requests.Session:
    """A session for the local test environment: trusts the test CA and never uses a proxy."""
    session = requests.Session()
    session.trust_env = False
    session.verify = str(CERTS / "ca.crt")
    return session


def dav_client(service: str = "bridge") -> caldav.DAVClient:
    """The Python caldav library as an independent CalDAV client, discovering from .well-known."""
    # caldav reads proxy settings from the environment; the test system is always local.
    os.environ.setdefault("NO_PROXY", "localhost,127.0.0.1")
    os.environ.setdefault("no_proxy", "localhost,127.0.0.1")
    return caldav.DAVClient(url=f"{bridge_url(service)}/.well-known/caldav", username=USERNAME,
                            password=PASSWORD, ssl_verify_cert=str(CERTS / "ca.crt"), timeout=120)


def wait_until(check, timeout: float, what: str) -> None:
    deadline = time.monotonic() + timeout
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        try:
            if check():
                return
        except Exception as error:  # not up yet
            last_error = error
        time.sleep(0.5)
    raise TimeoutError(f"{what} not ready within {timeout}s (last error: {last_error})")


def wait_for_bridge(service: str = "bridge", timeout: float = 90) -> None:
    session = http()
    wait_until(lambda: session.get(f"{bridge_url(service)}/health/json", timeout=3).status_code == 200,
               timeout, f"{service} health endpoint")


@dataclass
class RunResult:
    exit_code: int
    output: str


class ComposeEnvironment:
    name = "compose"

    def __init__(self) -> None:
        if shutil.which("docker") is None:
            raise RuntimeError("E2E_ENV=compose needs the docker CLI")

    def _compose(self, *args: str, env: dict[str, str] | None = None, check: bool = True) -> subprocess.CompletedProcess:
        command = ["docker", "compose", "-f", str(COMPOSE_FILE),
                   "--profile", "stub", "--profile", "contract", "--profile", "contract-bridge", *args]
        return subprocess.run(command, check=check, capture_output=True, text=True,
                              env={**os.environ, **(env or {})}, timeout=600)

    def start_stub_stack(self) -> None:
        self._compose("up", "-d", "--wait", "donetick-stub", "bridge", "bridge-labels")
        wait_for_bridge("bridge")
        wait_for_bridge("bridge-labels")

    def start_donetick(self) -> None:
        self._compose("up", "-d", "--wait", "donetick")

    def start_contract_bridge(self, api_key: str) -> None:
        self._compose("up", "-d", "--force-recreate", "bridge-contract", env={"DONETICK_API_KEY": api_key})
        wait_for_bridge("bridge-contract")

    def stop(self, service: str) -> None:
        self._compose("stop", service)

    def start(self, service: str) -> None:
        self._compose("start", service)

    def restart(self, service: str) -> None:
        self._compose("restart", service)

    def run_bridge(self, overrides: dict[str, str]) -> RunResult:
        """Runs a one-off bridge container with changed settings and returns its exit code and log."""
        args = ["run", "--rm", "--no-deps", "-T"]
        for key, value in overrides.items():
            args += ["-e", f"{key}={value}"]
        result = self._compose(*args, "bridge", check=False)
        return RunResult(result.returncode, result.stdout + result.stderr)

    def logs(self, service: str) -> str:
        return self._compose("logs", "--no-color", service, check=False).stdout

    def close(self) -> None:
        """Containers are left to the caller (the workflow runs compose down)."""


@dataclass
class _Process:
    command: list[str]
    environment: dict[str, str]
    cwd: Path
    log: Path
    process: subprocess.Popen | None = field(default=None)

    def start(self) -> None:
        handle = self.log.open("ab")
        self.process = subprocess.Popen(self.command, cwd=self.cwd, env={**os.environ, **self.environment},
                                        stdout=handle, stderr=subprocess.STDOUT, start_new_session=True)

    def stop(self) -> None:
        if self.process and self.process.poll() is None:
            os.killpg(self.process.pid, signal.SIGTERM)
            try:
                self.process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                os.killpg(self.process.pid, signal.SIGKILL)
        self.process = None


class LocalEnvironment:
    """Local processes instead of containers. Configure with:

    E2E_BRIDGE_DLL    path to the published DonetickCalDav.dll (dotnet publish src/DonetickCalDav)
    E2E_WIREMOCK      dotnet-wiremock command (default: dotnet-wiremock on PATH)
    E2E_DONETICK_BIN  Donetick release binary, for the contract tests
    """

    name = "local"

    def __init__(self) -> None:
        self.work = Path(tempfile.mkdtemp(prefix="donetick-caldav-e2e-"))
        self.dll = Path(os.environ.get("E2E_BRIDGE_DLL", ROOT / "artifacts" / "publish" / "DonetickCalDav.dll"))
        self.processes: dict[str, _Process] = {}
        self.api_key = API_KEY

    def _bridge(self, service: str, donetick_url: str, api_key: str, extra: dict[str, str] | None = None) -> _Process:
        environment = {
            **BRIDGE_ENVIRONMENT,
            "Donetick__BaseUrl": donetick_url,
            "Donetick__ApiKey": api_key,
            "CalDav__ListenPort": str(BRIDGE_PORTS[service]),
            "CalDav__Tls__CertPath": str(CERTS / "bridge.crt"),
            "CalDav__Tls__KeyPath": str(CERTS / "bridge.key"),
            "DOTNET_ROLL_FORWARD": "Major",
            **(extra or {}),
        }
        return _Process(["dotnet", str(self.dll)], environment, self.dll.parent, self.work / f"{service}.log")

    def start_stub_stack(self) -> None:
        stub_dir = self.work / "stub"
        (stub_dir / "__admin").mkdir(parents=True, exist_ok=True)
        shutil.copytree(MAPPINGS, stub_dir / "__admin" / "mappings", dirs_exist_ok=True)
        wiremock = os.environ.get("E2E_WIREMOCK", "dotnet-wiremock")
        self.processes["donetick-stub"] = _Process(
            [wiremock, "--Urls", "http://0.0.0.0:9091", "--ReadStaticMappings", "true"],
            {"DOTNET_ROLL_FORWARD": "Major"}, stub_dir, self.work / "donetick-stub.log")
        self.processes["bridge"] = self._bridge("bridge", STUB_URL, API_KEY)
        self.processes["bridge-labels"] = self._bridge("bridge-labels", STUB_URL, API_KEY, {"CalDav__GroupByLabel": "true"})
        self.start("donetick-stub")
        session = http()
        wait_until(lambda: session.get(f"{STUB_URL}/__admin/health", timeout=2).status_code == 200, 60, "stub")
        self.start("bridge")
        self.start("bridge-labels")
        wait_for_bridge("bridge")
        wait_for_bridge("bridge-labels")

    def start_donetick(self) -> None:
        binary = Path(os.environ["E2E_DONETICK_BIN"])
        data = self.work / "donetick"
        data.mkdir(exist_ok=True)
        self.processes["donetick"] = _Process(
            [str(binary)], {"DT_ENV": "selfhosted", "DT_SQLITE_PATH": str(data / "donetick.db")}, data, self.work / "donetick.log")
        self.start("donetick")
        session = http()
        wait_until(lambda: session.get(f"{DONETICK_URL}/api/v1/health", timeout=2).status_code == 200, 60, "Donetick")

    def start_contract_bridge(self, api_key: str) -> None:
        self.stop("bridge-contract")
        self.processes["bridge-contract"] = self._bridge("bridge-contract", DONETICK_URL, api_key)
        self.start("bridge-contract")
        wait_for_bridge("bridge-contract")

    def stop(self, service: str) -> None:
        if service in self.processes:
            self.processes[service].stop()

    def start(self, service: str) -> None:
        self.processes[service].start()

    def restart(self, service: str) -> None:
        self.stop(service)
        self.start(service)

    def run_bridge(self, overrides: dict[str, str]) -> RunResult:
        process = self._bridge("bridge", STUB_URL, API_KEY, overrides)
        result = subprocess.run(process.command, cwd=process.cwd, env={**os.environ, **process.environment},
                                capture_output=True, text=True, timeout=60)
        return RunResult(result.returncode, result.stdout + result.stderr)

    def logs(self, service: str) -> str:
        log = self.work / f"{service}.log"
        return log.read_text(errors="replace") if log.exists() else ""

    def close(self) -> None:
        for process in self.processes.values():
            process.stop()


def create_environment():
    kind = os.environ.get("E2E_ENV", "compose")
    if kind == "compose":
        return ComposeEnvironment()
    if kind == "local":
        return LocalEnvironment()
    raise ValueError(f"Unknown E2E_ENV '{kind}' (use compose or local)")

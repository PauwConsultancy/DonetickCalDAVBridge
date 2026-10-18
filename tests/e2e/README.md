# End-to-end and contract tests
The two outer automated test levels of the [testing strategy](../../docs/testing-strategy.md), written in Python with pytest and the `caldav` library as an independent CalDAV client:

| Marker | Level | System under test | Scenarios | Runs in CI |
|--------|-------|-------------------|-----------|------------|
| `e2e` | L4 | The bridge image against the WireMock stub with the reference data, over HTTPS | E2E-01 to E2E-11 (`test_e2e_bridge.py`) | Nightly and on tags (`.github/workflows/e2e.yml`) |
| `contract` | L5 | A real Donetick, provisioned on the fly, and the bridge against it | CT-01 to CT-09 (`test_contract_donetick.py`) | Weekly and on demand (`.github/workflows/contract.yml`) |

E2E-11 waits for the real retry delays (about a minute) and is also marked `slow`.

## Running with Docker (as CI does)

```bash
pip install -r tests/e2e/requirements.txt
tests/e2e/certs/generate.sh

docker compose -f tests/e2e/docker-compose.e2e.yml --profile stub up -d --build --wait
pytest tests/e2e -m e2e
docker compose -f tests/e2e/docker-compose.e2e.yml --profile stub down -v

pytest tests/e2e -m contract   # starts Donetick and the contract bridge itself
docker compose -f tests/e2e/docker-compose.e2e.yml --profile contract --profile contract-bridge down -v
```

## Running without Docker
`E2E_ENV=local` runs the same tests against local processes instead of containers: the published bridge, the `dotnet-wiremock` tool and a Donetick release binary. It does not test the image itself.

```bash
dotnet publish src/DonetickCalDav -c Release -o artifacts/publish
dotnet tool install --global dotnet-wiremock --version 2.19.0
tests/e2e/certs/generate.sh
E2E_ENV=local pytest tests/e2e -m e2e

# contract: download donetick_Linux_x86_64.tar.gz (or the build for your platform) from the Donetick releases
E2E_ENV=local E2E_DONETICK_BIN=/path/to/donetick pytest tests/e2e -m contract
```

| Variable | Default | Meaning |
|----------|---------|---------|
| `E2E_ENV` | `compose` | `compose` or `local` |
| `E2E_BRIDGE_DLL` | `artifacts/publish/DonetickCalDav.dll` | Local backend: the published bridge |
| `E2E_WIREMOCK` | `dotnet-wiremock` | Local backend: the stub command |
| `E2E_DONETICK_BIN` | none | Local backend: the Donetick binary for the contract tests |

## Layout

| File | Purpose |
|------|---------|
| `docker-compose.e2e.yml` | Profiles `stub` (stub, bridge, bridge with GroupByLabel), `contract` (Donetick) and `contract-bridge` |
| `stub/Dockerfile` | WireMock.Net (`dotnet-wiremock` 2.19.0) serving `tests/fixtures/donetick/mappings` |
| `donetick/Dockerfile` | Donetick from the official release binary, pinned by tag and SHA-256 |
| `certs/generate.sh` | Throwaway CA and certificate for HTTPS; output in `certs/out` (not committed) |
| `environment.py` | Starts and controls the system under test, with a compose and a local backend |
| `conftest.py` | Fixtures: stub admin API, raw HTTP access to the bridge, caldav client, provisioned Donetick |
| `donetick_api.py` | Provisioning (sign-up, login, API token) and reference data for a real Donetick |
| `record_donetick_fixtures.py` | Records real eAPI payloads into `tests/fixtures/donetick/recorded` |

Ports: stub 9091, bridge 5232, bridge with GroupByLabel 5233, Donetick 2021, contract bridge 5234. The test user is `e2e` / `e2e-password`, the stub's API key `test-api-key`.

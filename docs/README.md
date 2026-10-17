# Donetick CalDAV Bridge Documentation
This folder holds the design documentation of the bridge, reverse engineered from the code at version 0.7.1. Start with the functional design for what the bridge does, and the technical design for how it works. The testing strategy describes how the bridge is, and will be, tested. Defects, risks and documentation drift found along the way are kept apart in the findings document, so the designs only describe the bridge as it is.

## Documents

| Document | Audience | Content |
|----------|----------|---------|
| [functional-design.md](functional-design.md) | User, operator, reviewer | Goals, actors, use cases, functional requirements, business rules, field mapping, configuration, non-functional requirements, limitations |
| [technical-design.md](technical-design.md) | Developer, maintainer | Architecture, stack, data model, startup, request pipeline, CalDAV implementation, caching, write path, VTODO mapping, Donetick integration, security, deployment, testing, design decisions |
| [testing-strategy.md](testing-strategy.md) | Developer, maintainer | Risk-based test strategy: test levels, scenario catalogue, test data, CI gates, traceability and roadmap |
| [testing/apple-acceptance.md](testing/apple-acceptance.md) | Tester, maintainer | Manual Apple acceptance checklist, run before each release |
| [findings.md](findings.md) | Developer, maintainer | Defects, risks, improvement proposals and documentation drift found during the reverse engineering |
| [../README.md](../README.md) | User, operator | Installation and configuration guide |
| [../instructions.md](../instructions.md) | Developer, AI assistant | Rationale behind deliberate design choices and stop conditions |
| [../CHANGELOG.md](../CHANGELOG.md) | Everyone | Release history |

## Diagrams
All diagrams are PlantUML sources in [diagrams/](diagrams/), rendered to SVG in [diagrams/svg/](diagrams/svg/). The Markdown documents embed the SVGs and link to the sources.

| Diagram | Type | Shows | Used in |
|---------|------|-------|---------|
| [system-context](diagrams/system-context.puml) | C4 context | Bridge, clients, Donetick, monitoring | Functional 4 |
| [use-cases](diagrams/use-cases.puml) | Use case | UC-01 to UC-12 | Functional 5 |
| [act-calendar-assignment](diagrams/act-calendar-assignment.puml) | Activity | Which list a chore lands in | Functional UC-07, Technical 12 |
| [components](diagrams/components.puml) | C4 component | Internal components and dependencies | Technical 2 |
| [domain-model](diagrams/domain-model.puml) | Class | Donetick models, cache, virtual calendars | Technical 5 |
| [act-request-pipeline](diagrams/act-request-pipeline.puml) | Activity | Middleware routing and authentication | Technical 7 |
| [url-namespace](diagrams/url-namespace.puml) | Salt tree | URL hierarchy, auth and handlers | Technical 8 |
| [seq-discovery](diagrams/seq-discovery.puml) | Sequence | Account discovery and initial load | Technical 8 |
| [seq-sync](diagrams/seq-sync.puml) | Sequence | Startup load and polling | Technical 9 |
| [act-put-routing](diagrams/act-put-routing.puml) | Activity | PUT decision logic | Technical 10 |
| [seq-create](diagrams/seq-create.puml) | Sequence | Creating a task from the client | Technical 10 |
| [seq-complete](diagrams/seq-complete.puml) | Sequence | Completing a task, Apple recurring quirk | Technical 10 |
| [seq-update-delete](diagrams/seq-update-delete.puml) | Sequence | Editing and deleting a task | Technical 10 |
| [deployment](diagrams/deployment.puml) | Deployment | Reference homelab setup and TLS options | Technical 15 |
| [test-levels](diagrams/test-levels.puml) | Layered stack | Test levels L1 to L6 | Testing 5 |
| [test-harness-integration](diagrams/test-harness-integration.puml) | Component | In-process integration harness with WireMock and fake time | Testing 5.3 |
| [test-apple-replay](diagrams/test-apple-replay.puml) | Activity | Capturing and replaying Apple client traffic | Testing 5.4 |
| [test-e2e-topology](diagrams/test-e2e-topology.puml) | Deployment | Compose setup for container end-to-end and contract tests | Testing 5.5, 5.6 |
| [test-pipeline](diagrams/test-pipeline.puml) | Activity | CI stages and quality gates | Testing 10 |

Shared styling for the non-C4 diagrams lives in [diagrams/style.iuml](diagrams/style.iuml). The C4 diagrams use the C4-PlantUML library bundled with PlantUML (`!include <C4/...>`), so no network access is needed.

## Rendering the diagrams
Run [diagrams/render.sh](diagrams/render.sh) after changing a `.puml` file. It uses a local `plantuml` command when available and falls back to the `plantuml/plantuml` Docker image. Graphviz is needed for the component, class, use case and deployment diagrams; the Docker image includes it.

```sh
./docs/diagrams/render.sh
```

IDE plugins (PlantUML Integration for JetBrains Rider and IntelliJ, PlantUML for VS Code) preview the sources directly.

## Keeping the documentation current
- Behaviour visible to users (new option, new rule, changed mapping): update the functional design and the README.
- Structure, protocol handling, caching, integration or deployment: update the technical design and the affected diagram.
- Test approach, levels or tooling: update the testing strategy, and the traceability table when tests are added.
- A deliberate choice that others might "fix": record the reason in `instructions.md` and summarise it in technical design chapter 17.
- When a finding is resolved, remove its row from `findings.md` and mention the fix in the changelog. New defects or risks go in `findings.md` or `backlog.md`, not in the designs.

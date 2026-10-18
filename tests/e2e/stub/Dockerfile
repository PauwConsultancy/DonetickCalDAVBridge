# Donetick stub for the L4 end-to-end tests: WireMock.Net as the dotnet-wiremock tool, serving the
# mapping files mounted at /stub/__admin/mappings (tests/fixtures/donetick/mappings).
FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG WIREMOCK_VERSION=2.19.0
RUN dotnet tool install --global dotnet-wiremock --version ${WIREMOCK_VERSION}

# The tool targets .NET 8; allow it to run on the .NET 10 runtime of this image.
ENV PATH="${PATH}:/root/.dotnet/tools" \
    DOTNET_ROLL_FORWARD=Major \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

WORKDIR /stub
EXPOSE 9091
HEALTHCHECK --interval=2s --timeout=2s --retries=30 CMD curl -fsS http://localhost:9091/__admin/health || exit 1
ENTRYPOINT ["dotnet-wiremock", "--Urls", "http://0.0.0.0:9091", "--ReadStaticMappings", "true", "--WireMockLogger", "WireMockConsoleLogger"]

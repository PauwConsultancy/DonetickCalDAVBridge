# A real Donetick for the L5 contract tests, built from the official release binary so the version
# is pinned by tag and checksum. Change both arguments together when upgrading.
FROM debian:bookworm-slim

ARG DONETICK_VERSION=v0.1.82
ARG DONETICK_SHA256=16430f8e71b4c71359668d183f946ff5fd38cb21302b4373c61b7e2fdc5e77f8

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /donetick
RUN curl -fsSL -o donetick.tar.gz \
      "https://github.com/donetick/donetick/releases/download/${DONETICK_VERSION}/donetick_Linux_x86_64.tar.gz" \
 && echo "${DONETICK_SHA256}  donetick.tar.gz" | sha256sum -c - \
 && tar xzf donetick.tar.gz \
 && rm donetick.tar.gz

ENV DT_ENV=selfhosted \
    DT_SQLITE_PATH=/data/donetick.db
RUN mkdir -p /data
EXPOSE 2021
HEALTHCHECK --interval=2s --timeout=2s --retries=60 CMD curl -fsS http://localhost:2021/api/v1/health || exit 1
ENTRYPOINT ["./donetick"]

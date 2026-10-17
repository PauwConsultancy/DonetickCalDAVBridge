#!/usr/bin/env sh
# Renders every PlantUML source in this folder to SVG in ./svg.
# Uses a local `plantuml` command when available, otherwise the plantuml/plantuml Docker image.
set -eu

cd "$(dirname "$0")"

if command -v plantuml >/dev/null 2>&1; then
  plantuml -tsvg -o svg ./*.puml
elif command -v docker >/dev/null 2>&1; then
  docker run --rm -v "$PWD":/data -w /data plantuml/plantuml -tsvg -o svg ./*.puml
else
  echo "Neither plantuml nor docker found. Install PlantUML (with Graphviz) or Docker." >&2
  exit 1
fi

echo "Rendered to $(pwd)/svg"

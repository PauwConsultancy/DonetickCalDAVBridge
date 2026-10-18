"""Merges Cobertura files from the test projects and checks the coverage gate (testing strategy, chapter 10).

    python tests/coverage/coverage_report.py artifacts/test-results/*.cobertura.xml
    python tests/coverage/coverage_report.py --update-baseline artifacts/test-results/*.cobertura.xml

Each test project writes its own Cobertura file; a line counts as covered when any project covered it.
Generated code (files under obj/, such as regex and options-validator source generators) is excluded,
so the figures are for hand-written code. Branch coverage per line takes the best project, which is an
approximation: Cobertura does not say which outcomes were taken.

Gate: line coverage of hand-written code must reach the floor and may not drop more than the
tolerance below the baseline stored in baseline.json (a ratchet). The report is printed as Markdown
and appended to $GITHUB_STEP_SUMMARY when that variable is set.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
BASELINE = HERE / "baseline.json"
CONDITION = re.compile(r"\((\d+)/(\d+)\)")


def load(files: list[str]) -> dict[str, dict[int, tuple[int, int, int]]]:
    """Returns {source file: {line: (hits, covered conditions, total conditions)}}, merged over files."""
    merged: dict[str, dict[int, tuple[int, int, int]]] = defaultdict(dict)
    for name in files:
        for cls in ET.parse(name).getroot().iter("class"):
            source = cls.get("filename", "")
            if re.search(r"[\\/]obj[\\/]", source):
                continue
            lines = merged[source]
            for line in cls.iter("line"):
                number = int(line.get("number", "0"))
                hits = int(line.get("hits", "0"))
                covered, total = 0, 0
                match = CONDITION.search(line.get("condition-coverage", ""))
                if match:
                    covered, total = int(match.group(1)), int(match.group(2))
                old = lines.get(number, (0, 0, 0))
                lines[number] = (old[0] + hits, max(old[1], covered), max(old[2], total))
    return merged


def relative(source: str) -> str:
    marker = "src/DonetickCalDav/"
    normalised = source.replace("\\", "/")
    return normalised[normalised.index(marker) + len(marker):] if marker in normalised else normalised


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("files", nargs="+", help="Cobertura XML files")
    parser.add_argument("--update-baseline", action="store_true", help="store the measured line coverage as baseline")
    args = parser.parse_args()

    merged = load(args.files)
    config = json.loads(BASELINE.read_text(encoding="utf-8"))
    rows = []
    total_lines = covered_lines = total_branches = covered_branches = 0
    for source, lines in sorted(merged.items(), key=lambda item: relative(item[0])):
        file_lines = len(lines)
        file_covered = sum(1 for hits, _, _ in lines.values() if hits > 0)
        file_branches = sum(total for _, _, total in lines.values())
        file_covered_branches = sum(covered for _, covered, _ in lines.values())
        total_lines += file_lines
        covered_lines += file_covered
        total_branches += file_branches
        covered_branches += file_covered_branches
        rows.append((relative(source), file_covered, file_lines, file_covered_branches, file_branches))

    line_rate = covered_lines / total_lines if total_lines else 0.0
    branch_rate = covered_branches / total_branches if total_branches else 0.0

    report = [
        "## Coverage of hand-written code",
        "",
        f"Lines: **{line_rate:.1%}** ({covered_lines} of {total_lines}); branches: **{branch_rate:.1%}** "
        f"({covered_branches} of {total_branches}). Floor {config['floor']:.0%}, baseline {config['line']:.1%}.",
        "",
        "| File | Lines | Branches |",
        "|------|-------|----------|",
    ]
    for name, file_covered, file_lines, file_covered_branches, file_branches in rows:
        lines_text = f"{file_covered / file_lines:.0%} ({file_covered}/{file_lines})" if file_lines else "n/a"
        branches_text = f"{file_covered_branches / file_branches:.0%} ({file_covered_branches}/{file_branches})" if file_branches else "n/a"
        report.append(f"| {name} | {lines_text} | {branches_text} |")
    text = "\n".join(report) + "\n"
    print(text)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(text)

    if args.update_baseline:
        config["line"] = round(line_rate, 4)
        BASELINE.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
        print(f"Baseline updated to {line_rate:.2%}.")
        return 0

    failures = []
    if line_rate < config["floor"]:
        failures.append(f"line coverage {line_rate:.2%} is below the floor of {config['floor']:.0%}")
    if line_rate < config["line"] - config["tolerance"]:
        failures.append(f"line coverage {line_rate:.2%} dropped below the baseline of {config['line']:.2%}")
    for failure in failures:
        print(f"Coverage gate failed: {failure}.", file=sys.stderr)
    if not failures and line_rate > config["line"] + config["tolerance"]:
        print(f"Coverage rose to {line_rate:.2%}; raise the baseline with --update-baseline.")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())

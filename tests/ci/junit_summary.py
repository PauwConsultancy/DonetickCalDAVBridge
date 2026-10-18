"""Writes a short Markdown summary of JUnit XML test results (one file per test project).

    python tests/ci/junit_summary.py artifacts/test-results

Prints the summary and appends it to $GITHUB_STEP_SUMMARY when set. Lists failed tests with the first
line of their message. Never fails the build itself: the test step does that.
"""

from __future__ import annotations

import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main(folder: str) -> int:
    files = sorted(Path(folder).glob("*.xml"))
    rows, failures = [], []
    for file in files:
        root = ET.parse(file).getroot()
        if root.tag not in ("testsuites", "testsuite"):
            continue
        suites = [root] if root.tag == "testsuite" else list(root.iter("testsuite"))
        for suite in suites:
            cases = list(suite.iter("testcase"))
            failed = [c for c in cases if c.find("failure") is not None or c.find("error") is not None]
            skipped = [c for c in cases if c.find("skipped") is not None]
            seconds = sum(float(c.get("time", "0") or 0) for c in cases)
            rows.append((suite.get("name", file.stem), len(cases), len(failed), len(skipped), seconds))
            for case in failed:
                node = case.find("failure") if case.find("failure") is not None else case.find("error")
                message = (node.get("message") or node.text or "").strip().splitlines()
                failures.append((case.get("classname", ""), case.get("name", ""), message[0] if message else ""))

    lines = ["## Test results", ""]
    if not rows:
        lines.append(f"No JUnit results found in `{folder}`.")
    else:
        lines += ["| Test assembly | Tests | Failed | Skipped | Time |", "|---------------|-------|--------|---------|------|"]
        lines += [f"| {Path(name).name} | {total} | {failed} | {skipped} | {seconds:.1f} s |"
                  for name, total, failed, skipped, seconds in rows]
    if failures:
        lines += ["", "### Failed tests", ""]
        lines += [f"- `{cls}.{name}`: {message}" for cls, name, message in failures]
    text = "\n".join(lines) + "\n"
    print(text)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "artifacts/test-results"))

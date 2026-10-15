"""Summarizes the raw.csv written by measure.sh as a Markdown table.

Builds run in interleaved pairs: run r without the package, then run r with it. For each pair the
overhead is (with / without - 1). The table gives the median of those per-pair overheads and their
interquartile range, so the figure is relative to the same project's build without the analyzer.
It also gives the analyzer time the compiler reported for ZeroAlloc.Analyzers, as a share of the
median build without the package.
"""

import csv
import statistics
import sys
from collections import defaultdict


def quartiles(values):
    if len(values) < 2:
        return values[0], values[0]
    q = statistics.quantiles(values, n=4, method="inclusive")
    return q[0], q[2]


def main(path):
    wall = defaultdict(dict)
    analyzer = defaultdict(list)
    order = []
    with open(path, newline="") as f:
        for row in csv.DictReader(f):
            if row["kind"] == "warmup":
                continue
            key = (row["scenario"], row["kind"])
            if key not in order:
                order.append(key)
            wall[key + (row["variant"],)][int(row["run"])] = int(row["wall_ms"])
            if row["variant"] == "on":
                analyzer[key].append(float(row["analyzer_s"]) * 1000)

    print("| Scenario | Build | Overhead, median | Overhead, IQR | ZA analyzer time, share of build | Pairs |")
    print("|---|---|---:|---:|---:|---:|")
    for scenario, kind in order:
        off = wall[(scenario, kind, "off")]
        on = wall[(scenario, kind, "on")]
        runs = sorted(set(off) & set(on))
        overhead = [(on[r] / off[r] - 1) * 100 for r in runs]
        med = statistics.median(overhead)
        q1, q3 = quartiles(sorted(overhead))
        share = statistics.median(analyzer[(scenario, kind)]) / statistics.median(off.values()) * 100
        print(
            f"| {scenario} | {kind} | {med:+.1f}% | {q1:+.1f}% to {q3:+.1f}% | {share:.1f}% | {len(runs)} |"
        )


if __name__ == "__main__":
    main(sys.argv[1])

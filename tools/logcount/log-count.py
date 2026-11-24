#!/usr/bin/env python3
"""Count the console lines of a client log by their shape (numbers as N, quoted text as '..'), so a print that fires per
packet or per frame shows at the top.

  log-count.py LOG [--top 30] [--max-repeat N]
      the line shapes of LOG, most frequent first; with --max-repeat, exit 1 when a shape outside the startup lines
      repeats more than N times
  log-count.py BEFORE AFTER [--top 30]
      the count of each shape in both logs, the largest drop first, and the totals

A busy session to compare: a field with a trap region and a few mob kills, run once with MS2_LIVE_CHECK_LOG=1 (every
check line) and once with MS2_LIVE_CHECK_LOG=0 (what a normal run prints).
"""
import argparse
import collections
import re
import sys

NUM = re.compile(r"(?<![A-Za-z_0-9])-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?(?![A-Za-z_])")
QUOTED = re.compile(r"'[^']*'")
HEX = re.compile(r"0x[0-9a-fA-F]+")


def shape(line: str) -> str:
    line = HEX.sub("0x#", line.rstrip("\n"))
    line = QUOTED.sub("'..'", line)
    return NUM.sub("N", line).replace("0x#", "0xN")


def count(path: str) -> collections.Counter:
    c = collections.Counter()
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            if line.strip():
                c[shape(line)] += 1
    return c


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("logs", nargs="+")
    ap.add_argument("--top", type=int, default=30)
    ap.add_argument("--max-repeat", type=int, default=0)
    a = ap.parse_args()
    if len(a.logs) == 1:
        c = count(a.logs[0])
        print(f"{sum(c.values())} lines, {len(c)} shapes")
        for s, n in c.most_common(a.top):
            print(f"{n:7d}  {s[:160]}")
        if a.max_repeat:
            over = [(s, n) for s, n in c.items() if n > a.max_repeat]
            for s, n in over:
                print(f"OVER {n} > {a.max_repeat}: {s[:160]}")
            return 1 if over else 0
        return 0
    if len(a.logs) != 2:
        ap.error("one log, or two to compare")
    b, f = count(a.logs[0]), count(a.logs[1])
    rows = sorted(set(b) | set(f), key=lambda s: (f[s] - b[s], -b[s]))
    print(f"before {sum(b.values())} lines, after {sum(f.values())} lines")
    for s in rows[: a.top]:
        print(f"{b[s]:7d} {f[s]:7d}  {s[:150]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

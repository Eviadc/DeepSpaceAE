"""Extract startup errors without using F8. Reads a log, writes a separate report only."""
import argparse
from pathlib import Path
import re

RELEVANT = re.compile(r"DeepSpaceAE|deepspaceae|Harmony|Exception|\bError\b|ModLoader|ModHook|LocHelper|localization|missing translation|no translation found|font asset|text object", re.IGNORECASE)


def console_glyph_warning(line):
    # Only the reported OfficeCodePro/[Text] family. Keep errors and other fonts/objects.
    if re.search(r"Exception|\bError\b", line):
        return False
    return ("The character with Unicode value" in line and "[OfficeCodePro" in line
            and "font asset" in line and "in text object [Text]" in line)


def extract(lines, context=12, limit=4000):
    result = []
    remaining = 0
    glyphs = 0
    sample = None
    truncated = False
    total = 0
    for total, line in enumerate(lines, 1):
        line = line.rstrip("\r\n")
        if console_glyph_warning(line):
            glyphs += 1
            sample = sample or line
            continue
        if RELEVANT.search(line):
            remaining = max(remaining, context + 1)
        if remaining:
            if len(result) < limit:
                result.append(f"{total}: {line}")
            else:
                truncated = True
            remaining -= 1
    header = ["DeepSpaceAE startup diagnostics (copy; original log unchanged)",
              f"Input lines: {total}", f"OfficeCodePro [Text] glyph warnings omitted from this copy: {glyphs}",
              f"Report truncated: {truncated}"]
    if sample:
        header += ["First matching font warning:", sample]
    return "\n".join(header + ["", "Relevant entries and following context:"] + result) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", type=Path, help="Player.log or GameLog_*.txt from the same startup")
    parser.add_argument("--output", type=Path, required=True, help="New report path; must differ from the original log")
    args = parser.parse_args()
    if args.log.resolve() == args.output.resolve():
        parser.error("Refusing to overwrite the original log")
    with args.log.open("r", encoding="utf-8-sig", errors="replace") as original:
        report = extract(original)
    with args.output.open("x", encoding="utf-8") as target:
        target.write(report)
    print(f"Report saved: {args.output}")


if __name__ == "__main__":
    main()

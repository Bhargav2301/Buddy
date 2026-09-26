#!/usr/bin/env python3
"""Keep Buddy's native versions and release notes in sync; no dependencies."""
import argparse
import datetime
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "apps/windows/Buddy.Windows/app.manifest"
CHANGELOG = ROOT / "CHANGELOG.md"


def parse(value):
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", value):
        raise ValueError("Use a version in major.minor.patch form, without a v prefix.")
    major, minor, patch = map(int, value.split("."))
    if major > 2000 or minor > 999 or patch > 999:
        raise ValueError("Version exceeds supported native version range (2000.999.999).")
    return major, minor, patch


def current():
    value = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    parse(value)
    return value


def notes(version):
    text = CHANGELOG.read_text(encoding="utf-8")
    match = re.search(rf"^## \[{re.escape(version)}\] - (\d{{4}}-\d{{2}}-\d{{2}})\n(.*?)(?=^## |\Z)", text, re.M | re.S)
    if not match or not match[2].strip() or "TODO:" in match[2]:
        raise ValueError(f"Add dated, complete release notes for {version} to CHANGELOG.md.")
    datetime.date.fromisoformat(match[1])
    return match[2].strip()


def check():
    version = current()
    identity = ET.parse(MANIFEST).getroot().find("{urn:schemas-microsoft-com:asm.v1}assemblyIdentity")
    if identity is None or identity.get("version") != version + ".0":
        raise ValueError("Windows manifest differs from VERSION. Use the bump command.")
    notes(version)
    ref = os.environ.get("GITHUB_REF", "")
    if ref.startswith("refs/tags/") and ref != "refs/tags/v" + version:
        raise ValueError(f"Tag {ref} does not match VERSION {version}.")
    major, minor, patch = parse(version)
    print(f"Buddy {version}; Windows {version}.0; Android code {major * 1_000_000 + minor * 1000 + patch + 1}")


def bump(value):
    old = current()
    if parse(value) <= parse(old):
        raise ValueError(f"New version must be greater than {old}; published versions are immutable.")
    changelog = CHANGELOG.read_text(encoding="utf-8")
    if re.search(rf"^## \[{re.escape(value)}\]", changelog, re.M):
        raise ValueError("That version already has release notes.")
    manifest = MANIFEST.read_text(encoding="utf-8")
    manifest, count = re.subn(r'(<assemblyIdentity\s+version=")[^"]+("\s+name="Buddy.Windows")', rf'\g<1>{value}.0\2', manifest)
    if count != 1:
        raise ValueError("Cannot locate the Windows assembly identity.")
    entry = f"## [{value}] - {datetime.date.today().isoformat()}\n\n- TODO: describe the changes and validation for this version.\n\n"
    offset = changelog.index("## [")
    (ROOT / "VERSION").write_text(value + "\n", encoding="utf-8")
    MANIFEST.write_text(manifest, encoding="utf-8")
    CHANGELOG.write_text(changelog[:offset] + entry + changelog[offset:], encoding="utf-8")
    print(f"Bumped {old} to {value}. Complete CHANGELOG.md, then run: python scripts/version.py check")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("check", help="Validate release version and changelog")
    command = commands.add_parser("bump", help="Prepare the next release version")
    command.add_argument("version")
    commands.add_parser("notes", help="Print release notes for the current version")
    args = parser.parse_args()
    try:
        if args.command == "check":
            check()
        elif args.command == "bump":
            bump(args.version)
        else:
            print(notes(current()))
    except (ValueError, OSError, ET.ParseError) as error:
        print(f"Version error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

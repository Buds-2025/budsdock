from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = ET.parse(ROOT / "src/BudsDock/BudsDock.csproj").findtext(".//Version")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description="Package the current BudsDock release.")
    parser.add_argument("--variant", choices=["portable", "compact"], default="portable")
    args = parser.parse_args()
    name = f"BudsDock-{VERSION}-win-x64-{args.variant}"
    source = ROOT / "artifacts/publish" / name
    if not (source / "BudsDock.exe").is_file():
        raise SystemExit(f"Missing publish output: {source}. Publish this variant first.")
    output = ROOT / "outputs"
    output.mkdir(exist_ok=True)
    destination = output / f"{name}.zip"
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(source.rglob("*")):
            if path.is_file() and path.suffix.lower() != ".pdb":
                archive.write(path, Path(name) / path.relative_to(source))
    checksum = sha256(destination)
    (output / f"{name}.sha256").write_text(f"{checksum}  {destination.name}\n", encoding="utf-8")
    manifest = {"version": VERSION, "variant": args.variant, "archive": destination.name,
                "archiveBytes": destination.stat().st_size, "executableBytes": (source / "BudsDock.exe").stat().st_size,
                "sha256": checksum, "runtimeRequired": args.variant == "compact"}
    (output / f"{name}.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()

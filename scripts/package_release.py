from __future__ import annotations

import hashlib
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUTS = ROOT / "outputs"
PUBLISH = ROOT / "artifacts" / "publish" / "BudsDock-1.0.0-win-x64"
PORTABLE_ZIP = OUTPUTS / "BudsDock-1.0.0-win-x64-portable.zip"

def add_tree(archive: zipfile.ZipFile, source: Path, archive_root: str) -> None:
    for path in sorted(source.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        archive.write(path, Path(archive_root) / relative)


def create_zip(destination: Path, source: Path, archive_root: str) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        add_tree(archive, source, archive_root)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


if not (PUBLISH / "BudsDock.exe").exists():
    raise SystemExit("Publish output is missing. Run dotnet publish first.")

create_zip(PORTABLE_ZIP, PUBLISH, "BudsDock-1.0.0-win-x64")
checksums = f"{sha256(PORTABLE_ZIP)}  {PORTABLE_ZIP.name}\n"
(OUTPUTS / "SHA256SUMS.txt").write_bytes(checksums.encode("utf-8"))

for output in (PORTABLE_ZIP, OUTPUTS / "SHA256SUMS.txt"):
    print(f"{output.name}: {output.stat().st_size} bytes")

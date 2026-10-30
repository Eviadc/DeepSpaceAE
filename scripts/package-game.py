"""Package only our explicitly listed mod files; never scan/copy a Managed or bin directory."""
from pathlib import Path
import hashlib
import json
import subprocess
import struct
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
files = {
    "DeepSpaceAE/DeepSpaceAE.Core.dll": root / "src/DeepSpaceAE.Core/bin/Release/netstandard2.1/DeepSpaceAE.Core.dll",
    "DeepSpaceAE/DeepSpaceAE.GameAdapter.dll": root / "src/DeepSpaceAE.GameAdapter/bin/Release/netstandard2.1/DeepSpaceAE.GameAdapter.dll",
    "DeepSpaceAE/manifest.xml": root / "assets/game/manifest.xml",
    "DeepSpaceAE/安装与测试.md": root / "docs/game-test.md",
}
for key in ("core", "pipeline", "item-storage-bus", "terminal"):
    files[f"DeepSpaceAE/Textures/Items/{key}.png"] = root / f"assets/game/Textures/Items/{key}.png"
for source in files.values():
    if not source.is_file():
        raise SystemExit(f"Missing package input: {source.name}. Build the game adapter first.")
declared = ET.parse(files["DeepSpaceAE/manifest.xml"]).getroot().findall("Assemblies/Assembly")
assert sorted(e.text for e in declared) == sorted(Path(n).name for n in files if n.endswith(".dll"))
assert sorted(Path(n).name for n in files if n.endswith(".dll")) == ["DeepSpaceAE.Core.dll", "DeepSpaceAE.GameAdapter.dll"]
if subprocess.check_output(["git", "status", "--porcelain"], cwd=root, text=True):
    raise SystemExit("Commit or preserve/inspect source changes before packaging a named revision.")
revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
for name, path in files.items():
    if name.endswith(".dll") and revision.encode("ascii") not in path.read_bytes():
        raise SystemExit(f"{path.name} does not contain the current source revision. Rebuild after committing.")
    if name.endswith(".png"):
        data = path.read_bytes()
        assert data[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", data[16:24]) == (16, 16)
output = root / "artifacts/packages" / f"DeepSpaceAE-0.1.0-alpha-100-REV3-{revision[:7]}.zip"
if output.exists():
    raise SystemExit("Package already exists; refusing to overwrite.")
output.parent.mkdir(parents=True, exist_ok=True)
hashes = {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in files.items()}
info = {
    "sourceCommit": revision,
    "gameLabel": "100-REV3",
    "steamBuildId": "26284217 (user-reported)",
    "referenceAssemblySha256": "0fd3de05d961c8a415758f8f78a8e0a32692b8b45fb34350df885ed8bd5e8eb0",
    "configuration": "Release",
    "targetFramework": "netstandard2.1",
    "gameRuntimeTest": "NOT RUN; to be tested by the user on Windows x64",
    "files": hashes,
}
with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED) as archive:
    for name, path in sorted(files.items()):
        archive.write(path, name)
    archive.writestr("DeepSpaceAE/build-info.json", json.dumps(info, ensure_ascii=False, indent=2) + "\n")
with zipfile.ZipFile(output) as archive:
    assert archive.testzip() is None
    assert set(archive.namelist()) == set(files) | {"DeepSpaceAE/build-info.json"}
    for name, checksum in hashes.items():
        assert hashlib.sha256(archive.read(name)).hexdigest() == checksum
checksum = hashlib.sha256(output.read_bytes()).hexdigest()
output.with_suffix(".zip.sha256").write_text(f"{checksum}  {output.name}\n")
print(output)
print("SHA256:", checksum)

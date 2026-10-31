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
    "DeepSpaceAE/加载诊断.md": root / "docs/load-troubleshooting.md",
    "DeepSpaceAE/extract-load-log.py": root / "scripts/extract-load-log.py",
}
keys = ["core", "pipeline", "item-storage-bus", "terminal"] + [f"pipeline-{m}" for m in range(16)]
keys += [k + "-off" for k in keys.copy()]
keys += ["capacity-upgrade", "tag-upgrade", "wrench"]
keys += [f"core-{i}" for i in range(12)]
assert len(keys) == len(set(keys)) == 55
for key in keys:
    files[f"DeepSpaceAE/Textures/Items/{key}.png"] = root / f"assets/game/Textures/Items/{key}.png"
files["DeepSpaceAE/THIRD-PARTY-NOTICES.md"] = root / "assets/THIRD-PARTY-NOTICES.md"
files["DeepSpaceAE/artwork-sources.json"] = root / "assets/ae2-source/sources.json"
for locale in ("en", "zh"):
    files[f"DeepSpaceAE/Localization/{locale}.csv"] = root / f"assets/game/Localization/{locale}.csv"
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
        size = 48 if Path(name).stem == "core" or Path(name).stem.startswith("core-") else 16
        assert data[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", data[16:24]) == (size, size)
version = ET.parse(files["DeepSpaceAE/manifest.xml"]).getroot().findtext("ModVersion")
assert version == "0.3.3-alpha"
audit_path = root / "artifacts/validation/game-patch-audit.json"
if not audit_path.is_file():
    raise SystemExit("Missing game patch metadata audit; run scripts/build-game.ps1 (Windows) or scripts/build-game.sh first.")
audit = json.loads(audit_path.read_text())
assert audit["success"] is True and audit["findings"]
assert all(f["Result"] == "PASS" for f in audit["findings"])
assert audit["adapterSha256"] == hashlib.sha256(files["DeepSpaceAE/DeepSpaceAE.GameAdapter.dll"].read_bytes()).hexdigest()
assert audit["gameSha256"] == "6398d44aa1675e1f02b63bc668868f970e0f5d3ae5a50f2aced8d06ca084e3b5"
output = root / "artifacts/packages" / f"DeepSpaceAE-{version}-100-Rev6-{revision[:7]}.zip"
if output.exists():
    with zipfile.ZipFile(output) as previous:
        expected = set(files) | {"DeepSpaceAE/build-info.json"}
        if set(previous.namelist()) != expected or any(previous.read(name) != path.read_bytes() for name, path in files.items()):
            raise SystemExit("Existing package differs from current build; preserve it elsewhere before retrying.")
        previous_info = json.loads(previous.read("DeepSpaceAE/build-info.json"))
        if previous_info["sourceCommit"] != revision or previous_info["referenceAssemblySha256"] != audit["gameSha256"]:
            raise SystemExit("Existing package provenance differs from current build.")
    print(output)
    print("Reusing unchanged package.")
    raise SystemExit(0)
output.parent.mkdir(parents=True, exist_ok=True)
hashes = {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in files.items()}
info = {
    "sourceCommit": revision,
    "gameLabel": "100-Rev6",
    "steamBuildId": "26337366 (local Steam appmanifest)",
    "referenceAssemblySha256": "6398d44aa1675e1f02b63bc668868f970e0f5d3ae5a50f2aced8d06ca084e3b5",
    "configuration": "Release",
    "patchMetadataAudit": {"status": "PASS", "declarations": len(audit["findings"]), "execution": "NOT RUN"},
    "modVersion": version,
    "artworkLicense": "CC-BY-NC-SA-3.0; see THIRD-PARTY-NOTICES.md",
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

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
    "DeepSpaceAE/README.md": root / "docs/game-test.md",
    "DeepSpaceAE/LICENSE": root / "LICENSE",
    "DeepSpaceAE/SOURCE.md": root / "SOURCE.md",
}
keys = ["core", "pipeline", "item-storage-bus", "terminal"] + [f"pipeline-{m}" for m in range(16)]
keys += [k + "-off" for k in keys.copy() if k != "item-storage-bus"]
keys += ["capacity-upgrade", "tag-upgrade", "wrench"]
keys += [f"core-{i}" for i in range(12)]
keys += [f"bus-face-{side}{suffix}" for side in range(4) for suffix in ("", "-off")]
keys += ["drive", "drive-off", "disk-small", "disk-large"]
keys += [f"{kind}-bus" for kind in ("import","export")]
keys += [f"{kind}-face-{side}{suffix}" for kind in ("import","export") for side in range(4) for suffix in ("","-off")]
keys += ["steve-portrait","steve-contact"]
keys += ["energy-receiver","energy-receiver-off"]
keys += ["drawer"]
keys += ["item-interface","item-interface-off","fluid-interface","fluid-interface-off","disk-ghost","engineer-start"]
keys += [f"device-port-{side}{suffix}" for side in range(4) for suffix in ("","-off")]
keys += [f"device-port-{depth}-{side}{suffix}" for depth in (3,4) for side in range(4) for suffix in ("","-off")]
assert len(keys) == len(set(keys)) == 119
for key in keys:
    files[f"DeepSpaceAE/Textures/Items/{key}.png"] = root / f"assets/game/Textures/Items/{key}.png"
files["DeepSpaceAE/THIRD-PARTY-NOTICES.md"] = root / "assets/THIRD-PARTY-NOTICES.md"
files["DeepSpaceAE/artwork-sources.json"] = root / "assets/ae2-source/sources.json"
files["DeepSpaceAE/drawer-artwork-sources.json"] = root / "assets/drawers-source/sources.json"
files["DeepSpaceAE/StorageDrawers-LICENSE.txt"] = root / "assets/drawers-source/LICENSE"
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
        stem = Path(name).stem
        dimensions = (288,211) if stem=="steve-portrait" else (19,19) if stem=="engineer-start" else (16,48) if stem=="wrench" else (48,48) if stem == "core" or stem.startswith("core-") else (32,32) if stem in ("drive","drive-off","energy-receiver","energy-receiver-off","drawer","terminal","terminal-off","item-interface","item-interface-off","fluid-interface","fluid-interface-off") else (32,16) if stem in ("disk-small","disk-large","disk-ghost","steve-contact") else (16,16)
        assert data[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", data[16:24]) == dimensions
version = ET.parse(files["DeepSpaceAE/manifest.xml"]).getroot().findtext("ModVersion")
assert version == "0.14.0-alpha"
audit_path = root / "artifacts/validation/game-patch-audit.json"
if not audit_path.is_file():
    raise SystemExit("Missing game patch metadata audit; run scripts/build-game.ps1 (Windows) or scripts/build-game.sh first.")
audit = json.loads(audit_path.read_text())
assert audit["success"] is True and audit["findings"]
assert all(f["Result"] == "PASS" for f in audit["findings"])
assert audit["adapterSha256"] == hashlib.sha256(files["DeepSpaceAE/DeepSpaceAE.GameAdapter.dll"].read_bytes()).hexdigest()
assert audit["gameSha256"] == "d9abc029f6b390619c31175df5735901aec6dc071c35c50ce5b3fdf92e3ede46"
output = root / "artifacts/packages" / f"DeepSpaceAE-{version}-100-Rev12-{revision[:7]}.zip"
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
    "gameLabel": "100-Rev12",
    "gameApplicationVersion": "0.46D",
    "steamBuildId": "26496118",
    "referenceAssemblySha256": "d9abc029f6b390619c31175df5735901aec6dc071c35c50ce5b3fdf92e3ede46",
    "configuration": "Release",
    "patchMetadataAudit": {"status": "PASS", "declarations": len(audit["findings"]), "execution": "NOT RUN"},
    "modVersion": version,
    "artworkLicense": "AE2 (including Fluix Researcher adaptation): CC-BY-NC-SA-3.0; StorageDrawers: MIT; see THIRD-PARTY-NOTICES.md",
    "targetFramework": "netstandard2.1",
    "gameRuntimeTest": "NOT RUN for this build",
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

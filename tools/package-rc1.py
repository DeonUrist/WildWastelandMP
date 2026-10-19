"""Package the current MP release from published base assets and shipped defaults."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import zipfile

REPO = Path(__file__).resolve().parents[1]
VERSION = "0.1.41"
RELEASE = "RC5"
BASE_SHA256 = "a8e96e62922435e3ae5da8b5d92493ec6e9c1da7bc092509461db94d4a468dda"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True,
                        help="Published WildWasteland-0.1.40-RC37.zip")
    parser.add_argument("--dll", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError(args.output)
    if hashlib.sha256(args.baseline.read_bytes()).hexdigest() != BASE_SHA256:
        raise ValueError("Published baseline checksum mismatch")
    if 'VERSION = "' + VERSION + '"' not in (REPO / "src/Core/Plugin.cs").read_text():
        raise ValueError("Source version mismatch")
    dll = args.dll.read_bytes()
    files = {}
    inherited = {}
    with zipfile.ZipFile(args.baseline) as old:
        for entry in old.infolist():
            parts = PurePosixPath(entry.filename).parts
            if not parts or parts[0] != "WildWasteland" or ".." in parts:
                raise ValueError("Unsafe baseline entry: " + entry.filename)
            if entry.is_dir():
                continue
            name = "/".join(parts[1:])
            if name.lower().endswith(".dll"):
                continue
            # The published Coyote bike is a shipped template, not a user's writable template folder.
            if name.startswith("Patrols/PlayerTemplates/"):
                name = name.replace("Patrols/PlayerTemplates/", "Patrols/BaseTemplates/", 1)
            files[name] = old.read(entry)
            inherited[name] = hashlib.sha256(files[name]).hexdigest()

    def put(source, destination):
        files[destination] = source.read_bytes()

    def tree(source, destination, suffixes=None):
        for path in sorted(source.rglob("*")):
            if path.is_file() and (suffixes is None or path.suffix.lower() in suffixes):
                put(path, destination + "/" + path.relative_to(source).as_posix())

    files["WildWastelandMP.dll"] = dll
    for name in ("README.md", "README-MULTIPLAYER.md", "WILD-WASTELAND-GUIDE.md",
                 "Start-WildWastelandMP.bat", "Launch-WildWastelandMP.ps1"):
        put(REPO / name, name)
    release_notes = "release-" + VERSION + "-" + RELEASE + ".md"
    put(REPO / "notes" / release_notes, "RELEASE-NOTES.md")
    for name in (release_notes, "release-0.1.39-RC3.md", "release-0.1.38-RC2.md", "release-0.1.37-RC1.md", "seated-melee-throws.md", "low-crawl.md",
                 "camp-merchant-idle.md", "weather-distance-haze.md"):
        put(REPO / "notes" / name, "notes/" + name)
    tree(REPO / "compat/ApocalypterMP", "compat/ApocalypterMP", {".md"})
    tree(REPO / "notes/sp-port-reference-2026-10-19/LoadingScreens", "LoadingScreens")
    tree(REPO / "src/Modules/PlayerBody/Embedded/CharacterPortraits", "PlayerBody/Portraits")
    put(REPO / "src/Modules/PlayerBody/Embedded/Textures/female_kick.png",
        "PlayerBody/Models/Player2_female_kick.png")
    tree(REPO / "src/Modules/Survival/Embedded", "Survival")
    tree(REPO / "src/Modules/Quests/Embedded", "Quests", {".wav", ".txt", ".example"})
    tree(REPO / "src/Modules/Patrols/Embedded/DefaultTemplates", "Patrols/BaseTemplates")
    put(REPO / "src/Modules/Patrols/Embedded/EditorDefaults.json", "Patrols/PatrolsAndLoot.json")

    readme = files["README.md"].decode("utf-8-sig")
    if "BepInEx 5" not in readme or "https://www.nexusmods.com/apocalypter/mods/10" not in readme:
        raise ValueError("Missing installation requirements")
    # Documentation remains in the repository and release page, outside the install ZIP.
    files = {name: data for name, data in files.items()
             if not name.lower().endswith((".md", ".pdf", ".rst"))
             and PurePosixPath(name).parts[0].lower() not in {"notes", "compat", "docs"}}
    names = sorted(files)
    for name in names:
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or ":" in name or "\\" in name:
            raise ValueError("Unsafe output entry: " + name)
        if path.suffix.lower() in {".cfg", ".es3", ".wwnav", ".log", ".pdb"}:
            raise ValueError("Personal/runtime data in archive: " + name)
        if any(part.lower() in {"saves", "backups", "cache", "playertemplates"} for part in path.parts):
            raise ValueError("Runtime directory in archive: " + name)
    if [n for n in names if n.lower().endswith(".dll")] != ["WildWastelandMP.dll"]:
        raise ValueError("Unexpected plugin/dependency DLL")
    if sum(name.startswith("LoadingScreens/Screen") and name.endswith(".png") for name in names) != 18:
        raise ValueError("Expected eighteen loading images")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.output, "x", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name in names:
            entry = zipfile.ZipInfo("WildWastelandMP/" + name, date_time=(2026, 10, 19, 12, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, files[name])
    with zipfile.ZipFile(args.output) as archive:
        if len(archive.namelist()) != len(set(archive.namelist())) or archive.testzip() is not None:
            raise ValueError("Duplicate entries or archive CRC failure")
        for name in names:
            if archive.read("WildWastelandMP/" + name) != files[name]:
                raise ValueError("Archive content mismatch: " + name)
    metadata = {
        "version": VERSION, "release": RELEASE, "handshake": 24,
        "archive": args.output.name, "entries": len(files), "bytes": args.output.stat().st_size,
        "sha256": hashlib.sha256(args.output.read_bytes()).hexdigest(),
        "dll_md5": hashlib.md5(dll).hexdigest().upper(),
        "baseline_sha256": BASE_SHA256,
        "unchanged_baseline_entries": sum(name in files and hashlib.sha256(files[name]).hexdigest() == digest
                                          for name, digest in inherited.items()),
        "dependency_dlls_bundled": False,
    }
    args.output.with_suffix(".package.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    args.output.with_suffix(".sha256").write_text(metadata["sha256"] + "  " + args.output.name + "\n", encoding="ascii")
    print(json.dumps(metadata, indent=2))


if __name__ == "__main__":
    main()

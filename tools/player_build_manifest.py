#!/usr/bin/env python3
"""Capture, create and verify supplementary Windows player identity receipts.

This tool does not run Unity, authenticate a compiler, or certify gameplay.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys
import zipfile

SOURCE_ROOTS = ("Assets", "Packages", "ProjectSettings")
RECEIPT = "BUILD-INFO.json"
ENCODING = "UTF-8 JSON array, keys sorted, compact separators, one final newline"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest_bytes(raw):
    return hashlib.sha256(raw).hexdigest()


def regular(path):
    path = Path(path).absolute()
    for parent in [path] + list(path.parents):
        info = parent.lstat()
        require(not stat.S_ISLNK(info.st_mode) and
                not getattr(info, "st_file_attributes", 0) & 0x400,
                "Symlink/reparse path refused: " + str(parent))
    require(path.is_file(), "Not a regular file: " + str(path))
    return path


def digest(path):
    path = regular(path)
    before = path.stat()
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    after = path.stat()
    require((before.st_size, before.st_mtime_ns, before.st_ino) ==
            (after.st_size, after.st_mtime_ns, after.st_ino), "File changed while hashing: " + str(path))
    return h.hexdigest()


def unique_json(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(regular(path).read_text(encoding="utf-8-sig"), object_pairs_hook=unique_json)


def encoded(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n").encode("utf-8")


def safe_name(name):
    require(isinstance(name, str) and name and "\\" not in name and ":" not in name and
            not name.startswith("/") and all(p not in ("", ".", "..") for p in name.split("/")),
            "Unsafe relative path: " + str(name))
    return name


def file_tree(directory, exclude_receipt=False):
    directory = Path(directory).absolute()
    require(directory.is_dir() and directory.resolve() == directory, "Invalid or linked directory")
    result, names = [], set()
    for current, folders, files in os.walk(directory, followlinks=False):
        for name in folders + files:
            p = Path(current) / name
            info = p.lstat()
            require(not stat.S_ISLNK(info.st_mode) and
                    not getattr(info, "st_file_attributes", 0) & 0x400, "Linked entry: " + str(p))
        for name in files:
            p = Path(current) / name
            relative = safe_name(p.relative_to(directory).as_posix())
            require(relative.casefold() not in names, "Case-colliding file: " + relative)
            names.add(relative.casefold())
            if exclude_receipt and relative == RECEIPT:
                continue
            require(relative.casefold() != RECEIPT.casefold(), "Unexpected existing build receipt")
            result.append(dict(path=relative, bytes=p.stat().st_size, sha256=digest(p)))
    return sorted(result, key=lambda item: item["path"])


def git(repo, *args, data=None):
    return subprocess.check_output(["git", "-C", str(repo)] + list(args), input=data)


def committed_inputs(repo, commit):
    require(re.fullmatch(r"[0-9a-f]{40}", commit or ""), "Use a full 40-character source commit")
    require(git(repo, "rev-parse", "HEAD").decode().strip() == commit, "Checkout HEAD differs from source commit")
    entries = []
    for record in git(repo, "ls-tree", "-rz", commit, "--", *SOURCE_ROOTS).split(b"\0"):
        if not record:
            continue
        metadata, raw_name = record.split(b"\t", 1)
        mode, kind, oid = metadata.split()
        name = safe_name(raw_name.decode("utf-8"))
        require(kind == b"blob" and mode in (b"100644", b"100755"), "Nonregular committed source: " + name)
        entries.append((name, oid))
    require(entries, "No committed Unity inputs")
    stream = io.BytesIO(git(repo, "cat-file", "--batch", data=b"\n".join(oid for _, oid in entries) + b"\n"))
    result = {}
    for name, expected in entries:
        oid, kind, size = stream.readline().split()
        raw = stream.read(int(size))
        require(oid == expected and kind == b"blob" and stream.read(1) == b"\n", "Invalid Git blob response")
        result[name] = digest_bytes(raw)
    actual = {}
    for folder in SOURCE_ROOTS:
        for item in file_tree(Path(repo) / folder):
            actual[folder + "/" + item["path"]] = item["sha256"]
    require(actual == result, "Dirty, missing or extra Unity source files differ from the commit")
    staged = git(repo, "diff", "--cached", "--name-only", commit, "--", *SOURCE_ROOTS)
    require(not staged.strip(), "Staged Unity inputs differ from the commit")
    return result


def archive_inputs(path):
    container_sha256 = digest(path)
    result, folded = {}, set()
    with zipfile.ZipFile(path) as archive:
        for info in archive.infolist():
            if info.is_dir():
                safe_name(info.filename.rstrip("/"))
                continue
            name = safe_name(info.filename)
            require(name.split("/")[0] in SOURCE_ROOTS, "Archive contains non-source entry: " + name)
            require(name.casefold() not in folded, "Duplicate/case-colliding archive entry: " + name)
            folded.add(name.casefold())
            require(not stat.S_ISLNK(info.external_attr >> 16), "Archive symlink refused: " + name)
            with archive.open(info) as stream:
                h = hashlib.sha256()
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    h.update(block)
            result[name] = h.hexdigest()
    require(result, "Empty source archive")
    require(digest(path) == container_sha256, "Archive changed while validating payloads")
    return result, container_sha256


def zip_inputs(path):
    return archive_inputs(path)[0]


def path_key(value):
    value = str(value).replace("\\", "/")
    if re.match(r"^[A-Za-z]:/", value):
        return value.casefold().rstrip("/")
    value = str(Path(value).absolute()).replace("\\", "/")
    match = re.match(r"^/mnt/([A-Za-z])/(.*)$", value)
    return (match[1] + ":/" + match[2]).casefold().rstrip("/") if match else value.rstrip("/")


def argument(command, name):
    require(command.count(name) == 1, "Build command requires exactly one " + name)
    index = command.index(name)
    require(index + 1 < len(command), "Missing build command argument")
    return command[index + 1]


def execution_evidence(execution_path, log_path, runner_path, player):
    execution = read_json(execution_path)
    require(execution.get("mode") == "build" and execution.get("exit_code") == 0 and
            execution.get("stopped") is None, "Build did not finish successfully")
    require(execution.get("runner_sha256") == digest(runner_path), "Build runner hash mismatch")
    command = execution.get("command", [])
    require(isinstance(command, list) and all(isinstance(x, str) for x in command), "Invalid build command")
    require(argument(command, "-executeMethod") == "CastlePlayerBuild.BuildForBatch", "Wrong build entry point")
    log_flag = "--log-file" if "--log-file" in command else "-logFile"
    require(path_key(argument(command, log_flag)) == path_key(log_path), "Build log path mismatch")
    output = execution.get("player_output", "")
    filename = output.replace("\\", "/").rsplit("/", 1)[-1]
    require(filename.lower().endswith(".exe") and filename != ".exe", "Missing Windows player output")
    require(path_key(output) == path_key(Path(player) / filename), "Build output directory mismatch")
    text = regular(log_path).read_text(encoding="utf-8-sig", errors="replace")
    summaries = re.findall(r"Castle Windows build: (\w+); errors=(\d+); warnings=(\d+); bytes=(\d+); duration=([^\r\n]+)", text)
    require(len(summaries) == 1 and summaries[0][0] == "Succeeded" and summaries[0][1] == "0",
            "Missing unique successful build summary")
    summary = summaries[0]
    return execution, filename, dict(result=summary[0], errors=int(summary[1]), warnings=int(summary[2]),
                                     bytes=int(summary[3]), duration=summary[4])


def capture(execution, editor_log, runner, player_dir, project, imported_archive):
    evidence_hashes = [digest(p) for p in (execution, editor_log, runner)]
    data, exe, summary = execution_evidence(execution, editor_log, runner, player_dir)
    command = data["command"]
    if "-projectPath" in command:
        recorded_project = argument(command, "-projectPath")
    else:
        recorded_project = argument(command, "run")
    require(path_key(project) == path_key(recorded_project), "Recorded project path mismatch")
    imported = {}
    for folder in SOURCE_ROOTS:
        for item in file_tree(Path(project) / folder):
            imported[folder + "/" + item["path"]] = item["sha256"]
    archived, archive_sha256 = archive_inputs(imported_archive)
    require(archived == imported, "Imported archive differs from actual completed project source")
    files = file_tree(player_dir)
    names = {x["path"] for x in files}
    require(exe in names and "UnityPlayer.dll" in names and
            any(n.startswith(exe[:-4] + "_Data/") for n in names), "Incomplete Windows player directory")
    require(sum(x["bytes"] for x in files) == summary["bytes"], "Engine bytes disagree with the build summary")
    require(files == file_tree(player_dir), "Player changed during completion capture")
    current = {folder + "/" + item["path"]: item["sha256"] for folder in SOURCE_ROOTS
               for item in file_tree(Path(project) / folder)}
    require(current == imported and digest(imported_archive) == archive_sha256 and
            evidence_hashes == [digest(p) for p in (execution, editor_log, runner)],
            "Source archive or build evidence changed during capture")
    return dict(schema_version=1, kind="Player files observed after recorded build completion",
                execution_sha256=evidence_hashes[0], editor_log_sha256=evidence_hashes[1],
                runner_sha256=evidence_hashes[2], player_output=data["player_output"],
                source_sha256=data.get("source_sha256"), imported_source_sha256=imported,
                imported_archive_sha256=archive_sha256, build=summary, engine_files=files)


def import_review(path, commit, original, imported, source_sha256, imported_sha256):
    changed = sorted(p for p in set(original) | set(imported) if original.get(p) != imported.get(p))
    if not changed:
        require(path is None, "Import review supplied for unchanged inputs")
        return None
    require(path is not None, "Imported source differs; exact reviewed hash pairs are required")
    review = read_json(path)
    require(review.get("schema_version") == 1 and review.get("source_commit") == commit and
            review.get("source_archive_sha256") == source_sha256 and
            review.get("imported_archive_sha256") == imported_sha256 and
            review.get("unreviewed_pairs") == 0 and review.get("unresolved_findings") == 0,
            "Import review provenance is incomplete or mismatched")
    pairs = review.get("differences", [])
    require(isinstance(pairs, list) and len(pairs) == len(changed) and
            {p.get("path") for p in pairs} == set(changed), "Review must cover each changed path exactly once")
    for pair in pairs:
        name = pair["path"]
        asset_import = name.startswith("Assets/") and PurePosixPath(name).suffix in (".mat", ".unity", ".asset", ".meta")
        settings_import = name in ("ProjectSettings/ProjectAuditorSettings.asset",
                                   "ProjectSettings/ProjectSettings.asset", "ProjectSettings/SceneTemplateSettings.json")
        require((asset_import or settings_import) and name in imported and
                (name in original or name.endswith(".meta") or name == "ProjectSettings/SceneTemplateSettings.json"),
                "Unreviewable code/config/removal drift: " + name)
        require(pair.get("source_sha256") == original.get(name) and pair.get("imported_sha256") == imported[name]
                and isinstance(pair.get("reason"), str) and bool(pair["reason"].strip()), "Invalid reviewed pair: " + name)
    return dict(sha256=digest(path), differences=pairs)


def create(repo, commit, source_archive, imported_archive, review, completion, execution, editor_log, runner, player_dir):
    evidence_paths = [execution, editor_log, runner, completion] + ([review] if review is not None else [])
    evidence_hashes = [digest(p) for p in evidence_paths]
    original = committed_inputs(repo, commit)
    archived, source_sha256 = archive_inputs(source_archive)
    require(archived == original, "Source archive differs from committed Unity inputs")
    imported, imported_sha256 = archive_inputs(imported_archive)
    reviewed = import_review(review, commit, original, imported, source_sha256, imported_sha256)
    observed = read_json(completion)
    run, exe, summary = execution_evidence(execution, editor_log, runner, player_dir)
    require(observed.get("schema_version") == 1 and observed.get("execution_sha256") == digest(execution) and
            observed.get("editor_log_sha256") == digest(editor_log) and observed.get("runner_sha256") == digest(runner),
            "Completion manifest is not bound to these build records")
    # A warm project may already contain some reviewed import results. Every
    # committed path is still required; additions may only come from the final
    # reviewed archive, and each payload must be one of that path's exact pair.
    pre_build = run.get("source_sha256")
    require(isinstance(pre_build, dict) and set(original) <= set(pre_build) <= set(imported) and
            all(value == imported[name] or (name in original and value == original[name])
                for name, value in pre_build.items()) and observed.get("source_sha256") == pre_build,
            "Recorded pre-build inputs must match exact committed/reviewed hashes and completion evidence")
    require(observed.get("imported_source_sha256") == imported and
            observed.get("imported_archive_sha256") == imported_sha256,
            "Imported archive was not captured with these build outputs")
    require(observed.get("player_output") == run["player_output"] and observed.get("build") == summary,
            "Completion manifest build identity mismatch")
    files = file_tree(player_dir, exclude_receipt=True)
    names = {item["path"] for item in files}
    require(exe in names and "UnityPlayer.dll" in names and
            any(name.startswith(exe[:-4] + "_Data/") for name in names), "Incomplete Windows player directory")
    require(files == observed.get("engine_files") and sum(x["bytes"] for x in files) == summary["bytes"],
            "Player files differ from completion hashes or build byte count")
    version_text = (Path(repo) / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    match = re.search(r"^m_EditorVersion: (\S+)$", version_text, re.M)
    require(match is not None, "Missing pinned Unity version")
    editor = argument(run["command"], "--editor-path").replace("\\", "/")
    require("/" + match[1] + "/Editor/Unity.exe" in editor, "Recorded editor differs from pinned Unity version")
    # Recheck source and outputs before publishing. No source or engine file is written.
    require(committed_inputs(repo, commit) == original and file_tree(player_dir, exclude_receipt=True) == files,
            "Source or player changed during receipt validation")
    require(digest(source_archive) == source_sha256 and digest(imported_archive) == imported_sha256 and
            [digest(p) for p in evidence_paths] == evidence_hashes, "Evidence changed during receipt validation")
    return dict(schema_version=2, kind="Supplementary identity receipt; not emitted by Unity",
                source_commit=commit, source_archive_sha256=source_sha256, unity=match[1],
                target="StandaloneWindows64", result="Succeeded", executable=exe,
                pre_build_source_sha256=pre_build,
                imported_archive_sha256=imported_sha256, imported_source_sha256=imported,
                import_review=reviewed, engine_files=files, engine_file_count=len(files),
                engine_bytes=sum(x["bytes"] for x in files), engine_manifest_sha256=digest_bytes(encoded(files)),
                engine_manifest_encoding=ENCODING, build=summary,
                provenance={"execution_sha256":evidence_hashes[0], "editor_log_sha256":evidence_hashes[1],
                            "runner_sha256":evidence_hashes[2], "completion_manifest_sha256":evidence_hashes[3]},
                limitations=["Unsigned local build association, not compiler attestation.",
                             "Build success does not establish gameplay, standalone launch, cold-cache or human acceptance."])


def write_once(path, value):
    path = Path(path)
    raw = (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    require(path.parent.is_dir() and path.parent.resolve() == path.parent.absolute(), "Invalid output parent")
    if path.exists() or path.is_symlink():
        require(regular(path).read_bytes() == raw, "Existing identity differs; refusing overwrite")
        return digest_bytes(raw)
    # Exclusive creation never replaces an existing identity. If interrupted, a
    # partial file is deliberately retained and future invocations refuse it.
    with path.open("xb") as stream:
        stream.write(raw)
        stream.flush()
        os.fsync(stream.fileno())
    return digest(path)


def verify(player_dir, receipt_sha256, source_archive=None, commit=None):
    path = Path(player_dir) / RECEIPT
    require(re.fullmatch(r"[0-9a-f]{64}", receipt_sha256 or "") and digest(path) == receipt_sha256,
            "Receipt differs from the separately retained trusted hash")
    receipt = read_json(path)
    require(receipt.get("schema_version") == 2 and receipt.get("result") == "Succeeded" and
            receipt.get("target") == "StandaloneWindows64", "Unsupported or failed build receipt")
    require(re.fullmatch(r"[0-9a-f]{40}", receipt.get("source_commit", "")), "Invalid receipt source commit")
    if commit is not None:
        require(receipt["source_commit"] == commit, "Unexpected source commit")
    if source_archive is not None:
        require(digest(source_archive) == receipt.get("source_archive_sha256"), "Unexpected source archive")
    files = file_tree(player_dir, exclude_receipt=True)
    require(files == receipt.get("engine_files") and len(files) == receipt.get("engine_file_count") and
            sum(x["bytes"] for x in files) == receipt.get("engine_bytes") and
            digest_bytes(encoded(files)) == receipt.get("engine_manifest_sha256"), "Engine files differ from receipt")
    return dict(status="verified", source_commit=receipt["source_commit"], engine_file_count=len(files),
                engine_bytes=receipt["engine_bytes"], receipt_sha256=receipt_sha256)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="operation", required=True)
    cap = sub.add_parser("capture", help="Retain engine hashes immediately after successful build, before delivery")
    make = sub.add_parser("create", help="Validate source/build provenance and create BUILD-INFO.json once")
    check = sub.add_parser("verify", help="Verify delivered player against a separately retained receipt hash")
    for item in (cap, make):
        for arg in ("execution", "editor-log", "runner", "player-dir"):
            item.add_argument("--" + arg, type=Path, required=True)
    cap.add_argument("--output", type=Path, required=True)
    cap.add_argument("--project", type=Path, required=True)
    cap.add_argument("--imported-archive", type=Path, required=True)
    for arg in ("repo", "source-archive", "imported-archive", "completion"):
        make.add_argument("--" + arg, type=Path, required=True)
    make.add_argument("--commit", required=True)
    make.add_argument("--review", type=Path)
    check.add_argument("--player-dir", type=Path, required=True)
    check.add_argument("--receipt-sha256", required=True)
    check.add_argument("--source-archive", type=Path)
    check.add_argument("--commit")
    args = vars(parser.parse_args(argv))
    operation = args.pop("operation")
    try:
        if operation == "capture":
            output = args.pop("output")
            require(output.absolute() != Path(args["player_dir"]).absolute() and
                    Path(args["player_dir"]).absolute() not in output.absolute().parents,
                    "Store completion manifest outside the engine output directory")
            value = capture(**args)
            result = dict(status="captured", manifest_sha256=write_once(output, value),
                          engine_file_count=len(value["engine_files"]))
        elif operation == "create":
            value = create(**args)
            result = dict(status="created", source_commit=value["source_commit"],
                          receipt_sha256=write_once(args["player_dir"] / RECEIPT, value),
                          engine_file_count=value["engine_file_count"], engine_bytes=value["engine_bytes"])
        else:
            result = verify(**args)
        print(json.dumps(result, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        print("Build identity refused: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

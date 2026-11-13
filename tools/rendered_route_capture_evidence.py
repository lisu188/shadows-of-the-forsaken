"""Pure structural validation of the two existing FullCastleRoute capture sets.

These are explicit fixture captures, not ordinary rendering, image quality or
human acceptance. The caller separately binds source, fresh NUnit/teardown,
process, logs and ordinary-render/performance evidence. No engine is launched.
"""
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import stat


# Resolve the tracked sibling explicitly, independent of cwd and sys.path.
_spec = importlib.util.spec_from_file_location(
    "forsaken_route_capture_png", Path(__file__).resolve().with_name("presentation_evidence.py"))
_png = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_png)
decode_png = _png.decode_png

CASES = {
    "main": "CastleMainRouteCompletesThroughControlsWithoutSecret",
    "secret": "CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight",
}
ENCOUNTERS = {
    "FirstEnemyDefeated": "First Demon",
    "MinibossDefeated": "Corrupted Guard",
    "FinalEnemyDefeated": "Final Demon",
}
COMMON_CAPTURES = (
    "route-courtyard", "route-fight-FirstEnemyDefeated", "route-rune-lever",
    "route-fight-MinibossDefeated", "route-throne", "route-library-sealed",
    "route-library", "route-catacombs", "route-final-ready", "route-fight-FinalEnemyDefeated",
)
FRAMING_SCOPE = ("Actual front-facing actor triangles at the observed windup; player obscuration and viewport only. "
                 "Not environment, lighting or human acceptance.")
MAX_FILE_BYTES = 8 * 1024 * 1024
MAX_TOTAL_BYTES = 64 * 1024 * 1024
MAX_FRAMING_BYTES = 64 * 1024
MAX_INPUTS = 100_000
INPUT_KEYS = frozenset(("W", "A", "S", "D", "E"))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def number(value, label, minimum=None, maximum=None):
    try:
        finite = type(value) in (int, float) and math.isfinite(value)
    except OverflowError:
        finite = False
    require(finite, "Invalid finite " + label)
    require((minimum is None or value >= minimum) and (maximum is None or value <= maximum),
            "Out-of-range " + label)
    return value


def integer(value, label, minimum=0, maximum=5_000_000):
    require(type(value) is int and minimum <= value <= maximum, "Invalid integer " + label)
    return value


def fields(value, expected, label):
    require(type(value) is dict and set(value) == set(expected), "Unexpected/missing " + label + " fields")


def unique_json(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def parse_json(raw):
    def invalid(value):
        raise ValueError("Nonfinite JSON: " + value)
    try:
        return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique_json, parse_constant=invalid)
    except RecursionError as error:
        raise ValueError("Excessively nested capture JSON") from error


def expected_files(kind):
    require(kind in CASES, "Unknown route kind")
    name = CASES[kind]
    captures = COMMON_CAPTURES + (("route-relic", "route-complete-secret") if kind == "secret"
                                  else ("route-complete-main",))
    return {
        "png": tuple(name + "-" + capture + ".png" for capture in captures),
        "framing": tuple(name + "-route-fight-" + objective + "-framing.json" for objective in ENCOUNTERS),
        "trace": name + "-input.json",
    }


def expected_progression(secret):
    """Ordered Changed callbacks from the unchanged RunRoute progression path."""
    first = "FirstEnemyDefeated"
    puzzle = first + ", MainPuzzleSolved"
    throne = puzzle + ", MinibossDefeated"
    library = throne + ", LibraryOpened"
    result = ["FirstEncounter:None", "FirstEncounter:" + first, "Puzzle:" + first,
              "Puzzle:" + puzzle, "FirstEncounter:" + puzzle, "ThroneRoom:" + puzzle,
              "ThroneRoom:" + throne, "Library:" + throne, "Library:" + library, "Catacombs:" + library]
    flags = library
    if secret:
        lever = library + ", SecretLeverPulled"
        flags = lever + ", BonusDiscovered"
        result += ["Catacombs:" + lever, "BonusRoom:" + lever, "BonusRoom:" + flags,
                   "ThroneRoom:" + flags, "Library:" + flags, "Catacombs:" + flags]
    final = flags + ", FinalEnemyDefeated"
    return result + ["FinalArena:" + flags, "FinalArena:" + final, "Exit:" + final]


def validate_trace(value, secret):
    fields(value, ("seconds", "secret", "health", "progression", "inputs"), "route trace")
    require(type(value["secret"]) is bool and value["secret"] is secret, "Route kind differs")
    seconds = number(value["seconds"], "completed session seconds", 0)
    require(seconds > 0, "Completed route has no elapsed time")
    # The saved player has maximumHealth=200 (CastleLayoutBuilder), not a DOCX balance rule.
    health = integer(value["health"], "surviving player health", 1, 200)
    require(value["progression"] == expected_progression(secret), "Route progression order/objectives differ")
    inputs = value["inputs"]
    require(type(inputs) is list and 2 <= len(inputs) <= MAX_INPUTS, "Missing/excessive input transitions")
    previous_time, previous_signature, used, attack_seen = -1, None, set(), False
    for item in inputs:
        fields(item, ("time", "keys", "attack"), "input transition")
        timestamp = number(item["time"], "input time", 0)
        keys = item["keys"]
        require(type(keys) is list and len(keys) <= len(INPUT_KEYS) and
                all(type(key) is str and key in INPUT_KEYS for key in keys) and len(set(keys)) == len(keys),
                "Foreign/duplicate input key")
        require(type(item["attack"]) is bool, "Invalid attack boolean")
        require(timestamp >= previous_time, "Input times reversed")
        signature = (tuple(keys), item["attack"])
        require(signature != previous_signature, "Repeated input state is not a transition")
        previous_time, previous_signature = timestamp, signature
        used.update(keys)
        attack_seen |= item["attack"]
    require(inputs[0] == {"time": 0, "keys": [], "attack": False}, "Input trace did not start released at zero")
    require(inputs[-1]["keys"] == [] and inputs[-1]["attack"] is False, "Final input was not released")
    require({"W", "A", "D", "E"} <= used and attack_seen, "Movement/interaction/attack inputs missing")
    # Input.time uses Time.time; session seconds freeze at completion. Ties and
    # final input times slightly after that frozen value are valid fixture data.
    return {"seconds": seconds, "secret": secret, "health": health,
            "progression": value["progression"], "input_count": len(inputs),
            "first_input_time": inputs[0]["time"], "last_input_time": inputs[-1]["time"],
            "input_keys": sorted(used), "attack_observed": attack_seen, "final_input_released": True}


def vector(value, label, quaternion=False):
    keys = ("x", "y", "z", "w") if quaternion else ("x", "y", "z")
    fields(value, keys, label)
    values = [number(value[key], label + "." + key) for key in keys]
    if quaternion:
        require(math.isclose(sum(component * component for component in values), 1, abs_tol=.0001),
                "Nonunit camera quaternion")


def validate_framing(value, test, objective):
    floats = ("cameraDistance", "cameraHeight", "cameraPivotHeight", "lookHeightOffset", "shoulderOffset",
              "shoulderAimFraction", "fieldOfView", "aspect", "measurementMilliseconds")
    integers = ("columns", "rows", "sampledRays", "playerMeshes", "enemyMeshes")
    vectors = ("playerPosition", "enemyPosition", "cameraPosition")
    fields(value, ("scope", "encounter", "test", "geometryFailure", "cameraRotation", "regions",
                   *vectors, *floats, *integers), "combat framing")
    require(objective in ENCOUNTERS and value["scope"] == FRAMING_SCOPE and value["test"] == test and
            value["encounter"] == ENCOUNTERS[objective], "Framing case/encounter/scope differs")
    require(value["geometryFailure"] == "", "Combat framing geometry failed")
    for key in vectors:
        vector(value[key], key)
    vector(value["cameraRotation"], "cameraRotation", True)
    for key in floats:
        number(value[key], key)
    require(value["cameraDistance"] > 0 and value["cameraHeight"] >= 0 and value["cameraPivotHeight"] >= 0 and
            0 <= value["shoulderAimFraction"] <= 1 and 1 <= value["fieldOfView"] < 179 and
            value["measurementMilliseconds"] >= 0 and math.isclose(value["aspect"], 16 / 9, abs_tol=.0001),
            "Invalid framing camera/measurement configuration")
    for key in integers:
        integer(value[key], key, 1)
    require((value["columns"], value["rows"], value["sampledRays"]) == (48, 64, 3072),
            "Framing sample grid differs")
    names = ["head", "torso", "right attack arm"]
    if objective != "MinibossDefeated":
        names.append("left attack arm")
    regions = value["regions"]
    require(type(regions) is list and len(regions) == len(names), "Framing regions missing/excessive")
    summaries = []
    for region, name in zip(regions, names):
        fields(region, ("name", "silhouetteSamples", "visibleSamples", "playerBlockedSamples",
                        "outsideViewSamples", "visibleFraction"), "framing region")
        require(region["name"] == name, "Framing region order/name differs")
        counts = {key: integer(region[key], key, 0, 3072) for key in
                  ("silhouetteSamples", "visibleSamples", "playerBlockedSamples", "outsideViewSamples")}
        silhouette, visible = counts["silhouetteSamples"], counts["visibleSamples"]
        require(silhouette >= 12 and silhouette == visible + counts["playerBlockedSamples"] + counts["outsideViewSamples"],
                "Insufficient/inconsistent framing samples")
        fraction = number(region["visibleFraction"], "visible fraction", .5, 1)
        require(math.isclose(fraction, visible / silhouette, rel_tol=0, abs_tol=.000001),
                "Framing visible fraction differs from samples")
        summaries.append({"name": name, **counts, "visibleFraction": fraction})
    require(sum(region["silhouetteSamples"] for region in regions) <= value["sampledRays"],
            "Framing regions exceed sampled rays")
    return {"objective": objective, "encounter": value["encounter"], "geometry_failure": None,
            "sampled_rays": value["sampledRays"], "regions": summaries}


def _token(info):
    return info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns


def _unlinked(path):
    require(not path.is_symlink() and path.resolve() == path.absolute(), "Capture path is linked/escaped")


def validate_captures(output):
    """Read output/captures: exactly31 confined bounded files, including23 decoded PNGs.

    SHA and timestamps are returned for the caller's freshness/run binding. No
    cleanup, authenticity, human quality or ordinary-render claim is inferred.
    """
    output = Path(output).absolute()
    root = output / "captures"
    _unlinked(output)
    _unlinked(root)
    require(root.is_dir(), "Capture directory missing")
    expected = {name for kind in CASES for group in expected_files(kind).values()
                for name in ((group,) if type(group) is str else group)}
    seen = set()
    for entry in root.iterdir():
        require(entry.name in expected and entry.name not in seen, "Unexpected capture artifact: " + entry.name)
        seen.add(entry.name)
    require(seen == expected and len(seen) == 31, "Missing route capture artifacts")
    files, total = [], 0

    def raw(name, maximum=MAX_FILE_BYTES):
        nonlocal total
        path = root / name
        _unlinked(path)
        info = path.lstat()
        require(stat.S_ISREG(info.st_mode) and not (getattr(info, "st_file_attributes", 0) & 0x400) and
                0 < info.st_size <= min(MAX_FILE_BYTES, maximum), "Nonregular/linked/excessive capture file: " + name)
        require(total + info.st_size <= MAX_TOTAL_BYTES, "Capture total size exceeds bound")
        with path.open("rb") as handle:
            require(_token(os.fstat(handle.fileno())) == _token(info), "Capture changed before read")
            data = handle.read(min(MAX_FILE_BYTES, maximum) + 1)
            require(_token(os.fstat(handle.fileno())) == _token(info), "Capture changed during read")
        _unlinked(path)
        require(_token(path.lstat()) == _token(info) and len(data) == info.st_size,
                "Capture changed during validation")
        total += len(data)
        files.append({"path": "captures/" + name, "bytes": len(data),
                      "sha256": hashlib.sha256(data).hexdigest(), "mtime_ns": info.st_mtime_ns})
        return data

    cases = []
    for kind, test in CASES.items():
        names = expected_files(kind)
        for name in names["png"]:
            width, height, bpp, _rows = decode_png(raw(name), (960, 540))
            files[-1]["PNG"] = {"width": width, "height": height, "bytes_per_pixel": bpp, "decoded": True}
        framing = [validate_framing(parse_json(raw(name, MAX_FRAMING_BYTES)), test, objective)
                   for name, objective in zip(names["framing"], ENCOUNTERS)]
        trace = validate_trace(parse_json(raw(names["trace"])), kind == "secret")
        cases.append({"kind": kind, "test": test, "png_count": len(names["png"]),
                      "framing": framing, "trace": trace})
    return {"schema_version": 1, "file_count": len(files), "png_count": 23, "framing_count": 6,
            "trace_count": 2, "total_bytes": total, "files": files, "cases": cases,
            "limits": "Explicit fixture capture structure only; caller binds freshness/source/NUnit/teardown. "
                      "No ordinary rendering, frame performance, physical audio or human image-quality acceptance."}

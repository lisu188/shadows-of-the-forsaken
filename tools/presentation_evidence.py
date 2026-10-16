"""Validate retained native HUD/exploration evidence without Unity or Pillow.

The harness must separately require the exact three passed NUnit cases, including
their teardown, clean native logs and an unintervened self-exit. These fixture
JSON schemas do not contain cleanup receipts or UTC stamps. File freshness is
bound to the fresh harness directory/run and, when supplied, callback case times.
"""
import datetime as dt
import hashlib
import json
import math
from pathlib import Path
import re
import stat
import struct
import zlib

PREFIX = "ShadowsOfTheForsaken.Tests.PlayMode."
HUD_CASES = {
    "05-puzzle-west-wall": PREFIX + "PrimaryCastleHudRenderingTests.PrimaryHudRendersAtRecordedPuzzleWestWallPose",
    "08-puzzle-northwest-corner": PREFIX + "PrimaryCastleHudRenderingTests.PrimaryHudRendersAtRecordedPuzzleNorthwestCornerPose",
}
EXPLORATION_CASE = PREFIX + "CastleCameraExplorationTests.LowerRoomsRetainVisibleCameraDuringUnsnappedInputExploration"
EXPECTED = frozenset([*HUD_CASES.values(), EXPLORATION_CASE])
CAPTURES = (
    "00-saved-spawn", "01-courtyard-wall", "02-entrance-wall", "03-encounter-north-wall",
    "04-rune-lever-unsnapped", "05-human-final-position-vicinity", "06-puzzle-southwest-corner",
    "07-west-plinth-channel", "08-puzzle-northwest-corner",
)
OBJECTIVES = "FirstEnemyDefeated, MainPuzzleSolved"
REGIONS = ("Health", "Controls left half", "Controls right half")
RADIUS = .5571978688240051
POSES = {
    "05-puzzle-west-wall": ((-11.6800261, .01999974, 21.9406700), (-11.4175215, 2.3006697, 20.6123257),
                            (-.051230356, -.263642341, -.014023145, .963157117)),
    "08-puzzle-northwest-corner": ((-11.6989841, .01999986, 27.6800003), (-11.0898542, 3.9041939, 25.1492062),
                                  (.212268561, -.262756228, .059376132, .939348578)),
}
MAX_FILE_BYTES = 8 * 1024 * 1024
MAX_TOTAL_BYTES = 64 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def number(value, label, low=None, high=None):
    require(type(value) in (int, float) and math.isfinite(value), "Invalid finite " + label)
    require((low is None or value >= low) and (high is None or value <= high), "Out-of-range " + label)
    return value


def integer(value, label, low=0, high=5_000_000):
    require(type(value) is int and low <= value <= high, "Invalid integer " + label)
    return value


def unique_json(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def utc(value):
    stamp = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(stamp.tzinfo is not None and stamp.utcoffset() == dt.timedelta(0), "Expected UTC timestamp")
    return stamp


def vector(value, label, quaternion=False):
    keys = ("x", "y", "z", "w") if quaternion else ("x", "y", "z")
    require(isinstance(value, dict) and all(key in value for key in keys), "Missing " + label)
    values = [number(value[key], label) for key in keys]
    if quaternion:
        require(math.isclose(sum(v * v for v in values), 1, abs_tol=.0001), "Nonunit " + label)
    return values


def decode_png(raw, expected_size):
    """Bounded CRC-checked 8-bit RGB/RGBA Unity PNG decoder, top-down rows."""
    require(len(raw) <= MAX_FILE_BYTES and raw.startswith(b"\x89PNG\r\n\x1a\n"), "Invalid PNG signature/size")
    cursor, compressed, header, ended = 8, [], None, False
    while cursor < len(raw):
        require(cursor + 12 <= len(raw), "Truncated PNG chunk")
        size, = struct.unpack(">I", raw[cursor:cursor + 4])
        require(size <= MAX_FILE_BYTES and cursor + size + 12 <= len(raw), "Invalid PNG chunk length")
        kind, payload = raw[cursor + 4:cursor + 8], raw[cursor + 8:cursor + 8 + size]
        crc, = struct.unpack(">I", raw[cursor + 8 + size:cursor + 12 + size])
        require(zlib.crc32(kind + payload) & 0xffffffff == crc, "PNG CRC differs")
        cursor += size + 12
        if header is None:
            require(kind == b"IHDR" and size == 13, "PNG header must be first")
            w, h, depth, color, comp, filt, interlace = struct.unpack(">IIBBBBB", payload)
            require((w, h) == expected_size and depth == 8 and color in (2, 6) and
                    comp == filt == interlace == 0, "Unsupported PNG dimensions/encoding")
            header = w, h, 3 if color == 2 else 4
        elif kind == b"IDAT":
            compressed.append(payload)
        elif kind == b"IEND":
            require(size == 0 and cursor == len(raw), "Invalid PNG ending/trailing bytes")
            ended = True
        else:
            require(kind != b"IHDR" and kind[0] & 32, "Unknown critical PNG chunk")
    require(ended and compressed, "Incomplete PNG")
    w, h, bpp = header
    stride = w * bpp
    inflater = zlib.decompressobj()
    pixels = inflater.decompress(b"".join(compressed), h * (stride + 1) + 1)
    require(len(pixels) == h * (stride + 1) and inflater.eof and not inflater.unused_data and
            not inflater.unconsumed_tail, "Invalid/bomb PNG payload")
    rows, previous = [], bytearray(stride)
    for y in range(h):
        start = y * (stride + 1)
        kind = pixels[start]
        require(kind <= 4, "Unknown PNG row filter")
        row = bytearray(pixels[start + 1:start + stride + 1])
        if kind:
            for x in range(stride):
                left, up = (row[x - bpp] if x >= bpp else 0), previous[x]
                upperleft = previous[x - bpp] if x >= bpp else 0
                if kind == 1:
                    prediction = left
                elif kind == 2:
                    prediction = up
                elif kind == 3:
                    prediction = (left + up) // 2
                else:
                    p = left + up - upperleft
                    a, b, c = abs(p - left), abs(p - up), abs(p - upperleft)
                    prediction = left if a <= b and a <= c else up if b <= c else upperleft
                row[x] = (row[x] + prediction) & 255
        rows.append(row)
        previous = row
    return w, h, bpp, rows


def changed_pixels(visible, hidden, rect):
    require(visible[:3] == hidden[:3], "Paired image encoding/dimensions differ")
    require(isinstance(rect, dict), "Missing viewport bounds")
    x, y, rw, rh = [number(rect.get(k), "viewport " + k) for k in ("x", "y", "width", "height")]
    require(0 <= x < 1 and 0 <= y < 1 and rw > 0 and rh > 0 and x + rw <= 1.00001 and y + rh <= 1.00001,
            "Viewport region escaped image")
    w, h, bpp, rows = visible
    left, right = max(0, math.ceil(x * w)), min(w, math.floor((x + rw) * w))
    bottom, top = max(0, math.ceil(y * h)), min(h, math.floor((y + rh) * h))
    require(left < right and bottom < top, "Empty pixel region")
    return sum(sum(abs(rows[h - 1 - py][px * bpp + c] - hidden[3][h - 1 - py][px * bpp + c])
                   for c in range(3)) > 8 for py in range(bottom, top) for px in range(left, right))


def validate_hud(value, images):
    require(value.get("pose") in HUD_CASES and value.get("scene") == "Assets/Scenes/ForsakenCastle.unity" and
            value.get("renderMode") == "ScreenSpaceCamera", "Unexpected primary HUD pose/scene/mode")
    require(value.get("width") == 1280 and value.get("height") == 720 and
            value.get("healthText") == "Health  200 / 200", "HUD resolution/health differs")
    require(value.get("frozenActorPartsUnchanged") is True and isinstance(value.get("textOriginallyEnabled"), list) and
            len(value["textOriginallyEnabled"]) == 2 and all(v is True for v in value["textOriginallyEnabled"]),
            "HUD comparison actor/text baseline differs")
    integer(value.get("frozenActorCount"), "frozen actors", 1)
    integer(value.get("frozenActorPartCount"), "frozen actor parts", 1)
    frames = [integer(value.get(k), k, 1) for k in ("visibleFrame", "hiddenFrame", "textHiddenFrame")]
    require(frames[0] < frames[1] < frames[2], "HUD paired frames are not ordered")
    near, plane = number(value.get("nearClip"), "near clip", .001), number(value.get("planeDistance"), "canvas depth")
    require(plane > near and math.isclose(number(value.get("aspect"), "HUD aspect"), 16 / 9, abs_tol=.0001),
            "HUD is clipped or wrong viewport")
    for key in ("cameraPosition", "playerPosition"):
        vector(value.get(key), key)
    for key in ("cameraRotation", "recordedCameraRotation", "assignedCameraRotation"):
        vector(value.get(key), key, True)
    for key, expected in zip(("playerPosition", "cameraPosition", "recordedCameraRotation"), POSES[value["pose"]]):
        actual = vector(value[key], key, key.endswith("Rotation"))
        require(all(math.isclose(a, b, abs_tol=.0001) for a, b in zip(actual, expected)), "HUD recorded pose differs: " + key)
    require(all(math.isclose(value["cameraRotation"][k], value["assignedCameraRotation"][k], abs_tol=.0001)
                for k in ("x", "y", "z", "w")), "HUD camera changed during paired capture")
    number(value.get("fieldOfView"), "field of view", 1, 179)
    require(value.get("minimumChangedPixels") == 32, "HUD difference threshold changed")
    regions = value.get("regions")
    require(isinstance(regions, list) and [r.get("name") for r in regions] == list(REGIONS), "HUD regions missing/foreign/duplicate")
    checks = []
    for region in regions:
        require(region.get("inFrontOfCamera") is True and region.get("insideViewport") is True, "HUD region is off camera")
        for key in ("worldCorners", "viewportCorners"):
            corners = region.get(key)
            require(isinstance(corners, list) and len(corners) == 4, "HUD region corners missing")
            for corner in corners:
                vector(corner, key)
                if key == "viewportCorners":
                    require(-.00001 <= corner["x"] <= 1.00001 and -.00001 <= corner["y"] <= 1.00001 and corner["z"] > near,
                            "HUD region is outside viewport/near plane")
        counts = {}
        for field, target in (("changedPixels", "hud-disabled"), ("textChangedPixels", "hud-text-disabled")):
            actual = changed_pixels(images["hud-visible"], images[target], region.get("viewportBounds"))
            require(integer(region.get(field), field) == actual and actual >= 32, "HUD retained pixels differ/insufficient: " + field)
            counts[field] = actual
        checks.append({"name": region["name"], **counts})
    hidden = images["hud-disabled"]
    world = sum(sum(row[i:i + 3]) > 24 for row in hidden[3] for i in range(0, len(row), hidden[2]))
    require(integer(value.get("worldPixelsWithHudDisabled"), "world pixels") == world and world > 1280 * 720 // 20,
            "HUD-disabled world missing or pixel measurement differs")
    return {"pose": value["pose"], "regions": checks, "near_clip": near, "plane_distance": plane, "world_pixels": world}


def validate_exploration(value):
    require(value.get("completed") is True, "Exploration incomplete")
    integer(value.get("observedFrames"), "observed frames", 101)
    require(type(value.get("unsafeFrames")) is int and value["unsafeFrames"] == 0 and
            type(value.get("blockedWarnings")) is int and value["blockedWarnings"] == 0 and
            value.get("episodes") == [] and value.get("warnings") == [], "Unsafe/blocked exploration")
    elapsed = number(value.get("elapsedObservedSeconds"), "observed elapsed", .1, 300)
    viewport = value.get("viewport")
    require(isinstance(viewport, dict), "Missing exploration viewport")
    for key in ("requestedAspect", "observedAspect"):
        require(math.isclose(number(viewport.get(key), key), 16 / 9, abs_tol=.0001), "Exploration aspect differs")
    for key in ("requestedRadius", "observedRadius"):
        require(math.isclose(number(viewport.get(key), key), RADIUS, abs_tol=.0001), "Primary clearance sphere differs")

    def pose(item):
        require(isinstance(item, dict) and item.get("hasSafePose") is True and item.get("cameraEnabled") is True,
                "Unsafe/disabled sampled camera")
        integer(item.get("frame"), "sample frame", 1)
        number(item.get("seconds"), "sample seconds", 0, elapsed)
        require(isinstance(item.get("phase"), str) and 0 < len(item["phase"]) <= 256, "Missing sample phase")
        require(item.get("room") in ("Courtyard", "FirstEncounter", "Puzzle") and item.get("objectives") in
                ("None", "FirstEnemyDefeated", OBJECTIVES), "Foreign exploration room/objectives")
        vector(item.get("position"), "player position")
        require(item["position"]["y"] > -.5, "Player fell below lower floor")
        vector(item.get("cameraPosition"), "camera position")
        vector(item.get("cameraRotation"), "camera rotation", True)
        number(item.get("yaw"), "yaw")
        require(math.isclose(number(item.get("aspect"), "sample aspect"), 16 / 9, abs_tol=.0001) and
                math.isclose(number(item.get("effectiveRadius"), "sample radius"), RADIUS, abs_tol=.0001), "Sample viewport/radius differs")

    samples, inputs, captures = value.get("samples"), value.get("inputs"), value.get("captures")
    require(isinstance(samples, list) and 2 <= len(samples) <= 10000, "Missing/excessive sampled history")
    for item in samples:
        pose(item)
    require(all(a["frame"] <= b["frame"] and a["seconds"] <= b["seconds"] for a, b in zip(samples, samples[1:])),
            "Exploration sample clocks reversed")
    first, last = samples[0], samples[-1]
    require(first["room"] == "Courtyard" and first["objectives"] == "None" and
            abs(first["position"]["x"]) < .1 and abs(first["position"]["z"] + 88) < .1,
            "Exploration did not start at saved courtyard spawn")
    require(last["room"] == "Puzzle" and last["objectives"] == OBJECTIVES, "Exploration final progression differs")
    require(value["observedFrames"] >= last["frame"] - first["frame"] + 1, "Observed frame count cannot cover samples")
    require(isinstance(inputs, list) and 2 <= len(inputs) <= 100000, "Missing/excessive input history")
    keys = set()
    for item in inputs:
        integer(item.get("frame"), "input frame", 1)
        number(item.get("seconds"), "input seconds", 0, elapsed)
        require(type(item.get("attack")) is bool and isinstance(item.get("keys"), list) and
                len(item["keys"]) == len(set(item["keys"])) and set(item["keys"]) <= {"W", "A", "S", "D", "E", "Space"}, "Foreign/malformed input")
        keys.update(item["keys"])
    require(all(a["frame"] <= b["frame"] and a["seconds"] <= b["seconds"] for a, b in zip(inputs, inputs[1:])), "Input clocks reversed")
    require({"W", "A", "D", "E", "Space"} <= keys and any(i["attack"] for i in inputs) and
            inputs[-1]["keys"] == [] and inputs[-1]["attack"] is False, "Required movement/interaction/fight or released final input missing")
    require(isinstance(captures, list) and [c.get("name") for c in captures] == list(CAPTURES), "Exploration checkpoint inventory differs")
    for index, capture in enumerate(captures):
        require(capture.get("file") == CAPTURES[index] + ".png" and capture.get("width") == 960 and capture.get("height") == 540 and
                isinstance(capture.get("status"), str) and capture["status"].startswith("rendered from sampled LateUpdate pose without snap;"),
                "Exploration capture missing/not rendered unsnapped")
        pose(capture.get("pose"))
        expected_state = ("Courtyard", "None") if index < 3 else ("FirstEncounter", "FirstEnemyDefeated") if index == 3 else ("Puzzle", OBJECTIVES)
        require((capture["pose"]["room"], capture["pose"]["objectives"]) == expected_state, "Checkpoint progression differs")
    require(all(a["pose"]["seconds"] < b["pose"]["seconds"] for a, b in zip(captures, captures[1:])), "Checkpoints are not ordered")
    return {"observed_frames": value["observedFrames"], "elapsed_seconds": elapsed, "unsafe_frames": 0, "blocked_warnings": 0,
            "sample_count": len(samples), "input_count": len(inputs), "checkpoint_names": list(CAPTURES), "final_room": last["room"],
            "final_objectives": last["objectives"], "final_input_released": True}


def case_intervals(events, start, end):
    if events is None:
        return {case: (start, end) for case in EXPECTED}
    if isinstance(events, bytes):
        require(len(events) <= 8 * 1024 * 1024, "Excessive callback events")
        events = [json.loads(line, object_pairs_hook=unique_json) for line in events.decode("utf-8-sig").splitlines() if line.strip()]
    require(isinstance(events, list) and 8 <= len(events) <= 128, "Missing bounded presentation case events")
    starts, finishes, previous = {}, {}, start
    for event in events:
        stamp = utc(event["utc"])
        require(previous <= stamp <= end, "Callback event is stale/reversed/outside run")
        previous = stamp
        if event.get("kind") not in ("test-started", "test-finished") or event.get("is_suite") is True:
            continue
        require(event.get("is_suite") is False and event.get("full_name") in EXPECTED, "Foreign/malformed leaf case")
        case = event["full_name"]
        target = starts if event["kind"] == "test-started" else finishes
        require(case not in target, "Duplicate leaf case event")
        target[case] = stamp
        if target is finishes:
            require(case in starts and stamp >= starts[case] and event.get("result") == "Passed" and
                    all(type(event.get(k)) is int and event[k] == n for k, n in (("passed", 1), ("failed", 0), ("skipped", 0), ("inconclusive", 0))),
                    "Presentation case/teardown did not pass")
    require(set(starts) == set(finishes) == EXPECTED, "Missing presentation case boundary")
    return {case: (starts[case], finishes[case]) for case in EXPECTED}


def validate_presentation_output(output, started_utc, completed_utc, events=None):
    """Validate exactly18 fresh confined files; return SHA-bound raw/recomputed evidence."""
    output = Path(output).absolute()
    start, end = utc(started_utc), utc(completed_utc)
    require(start <= end, "Reversed run timestamps")
    intervals = case_intervals(events, start, end)
    root = output / "captures"
    require(root.is_dir() and root.resolve() == root.absolute() and not root.is_symlink(), "Capture root escaped fresh output")
    bundles = list(root.iterdir())
    require(len(bundles) == 3 and all(p.is_dir() and not p.is_symlink() and p.resolve() == p.absolute() for p in bundles),
            "Expected exactly three unlinked presentation bundles")
    hud_dirs = [p for p in bundles if re.fullmatch(r"primary-hud-[0-9a-f]{32}", p.name)]
    exploration_dirs = [p for p in bundles if re.fullmatch(r"camera-exploration-[0-9a-f]{32}", p.name)]
    require(len(hud_dirs) == 2 and len(exploration_dirs) == 1, "Presentation bundle profile differs")
    files, total = [], 0

    def raw(path, case):
        nonlocal total
        info = path.lstat()
        require(not path.is_symlink() and stat.S_ISREG(info.st_mode) and path.resolve() == path.absolute() and
                0 < info.st_size <= MAX_FILE_BYTES, "Linked/nonregular/excessive presentation artifact")
        stamp = dt.datetime.fromtimestamp(info.st_mtime, dt.timezone.utc)
        low, high = intervals[case]
        require(low <= stamp <= high, "Presentation artifact is stale/outside its actual case")
        data = path.read_bytes()
        require(len(data) == info.st_size, "Presentation file changed during validation")
        total += len(data)
        require(total <= MAX_TOTAL_BYTES, "Presentation artifacts exceed bounded size")
        files.append({"path": path.relative_to(output).as_posix(), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                      "mtime_utc": stamp.isoformat(), "case_name": case})
        return data

    hud, seen = [], set()
    for bundle in sorted(hud_dirs):
        metrics = list(bundle.glob("*-evidence.json"))
        require(len(metrics) == 1, "Expected one HUD metrics file")
        pose = metrics[0].name.removesuffix("-evidence.json")
        require(pose in HUD_CASES and pose not in seen, "Duplicate/foreign HUD pose")
        seen.add(pose)
        expected_names = {pose + "-evidence.json", *(pose + "-" + suffix + ".png" for suffix in ("hud-visible", "hud-disabled", "hud-text-disabled"))}
        require({p.name for p in bundle.iterdir()} == expected_names, "Unexpected/missing HUD artifact")
        value = json.loads(raw(metrics[0], HUD_CASES[pose]), object_pairs_hook=unique_json)
        require(value.get("pose") == pose, "HUD pose differs from file")
        images = {suffix: decode_png(raw(bundle / (pose + "-" + suffix + ".png"), HUD_CASES[pose]), (1280, 720))
                  for suffix in ("hud-visible", "hud-disabled", "hud-text-disabled")}
        hud.append(validate_hud(value, images))
    require(seen == set(HUD_CASES), "HUD case evidence missing")
    bundle = exploration_dirs[0]
    require({p.name for p in bundle.iterdir()} == {"evidence.json", *(name + ".png" for name in CAPTURES)}, "Unexpected/missing exploration artifacts")
    exploration = validate_exploration(json.loads(raw(bundle / "evidence.json", EXPLORATION_CASE), object_pairs_hook=unique_json))
    low, high = intervals[EXPLORATION_CASE]
    require(exploration["elapsed_seconds"] <= (high - low).total_seconds() + .001,
            "Observed exploration duration exceeds its actual case/run interval")
    for name in CAPTURES:
        image = decode_png(raw(bundle / (name + ".png"), EXPLORATION_CASE), (960, 540))
        require(sum(sum(row[i:i + 3]) > 24 for row in image[3] for i in range(0, len(row), image[2])) > 960 * 540 // 20,
                "Exploration capture has no rendered world")
    return {"test_profile": "presentation", "expected_cases": sorted(EXPECTED), "HUD": hud, "exploration": exploration,
            "files": files, "file_count": len(files), "total_bytes": total, "case_timestamps_bound": events is not None,
            "cleanup_scope": "No JSON cleanup flags exist; exact passed NUnit teardown and uninjected self-exit/log gates are required separately.",
            "limits": "Fixture HUD pixels and unsnapped lower-room input only; no human timing/readability, physical audio, full route or performance acceptance."}

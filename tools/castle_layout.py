#!/usr/bin/env python3
"""Deterministic, metre-scale blockout traced from DOCX section 8.

The map's cell centres are eight metres apart. Room shapes are implementation
choices; the occupied cells and the lower dead-end/backtracking are preserved.
The library occupies the eastern connector as a descent. The below-map return
passage is the explicit design-decision interpretation of section 6's shortcut.
No gameplay gates, enemies, rewards or progression are generated here.
"""

import argparse
import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/LevelLayout/Editor/CastleLayout.json"
GRID_SIZE = 8
WALL_HEIGHT = 4.5
WALL_THICKNESS = 0.4
SLAB_THICKNESS = 0.5


def vec(x=0, y=0, z=0):
    return {"x": round(float(x), 6), "y": round(float(y), 6), "z": round(float(z), 6)}


def rgba(r, g, b, a=1):
    return {"r": r, "g": g, "b": b, "a": a}


def build_layout():
    data = {
        "version": 1, "gridSize": GRID_SIZE, "spawn": vec(0, .05, -2),
        "materials": [], "objects": [], "rooms": [], "passages": [],
        "anchors": [], "lights": [],
    }
    palette = {
        "Stone": (.24, .27, .30), "Wall": (.30, .32, .35),
        "Trim": (.43, .44, .43), "DarkStone": (.16, .18, .21),
        "Wood": (.24, .14, .10), "Iron": (.12, .14, .17),
        "Rune": (.25, .68, .77), "Glass": (.18, .32, .60),
        "BookRed": (.40, .08, .10), "BookGold": (.55, .38, .14),
        "Blood": (.24, .025, .035), "Flame": (1., .48, .12),
        "Player": (.65, .48, .25),
    }
    for name, color in palette.items():
        emission = color if name in ("Flame", "Rune", "Glass") else (0, 0, 0)
        data["materials"].append({"name": name, "color": rgba(*color), "emission": rgba(*emission)})

    def obj(name, position, scale=(1, 1, 1), material="Stone", collider=True,
            rotation=(0, 0, 0), parent="Geometry", kind="Cube"):
        data["objects"].append({"name": name, "parent": parent, "position": vec(*position),
            "rotation": vec(*rotation), "scale": vec(*scale), "kind": kind,
            "material": material, "collider": collider})

    for group in ("GroundStructure", "UpperStructure", "SecretStructure", "Ramps", "Landmarks"):
        obj(group, (0, 0, 0), material="", collider=False, kind="Empty")

    def anchor(name, position):
        data["anchors"].append({"name": name, "parent": "Anchors", "position": vec(*position)})

    room_specs = [
        ("Courtyard", 5, 10, 0), ("FirstEncounter", 5, 8, 0),
        ("Puzzle", 4, 7, 0), ("ThroneRoom", 6, 6, 0),
        ("Library", 7, 5, -2), ("Catacombs", 7, 4, -4),
        ("BonusRoom", 4, 2, -4), ("FinalArena", 6, 2, -4),
        ("Exit", 6, .75, -4),
    ]
    centres = {}
    for room_id, column, row, elevation in room_specs:
        centre = ((column - 5) * GRID_SIZE, elevation, (10 - row) * GRID_SIZE)
        centres[room_id] = centre
        data["rooms"].append({"id": room_id, "objectName": "Room_" + room_id,
            "column": column, "row": row, "position": vec(*centre)})

    def passage(start, end, points, gate, yaw=0):
        data["passages"].append({"from": start, "to": end,
            "objectName": "Passage_" + start + "_" + end,
            "waypoints": [vec(x, y + .05, z) for x, y, z in points],
            "gatePosition": vec(*gate), "gateRotation": vec(0, yaw, 0)})

    passage("Courtyard", "FirstEncounter", [(0, 0, 0), (0, 0, 16)], (0, 0, 12))
    passage("FirstEncounter", "Puzzle", [(0, 0, 16), (-8, 0, 16), (-8, 0, 24)], (-8, 0, 20))
    passage("FirstEncounter", "ThroneRoom", [(0, 0, 16), (8, 0, 16), (8, 0, 32)], (8, 0, 28))
    passage("ThroneRoom", "Library", [(8, 0, 32), (16, 0, 32), (16, 0, 36), (16, -2, 40)], (12, 0, 32), 90)
    passage("Library", "Catacombs", [(16, -2, 40), (16, -4, 44), (16, -4, 48)], (16, -4, 44))
    passage("Catacombs", "FinalArena", [(16, -4, 48), (8, -4, 48), (8, -4, 64)], (8, -4, 60))
    passage("Catacombs", "BonusRoom", [(16, -4, 48), (-8, -4, 48), (-8, -4, 64)], (-8, -4, 60))
    passage("ThroneRoom", "BonusRoom", [(8, 0, 32), (0, 0, 32), (-24, -12, 32),
        (-30, -12, 32), (-30, -12, 64), (-28, -12, 64), (-12, -4, 64), (-8, -4, 64)], (4, 0, 32), 90)
    passage("FinalArena", "Exit", [(8, -4, 64), (8, -4, 74)], (8, -4, 68))

    # Rectilinear floor unions provide one closed perimeter, with no hand-joined
    # corner gaps. Integers describe one-metre cells, not a runtime tile system.
    floors = {0: set(), -4: set(), -12: set()}

    def rectangle(level, x0, z0, x1, z1):
        for x in range(int(x0), int(x1)):
            for z in range(int(z0), int(z1)):
                floors[level].add((x, z))

    for room_id, (x, y, z) in centres.items():
        if room_id == "Library":
            continue
        half_x, half_z = (2, 4) if room_id == "Exit" else (4, 4)
        rectangle(y, x-half_x, z-half_z, x+half_x, z+half_z)

    def corridor(level, points, turn_caps=True):
        for (x0, z0), (x1, z1) in zip(points, points[1:]):
            if x0 == x1:
                rectangle(level, x0-2, min(z0, z1), x0+2, max(z0, z1))
            elif z0 == z1:
                rectangle(level, min(x0, x1), z0-2, max(x0, x1), z0+2)
            else:
                raise ValueError("Flat corridors must follow the map's orthogonal grid")
        if turn_caps:
            for x, z in points[1:-1]:
                rectangle(level, x-2, z-2, x+2, z+2)

    corridor(0, [(0, 0), (0, 16)])
    corridor(0, [(0, 16), (-8, 16), (-8, 24)])
    corridor(0, [(0, 16), (8, 16), (8, 32)])
    corridor(0, [(8, 32), (16, 32), (16, 36)])
    corridor(0, [(8, 32), (0, 32)])
    corridor(-4, [(16, 48), (8, 48), (8, 64)])
    corridor(-4, [(16, 48), (-8, 48), (-8, 64)])
    corridor(-4, [(8, 64), (8, 74)])
    corridor(-12, [(-24, 32), (-30, 32), (-30, 64)])
    # The north landing is to the west of the ascending ramp, so its flat floor
    # cannot cover the slope or require a sideways step onto a raised edge.
    rectangle(-12, -32, 62, -28, 66)

    def merged_rectangles(cells):
        remaining = set(cells)
        while remaining:
            x0, z0 = min(remaining, key=lambda p: (p[1], p[0]))
            x1 = x0 + 1
            while (x1, z0) in remaining:
                x1 += 1
            z1 = z0 + 1
            while all((x, z1) in remaining for x in range(x0, x1)):
                z1 += 1
            remaining.difference_update((x, z) for x in range(x0, x1) for z in range(z0, z1))
            yield x0, z0, x1, z1

    # Only these six boundary openings meet sloped, independently constructed
    # surfaces. Every other outer floor edge receives a solid wall.
    openings = {
        0: [("z", 36, 14, 18), ("x", 0, 30, 34)],
        -4: [("z", 44, 14, 18), ("x", -12, 62, 66)],
        -12: [("x", -24, 30, 34), ("x", -28, 62, 66)],
    }
    groups = {0: "GroundStructure", -4: "UpperStructure", -12: "SecretStructure"}
    for level, cells in floors.items():
        group = groups[level]
        for index, (x0, z0, x1, z1) in enumerate(merged_rectangles(cells)):
            obj(f"{group}_Floor_{index:03}", ((x0+x1)/2, level-.25, (z0+z1)/2),
                (x1-x0, .5, z1-z0), parent=group)
        # Courtyard/initial approach remain open to the night sky; the first
        # threat space is also uncovered. The roof begins after that junction.
        roof_cells = {(x, z) for x, z in cells if level != 0 or z >= 20}
        for index, (x0, z0, x1, z1) in enumerate(merged_rectangles(roof_cells)):
            obj(f"{group}_Ceiling_{index:03}", ((x0+x1)/2, level+4.75, (z0+z1)/2),
                (x1-x0, .5, z1-z0), "DarkStone", parent=group)
        edges = {}
        for x, z in sorted(cells):
            for dx, dz, axis, fixed, varying, side in [
                (-1, 0, "x", x, z, -1), (1, 0, "x", x+1, z, 1),
                (0, -1, "z", z, x, -1), (0, 1, "z", z+1, x, 1),
            ]:
                if (x+dx, z+dz) in cells:
                    continue
                if any(axis == a and fixed == f and low <= varying < high
                       for a, f, low, high in openings[level]):
                    continue
                edges.setdefault((axis, fixed, side), set()).add(varying)
        wall_index = 0
        for (axis, fixed, side), spans in sorted(edges.items()):
            while spans:
                low = min(spans)
                high = low+1
                while high in spans:
                    high += 1
                spans.difference_update(range(low, high))
                boundary = fixed + side*WALL_THICKNESS/2
                # Exact endpoints preserve four clear metres at corridor
                # mouths. Perpendicular outer walls meet at the boundary;
                # extending them would intrude into the adjoining doorway.
                length = high-low
                position = (boundary, level+2.25, (low+high)/2) if axis == "x" else ((low+high)/2, level+2.25, boundary)
                scale = (.4, WALL_HEIGHT, length) if axis == "x" else (length, WALL_HEIGHT, .4)
                obj(f"{group}_Wall_{wall_index:03}", position, scale, "Wall", parent=group)
                wall_index += 1

    def ramp(name, start, end, width):
        x0, y0, z0 = start
        x1, y1, z1 = end
        horizontal = math.hypot(x1-x0, z1-z0)
        pitch = math.degrees(math.atan2(-(y1-y0), horizontal))
        yaw = math.degrees(math.atan2(x1-x0, z1-z0))
        length = math.hypot(horizontal, y1-y0)
        # Cube local +Z follows the slope and local +Y is its top normal.
        angle = math.radians(pitch)
        yaw_radians = math.radians(yaw)
        normal = (math.sin(angle)*math.sin(yaw_radians), math.cos(angle), math.sin(angle)*math.cos(yaw_radians))
        centre = ((x0+x1)/2, (y0+y1)/2, (z0+z1)/2)
        floor_centre = tuple(centre[i] - normal[i]*.25 for i in range(3))
        obj(name+"_Floor", floor_centre, (width, .5, length), "Stone",
            rotation=(pitch, yaw, 0), parent="Ramps")
        # The roof's lower face is 4.5 vertical metres above the floor plane.
        # A little overlap at the ends closes seams with the flat ceilings.
        roof_centre = (floor_centre[0], floor_centre[1]+5, floor_centre[2])
        obj(name+"_Ceiling", roof_centre, (width+.8, .5, length+.4), "DarkStone",
            rotation=(pitch, yaw, 0), parent="Ramps")
        perpendicular = (math.cos(yaw_radians), -math.sin(yaw_radians))
        segments = int(math.ceil(horizontal/2))
        for index in range(segments):
            ta, tb = index/segments, (index+1)/segments
            lower = min(y0+(y1-y0)*ta, y0+(y1-y0)*tb)
            upper = max(y0+(y1-y0)*ta, y0+(y1-y0)*tb)+WALL_HEIGHT
            t = (ta+tb)/2
            for side in (-1, 1):
                x = x0+(x1-x0)*t+perpendicular[0]*side*(width/2+.2)
                z = z0+(z1-z0)*t+perpendicular[1]*side*(width/2+.2)
                obj(f"{name}_Side_{side}_{index:02}", (x, (lower+upper)/2, z),
                    (.4, upper-lower, horizontal/segments), "Wall",
                    rotation=(0, yaw, 0), parent="Ramps")

    # Eight metres of shelf-lined library with four clear metres in its centre.
    ramp("LibraryDescent", (16, 0, 36), (16, -4, 44), 8)
    for x in (13, 19):
        obj(f"LibraryEntranceShoulder_{x}", (x, 2.25, 35.8), (2, 4.5, .4), "Wall", parent="Ramps")
    ramp("SecretDescent", (0, 0, 32), (-24, -12, 32), 4)
    ramp("SecretAscent", (-28, -12, 64), (-12, -4, 64), 4)

    # Open portal surrounds show the future blocker positions without adding
    # closed colliders or bypassing the authoritative progression state.
    for portal in data["passages"]:
        x, y, z = (portal["gatePosition"][axis] for axis in ("x", "y", "z"))
        yaw = portal["gateRotation"]["y"]
        dx, dz = math.cos(math.radians(yaw)), -math.sin(math.radians(yaw))
        stem = portal["objectName"] + "_Frame"
        for side in (-1, 1):
            obj(stem+str(side), (x+dx*side*2.25, y+2, z+dz*side*2.25),
                (.5, 4, .5), "Trim", parent="Landmarks")
        obj(stem+"Lintel", (x, y+4.25, z), (5, .5, .6), "Trim",
            rotation=(0, yaw, 0), parent="Landmarks")

    for name, position in [
        ("Spawn", (0, .05, -2)), ("FirstEnemy", (0, 0, 18)),
        ("MainPuzzle", (-9.5, 0, 25.5)), ("ThroneMiniboss", (8, 0, 34)),
        ("LibraryMechanism", (16, -2, 40)), ("SecretLever", (18.5, -4, 49.5)),
        ("BonusDiscovery", (-8, -4, 66)), ("FinalEnemy", (8, -4, 66)),
        ("Exit", (8, -4, 76)),
    ]:
        anchor(name, position)

    # Readable landmark silhouettes are local primitives, never extracted DOCX
    # reference art. Keep the centre-line routes and four-metre doors clear.
    obj("Courtyard_MonumentBase", (-2.7, .25, 1), (1.2, .5, 1.2), "Trim", parent="Landmarks")
    obj("Courtyard_BrokenMonument", (-2.7, 1.5, 1), (.65, 2, .65), "Wall", parent="Landmarks")
    obj("Puzzle_RunePlinth", (-10.5, .55, 25.5), (1, 1.1, 1), "DarkStone", parent="Landmarks")
    obj("Puzzle_RuneFace", (-10.5, 1.12, 25.5), (.6, .04, .6), "Rune", False, parent="Landmarks")
    for dx, dz in [(-2.8, -2.8), (2.8, -2.8), (-2.8, 2.8), (2.8, 2.8)]:
        label = f"Throne_Column_{dx}_{dz}"
        obj(label+"Base", (8+dx, .15, 32+dz), (.9, .3, .9), "Trim", parent="Landmarks")
        height = 3.5 if dz > 0 else 1.8
        obj(label, (8+dx, .3+height/2, 32+dz), (.5, height, .5), "Wall", parent="Landmarks")
    obj("Throne_Seat", (8, .7, 35), (1.6, 1.4, 1), "DarkStone", parent="Landmarks")
    obj("Throne_Back", (8, 1.9, 35.5), (1.6, 2.2, .35), "Iron", parent="Landmarks")
    obj("Throne_BloodTrace", (6, .012, 34), (.75, .02, 1.2), "Blood", False, parent="Landmarks")
    # Small flat pedestals meet the sloped floor at their downhill edge and do
    # not protrude into the library's central four-metre walking strip.
    for side in (-1, 1):
        for index, z in enumerate((37.5, 40, 42.5)):
            floor = -(z-36)/2
            x = 16+side*3.1
            obj(f"Library_ShelfPlinth_{side}_{index}", (x, floor, z), (.85, .8, 1.5), "Stone", parent="Landmarks")
            obj(f"Library_Shelf_{side}_{index}", (x, floor+1.65, z), (.65, 2.5, 1.4), "Wood", parent="Landmarks")
            for book in range(4):
                obj(f"Library_Book_{side}_{index}_{book}", (x-side*.38, floor+1.4+book*.25, z),
                    (.12, .16, 1.05), "BookRed" if book % 2 else "BookGold", False, parent="Landmarks")
    for room_id in ("Catacombs", "BonusRoom", "FinalArena"):
        x, y, z = centres[room_id]
        for side in (-1, 1):
            obj(room_id+f"_Sarcophagus_{side}", (x+side*2.9, y+.45, z+3),
                (1, .9, 1.5), "DarkStone", parent="Landmarks")
    obj("Catacombs_CultMark", (18.75, -3.985, 46.5), (1, .02, 1), "Rune", False,
        rotation=(0, 45, 0), parent="Landmarks")
    obj("SecretLever_Plinth", (18.8, -3.6, 49.5), (.7, .8, .7), "DarkStone", parent="Landmarks")
    obj("SecretLever_Handle", (18.8, -2.9, 49.5), (.12, .8, .12), "Iron", False,
        rotation=(0, 0, -25), parent="Landmarks")
    obj("Bonus_Reliquary", (-8, -3.65, 66.5), (1, .7, .7), "BookGold", parent="Landmarks")

    # Pointed window shapes sit against solid walls: decorative glass never
    # creates an accidental escape route or competes with a passage doorway.
    for room_id in ("Puzzle", "ThroneRoom", "BonusRoom", "FinalArena"):
        x, y, z = centres[room_id]
        x -= 2.5
        z += 3.96
        obj(room_id+"_WindowGlass", (x, y+2.7, z), (1.15, 1.9, .06), "Glass", False, parent="Landmarks")
        for side in (-1, 1):
            obj(room_id+f"_PointedArch_{side}", (x+side*.37, y+3.65, z-.04),
                (.14, 1.1, .15), "Trim", False, rotation=(0, 0, side*40), parent="Landmarks")

    torch_points = [(0, 2.8, 1), (-10.8, 2.8, 24), (10.8, 2.8, 32),
        (16, 1, 40), (18.7, -1.2, 48), (-10.7, -1.2, 64), (10.7, -1.2, 64),
        (-8, -2, 32), (-24, -8, 32), (-30, -9.2, 48), (-20, -5.2, 64)]
    for index, (x, y, z) in enumerate(torch_points):
        obj(f"Torch_{index:02}_Flame", (x, y, z), (.15, .3, .15), "Flame", False, parent="Landmarks")
        data["lights"].append({"name": f"Torch_{index:02}", "position": vec(x, y, z),
            "color": rgba(1, .63, .32), "intensity": 2.2, "range": 9})

    return data


def serialized_layout():
    return json.dumps(build_layout(), ensure_ascii=False, indent=2) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check the committed JSON without modifying it")
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()
    content = serialized_layout()
    if args.check:
        if not args.output.is_file() or args.output.read_text(encoding="utf-8") != content:
            parser.exit(1, f"Castle layout is stale: {args.output}\n")
        print("Castle layout JSON matches its deterministic source.")
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("w", encoding="utf-8", newline="\n") as output:
            output.write(content)
        print(f"Wrote {args.output} ({len(content.encode('utf-8'))} bytes).")


if __name__ == "__main__":
    main()

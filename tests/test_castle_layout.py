"""Source/model checks for DOCX section 8. These do not execute Unity physics."""

import json
import math
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]
MODEL_PATH = ROOT / "Assets/LevelLayout/Editor/CastleLayout.json"
SCENE_PATH = ROOT / "Assets/Scenes/ForsakenCastle.unity"
ROOM_CELLS = {
    "Courtyard": (5, 10, 0),
    "FirstEncounter": (5, 8, 0),
    "Puzzle": (4, 7, 0),
    "ThroneRoom": (6, 6, 0),
    "Library": (7, 5, -2),
    "Catacombs": (7, 4, -4),
    "BonusRoom": (4, 2, -4),
    "FinalArena": (6, 2, -4),
}
ANCHOR_NAMES = {"Spawn", "FirstEnemy", "MainPuzzle", "ThroneMiniboss", "LibraryMechanism",
                "SecretLever", "BonusDiscovery", "FinalEnemy", "Exit"}


def vector(value):
    return tuple(float(value[axis]) for axis in "xyz")


def snake_case(value):
    return re.sub(r"(?<!^)(?=[A-Z])", "_", value).lower()


def quaternion_rotate(point, quaternion):
    x, y, z, w = quaternion
    cross = (y * point[2] - z * point[1], z * point[0] - x * point[2], x * point[1] - y * point[0])
    double_cross = (y * cross[2] - z * cross[1], z * cross[0] - x * cross[2], x * cross[1] - y * cross[0])
    return tuple(value + 2 * w * first + 2 * second for value, first, second in zip(point, cross, double_cross))


def inverse_rotate(point, rotation):
    """Inverse Unity Euler rotation: Z then X then Y in the forward direction."""
    x, y, z = point
    for axis in (1, 0, 2):
        angle = -math.radians(rotation[axis])
        cosine, sine = math.cos(angle), math.sin(angle)
        if axis == 0:
            y, z = cosine * y - sine * z, sine * y + cosine * z
        elif axis == 1:
            x, z = cosine * x + sine * z, -sine * x + cosine * z
        else:
            x, y = cosine * x - sine * y, sine * x + cosine * y
    return x, y, z


class Box:
    """Independent oriented-box checks on serialized primitive dimensions."""

    def __init__(self, entry):
        self.name = entry["name"]
        self.center = vector(entry["position"])
        scale = vector(entry["scale"])
        # Unity's Cylinder primitive is two local units tall. Both supported
        # primitives deliberately use BoxCollider in the authoring pipeline.
        self.half = (scale[0] / 2, scale[1] * (1 if entry["kind"] == "Cylinder" else 0.5), scale[2] / 2)
        rotation = vector(entry["rotation"])
        columns = [inverse_rotate(axis, rotation) for axis in ((1, 0, 0), (0, 1, 0), (0, 0, 1))]
        self.inverse = tuple(tuple(columns[column][row] for column in range(3)) for row in range(3))

    def rotate(self, direction):
        return tuple(sum(a * b for a, b in zip(row, direction)) for row in self.inverse)

    def local(self, point):
        return self.rotate(tuple(a - b for a, b in zip(point, self.center)))

    def distance(self, point):
        return math.sqrt(sum(max(abs(value) - extent, 0) ** 2 for value, extent in zip(self.local(point), self.half)))

    def downward_hit(self, point, length):
        start = self.local(point)
        direction = self.rotate((0, -1, 0))
        near, far = 0.0, length
        for value, speed, extent in zip(start, direction, self.half):
            if abs(speed) < 1e-9:
                if abs(value) > extent + 1e-6:
                    return None
                continue
            hits = sorted(((-extent - value) / speed, (extent - value) / speed))
            near, far = max(near, hits[0]), min(far, hits[1])
            if near > far:
                return None
        return point[1] - near


class CastleLayoutTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.model = json.loads(MODEL_PATH.read_text(encoding="utf-8"))
        cls.contract = json.loads((ROOT / "docs/level-contract.json").read_text(encoding="utf-8"))
        cls.objects = {entry["name"]: entry for entry in cls.model["objects"]}
        cls.boxes = [Box(entry) for entry in cls.model["objects"] if entry["kind"] in {"Cube", "Cylinder"} and entry["collider"]]

    def test_cylinder_box_collider_uses_full_primitive_height(self):
        box = Box({"name": "Cylinder check", "kind": "Cylinder", "position": {"x": 0, "y": 0, "z": 0},
                   "rotation": {"x": 0, "y": 0, "z": 0}, "scale": {"x": 1, "y": 2, "z": 1}})
        self.assertEqual(box.distance((0, 1.9, 0)), 0)
        self.assertEqual(box.downward_hit((0, 3, 0), 4), 2)

    def test_map_cells_and_separate_library_descent_match_documented_choices(self):
        self.assertEqual(self.model["gridSize"], 8)
        rooms = {room["id"]: room for room in self.model["rooms"]}
        self.assertEqual({snake_case(name) for name in rooms}, {room["id"] for room in self.contract["rooms"]})
        self.assertEqual(len(rooms), len(self.model["rooms"]))
        for name, (column, row, height) in ROOM_CELLS.items():
            with self.subTest(room=name):
                room = rooms[name]
                self.assertEqual((room["column"], room["row"]), (column, row))
                self.assertEqual(vector(room["position"]), ((column - 5) * 8, height, (10 - row) * 8))
                self.assertEqual(room["objectName"], "Room_" + name)
        self.assertGreater(rooms["Exit"]["position"]["z"], rooms["FinalArena"]["position"]["z"])

    def test_only_contract_passages_exist_and_secret_never_connects_to_exit(self):
        actual = [frozenset((snake_case(passage["from"]), snake_case(passage["to"]))) for passage in self.model["passages"]]
        expected = [frozenset((edge["from"], edge["to"])) for edge in self.contract["connections"]]
        self.assertCountEqual(actual, expected)
        self.assertEqual(len(actual), len(set(actual)), "Duplicate physical passage")
        self.assertNotIn(frozenset(("puzzle", "throne_room")), actual)
        self.assertNotIn(frozenset(("bonus_room", "exit")), actual)

    def test_world_objects_have_unique_names_valid_parents_and_usable_dimensions(self):
        self.assertEqual(len(self.objects), len(self.model["objects"]))
        roots = {"Geometry", "Rooms", "Passages", "Anchors", "Lighting"}
        material_names = {material["name"] for material in self.model["materials"]}
        self.assertTrue(self.boxes, "Physical architecture is missing")
        self.assertEqual(len(self.boxes), sum(bool(entry["collider"]) for entry in self.model["objects"]),
                         "Every authored collider must participate in clearance checks")
        for entry in self.model["objects"]:
            with self.subTest(object=entry["name"]):
                self.assertNotEqual(entry["name"], entry["parent"])
                self.assertIn(entry["parent"], roots | set(self.objects) | {""})
                for field in ("position", "rotation", "scale"):
                    self.assertTrue(all(math.isfinite(value) for value in vector(entry[field])))
                self.assertTrue(all(value > 0 for value in vector(entry["scale"])))
                self.assertIn(entry["kind"], {"Empty", "Cube", "Cylinder"})
                if entry["material"]:
                    self.assertIn(entry["material"], material_names)

    def test_passages_start_and_end_in_their_rooms_and_ramps_have_bounded_slopes(self):
        rooms = {room["id"]: vector(room["position"]) for room in self.model["rooms"]}
        for passage in self.model["passages"]:
            with self.subTest(passage=passage["objectName"]):
                points = [vector(point) for point in passage["waypoints"]]
                self.assertGreaterEqual(len(points), 2)
                for point, room in ((points[0], rooms[passage["from"]]), (points[-1], rooms[passage["to"]])):
                    self.assertLessEqual(math.hypot(point[0] - room[0], point[2] - room[2]), 4.1)
                    self.assertLess(abs(point[1] - room[1] - 0.05), 0.1)
                for first, second in zip(points, points[1:]):
                    horizontal = math.hypot(first[0] - second[0], first[2] - second[2])
                    self.assertGreater(horizontal, 0.1, "A passage has a vertical teleport or duplicate waypoint")
                    self.assertLessEqual(abs(first[1] - second[1]) / horizontal, 0.55,
                                         "Ramp exceeds the selected approximately 27-degree slope")
        shortcut = next(passage for passage in self.model["passages"]
                        if {passage["from"], passage["to"]} == {"ThroneRoom", "BonusRoom"})
        self.assertLessEqual(min(point["y"] for point in shortcut["waypoints"]), -11.9,
                             "The secret shortcut needs its separate underground elevation")

    def test_routes_have_continuous_floors_and_clear_character_width(self):
        # Three lanes spanning 3.2 m plus the 0.3 m player radius check the chosen
        # four-metre corridors, including ramp seams. This is a geometric source
        # check only; CharacterController traversal remains a real PlayMode test.
        for passage in self.model["passages"]:
            points = [vector(point) for point in passage["waypoints"]]
            for first, second in zip(points, points[1:]):
                length = math.hypot(second[0] - first[0], second[2] - first[2])
                samples = max(1, math.ceil(length / 0.5))
                normal = (-(second[2] - first[2]) / length, (second[0] - first[0]) / length)
                for step in range(samples + 1):
                    ratio = step / samples
                    center = tuple(a + (b - a) * ratio for a, b in zip(first, second))
                    for lane in (-1.6, 0, 1.6):
                        point = (center[0] + normal[0] * lane, center[1], center[2] + normal[1] * lane)
                        context = f"{passage['objectName']} at {point}"
                        hits = [box.downward_hit((point[0], point[1] + 0.35, point[2]), 0.7) for box in self.boxes]
                        support = [height for height in hits if height is not None]
                        self.assertTrue(support, "Missing floor: " + context)
                        self.assertLess(abs(max(support) - (point[1] - 0.05)), 0.2, "Wrong floor height: " + context)
                        for body_height in (0.3, 1.0, 1.7):
                            body = (point[0], point[1] + body_height, point[2])
                            for box in self.boxes:
                                self.assertGreaterEqual(box.distance(body), 0.255,
                                                        f"Player clearance intersects {box.name}: {context}")

    def test_committed_scene_contains_real_geometry_and_original_runtime_scripts(self):
        scene = SCENE_PATH.read_text(encoding="utf-8")
        self.assertTrue(scene.startswith("%YAML 1.1"))
        records = re.split(r"^--- !u!", scene, flags=re.MULTILINE)[1:]
        identifiers = [re.match(r"\d+ &(\d+)", record).group(1) for record in records]
        self.assertEqual(len(identifiers), len(set(identifiers)), "Duplicate Unity local file IDs")
        local_references = set(re.findall(r"\{fileID: (\d+)\}", scene)) - {"0"}
        self.assertLessEqual(local_references, set(identifiers), "A local scene reference points to a missing object")
        names = [match.group(1).strip().strip('"') for record in records if record.startswith("1 &")
                 for match in [re.search(r"^  m_Name: (.+)$", record, re.MULTILINE)] if match]
        for name in ("Geometry", "Rooms", "Passages", "Anchors", "Lighting", "Player", "Main Camera", "Layout Preview"):
            self.assertEqual(names.count(name), 1, name)
        for room in self.model["rooms"]:
            self.assertEqual(names.count(room["objectName"]), 1, room["objectName"])
        for passage in self.model["passages"]:
            self.assertEqual(names.count(passage["objectName"]), 1, passage["objectName"])
        for name in self.objects:
            self.assertIn(name, names, "Model geometry is absent from the serialized scene")
        object_names = {}
        transforms = {}
        colliders = {}
        for record in records:
            if record.startswith("1 &"):
                identifier = re.match(r"1 &(\d+)", record).group(1)
                object_names[identifier] = re.search(r"^  m_Name: (.+)$", record, re.MULTILINE).group(1).strip().strip('"')
            elif record.startswith("4 &"):
                owner = re.search(r"^  m_GameObject: \{fileID: (\d+)\}", record, re.MULTILINE).group(1)
                transforms[owner] = record
            elif record.startswith("65 &"):
                owner = re.search(r"^  m_GameObject: \{fileID: (\d+)\}", record, re.MULTILINE).group(1)
                self.assertNotIn(owner, colliders, "Duplicate BoxCollider on one model object")
                colliders[owner] = record
        for identifier, name in object_names.items():
            if name not in self.objects:
                continue
            entry, transform = self.objects[name], transforms[identifier]
            self.assertEqual(identifier in colliders, bool(entry["collider"]), name + " collider")
            if entry["collider"]:
                self.assertIn("  m_IsTrigger: 0", colliders[identifier], name + " must block movement")
                self.assertIn("  m_Enabled: 1", colliders[identifier], name + " collider must be enabled")
                text = re.search(r"^  m_Size: \{([^}]+)\}", colliders[identifier], re.MULTILINE).group(1)
                size = tuple(float(value) for value in re.findall(r"[xyz]: ([^,}]+)", text))
                self.assertEqual(size, (1, 2 if entry["kind"] == "Cylinder" else 1, 1), name + " collider dimensions")
                text = re.search(r"^  m_Center: \{([^}]+)\}", colliders[identifier], re.MULTILINE).group(1)
                center = tuple(float(value) for value in re.findall(r"[xyz]: ([^,}]+)", text))
                self.assertEqual(center, (0, 0, 0), name + " collider center")
            for field, key in (("m_LocalPosition", "position"), ("m_LocalScale", "scale")):
                text = re.search(r"^  " + field + r": \{([^}]+)\}", transform, re.MULTILINE).group(1)
                actual = tuple(float(value) for value in re.findall(r"[xyz]: ([^,}]+)", text))
                self.assertEqual(len(actual), 3, name + " " + field)
                for value, expected in zip(actual, vector(entry[key])):
                    self.assertAlmostEqual(value, expected, places=5, msg=name + " " + field)
            text = re.search(r"^  m_LocalRotation: \{([^}]+)\}", transform, re.MULTILINE).group(1)
            quaternion = tuple(float(value) for value in re.findall(r"[xyzw]: ([^,}]+)", text))
            self.assertEqual(len(quaternion), 4, name + " rotation")
            self.assertAlmostEqual(sum(value * value for value in quaternion), 1, places=5, msg=name)
            expected_rotation = Box(entry).inverse
            for index, axis in enumerate(((1, 0, 0), (0, 1, 0), (0, 0, 1))):
                for actual, expected in zip(quaternion_rotate(axis, quaternion), expected_rotation[index]):
                    self.assertAlmostEqual(actual, expected, places=5, msg=name + " rotation")
        for guid in ("963b3f782c6d71942ad1e73d117b2820", "9191262690f98974abc0076595479fd6",
                     "052faaac586de48259a63d0c4782560b"):
            self.assertIn("guid: " + guid, scene)
        self.assertTrue(any(record.startswith("143 &") for record in records), "No real CharacterController")
        self.assertGreaterEqual(sum(record.startswith("65 &") for record in records), len(self.boxes), "Missing BoxColliders")
        controller_meta = (ROOT / "Assets/Progression/LevelProgressionController.cs.meta").read_text(encoding="utf-8")
        progression_guid = re.search(r"^guid: ([a-f0-9]+)$", controller_meta, re.MULTILINE).group(1)
        self.assertNotIn("guid: " + progression_guid, scene, "Preview must not fake gameplay progression")

    def test_gameplay_anchors_have_agreed_names_and_serialized_transforms(self):
        anchors = self.model["anchors"]
        self.assertCountEqual([anchor["name"] for anchor in anchors], ANCHOR_NAMES)
        for anchor in anchors:
            self.assertEqual(anchor["parent"], "Anchors", anchor["name"])
        self.assertEqual(next(anchor["position"] for anchor in anchors if anchor["name"] == "Spawn"), self.model["spawn"])
        scene = SCENE_PATH.read_text(encoding="utf-8")
        records = re.split(r"^--- !u!", scene, flags=re.MULTILINE)[1:]
        names, transforms = {}, {}
        for record in records:
            if record.startswith("1 &"):
                name = re.search(r"^  m_Name: (.+)$", record, re.MULTILINE).group(1).strip().strip('"')
                names.setdefault(name, []).append(re.match(r"1 &(\d+)", record).group(1))
            elif record.startswith("4 &"):
                owner = re.search(r"^  m_GameObject: \{fileID: (\d+)\}", record, re.MULTILINE).group(1)
                transforms[owner] = record
        self.assertEqual(len(names["Anchors"]), 1)
        parent = re.match(r"4 &(\d+)", transforms[names["Anchors"][0]]).group(1)
        for anchor in anchors:
            self.assertEqual(len(names.get(anchor["name"], [])), 1, anchor["name"])
            transform = transforms[names[anchor["name"]][0]]
            self.assertIn("m_Father: {fileID: " + parent + "}", transform, anchor["name"])
            text = re.search(r"^  m_LocalPosition: \{([^}]+)\}", transform, re.MULTILINE).group(1)
            position = tuple(float(value) for value in re.findall(r"[xyz]: ([^,}]+)", text))
            self.assertEqual(len(position), 3)
            for actual, expected in zip(position, vector(anchor["position"])):
                self.assertAlmostEqual(actual, expected, places=5, msg=anchor["name"])


if __name__ == "__main__":
    unittest.main()

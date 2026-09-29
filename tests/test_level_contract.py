"""Design-model checks only: no Unity player, physics or combat is executed."""
from copy import deepcopy
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from validate_level_contract import load, reachable, validate, verify_source


class LevelContractTests(unittest.TestCase):
    def setUp(self):
        self.contract = load(ROOT / "docs/level-contract.json")

    def event(self, name):
        return next(e for e in self.contract["events"] if e["id"] == name)

    def gate(self, first, second):
        return next(g for g in self.contract["connections"]
                    if {g["from"], g["to"]} == {first, second})

    def test_valid_contract_and_source(self):
        counts = validate(self.contract)
        verify_source(self.contract, ROOT)
        self.assertGreater(counts["with_secret"], counts["without_secret"])

    def test_validation_does_not_modify_contract(self):
        before = deepcopy(self.contract)
        validate(self.contract)
        self.assertEqual(before, self.contract)

    def test_main_route_wins_without_secret(self):
        states = reachable(self.contract, include_optional=False)
        wins = [s for s in states if s[0] == "exit"]
        self.assertTrue(wins)
        for room, flags in states:
            self.assertNotEqual("bonus_room", room)
            self.assertFalse({"secret_lever_pulled", "bonus_discovered"} & flags)

    def test_secret_route_can_win_and_return_to_throne(self):
        states = reachable(self.contract)
        self.assertTrue(any(room == "exit" and "bonus_discovered" in flags for room, flags in states))
        self.assertTrue(any(room == "throne_room" and "bonus_discovered" in flags for room, flags in states))

    def test_all_winning_states_include_every_required_event(self):
        required = {"first_enemy_defeated", "main_puzzle_solved", "miniboss_defeated",
                    "library_opened", "final_enemy_defeated"}
        wins = [flags for room, flags in reachable(self.contract) if room == "exit"]
        self.assertTrue(wins)
        self.assertEqual(required, set(self.contract["required_events"]))
        self.assertTrue(all(required <= flags for flags in wins))

    def test_library_is_mandatory_and_secret_is_not(self):
        self.assertEqual("library", self.event("library_opened")["room"])
        self.assertFalse(self.event("library_opened")["optional"])
        self.assertTrue(self.event("secret_lever_pulled")["optional"])
        self.assertTrue(self.event("bonus_discovered")["optional"])
        self.assertEqual("catacombs", self.event("secret_lever_pulled")["room"])
        for first, second in (("throne_room", "library"), ("library", "catacombs"),
                              ("catacombs", "bonus_room"), ("throne_room", "bonus_room")):
            self.assertIsNotNone(self.gate(first, second))

    def test_first_puzzle_requires_return_to_the_junction(self):
        self.assertIsNotNone(self.gate("first_encounter", "puzzle"))
        self.assertIsNotNone(self.gate("first_encounter", "throne_room"))
        self.assertFalse(any({g["from"], g["to"]} == {"puzzle", "throne_room"}
                             for g in self.contract["connections"]))

    def test_completion_is_terminal(self):
        graph = reachable(self.contract)
        self.assertTrue(all(not edges for state, edges in graph.items() if state[0] == "exit"))

    def test_each_enumeration_starts_clean(self):
        start = ("courtyard", frozenset())
        first = reachable(self.contract)
        first.clear()
        self.assertIn(start, reachable(self.contract))

    def test_rejects_bypass_into_throne(self):
        self.gate("first_encounter", "throne_room")["requires"] = []
        with self.assertRaisesRegex(ValueError, "bypassed entry requirement: throne_room"):
            validate(self.contract)

    def test_rejects_exit_before_final_fight(self):
        self.gate("final_arena", "exit")["requires"].remove("final_enemy_defeated")
        with self.assertRaisesRegex(ValueError, "bypassed entry requirement: exit"):
            validate(self.contract)

    def test_rejects_secret_becoming_mandatory(self):
        self.gate("catacombs", "final_arena")["requires"].append("bonus_discovered")
        with self.assertRaisesRegex(ValueError, "unreachable room: final_arena"):
            validate(self.contract)

    def test_rejects_unreachable_secret_room(self):
        self.contract["connections"] = [g for g in self.contract["connections"]
                                        if "bonus_room" not in (g["from"], g["to"])]
        with self.assertRaisesRegex(ValueError, "unreachable room: bonus_room"):
            validate(self.contract)

    def test_rejects_shortcut_bypassing_library(self):
        self.gate("throne_room", "bonus_room")["requires"] = ["miniboss_defeated"]
        with self.assertRaisesRegex(ValueError, "bypassed entry requirement"):
            validate(self.contract)

    def test_rejects_cyclic_event_dependencies(self):
        self.event("first_enemy_defeated")["requires"] = ["main_puzzle_solved"]
        with self.assertRaisesRegex(ValueError, "unreachable"):
            validate(self.contract)

    def test_rejects_required_event_only_available_in_optional_room(self):
        self.event("main_puzzle_solved")["room"] = "bonus_room"
        with self.assertRaisesRegex(ValueError, "unreachable"):
            validate(self.contract)

    def test_rejects_duplicate_room_event_and_connection(self):
        for key in ("rooms", "events", "connections"):
            with self.subTest(key=key):
                mutated = deepcopy(self.contract)
                mutated[key].append(deepcopy(mutated[key][0]))
                with self.assertRaisesRegex(ValueError, "duplicate"):
                    validate(mutated)

    def test_rejects_reversed_duplicate_connection(self):
        self.contract["connections"].append({"from": "first_encounter", "to": "courtyard", "requires": []})
        with self.assertRaisesRegex(ValueError, "duplicate"):
            validate(self.contract)

    def test_rejects_unknown_requirements_and_rooms(self):
        for mutation in ("event", "room", "gate"):
            with self.subTest(mutation=mutation):
                changed = deepcopy(self.contract)
                if mutation == "event":
                    changed["events"][0]["requires"] = ["unknown"]
                elif mutation == "room":
                    changed["events"][0]["room"] = "missing"
                else:
                    changed["connections"][0]["to"] = "missing"
                with self.assertRaisesRegex(ValueError, "unknown"):
                    validate(changed)

    def test_rejects_self_dependent_event(self):
        self.event("main_puzzle_solved")["requires"] = ["main_puzzle_solved"]
        with self.assertRaisesRegex(ValueError, "require itself"):
            validate(self.contract)

    def test_rejects_source_path_outside_repository(self):
        self.contract["source"]["path"] = "../private.docx"
        with self.assertRaisesRegex(ValueError, "unexpected source path"):
            validate(self.contract)

    def test_rejects_altered_source(self):
        self.contract["source"]["git_blob_sha"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "DOCX changed"):
            verify_source(self.contract, ROOT)

    def test_rejects_malformed_schema(self):
        changes = [{"schema_version": True}, {"schema_version": 2}, {"rooms": []},
                   {"start": []}, {"required_events": "all"}, {"exit": "missing"},
                   {"start": "exit"}, {"extra": 42}]
        for change in changes:
            with self.subTest(change=change):
                mutated = deepcopy(self.contract)
                mutated.update(change)
                with self.assertRaises(ValueError):
                    validate(mutated)

    def test_rejects_invalid_optional_type(self):
        self.contract["rooms"][0]["optional"] = "false"
        with self.assertRaisesRegex(ValueError, "boolean"):
            validate(self.contract)

    def test_rejects_duplicate_json_keys(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "duplicate.json"
            path.write_text('{"schema_version": 1, "schema_version": 2}', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
                load(path)

    def test_cli_succeeds_from_another_working_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, str(ROOT / "tools/validate_level_contract.py")],
                                    cwd=directory, capture_output=True, text=True, timeout=10)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Unity scenes, combat and timing were not tested", result.stdout)

    def test_cli_rejects_invalid_or_missing_input(self):
        with tempfile.TemporaryDirectory() as directory:
            broken = Path(directory) / "broken.json"
            broken.write_text("{not valid json", encoding="utf-8")
            for path in (broken, Path(directory) / "missing.json"):
                with self.subTest(path=path):
                    result = subprocess.run([sys.executable, str(ROOT / "tools/validate_level_contract.py"), str(path)],
                                            capture_output=True, text=True, timeout=10)
                    self.assertEqual(1, result.returncode)
                    self.assertIn("Invalid level contract", result.stderr)
                    self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()

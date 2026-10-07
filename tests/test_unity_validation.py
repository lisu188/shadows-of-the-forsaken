"""Synthetic fixtures test validation tools, not Unity execution or gameplay."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import unity_validation as validation

ROOM_PERFORMANCE_METHODS = (
    "RoomPerformanceTransitionsKeepVisitIdentityAndSessionCounters",
    "RoomPerformanceDefeatRestartAndQuitKeepOriginalTokens",
    "RoomPerformanceExternalResetWaitsForNewRunningSession",
    "RoomPerformanceStaleRoomCallbackCannotCloseTheNewSessionVisit",
    "RoomPerformanceCapLeavesExistingSessionEvidenceOperational",
    "RoomPerformanceDisableFlushesOnceAndDoesNotResume",
    "RoomPerformanceWriteFailureStopsObservationWithoutMutatingGameplay",
    "RoomPerformanceMarksConfigurationChangesEvenAfterRestoration",
)


class ProjectValidationTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.files = {
            "ProjectSettings/ProjectVersion.txt": f"m_EditorVersion: {validation.UNITY_VERSION}\n",
            "ProjectSettings/EditorBuildSettings.asset": "EditorBuildSettings:\n",
            "Packages/manifest.json": json.dumps({"dependencies": validation.PACKAGES}),
            "Packages/packages-lock.json": json.dumps({"dependencies": {k: {"version": v} for k, v in validation.PACKAGES.items()}}),
            "Assets/Example.cs": "class Example {}",
            "Assets/Example.cs.meta": "fileFormatVersion: 2\nguid: " + "a" * 32 + "\n",
        }
        for name, text in self.files.items():
            self.write(name, text)
        self.paths = set(self.files)

    def write(self, name, content):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def verify(self):
        return validation.validate_project(self.root, self.paths)

    def test_accepts_consistent_tracked_project(self):
        self.assertEqual({"tracked_files": 6, "assets": 1, "unique_guids": 1}, self.verify())

    def test_untracked_local_files_do_not_pollute_validation(self):
        self.write("Assets/Untracked.cs", "untracked")
        self.write("Library/generated", "cache")
        self.assertEqual(1, self.verify()["assets"])

    def test_rejects_meta_missing_from_git_even_if_present_on_disk(self):
        self.paths.remove("Assets/Example.cs.meta")
        with self.assertRaisesRegex(validation.ValidationError, "Missing asset metadata"):
            self.verify()

    def test_rejects_missing_tracked_file(self):
        (self.root / "Assets/Example.cs").unlink()
        with self.assertRaisesRegex(validation.ValidationError, "Missing or unsafe tracked file"):
            self.verify()

    def test_rejects_duplicate_or_zero_guid(self):
        for guid, message in [("a" * 32, "Duplicate GUID"), ("0" * 32, "Invalid GUID")]:
            with self.subTest(guid=guid):
                self.write("Assets/Second.cs", "class Second {}")
                self.write("Assets/Second.cs.meta", "guid: " + guid + "\n")
                self.paths.update(["Assets/Second.cs", "Assets/Second.cs.meta"])
                with self.assertRaisesRegex(validation.ValidationError, message):
                    self.verify()

    def test_rejects_malformed_or_multiple_guids(self):
        for text in ["guid: invalid", "guid: " + "a" * 32 + "\nguid: " + "b" * 32]:
            with self.subTest(text=text):
                self.write("Assets/Example.cs.meta", text)
                with self.assertRaisesRegex(validation.ValidationError, "Invalid GUID"):
                    self.verify()

    def test_requires_nested_folder_metadata(self):
        self.write("Assets/Scripts/Nested.cs", "class Nested {}")
        self.write("Assets/Scripts/Nested.cs.meta", "guid: " + "b" * 32)
        self.paths.update(["Assets/Scripts/Nested.cs", "Assets/Scripts/Nested.cs.meta"])
        with self.assertRaisesRegex(validation.ValidationError, "Missing folder metadata"):
            self.verify()
        self.write("Assets/Scripts.meta", "guid: " + "c" * 32 + "\nfolderAsset: yes\n")
        self.paths.add("Assets/Scripts.meta")
        self.assertEqual(2, self.verify()["assets"])

    def test_rejects_orphan_file_metadata(self):
        self.write("Assets/Gone.cs.meta", "guid: " + "c" * 32)
        self.paths.add("Assets/Gone.cs.meta")
        with self.assertRaisesRegex(validation.ValidationError, "Orphan file metadata"):
            self.verify()

    def test_rejects_folder_flag_on_file_metadata(self):
        self.write("Assets/Example.cs.meta", self.files["Assets/Example.cs.meta"] + "folderAsset: yes\n")
        with self.assertRaisesRegex(validation.ValidationError, "Folder metadata attached to a file"):
            self.verify()

    def test_rejects_generated_files_regardless_of_case(self):
        for name in ["Library/cache", "lOgS/output", "UserSettings/config", "tests/obj/a", "Build/game.exe"]:
            with self.subTest(name=name):
                self.write(name, "generated")
                with self.assertRaisesRegex(validation.ValidationError, "Generated file is tracked"):
                    validation.validate_project(self.root, self.paths | {name})

    def test_rejects_case_collisions_for_windows(self):
        self.write("assets/Another.txt", "case collision")
        with self.assertRaisesRegex(validation.ValidationError, "Case collision"):
            validation.validate_project(self.root, self.paths | {"assets/Another.txt"})

    def test_rejects_unsafe_and_symlink_paths(self):
        for name in ["../outside", "/absolute", "Assets\\Wrong.cs"]:
            with self.subTest(name=name):
                with self.assertRaisesRegex(validation.ValidationError, "Unsafe tracked path"):
                    validation.validate_project(self.root, self.paths | {name})
        (self.root / "Assets/Alias.cs").symlink_to(self.root / "Assets/Example.cs")
        with self.assertRaisesRegex(validation.ValidationError, "unsafe tracked file"):
            validation.validate_project(self.root, self.paths | {"Assets/Alias.cs"})

    def test_rejects_changed_editor_or_package_pin(self):
        for name, text, message in [
            ("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.1.0f1", "Expected Unity"),
            ("Packages/manifest.json", '{"dependencies":{}}', "Package pin/lock mismatch"),
            ("Packages/packages-lock.json", '{"dependencies":{}}', "Package pin/lock mismatch"),
            ("Packages/manifest.json", 'not json', "Invalid project configuration"),
        ]:
            with self.subTest(name=name, text=text):
                self.write(name, text)
                with self.assertRaisesRegex(validation.ValidationError, message):
                    self.verify()
                self.write(name, self.files[name])

    def test_requires_configuration_to_be_tracked(self):
        self.paths.remove("ProjectSettings/ProjectVersion.txt")
        with self.assertRaisesRegex(validation.ValidationError, "Required project file is not tracked"):
            self.verify()

    def test_rejects_empty_index(self):
        with self.assertRaisesRegex(validation.ValidationError, "No tracked files"):
            validation.validate_project(self.root, [])

    def test_git_index_and_cli_work_with_spaces_and_another_cwd(self):
        self.write("Assets/with space.cs", "class WithSpace {}")
        self.write("Assets/with space.cs.meta", "guid: " + "b" * 32)
        subprocess.run(["git", "init", "-q", str(self.root)], check=True, timeout=10)
        subprocess.run(["git", "-C", str(self.root), "add", "--all"], check=True, timeout=10)
        result = subprocess.run(
            [sys.executable, str(ROOT / "tools/unity_validation.py"), "project", "--root", str(self.root)],
            cwd=self.root.parent, capture_output=True, text=True, check=False, timeout=10,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(2, json.loads(result.stdout)["assets"])


def report_for(mode):
    root = ET.Element("test-run", result="Passed", failed="0", skipped="0", inconclusive="0")
    suite = ET.SubElement(root, "test-suite", type="Assembly", result="Passed")
    namespace = "ShadowsOfTheForsaken.Tests." + {"editmode": "EditMode", "playmode": "PlayMode"}[mode]
    for name in sorted(validation.EXPECTED_CASES[mode]):
        ET.SubElement(suite, "test-case", fullname=f"{namespace}.{name}", result="Passed")
    total = str(len(list(root.iter("test-case"))))
    root.set("total", total)
    root.set("passed", total)
    return root


class NUnitEvidenceTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.path = self.root / "results.xml"

    def save(self, report):
        ET.ElementTree(report).write(self.path, encoding="utf-8", xml_declaration=True)

    def test_accepts_complete_baseline_reports_for_both_modes(self):
        for mode in ("editmode", "playmode"):
            count = len(validation.EXPECTED_CASES[mode])
            with self.subTest(mode=mode):
                self.save(report_for(mode))
                self.assertEqual(count, validation.validate_results(self.path, mode)["passed"])

    def test_rejects_reports_that_omit_castle_scene_coverage(self):
        for mode, omitted in [("editmode", "CastleIsFirstEnabledSceneAndHasNoMissingComponents"),
                              ("playmode", "EveryPassageIsWalkableInBothDirections")]:
            with self.subTest(mode=mode):
                report = report_for(mode)
                suite = report.find("test-suite")
                for case in list(suite):
                    if omitted in case.get("fullname", ""):
                        suite.remove(case)
                count = str(len(list(report.iter("test-case"))))
                report.set("total", count)
                report.set("passed", count)
                self.save(report)
                with self.assertRaisesRegex(validation.ValidationError, omitted):
                    validation.validate_results(self.path, mode)

    def test_rejects_missing_report(self):
        with self.assertRaisesRegex(validation.ValidationError, "results missing"):
            validation.validate_results(self.path, "editmode")

    def test_rejects_malformed_xml_and_doctype(self):
        for text in ["<bad>", '<!DOCTYPE test-run [<!ENTITY x "x">]><test-run/>']:
            with self.subTest(text=text):
                self.path.write_text(text)
                with self.assertRaises(validation.ValidationError):
                    validation.validate_results(self.path, "editmode")

    def test_rejects_wrong_root_and_failed_run(self):
        report = report_for("editmode")
        report.tag = "test-results"
        self.save(report)
        with self.assertRaisesRegex(validation.ValidationError, "test-run"):
            validation.validate_results(self.path, "editmode")
        report.tag = "test-run"
        report.set("result", "Failed")
        self.save(report)
        with self.assertRaises(validation.ValidationError):
            validation.validate_results(self.path, "editmode")

    def test_rejects_success_with_zero_tests(self):
        report = report_for("editmode")
        report.remove(report.find("test-suite"))
        report.set("total", "0")
        report.set("passed", "0")
        self.save(report)
        with self.assertRaisesRegex(validation.ValidationError, "nonempty"):
            validation.validate_results(self.path, "editmode")

    def test_rejects_mismatched_counts(self):
        report = report_for("editmode")
        report.set("total", "100")
        self.save(report)
        with self.assertRaisesRegex(validation.ValidationError, "counts do not match"):
            validation.validate_results(self.path, "editmode")

    def test_rejects_invalid_and_missing_counts(self):
        for key, value in [("total", "abc"), ("total", "-1"), ("skipped", "1"), ("failed", "1"), ("inconclusive", "-1"), ("passed", None)]:
            report = report_for("editmode")
            if value is None:
                del report.attrib[key]
            else:
                report.set(key, value)
            with self.subTest(key=key, value=value):
                self.save(report)
                with self.assertRaises(validation.ValidationError):
                    validation.validate_results(self.path, "editmode")

    def test_rejects_failed_skipped_and_inconclusive_cases_despite_root_passed(self):
        for state in ["Failed", "Skipped", "Inconclusive", "Ignored"]:
            report = report_for("editmode")
            next(report.iter("test-case")).set("result", state)
            self.save(report)
            with self.subTest(state=state):
                with self.assertRaisesRegex(validation.ValidationError, "did not pass"):
                    validation.validate_results(self.path, "editmode")

    def test_rejects_failed_suite(self):
        report = report_for("editmode")
        report.find("test-suite").set("result", "Failed")
        self.save(report)
        with self.assertRaisesRegex(validation.ValidationError, "did not pass"):
            validation.validate_results(self.path, "editmode")

    def test_rejects_duplicate_and_missing_test_names(self):
        for duplicate in [True, False]:
            report = report_for("editmode")
            cases = list(report.iter("test-case"))
            cases[1].set("fullname", cases[0].get("fullname") if duplicate else "")
            self.save(report)
            with self.assertRaisesRegex(validation.ValidationError, "test names"):
                validation.validate_results(self.path, "editmode")

    def test_rejects_wrong_mode_and_missing_expected_baseline(self):
        self.save(report_for("playmode"))
        with self.assertRaisesRegex(validation.ValidationError, "Expected baseline"):
            validation.validate_results(self.path, "editmode")
        report = report_for("editmode")
        next(report.iter("test-case")).set("fullname", "Other.Tests.UnrelatedPassingTest")
        self.save(report)
        with self.assertRaisesRegex(validation.ValidationError, "Expected baseline"):
            validation.validate_results(self.path, "editmode")

    def test_rejects_unknown_mode(self):
        with self.assertRaisesRegex(validation.ValidationError, "Unknown test mode"):
            validation.validate_results(self.path, "standalone")

    def test_rejects_missing_gameplay_regression_despite_passing_other_tests(self):
        for mode, method in [
            ("editmode", "OneShotRejectsRepeatAndStaleTokensAfterReset"),
            ("editmode", "CompoundTargetsAreHitOncePerActionAndAgainOnNextAction"),
            ("editmode", "HashCollisionsDoNotMergeDistinctTargets"),
            ("playmode", "InteractionGateResetWaitsForTeleportedOccupantAndClosesAfterEscape"),
            ("playmode", "CombatPhysicsEmbeddedWallUsesTheActorsLocalPhysicsScene"),
        ]:
            with self.subTest(mode=mode, method=method):
                report = report_for(mode)
                case = next(c for c in report.iter("test-case") if f".{method}" in c.get("fullname", ""))
                case.set("fullname", "Other.Tests.UnrelatedPassingTest")
                self.save(report)
                with self.assertRaisesRegex(validation.ValidationError, method):
                    validation.validate_results(self.path, mode)

    def test_rejects_reports_that_omit_required_regression_families(self):
        for mode, fixture in [
            ("editmode", "CameraMotionTests"),
            ("editmode", "PlayerMotorTests"),
            ("editmode", "LevelProgressionTests"),
            ("editmode", "CombatRulesTests"),
            ("playmode", "CameraFollowTests"),
            ("playmode", "PlayerMovementTests"),
            ("playmode", "ProgressionControllerTests"),
            ("playmode", "FullCastleRouteTests"),
            ("playmode", "CombatControllerTests"),
            ("playmode", "ForsakenLevelTests"),
            ("playmode", "LevelInteractionTests"),
            ("playmode", "WorldTextTests"),
            ("playmode", "LevelHudLayoutTests"),
        ]:
            with self.subTest(mode=mode, fixture=fixture):
                report = report_for(mode)
                suite = report.find("test-suite")
                removed = [case for case in suite if f".{fixture}." in case.get("fullname", "")]
                self.assertTrue(removed, f"The catalog must require {fixture}")
                for case in removed:
                    suite.remove(case)
                count = str(len(list(report.iter("test-case"))))
                report.set("total", count)
                report.set("passed", count)
                self.save(report)
                with self.assertRaisesRegex(validation.ValidationError, fixture):
                    validation.validate_results(self.path, mode)

    def test_rejects_same_method_name_from_wrong_fixture(self):
        for mode, required in [
            ("editmode", "PlayerMotorTests"),
            ("playmode", "ProgressionControllerTests"),
            ("playmode", "CameraFollowTests"),
        ]:
            with self.subTest(mode=mode, fixture=required):
                report = report_for(mode)
                case = next(c for c in report.iter("test-case") if f".{required}." in c.get("fullname", ""))
                case.set("fullname", case.get("fullname").replace(f".{required}.", ".UnrelatedFixture."))
                self.save(report)
                with self.assertRaisesRegex(validation.ValidationError, required):
                    validation.validate_results(self.path, mode)

    def test_rejects_either_missing_authored_or_default_collision_case(self):
        for method in [
            "ShoulderFramingRetainsNearPlaneClearanceBesideWallAndDuringTurn",
            "LockedGateTurnDuringJumpRetainsAValidatedPreviousCameraPose",
        ]:
            for removed_index in [0, 1]:
                with self.subTest(method=method, removed_index=removed_index):
                    report = report_for("playmode")
                    suite = report.find("test-suite")
                    cases = [c for c in suite if f".CameraFollowTests.{method}(" in c.get("fullname", "")]
                    self.assertEqual(2, len(cases), "Both scene settings must be required")
                    suite.remove(cases[removed_index])
                    count = str(len(list(report.iter("test-case"))))
                    report.set("total", count)
                    report.set("passed", count)
                    self.save(report)
                    with self.assertRaisesRegex(validation.ValidationError, method):
                        validation.validate_results(self.path, "playmode")

    def test_rejects_missing_aim_or_mesh_ray_regression_case(self):
        for method in [
            "CameraFollowTests.LookHeightOffsetChangesOnlyAimAndRetainsTheCollisionPivot",
            "CameraFollowTests.ShoulderAimFractionChangesOnlyAimAndRetainsTheFullCameraBoom",
            "FullCastleRouteTests.FramingRayMissesRemainInfiniteInsideAndOutsideMeshBounds",
            "FullCastleRouteTests.FramingRayRejectsBackfacesAndHitsAtTheExclusiveDistanceLimit",
            "FullCastleRouteTests.FramingRaySelectsNearestSurfaceIncludingUnlabelledPlayer",
        ]:
            with self.subTest(method=method):
                report = report_for("playmode")
                case = next(c for c in report.iter("test-case") if f".{method}" in c.get("fullname", ""))
                # Preserve the root count with an unrelated passing case: the
                # named regression, not merely the total, must be required.
                case.set("fullname", "ShadowsOfTheForsaken.Tests.PlayMode.UnrelatedFixture.PassingTest")
                self.save(report)
                with self.assertRaisesRegex(validation.ValidationError, method):
                    validation.validate_results(self.path, "playmode")

    def test_historical_193_case_report_cannot_qualify_new_zero_aim_guard(self):
        method = "MovingTargetAtHeldCameraAimUsesSafeBoomWithoutInvalidRotation"
        report = report_for("playmode")
        suite = report.find("test-suite")
        case = next(c for c in suite if f".CameraFollowTests.{method}" in c.get("fullname", ""))
        suite.remove(case)
        # This is the historical camera suite, before both the zero-aim guard
        # and the eight later room-observer cases; do not relabel 201 as 193.
        for room_method in ROOM_PERFORMANCE_METHODS:
            case = next(c for c in suite if
                        f".PlayerValidationPerformanceTests.{room_method}" in c.get("fullname", ""))
            suite.remove(case)
        alternate_fixtures = (
            ".CombatControllerTests.", ".ForsakenLevelTests.", ".LevelHudLayoutTests.",
            ".LevelInteractionTests.", ".WorldTextTests.",
        )
        for case in list(suite):
            name = case.get("fullname", "")
            if any(fixture in name for fixture in alternate_fixtures) or name.endswith(
                    ".PlayerMovementTests.MouseAttackFiresOncePerPressThroughOwnedActionMap"):
                suite.remove(case)
        count = len(list(report.iter("test-case")))
        self.assertEqual(193, count)
        report.set("total", str(count))
        report.set("passed", str(count))
        self.save(report)
        with self.assertRaisesRegex(validation.ValidationError, method):
            validation.validate_results(self.path, "playmode")

    def test_room_observer_cases_cannot_be_omitted_or_satisfied_by_another_fixture(self):
        for method in ROOM_PERFORMANCE_METHODS:
            for substitute in ("PassingUnrelatedCase", method):
                with self.subTest(method=method, substitute=substitute):
                    report = report_for("playmode")
                    case = next(c for c in report.iter("test-case") if
                                f".PlayerValidationPerformanceTests.{method}" in c.get("fullname", ""))
                    # Keep the total and passing count: only the required named
                    # method in the correct fixture can satisfy this regression.
                    case.set("fullname", f"ShadowsOfTheForsaken.Tests.PlayMode.UnrelatedFixture.{substitute}")
                    self.save(report)
                    with self.assertRaisesRegex(validation.ValidationError, method):
                        validation.validate_results(self.path, "playmode")

    def test_rejects_missing_full_level_route_despite_all_executed_cases_passing(self):
        report = report_for("playmode")
        suite = report.find("test-suite")
        missing = next(case for case in suite if "MainRouteWinsWithoutSecret" in case.get("fullname", ""))
        suite.remove(missing)
        count = str(len(list(report.iter("test-case"))))
        report.set("total", count)
        report.set("passed", count)
        self.save(report)
        with self.assertRaisesRegex(validation.ValidationError, "MainRouteWinsWithoutSecret"):
            validation.validate_results(self.path, "playmode")

    def test_rejects_changed_frame_rate_arguments_without_changing_case_counts(self):
        for mode, method in [
            ("editmode", "CombatRulesTests.SwingHitsExactlyOnceAtRepresentativeFrameRates"),
            ("playmode", "PlayerMovementTests.GroundMovementAtRepresentativeFrameRates"),
        ]:
            with self.subTest(mode=mode):
                report = report_for(mode)
                wrong_rates = {"30": "31", "60": "32", "120": "33"}
                for case in report.iter("test-case"):
                    name = case.get("fullname")
                    if method + "(" in name:
                        prefix, argument = name.split("(", 1)
                        case.set("fullname", prefix + "(" + wrong_rates[argument[:-1]] + ")")
                self.save(report)
                with self.assertRaisesRegex(validation.ValidationError, method + r"\(30\)"):
                    validation.validate_results(self.path, mode)

    def test_rejects_changed_string_enum_boolean_and_nonfinite_arguments(self):
        replacements = [
            ('"Assets/PlayerMovement.cs"', '"Assets/Other.cs"'),
            ("(Jump)", "(Unknown)"),
            ("(False)", "(0)"),
            ("(float.NaN)", "(0.0f)"),
            ("(int.MaxValue)", "(8)"),
        ]
        for old, new in replacements:
            with self.subTest(argument=old):
                report = report_for("editmode")
                case = next(case for case in report.iter("test-case") if old in case.get("fullname"))
                original = case.get("fullname")
                case.set("fullname", original.replace(old, new))
                self.save(report)
                with self.assertRaises(validation.ValidationError):
                    validation.validate_results(self.path, "editmode")

    def test_rejects_changed_main_parameters_without_changing_case_counts(self):
        replacements = [
            ("editmode", "CombatStateTests.InvalidAttackTimingIsRejected",
             "(double.NaN,0.1d,0.1d)", "(0.0d,0.1d,0.1d)"),
            ("editmode", "EncounterStateTests.EncounterLifeAndSessionGatesPrecedePursuit",
             "(False,True,True,Dormant)", "(False,True,True,Chasing)"),
            ("playmode", "CameraFollowTests.LockedGateTurnDuringJumpRetainsAValidatedPreviousCameraPose",
             "(5.0f,2.0f,0.0f,1.4f,1.0f,60.0f)", "(5.0f,2.0f,0.0f,1.4f,1.0f,61.0f)"),
            ("playmode", "CastleLayoutTraversalTests.EveryPassageIsWalkableInBothDirections",
             '("Courtyard","FirstEncounter",False)', '("Courtyard","Puzzle",False)'),
            ("playmode", "EnemyEncounterTests.EncounterLostOrSuspendedTargetInterruptsTelegraph",
             '("time-scale")', '("timescale")'),
        ]
        for mode, method, old, new in replacements:
            with self.subTest(mode=mode, method=method):
                report = report_for(mode)
                case = next(case for case in report.iter("test-case")
                            if case.get("fullname").endswith("." + method + old))
                case.set("fullname", case.get("fullname").removesuffix(old) + new)
                self.save(report)
                with self.assertRaisesRegex(validation.ValidationError, method):
                    validation.validate_results(self.path, mode)

    def test_extra_passed_case_cannot_replace_a_required_parameter_identity(self):
        report = report_for("playmode")
        suite = report.find("test-suite")
        case = next(case for case in suite if "GroundMovementAtRepresentativeFrameRates(30)" in case.get("fullname"))
        case.set("fullname", case.get("fullname").replace("(30)", "(31)"))
        ET.SubElement(suite, "test-case", fullname="Other.Tests.ExtraPassingTest", result="Passed")
        count = str(len(list(report.iter("test-case"))))
        report.set("total", count)
        report.set("passed", count)
        self.save(report)
        with self.assertRaisesRegex(validation.ValidationError, r"GroundMovementAtRepresentativeFrameRates\(30\)"):
            validation.validate_results(self.path, "playmode")

    def test_cli_returns_failure_for_missing_report(self):
        result = subprocess.run(
            [sys.executable, str(ROOT / "tools/unity_validation.py"), "results", "--mode", "editmode", str(self.path)],
            capture_output=True, text=True, timeout=10,
        )
        self.assertEqual(1, result.returncode)
        self.assertIn("NOT verified", result.stderr)


class LocalRunnerCommandTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.editor = self.root / "Unity"
        self.editor.touch()
        self.output = self.root / "artifacts"
        self.output.mkdir()

    def run_editor(self):
        return validation.run_unity(self.root, self.editor, "editmode", self.output, 10)

    @patch.object(validation, "validate_project")
    @patch.object(validation.subprocess, "run")
    def test_command_omits_quit_and_checks_new_evidence(self, process, project):
        def execute(command, **kwargs):
            self.assertNotIn("-quit", command)
            self.assertIn("-runTests", command)
            self.assertEqual("EditMode", command[command.index("-testPlatform") + 1])
            self.assertEqual("Shadows.EditMode.Tests", command[command.index("-assemblyNames") + 1])
            self.assertEqual(10, kwargs["timeout"])
            ET.ElementTree(report_for("editmode")).write(self.output / "editmode-results.xml")
            return subprocess.CompletedProcess(command, 0)
        process.side_effect = execute
        self.assertEqual(len(validation.EXPECTED_CASES["editmode"]), self.run_editor()["passed"])
        project.assert_called_once_with(self.root)

    @patch.object(validation, "validate_project")
    @patch.object(validation.subprocess, "run", return_value=subprocess.CompletedProcess([], 0))
    def test_stale_passing_report_cannot_mask_empty_execution(self, process, project):
        ET.ElementTree(report_for("editmode")).write(self.output / "editmode-results.xml")
        with self.assertRaisesRegex(validation.ValidationError, "results missing"):
            self.run_editor()

    @patch.object(validation, "validate_project")
    @patch.object(validation.subprocess, "run", return_value=subprocess.CompletedProcess([], 1))
    def test_nonzero_editor_exit_is_failure(self, process, project):
        with self.assertRaisesRegex(validation.ValidationError, "exited with code 1"):
            self.run_editor()

    @patch.object(validation, "validate_project")
    @patch.object(validation.subprocess, "run", side_effect=subprocess.TimeoutExpired("Unity", 10))
    def test_editor_timeout_is_failure(self, process, project):
        with self.assertRaisesRegex(validation.ValidationError, "timed out"):
            self.run_editor()

    def test_missing_editor_fails_before_launch(self):
        self.editor.unlink()
        with self.assertRaisesRegex(validation.ValidationError, "editor not found"):
            self.run_editor()

    def test_nonpositive_timeout_fails(self):
        with self.assertRaisesRegex(validation.ValidationError, "positive timeout"):
            validation.run_unity(self.root, self.editor, "editmode", self.output, 0)


if __name__ == "__main__":
    unittest.main()

"""Validate tracked Unity assets and real NUnit evidence; never emulate the engine."""
import argparse
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
UNITY_VERSION = "6000.0.24f1"
PACKAGES = {
    "com.unity.inputsystem": "1.11.1",
    "com.unity.render-pipelines.universal": "17.0.3",
    "com.unity.test-framework": "1.4.5",
}
# Fixed acceptance cases from the source-declared final-level suites. Suffixes use
# the pinned NUnit runner's full-name format, including every argument. Never
# infer requirements from a report under validation or discard its arguments.
EXPECTED_TESTS = {
    "editmode": {
        "ProjectBaselineTests.EditorVersionMatchesPin": ('',),
        "ProjectBaselineTests.BaselineSceneIsEnabledInBuildSettings": ('',),
        "ProjectBaselineTests.RuntimeScriptImportsWithOriginalGuid": ('("Assets/PlayerMovement.cs","PlayerMovement","963b3f782c6d71942ad1e73d117b2820")', '("Assets/CameraFollow.cs","CameraFollow","9191262690f98974abc0076595479fd6")'),
        "ProjectBaselineTests.BaselineSceneHasNoMissingComponents": ('',),
        "ProjectBaselineTests.MaterialsHaveResolvableShaders": ('',),
        "LevelProgressionTests.StartsInCourtyardWithEmptySession": ('',),
        "LevelProgressionTests.MainRouteCompletesWithoutOptionalObjectives": ('',),
        "LevelProgressionTests.BonusRouteReturnsThroughThroneWithoutBypassingLibraryOrFinal": ('',),
        "LevelProgressionTests.PuzzleRequiresReturningToTheJunction": ('',),
        "LevelProgressionTests.BonusStaysClosedUntilLeverIsUsed": ('',),
        "LevelProgressionTests.FinalEnemyMustBeDefeatedBeforeExit": ('',),
        "LevelProgressionTests.WrongRoomAndWrongOrderAreRejectedWithoutNotifications": ('',),
        "LevelProgressionTests.DuplicateObjectiveChangesNothingAndDoesNotNotifyAgain": ('',),
        "LevelProgressionTests.NotificationsContainCommittedImmutableSnapshots": ('',),
        "LevelProgressionTests.CompletionIsTerminalAndEmittedOnlyOnce": ('',),
        "LevelProgressionTests.ResetClearsStateAndReclosesGates": ('(False)', '(True)'),
        "LevelProgressionTests.LateSignalsFromPreviousSessionAreRejectedEvenInMatchingRoom": ('',),
        "LevelProgressionTests.DifferentInstancesDoNotShareStateListenersOrSessionTokens": ('',),
        "LevelProgressionTests.UnsubscribedListenerDoesNotReceiveLaterChanges": ('',),
        "LevelProgressionTests.RejectsReentrantMutationsButAllowsQueries": ('',),
        "LevelProgressionTests.ListenerFailureDoesNotHideChangeFromOtherListenersOrLeaveGuardLocked": ('',),
        "LevelProgressionTests.InvalidRoomIsRejected": ('(-1)', '(9)', '(int.MaxValue)'),
        "LevelProgressionTests.InvalidOrCompositeObjectiveIsRejected": ('(0)', '(-1)', '(3)', '(127)', '(128)'),
        "LevelProgressionTests.AllPassagesRemainSymmetricAndUnconnectedRoomsStayBlocked": ('',),
        "PlayerMotorTests.NoInputProducesNoHorizontalMovement": ('',),
        "PlayerMotorTests.ForwardAndBackwardMovementUseIndependentAxis": ('(1.0f,0.5f)', '(-1.0f,-0.5f)', '(0.5f,0.25f)'),
        "PlayerMotorTests.RotationDoesNotStrafeWithoutForwardInput": ('',),
        "PlayerMotorTests.GroundedJumpStartsWithExistingJumpForceAndAppliesGravityImmediately": ('',),
        "PlayerMotorTests.AirborneJumpCannotResetVerticalVelocity": ('',),
        "PlayerMotorTests.StaleGroundedFlagDoesNotRestartAscendingJump": ('',),
        "PlayerMotorTests.CeilingImmediatelyCancelsAscent": ('',),
        "PlayerMotorTests.LandingResetsFallButCannotCancelAscent": ('',),
        "PlayerMotorTests.FallingSpeedIsBoundedAndResetClearsIt": ('',),
        "PlayerMotorTests.BallisticMotionIsFrameRateIndependent": ('(30)', '(60)', '(120)'),
        "PlayerMotorTests.MovingWhileTurningFollowsTheSameArcAtEveryFrameRate": ('(30)', '(60)', '(120)'),
        "PlayerMotorTests.TerminalVelocityIntegrationIsFrameRateIndependent": ('(30)', '(60)', '(120)'),
        "PlayerMotorTests.ZeroTimeDoesNotConsumeJumpOrChangeVelocity": ('',),
        "PlayerMotorTests.InvalidTimeIsRejectedWithoutMutation": ('(-1.0f)', '(float.NaN)', '(float.PositiveInfinity)', '(2.0f)'),
        "PlayerMotorTests.InvalidSettingsAreRejected": ('(-1.0f)', '(float.NaN)', '(float.PositiveInfinity)', '(10001.0f)'),
        "PlayerMotorTests.InputSanitizesAxesWithoutCouplingTurnAndForward": ('',),
        "PlayerMotorTests.HeldControlsCannotResumeBeforeNeutral": ('',),
        "PlayerMotorTests.ButtonsFireOnceUntilRelease": ('(Jump)', '(Attack)', '(Interact)'),
        "PlayerMotorTests.ACompleteTapBetweenFramesIsNotLost": ('',),
        "PlayerMotorTests.GateInstancesAndResetsDoNotShareState": ('',),
        "CameraMotionTests.StationaryTargetDampingIsFrameRateIndependent": ('(30)', '(60)', '(120)'),
        "CameraMotionTests.ReturningFromObstructionIsSmoothAndFrameRateIndependent": ('(30)', '(60)', '(120)'),
        "CameraMotionTests.ObstructionSafetyIsNotDelayedByDamping": ('(30)', '(60)', '(120)'),
        "CameraMotionTests.IrregularTimeStepsComposeForAStationaryTarget": ('',),
        "CameraMotionTests.ZeroTimeOrResponseDoesNotMove": ('',),
        "CameraMotionTests.LargeTimeStepDoesNotOvershoot": ('',),
        "CameraMotionTests.ExtremeFiniteEndpointsDoNotOverflow": ('',),
        "CameraMotionTests.MissingHitDoesNotShortenRequestedDistance": ('',),
        "CameraMotionTests.HitDistanceAndPaddingBoundCameraTravel": ('(8.0f,0.1f,5.0f)', '(2.0f,0.1f,1.9f)', '(0.01f,0.1f,0.0f)', '(0.0f,0.0f,0.0f)'),
        "CameraMotionTests.RejectsInvalidTimeAndResponse": ('(-1.0f)', '(float.NaN)', '(float.PositiveInfinity)', '(float.NegativeInfinity)'),
        "CameraMotionTests.RejectsNonfinitePositions": ('(float.NaN)', '(float.PositiveInfinity)', '(float.NegativeInfinity)'),
        "CameraMotionTests.RejectsInvalidRequestedDistanceAndPadding": ('(-1.0f)', '(float.NaN)', '(float.PositiveInfinity)', '(float.NegativeInfinity)'),
        "CameraMotionTests.RejectsInvalidHitDistance": ('(-1.0f)', '(float.NaN)', '(float.NegativeInfinity)'),
        "CombatRulesTests.HealthClampsLethalDamageAndDeathCannotBeRepeated": ('',),
        "CombatRulesTests.ResetRevivesAndRejectsDamageFromPreviousSession": ('',),
        "CombatRulesTests.HealthRejectsInvalidBalanceAndTokens": ('',),
        "CombatRulesTests.AttackHasWindupActiveRecoveryAndCannotQueueAnotherSwing": ('',),
        "CombatRulesTests.TargetCanBeHitOncePerSwingIncludingCompoundColliders": ('',),
        "CombatRulesTests.SlowFrameCrossingActiveWindowCannotLoseHit": ('',),
        "CombatRulesTests.OldAttackNeverReplacesCapturedSession": ('',),
        "CombatRulesTests.CancelClearsActiveWindowAndSession": ('',),
        "CombatRulesTests.SwingHitsExactlyOnceAtRepresentativeFrameRates": ('(30)', '(60)', '(120)'),
        "CombatRulesTests.TimingRejectsZeroActiveAndNonFiniteDurations": ('',),
        "CombatRulesTests.DirectionalConeRejectsBehindOutsideRangeAndInvalidValues": ('',),
    },
    "playmode": {
        "BaselineSceneTests.BaselineSceneRunsWithoutMissingComponents": ('',),
        "BaselineSceneTests.BaselineCameraAndDirectionalLightAreActive": ('',),
        "ProgressionControllerTests.StartsInNewSceneScopedSession": ('',),
        "ProgressionControllerTests.ForwardsCommittedChangesAndSuppressesDuplicateObjectives": ('',),
        "ProgressionControllerTests.DisableAndEnableDoNotDuplicateSubscriptionsOrResetProgress": ('',),
        "ProgressionControllerTests.InactiveGameObjectRejectsCommands": ('',),
        "ProgressionControllerTests.ResetNotifiesOnceAndRejectsPreviousRunCallbacks": ('',),
        "ProgressionControllerTests.DestroyedControllerRejectsCommandsAndNewInstanceStartsClean": ('',),
        "ProgressionControllerTests.SceneUnloadDoesNotLeakProgressOrNotificationsIntoNextScene": ('',),
        "PlayerMovementTests.GroundMovementAtRepresentativeFrameRates": ('(30)', '(60)', '(120)'),
        "PlayerMovementTests.AAndDOnlyRotateAndDoNotStrafe": ('',),
        "PlayerMovementTests.WallStopsForwardMovement": ('',),
        "PlayerMovementTests.LowCeilingCancelsJumpAndPlayerLands": ('',),
        "PlayerMovementTests.HeldJumpDoesNotJumpAgainAfterLanding": ('',),
        "PlayerMovementTests.CharacterCanClimbConfiguredSteps": ('',),
        "PlayerMovementTests.NarrowPassageRemainsTraversable": ('',),
        "PlayerMovementTests.FocusLossDropsMovementAndRequiresReleasedControls": ('',),
        "PlayerMovementTests.DisabledControlsFlushQueuedJumpAndCannotFireActions": ('',),
        "PlayerMovementTests.InteractFiresOnPressDespiteTemplateHoldInteraction": ('',),
        "PlayerMovementTests.PressAndReleaseWithinOneInputUpdateIsNotLost": ('',),
        "PlayerMovementTests.DisablingComponentDoesNotDisableOrRewriteSourceActions": ('',),
        "PlayerMovementTests.MissingRequiredActionDisablesComponentWithOneError": ('',),
        "PlayerMovementTests.PausingClearsQueuedInputUntilControlsReturnToNeutral": ('',),
        "PlayerMovementTests.DisabledCharacterControllerIsNotMoved": ('',),
        "PlayerMovementTests.MouseAttackFiresOncePerPressThroughOwnedActionMap": ('',),
        "CameraFollowTests.OriginalSerializedFieldsAndDefaultsRemainAvailable": ('',),
        "CameraFollowTests.MissingTargetWarnsOnceAndCanRecover": ('',),
        "CameraFollowTests.DestroyedTargetCanBeReplacedWithoutOldSmoothingState": ('',),
        "CameraFollowTests.AcquiresOneTaggedPlayerButDoesNotChooseBetweenTwo": ('',),
        "CameraFollowTests.RejectsSelfAsTargetWithoutInvalidLookRotation": ('',),
        "CameraFollowTests.WallShortensBoomImmediately": ('',),
        "CameraFollowTests.WallRemovalReturnsSmoothlyAtRepresentativeFrameRates": ('(30)', '(60)', '(120)'),
        "CameraFollowTests.LowCeilingAndCornerLeaveCollisionVolumeClear": ('',),
        "CameraFollowTests.ChildTargetIgnoresItsCharacterAndCompoundColliders": ('',),
        "CameraFollowTests.TriggersAndExcludedLayersDoNotBlockCamera": ('',),
        "CameraFollowTests.WideNearPlaneIsIncludedInCollisionRadius": ('',),
        "CameraFollowTests.SaturatedQueryBufferDoesNotLoseTheNearestWall": ('',),
        "CameraFollowTests.StartingInsideWallIsResolvedBeforeRendering": ('',),
        "CameraFollowTests.PartiallyOverlappingPivotDoesNotPermitCrossingThinWall": ('',),
        "CameraFollowTests.EmbeddedPivotFailsSafelyAndRestoresRenderingAfterRecovery": ('',),
        "CameraFollowTests.OriginallyDisabledCameraIsNotEnabledByRecovery": ('',),
        "CameraFollowTests.TeleportAndExplicitSnapDoNotFlyThroughOldSceneSpace": ('',),
        "CameraFollowTests.RapidTurnDoesNotPlaceCameraInsideCorner": ('',),
        "CameraFollowTests.ReenabledComponentStartsWithFreshTracking": ('',),
        "CameraFollowTests.UnloadedTargetSceneCanBeReplaced": ('',),
        "CombatControllerTests.PlayerSwingHitsCompoundEnemyOnceAndGenuineDeathReportsObjective": ('',),
        "CombatControllerTests.MovementAttackRequestReachesSubscribedCombat": ('',),
        "CombatControllerTests.InvalidArenaCannotReplaceConfiguredEncounter": ('',),
        "CombatControllerTests.WallAndRearTargetBlockMeleeDamage": ('',),
        "CombatControllerTests.EnemyTelegraphDealsNoWindupDamageThenHitsOnce": ('',),
        "CombatControllerTests.TimeScalePauseCancelsWindupBeforeResume": ('',),
        "CombatControllerTests.InvalidSimulationDeltaCancelsWindupBeforeResume": ('',),
        "CombatControllerTests.LeavingEncounterBoundsStopsPursuitAndDamage": ('',),
        "CombatControllerTests.SessionResetRevivesEnemyAndPlayerAndRejectsOldHit": ('',),
        "CombatControllerTests.DisabledConsumerCatchesUpToResetOnEnable": ('',),
        "CombatControllerTests.NativeUpdatePursuesAndDamagesAtUncappedFrameRate": ('',),
        "CombatControllerTests.NativeUpdatePursuesAndDamagesAtSixtyFramesPerSecond": ('',),
        "LevelInteractionTests.RuneMechanismRejectsStaleSessionAndAllowsRetryAfterWrongPress": ('',),
        "LevelInteractionTests.PhysicalGateOpensOnlyFromProgressionAndClosesAfterResetOrReenable": ('',),
        "LevelInteractionTests.InteractionChoosesOneNearestVisibleTargetAndCannotReachThroughWall": ('',),
        "LevelInteractionTests.RejectedCrossingReturnsToSourceAndCannotGrantLockedRoom": ('',),
        "ForsakenLevelTests.MainRouteWinsWithoutSecretAndVictoryRestartRestoresTheWholeScene": ('',),
        "ForsakenLevelTests.OptionalRelicAndPhysicalConcealedRampReturnStillRequireTheFinalFight": ('',),
        "ForsakenLevelTests.EnemyInflictedDefeatAndRestartRestoreHealthFoesAndRejectOldDamage": ('',),
        "ForsakenLevelTests.ClosedPuzzleGateStopsPhysicalJumpCrossingsBeforeFirstEnemyDefeat": ('',),
        "WorldTextTests.WorldInscriptionRendersNearbyAndIsOccludedByTheClosedLibraryDoor": ('',),
        "LevelHudLayoutTests.ControlsAndRenderedTimerFitFourByThreeViewport": ('',),
        "LevelHudLayoutTests.ControlsAndRenderedTimerFitSixteenByTenViewport": ('',),
        "LevelHudLayoutTests.ControlsAndRenderedTimerFitSixteenByNineViewport": ('',),
        "LevelHudLayoutTests.ControlsAndRenderedTimerFitUltrawideViewport": ('',),
    },
}
EXPECTED_CASES = {
    mode: frozenset(name + suffix for name, suffixes in methods.items() for suffix in suffixes)
    for mode, methods in EXPECTED_TESTS.items()
}
GENERATED_ROOTS = {"library", "temp", "obj", "logs", "usersettings", "build", "builds", "artifacts", "testresults"}


class ValidationError(ValueError):
    pass


def tracked_files(root):
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "--cached", "-z"],
        capture_output=True, check=True, timeout=30,
    )
    return set(result.stdout.decode("utf-8").rstrip("\0").split("\0")) - {""}


def read_text(root, name):
    path = root / name
    if path.is_symlink() or not path.resolve().is_relative_to(root.resolve()):
        raise ValidationError(f"Unsafe tracked path: {name}")
    return path.read_text(encoding="utf-8-sig")


def validate_project(root, paths=None):
    root = Path(root)
    paths = tracked_files(root) if paths is None else set(paths)
    if not paths:
        raise ValidationError("No tracked files; use a Git checkout, not an empty directory")
    errors, guids, folded = [], {}, {}
    assets = set()
    for name in sorted(paths):
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "\\" in name or not path.parts:
            errors.append(f"Unsafe tracked path: {name}")
            continue
        if path.parts[0].lower() in GENERATED_ROOTS or any(p.lower() in {"bin", "obj", "__pycache__"} for p in path.parts):
            errors.append(f"Generated file is tracked: {name}")
        for part in (path, *path.parents):
            if str(part) == ".":
                continue
            previous = folded.setdefault(str(part).casefold(), str(part))
            if previous != str(part):
                errors.append(f"Case collision: {previous} / {part}")
        if not (root / name).is_file() or (root / name).is_symlink() or not (root / name).resolve().is_relative_to(root.resolve()):
            errors.append(f"Missing or unsafe tracked file: {name}")
            continue
        if path.parts[0] != "Assets" or any(p.startswith(".") or p.endswith("~") for p in path.parts[1:]):
            continue
        if path.suffix == ".meta":
            try:
                text = read_text(root, name)
            except (OSError, UnicodeError, ValidationError) as exc:
                errors.append(str(exc))
                continue
            matches = re.findall(r"^guid:\s*([0-9a-fA-F]{32})\s*$", text, re.MULTILINE)
            if len(matches) != 1 or matches[0] == "0" * 32:
                errors.append(f"Invalid GUID: {name}")
            else:
                guid = matches[0].lower()
                previous = guids.setdefault(guid, name)
                if previous != name:
                    errors.append(f"Duplicate GUID: {previous} / {name}")
            target = name[:-5]
            is_folder = bool(re.search(r"^folderAsset:\s*yes\s*$", text, re.MULTILINE))
            if not is_folder and target not in paths:
                errors.append(f"Orphan file metadata: {name}")
            if is_folder and target in paths:
                errors.append(f"Folder metadata attached to a file: {name}")
        else:
            assets.add(name)
            if name + ".meta" not in paths:
                errors.append(f"Missing asset metadata: {name}.meta")
        for parent in path.parents:
            if str(parent) in {".", "Assets"}:
                break
            if str(parent) + ".meta" not in paths:
                errors.append(f"Missing folder metadata: {parent}.meta")
    required = ("ProjectSettings/ProjectVersion.txt", "Packages/manifest.json", "Packages/packages-lock.json", "ProjectSettings/EditorBuildSettings.asset")
    for name in required:
        if name not in paths:
            errors.append(f"Required project file is not tracked: {name}")
    try:
        version = read_text(root, required[0])
        if not re.search(rf"^m_EditorVersion: {re.escape(UNITY_VERSION)}\s*$", version, re.MULTILINE):
            errors.append(f"Expected Unity {UNITY_VERSION}")
        manifest = json.loads(read_text(root, required[1]))["dependencies"]
        locked = json.loads(read_text(root, required[2]))["dependencies"]
        for name, expected in PACKAGES.items():
            if manifest.get(name) != expected or locked.get(name, {}).get("version") != expected:
                errors.append(f"Package pin/lock mismatch: {name}, expected {expected}")
    except (OSError, UnicodeError, ValueError, KeyError, TypeError, AttributeError) as exc:
        errors.append(f"Invalid project configuration: {exc}")
    if not assets:
        errors.append("No tracked Unity assets")
    if errors:
        raise ValidationError("\n".join(sorted(set(errors))))
    return {"tracked_files": len(paths), "assets": len(assets), "unique_guids": len(guids)}


def validate_results(path, mode):
    if mode not in EXPECTED_TESTS:
        raise ValidationError(f"Unknown test mode: {mode}")
    path = Path(path)
    if not path.is_file():
        raise ValidationError(f"Unity results missing: {path}. Tests are NOT verified.")
    if path.stat().st_size > 10_000_000:
        raise ValidationError("Unity results exceed the 10 MB safety limit")
    data = path.read_bytes()
    if b"<!DOCTYPE" in data.upper() or b"<!ENTITY" in data.upper():
        raise ValidationError("DTD/entities are not permitted in test reports")
    try:
        root = ET.fromstring(data)
    except ET.ParseError as exc:
        raise ValidationError(f"Malformed NUnit XML: {exc}") from exc
    if root.tag != "test-run" or root.get("result") != "Passed":
        raise ValidationError("NUnit test-run must report Passed")
    try:
        counts = {key: int(root.attrib[key]) for key in ("total", "passed", "failed", "skipped", "inconclusive")}
    except (KeyError, ValueError) as exc:
        raise ValidationError("NUnit counts are missing or invalid") from exc
    cases = list(root.iter("test-case"))
    if not cases or counts["total"] != len(cases) or counts["passed"] != len(cases):
        raise ValidationError("NUnit counts do not match nonempty executed tests")
    if any(counts[key] != 0 for key in ("failed", "skipped", "inconclusive")):
        raise ValidationError("Failed, skipped or inconclusive Unity tests are not accepted")
    if any(node.get("result") != "Passed" for node in [*cases, *root.iter("test-suite")]):
        raise ValidationError("A Unity test or suite did not pass")
    names = [case.get("fullname", "") for case in cases]
    if not all(names) or len(set(names)) != len(names):
        raise ValidationError("Missing or duplicate test names")
    namespace = "ShadowsOfTheForsaken.Tests." + {"editmode": "EditMode", "playmode": "PlayMode"}[mode] + "."
    found = {name[len(namespace):] for name in names if name.startswith(namespace)}
    missing = sorted(EXPECTED_CASES[mode] - found)
    if missing:
        raise ValidationError("Expected baseline tests did not run: " + ", ".join(missing))
    return {"mode": mode, "passed": len(cases), "report": str(path)}


def run_unity(root, editor, mode, output, timeout):
    if mode not in EXPECTED_TESTS or timeout <= 0:
        raise ValidationError("A valid mode and positive timeout are required")
    root, output = Path(root).resolve(), Path(output).resolve()
    editor = Path(editor).expanduser().resolve()
    if not editor.is_file():
        raise ValidationError(f"Unity editor not found: {editor}")
    validate_project(root)
    output.mkdir(parents=True, exist_ok=True)
    results = output / f"{mode}-results.xml"
    results.unlink(missing_ok=True)
    command = [str(editor), "-batchmode", "-projectPath", str(root), "-runTests",
               "-testPlatform", {"editmode": "EditMode", "playmode": "PlayMode"}[mode],
               "-assemblyNames", {"editmode": "Shadows.EditMode.Tests", "playmode": "Shadows.PlayMode.Tests"}[mode],
               "-testResults", str(results), "-logFile", str(output / f"{mode}.log")]
    try:
        process = subprocess.run(command, check=False, timeout=timeout)
    except subprocess.TimeoutExpired as exc:
        raise ValidationError(f"Unity timed out after {timeout}s; tests are NOT verified") from exc
    if process.returncode != 0:
        raise ValidationError(f"Unity exited with code {process.returncode}; see {output / (mode + '.log')}")
    return validate_results(results, mode)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    project = commands.add_parser("project")
    project.add_argument("--root", type=Path, default=ROOT)
    results = commands.add_parser("results")
    results.add_argument("--mode", choices=EXPECTED_TESTS, required=True)
    results.add_argument("path", type=Path)
    run = commands.add_parser("run")
    run.add_argument("--root", type=Path, default=ROOT)
    run.add_argument("--editor", type=Path, required=True)
    run.add_argument("--mode", choices=EXPECTED_TESTS, required=True)
    run.add_argument("--output", type=Path, default=ROOT / "artifacts/unity-local")
    run.add_argument("--timeout", type=int, default=900)
    args = parser.parse_args()
    try:
        if args.command == "project":
            report = validate_project(args.root)
        elif args.command == "results":
            report = validate_results(args.path, args.mode)
        else:
            report = run_unity(args.root, args.editor, args.mode, args.output, args.timeout)
    except (ValidationError, OSError, UnicodeError, subprocess.SubprocessError) as exc:
        print(str(exc), file=sys.stderr)
        return 1
    print(json.dumps(report, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())

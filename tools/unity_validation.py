"""Validate tracked Unity assets and real NUnit evidence; never emulate the engine."""
import argparse
from collections import Counter
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
UNITY_VERSION = "6000.6.3f1"
PACKAGES = {
    "com.unity.inputsystem": "1.20.0",
    "com.unity.render-pipelines.universal": "17.6.0",
    "com.unity.test-framework": "1.8.0",
}
EXPECTED_TESTS = {
    "editmode": {
        "GothicEnvironmentUsesSavedDetailedMeshesWithinItsBudget": 1,
        "GothicStoneSurfacesUseRepeatableMipmappedOriginalAlbedos": 1,
        "GothicEmissiveMaterialsRetainTheirKeywordsAfterReimport": 1,
        "GothicGateAndRelicMeshesRemainBoundToTheirExistingStateComponents": 1,
        "EditorVersionMatchesPin": 1,
        "BaselineSceneIsEnabledInBuildSettings": 1,
        "RuntimeScriptImportsWithOriginalGuid": 2,
        "BaselineSceneHasNoMissingComponents": 1,
        "MaterialsHaveResolvableShaders": 1,
        "CastleIsFirstEnabledSceneAndHasNoMissingComponents": 1,
        "PlayerAndCameraUseExistingScriptsAndExplicitReferences": 1,
        "RoomAndPassageMarkersMatchTheProgressionTopologyWithoutRunningIt": 1,
        "ImportedSceneMarkersMatchTheCommittedLayoutModel": 1,
        "NearestVisibleCandidateInsideConeWins": 1,
        "RangeAndConeBoundariesAreInclusive": 1,
        "FacingThenAuthoredOrderThenIdBreakTiesRegardlessOfEnumeration": 1,
        "MalformedCandidateDistanceCannotBecomeASelection": 3,
        "InvalidSelectionSettingsAreRejected": 5,
        "NoCandidatesMeansNoSelection": 1,
        "OneShotRejectsRepeatAndStaleTokensAfterReset": 1,
        "EmptySessionCannotResetAOneShot": 1,
        "OccupiedOpenGateDefersClosureUntilClear": 1,
        "ApproachingAClosedGateCannotUnlockIt": 1,
        "ReopeningCancelsPendingClosure": 1,
        "DamageClampsHealthAndDeathCanOnlyHappenOnce": 1,
        "InvalidMaximumIsRejected": 3,
        "NonpositiveDamageDoesNotHealOrDamage": 3,
        "ResetRestoresConfiguredHealthAndInvalidatesOldLifeAndSession": 1,
        "ResetWithinSameSessionAlsoInvalidatesDelayedDamage": 1,
        "WindupActiveRecoveryAndCooldownBoundariesAreEnforced": 1,
        "RepeatedAttackRequestsCannotRestartAnAction": 3,
        "CompoundTargetsAreHitOncePerActionAndAgainOnNextAction": 1,
        "LargeStepSamplesAnEntireCrossedWindowExactlyOnce": 1,
        "InterruptCancelsPendingDamageWithoutBypassingRecovery": 3,
        "ResetDiscardsAnOldAttackAndCapturedTokens": 1,
        "OneHitAndFullRecoveryAtRepresentativeFrameRates": 3,
        "InvalidSimulationTimeIsRejected": 3,
        "InvalidAttackTimingIsRejected": 5,
        "TargetIdentityUsesEqualityInsteadOfHashCodes": 1,
        "GameplayAnchorsAndSpawnMatchThePlayableScene": 1,
        "PlayableSceneWiresOneSessionThreeEncountersAndFiveMechanisms": 1,
        "EncounterRequiresLegitimateRoomAndOriginalIdentityForActivation": 1,
        "EncounterRejectsDeathBeforeActivationAndForeignLife": 1,
        "EncounterQueuesDeathUntilReturningToItsRoomAndCreditsOnce": 1,
        "EncounterResetDiscardsPendingCreditAndRejectsOldCallbacks": 1,
        "EncounterCannotTransferOldDeathToSameSessionRespawn": 1,
        "EncounterLifeAndSessionGatesPrecedePursuit": 3,
        "EncounterPathFailureCannotFallBackToWalkingThroughObstacles": 4,
        "EncounterLosesTargetSafelyWithoutLeavingArena": 3
    },
    "playmode": {
        "TimedOutRealSceneLoadIsDrainedWithoutUnloadingPreexistingScenes": 1,
        "ActorPresentationReadsRealAttackDeathAndResetWithoutChangingColliders": 1,
        "ActorPresentationDisableRestoresBindPoseAndRejectsActorRootReference": 1,
        "PerformanceHistogramRetainsOverflowFramesAndConservativePercentile": 1,
        "PerformanceExclusionsPreserveWarmupAndEmptyMetricsCannotPassBudget": 1,
        "PerformanceObserverCreatesNoOutputWithoutOptIn": 1,
        "PerformanceObserverMeasuresActualFramesAndExcludesFocusPauseAndTimeScale": 1,
        "PerformanceLifecycleKeepsOriginalTokenAndResetsCountersAfterDeath": 1,
        "PerformanceCompletionWritesBoundedHistogramAndConfiguration": 1,
        "PerformanceSummaryCapLeavesRollingStatusOperational": 1,

        "BaselineSceneRunsWithoutMissingComponents": 1,
        "BaselineCameraAndDirectionalLightAreActive": 1,
        "EveryPassageIsWalkableInBothDirections": 18,
        "UnityJumpAtCourtyardLandsInsideBounds": 1,
        "PuzzleNorthWallRejectsWalkingAndJumpingThroughTheMapBoundary": 1,
        "InteractionRejectsOutsideRangeBehindActorAndBehindWall": 1,
        "InteractionRevalidatesMovedWallAndTargetAtPressTime": 1,
        "InteractionRejectsRayOriginInsideSolidWallButIgnoresOwnBody": 1,
        "InteractionSelectsOneCompoundTargetAndExposesUnavailablePrompt": 1,
        "InteractionEqualTargetsUseAuthoredOrderIndependentlyOfColliderEnumeration": 2,
        "InteractionHeldRepeatedAndReenabledInputPublishesOneCapturedSession": 1,
        "InteractionResetRejectsOldTokenAndAllowsExactlyOneNewUse": 2,
        "InteractionDestroyedDisabledOrMissingTargetClearsSelection": 1,
        "InteractionDisabledPlayerCannotUseAVisibleTarget": 1,
        "InteractionProgressionObjectiveMustBeAvailableAndCompletesOnce": 1,
        "InteractionGateClosedBlocksAndOpenAllowsRealCharacterMovement": 1,
        "InteractionGateResetWaitsForTeleportedOccupantAndClosesAfterEscape": 1,
        "InteractionGateApproachCannotOpenLockedPassageAndReenablePreservesState": 1,
        "CombatPhysicsWindupActiveWindowAndCooldownUseRealTarget": 1,
        "CombatPhysicsRejectsTargetsOutsideRangeOrForwardArc": 3,
        "CombatPhysicsWallBlocksDamageEvenWhenNotInTargetMask": 1,
        "CombatPhysicsEmbeddedWallUsesTheActorsLocalPhysicsScene": 1,
        "CombatPhysicsUnrelatedDefaultSceneWallDoesNotBlockLocalScene": 1,
        "CombatPhysicsCompoundColliderAndRepeatedFramesHitOnceWithoutSelfDamage": 1,
        "CombatPhysicsLateTargetCannotBeHitAfterActiveWindow": 1,
        "CombatPhysicsLargeFrameCrossingWindowStillHitsOnce": 1,
        "CombatPhysicsOverlappingLargeTargetUsesSurfaceDistance": 1,
        "CombatPhysicsDeathOnceAndDeadAttackerCannotAttack": 1,
        "CombatPhysicsResetInvalidatesPendingSwingAndOldDamageTokens": 1,
        "CombatPhysicsSessionResetCancelsSwingAndPreservesOriginalDeathToken": 1,
        "CombatPhysicsInterruptionsCannotReplayPendingSwing": 4,
        "CombatPhysicsPlayerInputHoldAndReenableDoNotDuplicateAttacks": 1,
        "CombatPhysicsPlayerControlLossCancelsPendingSwing": 3,
        "CombatPhysicsPlayerBridgeReconcilesDeathAndResetWhileDisabled": 1,
        "CombatPhysicsLethalChangedObserverResetPreservesOriginalDeathNotification": 1,
        "InteractionDemoSavedSceneOpensGateThroughInputAndRestarts": 1,
        "CombatDemoSavedSceneAttacksThroughInputAndRestarts": 1,
        "EncounterDormantActorRejectsDamageAndRoomReentryPreservesLife": 1,
        "EncounterDeferredAgentWaitsForNavigationDataWithoutMovingTheActor": 1,
        "EncounterRetreatingLastSwingCreditsEachFightExactlyOnceOnReturn": 3,
        "EncounterResetRestoresBodyAndRejectsOldDeathPayload": 1,
        "EncounterDisabledAcrossResetRestoresNewSessionWithoutOldCredit": 1,
        "EncounterWallPreventsDetectionAndMeleeThroughGate": 1,
        "EncounterLostOrSuspendedTargetInterruptsTelegraph": 5,
        "EncounterRealNavMeshChasesButCannotCrossAnUncarvedPhysicalGate": 1,
        "EncounterWithdrawnPlayerDoesNotPullEnemyOutOfArena": 1,
        "EncounterUnreachableTargetCannotBecomeStraightLineMovement": 1,
        "EncounterNavigatesAroundBakedObstacleWithoutEnteringIt": 1,
        "EncounterGateCarvingFollowsPhysicalClosureAndRecognizesKinematicEnemyOccupants": 1,
        "SessionRunningRejectsRestartAndPreservesDormantDamageLock": 1,
        "SessionEvidenceRecoversFromSnapshotFileContentionAndStopsAfterPersistentFailure": 1,
        "SessionDeathStopsMovementDamageAttacksAndRejectsExit": 1,
        "SessionExitRequiresEveryObjectiveAndLivingPlayer": 2,
        "SessionRestartRequiresFreshTerminalRAndClearsHeldGameplayInput": 1,
        "SessionRestartRequiresReleaseAfterFocusOrPause": 2,
        "SessionRepeatedRestartsCreateExactlyOneNewSessionEach": 1,
        "SessionRoomVolumeRejectsLowerPassageAndTracksRealMovement": 1,
        "SessionResetRelocatesPlayerBeforeClosingOccupiedGate": 1,
        "SessionExternalResetRestoresWorldAfterNotification": 1,
        "SessionStaleDeathNotificationCannotDefeatRestoredWorld": 1,
        "SessionTimerExcludesFocusLossAndApplicationPause": 1,
        "SessionRestartAfterVictoryClearsMandatoryAndOptionalProgress": 1,
        "SessionHealthRestorationCannotOverrideTerminalLocks": 1,
        "SessionHUDShowsHealthSelectionAndRelicCompletion": 1,
        "SessionHUDRestartButtonOnlyRestartsTerminalSession": 1,
        "CloseBodyCameraHysteresisKeepsPhysicsAndRestoresDistantView": 1,
        "CloseBodyCameraDisableRestoresThenReenableHidesAgain": 1,
        "CloseBodyCameraPreservesPreexistingFlagsAndIgnoresForeignVisuals": 1,
        "CloseBodyCameraTargetLossRestoresOriginalVisuals": 2,
        "RuneFeedbackMovesOnceAndRestoresOnSessionReset": 1,
        "RelicFeedbackHidesOnceAndReconcilesReenableAndNewSession": 1,
        "DamagedMechanismNeverCompletesPuzzleAndCanBeExaminedAfterReset": 1,
        "CastleLockedMainGatesRejectWalkingAndJumping": 1,
        "CastleFirstFightDeathRestartsTheSavedWorld": 1,
        "CastleThroneFightDeathRestartsTheSavedWorld": 1,
        "CastleFinalFightDeathRestartsTheSavedWorld": 1,
        "CastleMainRouteCompletesThroughControlsWithoutSecret": 1,
        "CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight": 1,
        "ShoulderFramingRevealsForwardEnemyPastThePlayerCollider": 1,
        "LockedGateTurnDuringJumpRetainsAValidatedPreviousCameraPose": 1,
        "ShoulderFramingRetainsNearPlaneClearanceBesideWallAndDuringTurn": 1
    }
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
    found = Counter(name.split("(", 1)[0].rsplit(".", 1)[-1] for name in names if name.startswith(namespace))
    missing = [name for name, count in EXPECTED_TESTS[mode].items() if found[name] < count]
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

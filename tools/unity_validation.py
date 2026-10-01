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
# Require every known fixture/method and its parameterized case count. Keep this
# catalog aligned with the complete Unity suites; a passing subset is not evidence.
# Fixture-qualified keys prevent a different class from satisfying a named check.
EXPECTED_TESTS = {
    "editmode": {
        "CameraMotionTests.ExtremeFiniteEndpointsDoNotOverflow": 1,
        "CameraMotionTests.HitDistanceAndPaddingBoundCameraTravel": 4,
        "CameraMotionTests.IrregularTimeStepsComposeForAStationaryTarget": 1,
        "CameraMotionTests.LargeTimeStepDoesNotOvershoot": 1,
        "CameraMotionTests.MissingHitDoesNotShortenRequestedDistance": 1,
        "CameraMotionTests.ObstructionSafetyIsNotDelayedByDamping": 3,
        "CameraMotionTests.RejectsInvalidHitDistance": 3,
        "CameraMotionTests.RejectsInvalidRequestedDistanceAndPadding": 4,
        "CameraMotionTests.RejectsInvalidTimeAndResponse": 4,
        "CameraMotionTests.RejectsNonfinitePositions": 3,
        "CameraMotionTests.ReturningFromObstructionIsSmoothAndFrameRateIndependent": 3,
        "CameraMotionTests.StationaryTargetDampingIsFrameRateIndependent": 3,
        "CameraMotionTests.ZeroTimeOrResponseDoesNotMove": 1,
        "CastleLayoutSceneTests.CastleIsFirstEnabledSceneAndHasNoMissingComponents": 1,
        "CastleLayoutSceneTests.GameplayAnchorsAndSpawnMatchThePlayableScene": 1,
        "CastleLayoutSceneTests.ImportedSceneMarkersMatchTheCommittedLayoutModel": 1,
        "CastleLayoutSceneTests.PlayableSceneWiresOneSessionThreeEncountersAndFiveMechanisms": 1,
        "CastleLayoutSceneTests.PlayerAndCameraUseExistingScriptsAndExplicitReferences": 1,
        "CastleLayoutSceneTests.RoomAndPassageMarkersMatchTheProgressionTopologyWithoutRunningIt": 1,
        "CastlePresentationSceneTests.GothicEmissiveMaterialsRetainTheirKeywordsAfterReimport": 1,
        "CastlePresentationSceneTests.GothicEnvironmentUsesSavedDetailedMeshesWithinItsBudget": 1,
        "CastlePresentationSceneTests.GothicGateAndRelicMeshesRemainBoundToTheirExistingStateComponents": 1,
        "CastlePresentationSceneTests.GothicStoneSurfacesUseRepeatableMipmappedOriginalAlbedos": 1,
        "CombatStateTests.CompoundTargetsAreHitOncePerActionAndAgainOnNextAction": 1,
        "CombatStateTests.DamageClampsHealthAndDeathCanOnlyHappenOnce": 1,
        "CombatStateTests.InterruptCancelsPendingDamageWithoutBypassingRecovery": 3,
        "CombatStateTests.InvalidAttackTimingIsRejected": 5,
        "CombatStateTests.InvalidMaximumIsRejected": 3,
        "CombatStateTests.InvalidSimulationTimeIsRejected": 3,
        "CombatStateTests.LargeStepSamplesAnEntireCrossedWindowExactlyOnce": 1,
        "CombatStateTests.NonpositiveDamageDoesNotHealOrDamage": 3,
        "CombatStateTests.OneHitAndFullRecoveryAtRepresentativeFrameRates": 3,
        "CombatStateTests.RepeatedAttackRequestsCannotRestartAnAction": 3,
        "CombatStateTests.ResetDiscardsAnOldAttackAndCapturedTokens": 1,
        "CombatStateTests.ResetRestoresConfiguredHealthAndInvalidatesOldLifeAndSession": 1,
        "CombatStateTests.ResetWithinSameSessionAlsoInvalidatesDelayedDamage": 1,
        "CombatStateTests.TargetIdentityUsesEqualityInsteadOfHashCodes": 1,
        "CombatStateTests.WindupActiveRecoveryAndCooldownBoundariesAreEnforced": 1,
        "EncounterStateTests.EncounterCannotTransferOldDeathToSameSessionRespawn": 1,
        "EncounterStateTests.EncounterLifeAndSessionGatesPrecedePursuit": 3,
        "EncounterStateTests.EncounterLosesTargetSafelyWithoutLeavingArena": 3,
        "EncounterStateTests.EncounterPathFailureCannotFallBackToWalkingThroughObstacles": 4,
        "EncounterStateTests.EncounterQueuesDeathUntilReturningToItsRoomAndCreditsOnce": 1,
        "EncounterStateTests.EncounterRejectsDeathBeforeActivationAndForeignLife": 1,
        "EncounterStateTests.EncounterRequiresLegitimateRoomAndOriginalIdentityForActivation": 1,
        "EncounterStateTests.EncounterResetDiscardsPendingCreditAndRejectsOldCallbacks": 1,
        "InteractionRulesTests.ApproachingAClosedGateCannotUnlockIt": 1,
        "InteractionRulesTests.EmptySessionCannotResetAOneShot": 1,
        "InteractionRulesTests.FacingThenAuthoredOrderThenIdBreakTiesRegardlessOfEnumeration": 1,
        "InteractionRulesTests.InvalidSelectionSettingsAreRejected": 5,
        "InteractionRulesTests.MalformedCandidateDistanceCannotBecomeASelection": 3,
        "InteractionRulesTests.NearestVisibleCandidateInsideConeWins": 1,
        "InteractionRulesTests.NoCandidatesMeansNoSelection": 1,
        "InteractionRulesTests.OccupiedOpenGateDefersClosureUntilClear": 1,
        "InteractionRulesTests.OneShotRejectsRepeatAndStaleTokensAfterReset": 1,
        "InteractionRulesTests.RangeAndConeBoundariesAreInclusive": 1,
        "InteractionRulesTests.ReopeningCancelsPendingClosure": 1,
        "LevelProgressionTests.AllPassagesRemainSymmetricAndUnconnectedRoomsStayBlocked": 1,
        "LevelProgressionTests.BonusRouteReturnsThroughThroneWithoutBypassingLibraryOrFinal": 1,
        "LevelProgressionTests.BonusStaysClosedUntilLeverIsUsed": 1,
        "LevelProgressionTests.CompletionIsTerminalAndEmittedOnlyOnce": 1,
        "LevelProgressionTests.DifferentInstancesDoNotShareStateListenersOrSessionTokens": 1,
        "LevelProgressionTests.DuplicateObjectiveChangesNothingAndDoesNotNotifyAgain": 1,
        "LevelProgressionTests.FinalEnemyMustBeDefeatedBeforeExit": 1,
        "LevelProgressionTests.InvalidOrCompositeObjectiveIsRejected": 5,
        "LevelProgressionTests.InvalidRoomIsRejected": 3,
        "LevelProgressionTests.LateSignalsFromPreviousSessionAreRejectedEvenInMatchingRoom": 1,
        "LevelProgressionTests.ListenerFailureDoesNotHideChangeFromOtherListenersOrLeaveGuardLocked": 1,
        "LevelProgressionTests.MainRouteCompletesWithoutOptionalObjectives": 1,
        "LevelProgressionTests.NotificationsContainCommittedImmutableSnapshots": 1,
        "LevelProgressionTests.PuzzleRequiresReturningToTheJunction": 1,
        "LevelProgressionTests.RejectsReentrantMutationsButAllowsQueries": 1,
        "LevelProgressionTests.ResetClearsStateAndReclosesGates": 2,
        "LevelProgressionTests.StartsInCourtyardWithEmptySession": 1,
        "LevelProgressionTests.UnsubscribedListenerDoesNotReceiveLaterChanges": 1,
        "LevelProgressionTests.WrongRoomAndWrongOrderAreRejectedWithoutNotifications": 1,
        "PlayerMotorTests.ACompleteTapBetweenFramesIsNotLost": 1,
        "PlayerMotorTests.AirborneJumpCannotResetVerticalVelocity": 1,
        "PlayerMotorTests.BallisticMotionIsFrameRateIndependent": 3,
        "PlayerMotorTests.ButtonsFireOnceUntilRelease": 3,
        "PlayerMotorTests.CeilingImmediatelyCancelsAscent": 1,
        "PlayerMotorTests.FallingSpeedIsBoundedAndResetClearsIt": 1,
        "PlayerMotorTests.ForwardAndBackwardMovementUseIndependentAxis": 3,
        "PlayerMotorTests.GateInstancesAndResetsDoNotShareState": 1,
        "PlayerMotorTests.GroundedJumpStartsWithExistingJumpForceAndAppliesGravityImmediately": 1,
        "PlayerMotorTests.HeldControlsCannotResumeBeforeNeutral": 1,
        "PlayerMotorTests.InputSanitizesAxesWithoutCouplingTurnAndForward": 1,
        "PlayerMotorTests.InvalidSettingsAreRejected": 4,
        "PlayerMotorTests.InvalidTimeIsRejectedWithoutMutation": 4,
        "PlayerMotorTests.LandingResetsFallButCannotCancelAscent": 1,
        "PlayerMotorTests.MovingWhileTurningFollowsTheSameArcAtEveryFrameRate": 3,
        "PlayerMotorTests.NoInputProducesNoHorizontalMovement": 1,
        "PlayerMotorTests.RotationDoesNotStrafeWithoutForwardInput": 1,
        "PlayerMotorTests.StaleGroundedFlagDoesNotRestartAscendingJump": 1,
        "PlayerMotorTests.TerminalVelocityIntegrationIsFrameRateIndependent": 3,
        "PlayerMotorTests.ZeroTimeDoesNotConsumeJumpOrChangeVelocity": 1,
        "ProjectBaselineTests.BaselineSceneHasNoMissingComponents": 1,
        "ProjectBaselineTests.BaselineSceneIsEnabledInBuildSettings": 1,
        "ProjectBaselineTests.EditorVersionMatchesPin": 1,
        "ProjectBaselineTests.MaterialsHaveResolvableShaders": 1,
        "ProjectBaselineTests.RuntimeScriptImportsWithOriginalGuid": 2,
    },
    "playmode": {
        "BaselineSceneTests.BaselineCameraAndDirectionalLightAreActive": 1,
        "BaselineSceneTests.BaselineSceneRunsWithoutMissingComponents": 1,
        "CameraFollowTests.AcquiresOneTaggedPlayerButDoesNotChooseBetweenTwo": 1,
        "CameraFollowTests.ChildTargetIgnoresItsCharacterAndCompoundColliders": 1,
        "CameraFollowTests.DestroyedTargetCanBeReplacedWithoutOldSmoothingState": 1,
        "CameraFollowTests.EmbeddedPivotFailsSafelyAndRestoresRenderingAfterRecovery": 1,
        "CameraFollowTests.LockedGateTurnDuringJumpRetainsAValidatedPreviousCameraPose": 2,
        "CameraFollowTests.LookHeightOffsetChangesOnlyAimAndRetainsTheCollisionPivot": 7,
        "CameraFollowTests.LowCeilingAndCornerLeaveCollisionVolumeClear": 1,
        "CameraFollowTests.MissingTargetWarnsOnceAndCanRecover": 1,
        "CameraFollowTests.MovingTargetAtHeldCameraAimUsesSafeBoomWithoutInvalidRotation": 1,
        "CameraFollowTests.OriginalSerializedFieldsAndDefaultsRemainAvailable": 1,
        "CameraFollowTests.OriginallyDisabledCameraIsNotEnabledByRecovery": 1,
        "CameraFollowTests.PartiallyOverlappingPivotDoesNotPermitCrossingThinWall": 1,
        "CameraFollowTests.RapidTurnDoesNotPlaceCameraInsideCorner": 1,
        "CameraFollowTests.ReenabledComponentStartsWithFreshTracking": 1,
        "CameraFollowTests.RejectsSelfAsTargetWithoutInvalidLookRotation": 1,
        "CameraFollowTests.SaturatedQueryBufferDoesNotLoseTheNearestWall": 1,
        "CameraFollowTests.ShoulderAimFractionChangesOnlyAimAndRetainsTheFullCameraBoom": 8,
        "CameraFollowTests.ShoulderFramingRetainsNearPlaneClearanceBesideWallAndDuringTurn": 2,
        "CameraFollowTests.ShoulderFramingRevealsForwardEnemyPastThePlayerCollider": 1,
        "CameraFollowTests.StartingInsideWallIsResolvedBeforeRendering": 1,
        "CameraFollowTests.TeleportAndExplicitSnapDoNotFlyThroughOldSceneSpace": 1,
        "CameraFollowTests.TriggersAndExcludedLayersDoNotBlockCamera": 1,
        "CameraFollowTests.UnloadedTargetSceneCanBeReplaced": 1,
        "CameraFollowTests.WallRemovalReturnsSmoothlyAtRepresentativeFrameRates": 3,
        "CameraFollowTests.WallShortensBoomImmediately": 1,
        "CameraFollowTests.WideNearPlaneIsIncludedInCollisionRadius": 1,
        "CameraPlayerOcclusionTests.CloseBodyCameraDisableRestoresThenReenableHidesAgain": 1,
        "CameraPlayerOcclusionTests.CloseBodyCameraHysteresisKeepsPhysicsAndRestoresDistantView": 1,
        "CameraPlayerOcclusionTests.CloseBodyCameraPreservesPreexistingFlagsAndIgnoresForeignVisuals": 1,
        "CameraPlayerOcclusionTests.CloseBodyCameraTargetLossRestoresOriginalVisuals": 2,
        "CastleActorPresentationTests.ActorPresentationDisableRestoresBindPoseAndRejectsActorRootReference": 1,
        "CastleActorPresentationTests.ActorPresentationReadsRealAttackDeathAndResetWithoutChangingColliders": 1,
        "CastleLayoutTraversalTests.EveryPassageIsWalkableInBothDirections": 18,
        "CastleLayoutTraversalTests.PuzzleNorthWallRejectsWalkingAndJumpingThroughTheMapBoundary": 1,
        "CastleLayoutTraversalTests.UnityJumpAtCourtyardLandsInsideBounds": 1,
        "CombatPhysicsTests.CombatPhysicsCompoundColliderAndRepeatedFramesHitOnceWithoutSelfDamage": 1,
        "CombatPhysicsTests.CombatPhysicsDeathOnceAndDeadAttackerCannotAttack": 1,
        "CombatPhysicsTests.CombatPhysicsEmbeddedWallUsesTheActorsLocalPhysicsScene": 1,
        "CombatPhysicsTests.CombatPhysicsInterruptionsCannotReplayPendingSwing": 4,
        "CombatPhysicsTests.CombatPhysicsLargeFrameCrossingWindowStillHitsOnce": 1,
        "CombatPhysicsTests.CombatPhysicsLateTargetCannotBeHitAfterActiveWindow": 1,
        "CombatPhysicsTests.CombatPhysicsLethalChangedObserverResetPreservesOriginalDeathNotification": 1,
        "CombatPhysicsTests.CombatPhysicsOverlappingLargeTargetUsesSurfaceDistance": 1,
        "CombatPhysicsTests.CombatPhysicsPlayerBridgeReconcilesDeathAndResetWhileDisabled": 1,
        "CombatPhysicsTests.CombatPhysicsPlayerControlLossCancelsPendingSwing": 3,
        "CombatPhysicsTests.CombatPhysicsPlayerInputHoldAndReenableDoNotDuplicateAttacks": 1,
        "CombatPhysicsTests.CombatPhysicsRejectsTargetsOutsideRangeOrForwardArc": 3,
        "CombatPhysicsTests.CombatPhysicsResetInvalidatesPendingSwingAndOldDamageTokens": 1,
        "CombatPhysicsTests.CombatPhysicsSessionResetCancelsSwingAndPreservesOriginalDeathToken": 1,
        "CombatPhysicsTests.CombatPhysicsUnrelatedDefaultSceneWallDoesNotBlockLocalScene": 1,
        "CombatPhysicsTests.CombatPhysicsWallBlocksDamageEvenWhenNotInTargetMask": 1,
        "CombatPhysicsTests.CombatPhysicsWindupActiveWindowAndCooldownUseRealTarget": 1,
        "EnemyEncounterTests.EncounterDeferredAgentWaitsForNavigationDataWithoutMovingTheActor": 1,
        "EnemyEncounterTests.EncounterDisabledAcrossResetRestoresNewSessionWithoutOldCredit": 1,
        "EnemyEncounterTests.EncounterDormantActorRejectsDamageAndRoomReentryPreservesLife": 1,
        "EnemyEncounterTests.EncounterGateCarvingFollowsPhysicalClosureAndRecognizesKinematicEnemyOccupants": 1,
        "EnemyEncounterTests.EncounterLostOrSuspendedTargetInterruptsTelegraph": 5,
        "EnemyEncounterTests.EncounterNavigatesAroundBakedObstacleWithoutEnteringIt": 1,
        "EnemyEncounterTests.EncounterRealNavMeshChasesButCannotCrossAnUncarvedPhysicalGate": 1,
        "EnemyEncounterTests.EncounterResetRestoresBodyAndRejectsOldDeathPayload": 1,
        "EnemyEncounterTests.EncounterRetreatingLastSwingCreditsEachFightExactlyOnceOnReturn": 3,
        "EnemyEncounterTests.EncounterUnreachableTargetCannotBecomeStraightLineMovement": 1,
        "EnemyEncounterTests.EncounterWallPreventsDetectionAndMeleeThroughGate": 1,
        "EnemyEncounterTests.EncounterWithdrawnPlayerDoesNotPullEnemyOutOfArena": 1,
        "FullCastleRouteTests.CastleFinalFightDeathRestartsTheSavedWorld": 1,
        "FullCastleRouteTests.CastleFirstFightDeathRestartsTheSavedWorld": 1,
        "FullCastleRouteTests.CastleLockedMainGatesRejectWalkingAndJumping": 1,
        "FullCastleRouteTests.CastleMainRouteCompletesThroughControlsWithoutSecret": 1,
        "FullCastleRouteTests.CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight": 1,
        "FullCastleRouteTests.CastleThroneFightDeathRestartsTheSavedWorld": 1,
        "FullCastleRouteTests.FramingRayMissesRemainInfiniteInsideAndOutsideMeshBounds": 1,
        "FullCastleRouteTests.FramingRayRejectsBackfacesAndHitsAtTheExclusiveDistanceLimit": 1,
        "FullCastleRouteTests.FramingRaySelectsNearestSurfaceIncludingUnlabelledPlayer": 2,
        "GameplayDemoTests.CombatDemoSavedSceneAttacksThroughInputAndRestarts": 1,
        "GameplayDemoTests.InteractionDemoSavedSceneOpensGateThroughInputAndRestarts": 1,
        "InteractionComponentTests.InteractionDestroyedDisabledOrMissingTargetClearsSelection": 1,
        "InteractionComponentTests.InteractionDisabledPlayerCannotUseAVisibleTarget": 1,
        "InteractionComponentTests.InteractionEqualTargetsUseAuthoredOrderIndependentlyOfColliderEnumeration": 2,
        "InteractionComponentTests.InteractionGateApproachCannotOpenLockedPassageAndReenablePreservesState": 1,
        "InteractionComponentTests.InteractionGateClosedBlocksAndOpenAllowsRealCharacterMovement": 1,
        "InteractionComponentTests.InteractionGateResetWaitsForTeleportedOccupantAndClosesAfterEscape": 1,
        "InteractionComponentTests.InteractionHeldRepeatedAndReenabledInputPublishesOneCapturedSession": 1,
        "InteractionComponentTests.InteractionProgressionObjectiveMustBeAvailableAndCompletesOnce": 1,
        "InteractionComponentTests.InteractionRejectsOutsideRangeBehindActorAndBehindWall": 1,
        "InteractionComponentTests.InteractionRejectsRayOriginInsideSolidWallButIgnoresOwnBody": 1,
        "InteractionComponentTests.InteractionResetRejectsOldTokenAndAllowsExactlyOneNewUse": 2,
        "InteractionComponentTests.InteractionRevalidatesMovedWallAndTargetAtPressTime": 1,
        "InteractionComponentTests.InteractionSelectsOneCompoundTargetAndExposesUnavailablePrompt": 1,
        "LevelSessionTests.SessionDeathStopsMovementDamageAttacksAndRejectsExit": 1,
        "LevelSessionTests.SessionEvidenceRecoversFromSnapshotFileContentionAndStopsAfterPersistentFailure": 1,
        "LevelSessionTests.SessionExitRequiresEveryObjectiveAndLivingPlayer": 2,
        "LevelSessionTests.SessionExternalResetRestoresWorldAfterNotification": 1,
        "LevelSessionTests.SessionHUDRestartButtonOnlyRestartsTerminalSession": 1,
        "LevelSessionTests.SessionHUDShowsHealthSelectionAndRelicCompletion": 1,
        "LevelSessionTests.SessionHealthRestorationCannotOverrideTerminalLocks": 1,
        "LevelSessionTests.SessionRepeatedRestartsCreateExactlyOneNewSessionEach": 1,
        "LevelSessionTests.SessionResetRelocatesPlayerBeforeClosingOccupiedGate": 1,
        "LevelSessionTests.SessionRestartAfterVictoryClearsMandatoryAndOptionalProgress": 1,
        "LevelSessionTests.SessionRestartRequiresFreshTerminalRAndClearsHeldGameplayInput": 1,
        "LevelSessionTests.SessionRestartRequiresReleaseAfterFocusOrPause": 2,
        "LevelSessionTests.SessionRoomVolumeRejectsLowerPassageAndTracksRealMovement": 1,
        "LevelSessionTests.SessionRunningRejectsRestartAndPreservesDormantDamageLock": 1,
        "LevelSessionTests.SessionStaleDeathNotificationCannotDefeatRestoredWorld": 1,
        "LevelSessionTests.SessionTimerExcludesFocusLossAndApplicationPause": 1,
        "MechanismFeedbackTests.DamagedMechanismNeverCompletesPuzzleAndCanBeExaminedAfterReset": 1,
        "MechanismFeedbackTests.RelicFeedbackHidesOnceAndReconcilesReenableAndNewSession": 1,
        "MechanismFeedbackTests.RuneFeedbackMovesOnceAndRestoresOnSessionReset": 1,
        "OwnedSceneLoadTests.TimedOutRealSceneLoadIsDrainedWithoutUnloadingPreexistingScenes": 1,
        "PlayerMovementTests.AAndDOnlyRotateAndDoNotStrafe": 1,
        "PlayerMovementTests.CharacterCanClimbConfiguredSteps": 1,
        "PlayerMovementTests.DisabledCharacterControllerIsNotMoved": 1,
        "PlayerMovementTests.DisabledControlsFlushQueuedJumpAndCannotFireActions": 1,
        "PlayerMovementTests.DisablingComponentDoesNotDisableOrRewriteSourceActions": 1,
        "PlayerMovementTests.FocusLossDropsMovementAndRequiresReleasedControls": 1,
        "PlayerMovementTests.GroundMovementAtRepresentativeFrameRates": 3,
        "PlayerMovementTests.HeldJumpDoesNotJumpAgainAfterLanding": 1,
        "PlayerMovementTests.InteractFiresOnPressDespiteTemplateHoldInteraction": 1,
        "PlayerMovementTests.LowCeilingCancelsJumpAndPlayerLands": 1,
        "PlayerMovementTests.MissingRequiredActionDisablesComponentWithOneError": 1,
        "PlayerMovementTests.NarrowPassageRemainsTraversable": 1,
        "PlayerMovementTests.PausingClearsQueuedInputUntilControlsReturnToNeutral": 1,
        "PlayerMovementTests.PressAndReleaseWithinOneInputUpdateIsNotLost": 1,
        "PlayerMovementTests.WallStopsForwardMovement": 1,
        "PlayerValidationPerformanceTests.PerformanceCompletionWritesBoundedHistogramAndConfiguration": 1,
        "PlayerValidationPerformanceTests.PerformanceExclusionsPreserveWarmupAndEmptyMetricsCannotPassBudget": 1,
        "PlayerValidationPerformanceTests.PerformanceHistogramRetainsOverflowFramesAndConservativePercentile": 1,
        "PlayerValidationPerformanceTests.PerformanceLifecycleKeepsOriginalTokenAndResetsCountersAfterDeath": 1,
        "PlayerValidationPerformanceTests.PerformanceObserverCreatesNoOutputWithoutOptIn": 1,
        "PlayerValidationPerformanceTests.PerformanceObserverMeasuresActualFramesAndExcludesFocusPauseAndTimeScale": 1,
        "PlayerValidationPerformanceTests.PerformanceSummaryCapLeavesRollingStatusOperational": 1,
        "ProgressionControllerTests.DestroyedControllerRejectsCommandsAndNewInstanceStartsClean": 1,
        "ProgressionControllerTests.DisableAndEnableDoNotDuplicateSubscriptionsOrResetProgress": 1,
        "ProgressionControllerTests.ForwardsCommittedChangesAndSuppressesDuplicateObjectives": 1,
        "ProgressionControllerTests.InactiveGameObjectRejectsCommands": 1,
        "ProgressionControllerTests.ResetNotifiesOnceAndRejectsPreviousRunCallbacks": 1,
        "ProgressionControllerTests.SceneUnloadDoesNotLeakProgressOrNotificationsIntoNextScene": 1,
        "ProgressionControllerTests.StartsInNewSceneScopedSession": 1,
    },
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
    found = Counter(name[len(namespace):].split("(", 1)[0] for name in names if name.startswith(namespace))
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

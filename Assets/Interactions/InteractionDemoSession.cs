using ShadowsOfTheForsaken.Progression;
using UnityEngine;

// Explicit component for the separate mechanic fixture only; never add to the level.
public sealed class InteractionDemoSession : MonoBehaviour
{
    public LevelProgressionController progression;
    public PlayerMovement player;
    public PlayerInteractor interactor;
    public CameraFollow follow;
    public ProgressionGate gate;
    public Transform spawn;

    private void Start() => RestartDemo();

    public void RestartDemo()
    {
        if (progression == null || player == null || spawn == null) return;
        player.SetControlsEnabled(false);
        var character = player.GetComponent<CharacterController>();
        character.enabled = false;
        player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        character.enabled = true;
        player.ResetMotion();
        progression.TryResetSession();
        var session = progression.Snapshot.SessionId;
        // This published, labelled test fixture starts at the puzzle prerequisite.
        // It does not claim that combat, the castle or the puzzle itself is implemented.
        progression.TryEnter(LevelRoom.FirstEncounter, session);
        progression.TryComplete(LevelObjective.FirstEnemyDefeated, session);
        progression.TryEnter(LevelRoom.Puzzle, session);
        if (follow != null) follow.SnapToTarget();
        player.SetControlsEnabled(true);
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(12, 12, 660, 132), "Interaction test fixture — not the castle level\n" +
            "W/S move, A/D turn, E use. Gold lever opens the gate; red rune is behind a wall.\n" +
            "Fixture starts in the puzzle with the first encounter prerequisite pre-completed.\n" +
            (interactor != null && interactor.SelectedTarget != null
                ? interactor.Prompt + (interactor.CanInteract ? " [E]" : " [unavailable / already used]") : "No visible mechanism in range") +
            "\nGate: " + (gate == null ? "unassigned" : gate.ClosePending ? "open — waiting for doorway to clear" : gate.IsOpen ? "open" : "closed"));
        if (GUI.Button(new Rect(12, 152, 160, 32), "Restart demo")) RestartDemo();
    }
}

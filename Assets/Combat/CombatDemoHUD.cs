using ShadowsOfTheForsaken.Progression;
using UnityEngine;

namespace ShadowsOfTheForsaken.Combat
{
    // A small manual test fixture, not the final level HUD or session manager.
    public sealed class CombatDemoHUD : MonoBehaviour
    {
        public CombatHealth player;
        public CombatHealth target;
        public LevelProgressionController progression;
        public Vector3 spawn = new Vector3(0, .05f, -1.5f);

        private void OnGUI()
        {
            if (player == null || target == null) return;
            GUI.Box(new Rect(12, 12, 610, 160), "Combat arena");
            GUI.Label(new Rect(24, 40, 580, 24), "W/S: move   A/D: turn   Space: jump   Left mouse: strike");
            GUI.Label(new Rect(24, 66, 580, 24),
                $"Player {player.Current}/{player.Maximum} | Target {target.Current}/{target.Maximum} | " +
                $"Attack: {player.GetComponent<MeleeCombat>().Phase}");
            GUI.Label(new Rect(24, 91, 580, 24), "Orange: preparation   Bright red: swing/hit   Grey: defeated");
            if (GUI.Button(new Rect(24, 123, 160, 32), "Target strikes"))
            {
                // The GUI click also reaches the existing LPM binding. Cancel
                // that player's preparation before requesting a target attack.
                player.GetComponent<MeleeCombat>().Interrupt();
                target.GetComponent<MeleeCombat>().TryAttack();
            }
            if (GUI.Button(new Rect(200, 123, 160, 32), "Reset arena")) ResetArena();
        }

        public void ResetArena()
        {
            if (progression == null || !progression.TryResetSession()) return;
            var character = player.GetComponent<CharacterController>();
            character.enabled = false;
            player.transform.SetPositionAndRotation(spawn, Quaternion.identity);
            character.enabled = true;
            player.GetComponent<PlayerMovement>().ResetMotion();
            Physics.SyncTransforms();
            if (Camera.main != null) Camera.main.GetComponent<CameraFollow>()?.SnapToTarget();
        }
    }
}

using System;

namespace ShadowsOfTheForsaken.Encounters
{
    public enum EncounterPhase { Dormant, Watching, Chasing, Attacking, Returning, Blocked, Suspended, Defeated }

    // DOCX sections 3/6: three individually activated encounters. This state owns
    // only encounter identity, never the level's progression flags.
    public sealed class EncounterState
    {
        public Guid SessionId { get; private set; }
        public Guid LifeId { get; private set; }
        public bool Activated { get; private set; }
        public bool HasPendingDeath { get; private set; }
        public bool DeathCredited { get; private set; }

        public void Reset(Guid session, Guid life)
        {
            SessionId = session;
            LifeId = life;
            Activated = HasPendingDeath = DeathCredited = false;
        }

        public bool TryActivate(Guid session, Guid life, bool inEncounterRoom)
        {
            if (!inEncounterRoom || Activated || session != SessionId || life != LifeId) return false;
            Activated = true;
            return true;
        }

        public bool RecordDeath(Guid session, Guid life)
        {
            if (!Activated || HasPendingDeath || DeathCredited || session != SessionId || life != LifeId) return false;
            HasPendingDeath = true;
            return true;
        }

        public bool CanCredit(Guid currentSession, Guid currentLife, bool inEncounterRoom, bool objectiveAvailable)
        {
            return HasPendingDeath && !DeathCredited && SessionId == currentSession && LifeId == currentLife &&
                inEncounterRoom && objectiveAvailable;
        }

        public bool ConfirmCredit(Guid capturedSession, Guid capturedLife)
        {
            if (!HasPendingDeath || DeathCredited || capturedSession != SessionId || capturedLife != LifeId) return false;
            HasPendingDeath = false;
            DeathCredited = true;
            return true;
        }

        public static EncounterPhase Decide(bool activated, bool alive, bool running,
            bool targetAvailable, bool targetInsideArena, bool targetDetected, bool attackInProgress,
            bool canStrike, bool canNavigate, bool atHome)
        {
            if (!alive) return EncounterPhase.Defeated;
            if (!running) return EncounterPhase.Suspended;
            if (!activated) return EncounterPhase.Dormant;
            if (!targetAvailable || !targetInsideArena || !targetDetected)
                return atHome ? EncounterPhase.Watching : canNavigate ? EncounterPhase.Returning : EncounterPhase.Blocked;
            if (attackInProgress || canStrike) return EncounterPhase.Attacking;
            return canNavigate ? EncounterPhase.Chasing : EncounterPhase.Blocked;
        }
    }
}

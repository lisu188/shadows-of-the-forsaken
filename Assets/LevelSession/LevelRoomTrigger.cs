using ShadowsOfTheForsaken.Progression;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class LevelRoomTrigger : MonoBehaviour
{
    public LevelSessionController session;
    public LevelRoom room;
    private BoxCollider region;

    private void Awake() { region = GetComponent<BoxCollider>(); region.isTrigger = true; }
    private void OnValidate() { GetComponent<BoxCollider>().isTrigger = true; }
    private void LateUpdate() => EvaluatePlayerPosition();
    private void OnTriggerEnter(Collider other) => CheckActor(other);
    private void OnTriggerStay(Collider other) => CheckActor(other);

    private void CheckActor(Collider other)
    {
        if (session != null && session.player != null && other != null &&
            other.transform.IsChildOf(session.player.transform)) EvaluatePlayerPosition();
    }

    public bool EvaluatePlayerPosition()
    {
        if (!isActiveAndEnabled || session == null || !session.IsRunning || session.player == null) return false;
        if (region == null) region = GetComponent<BoxCollider>();
        if (!region.enabled || !region.isTrigger) return false;
        var character = session.player.GetComponent<CharacterController>();
        if (character == null || !character.enabled) return false;
        Vector3 center = session.player.transform.TransformPoint(character.center);
        Vector3 local = transform.InverseTransformPoint(center) - region.center;
        Vector3 half = region.size * .5f;
        // A small interior margin avoids opposing volumes flipping the logical
        // room at an exact shared boundary. Check Y as well as X/Z: the secret
        // passage runs beneath other rooms.
        const float inset = .02f;
        if (Mathf.Abs(local.x) >= half.x - inset || Mathf.Abs(local.y) >= half.y - inset ||
            Mathf.Abs(local.z) >= half.z - inset) return false;
        return session.TryEnterRoom(room);
    }
}

using UnityEngine;

/// <summary>Small shared scene adapter; material blocks avoid creating a new material each attack.</summary>
internal sealed class CombatPresentation
{
    private readonly Renderer[] renderers;
    private readonly Color[] baseColors;
    private readonly bool[] rendererEnabled;
    private readonly Collider[] colliders;
    private readonly bool[] colliderEnabled;
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

    public CombatPresentation(GameObject owner)
    {
        renderers = owner.GetComponentsInChildren<Renderer>(true);
        baseColors = new Color[renderers.Length];
        rendererEnabled = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var material = renderers[i].sharedMaterial;
            baseColors[i] = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                material != null && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            rendererEnabled[i] = renderers[i].enabled;
        }
        colliders = owner.GetComponentsInChildren<Collider>(true);
        colliderEnabled = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) colliderEnabled[i] = colliders[i].enabled;
    }

    public void Tint(Color color, float strength)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].GetPropertyBlock(block);
            Color tint = Color.Lerp(baseColors[i], color, strength);
            block.SetColor("_BaseColor", tint);
            block.SetColor("_Color", tint);
            renderers[i].SetPropertyBlock(block);
        }
    }

    public void SetDefeated(bool defeated)
    {
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = !defeated && rendererEnabled[i];
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = !defeated && colliderEnabled[i];
        if (!defeated) Tint(Color.white, 0);
    }

    public static void ResetTransform(Transform target, Vector3 position, Quaternion rotation)
    {
        var controller = target.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        target.SetPositionAndRotation(position, rotation);
        if (wasEnabled) controller.enabled = true;
    }

    public static bool IsClear(Transform source, Transform target)
    {
        Vector3 start = source.position + Vector3.up * 0.5f;
        Vector3 end = target.position + Vector3.up * 0.5f;
        foreach (var hit in Physics.RaycastAll(start, end - start, Vector3.Distance(start, end),
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == source || hit.transform.IsChildOf(source) ||
                hit.transform == target || hit.transform.IsChildOf(target)) continue;
            return false;
        }
        return true;
    }
}

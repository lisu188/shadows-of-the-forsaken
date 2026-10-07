using UnityEngine;

// This scene is the DOCX geometry review for issue #8, not a completed game session.
public sealed class CastleLayoutPreview : MonoBehaviour
{
    private GUIStyle label;

    private void OnGUI()
    {
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 16,
                padding = new RectOffset(12, 12, 8, 8)
            };
        }
        GUI.Box(new Rect(12, 12, Mathf.Min(480, Screen.width - 24), 68),
            "Layout preview - gameplay not connected\nW/S: move   A/D: turn   Space: jump", label);
    }
}

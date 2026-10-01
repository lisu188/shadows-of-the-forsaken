using System;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LevelHUD : MonoBehaviour
{
    public LevelSessionController session;
    public LevelProgressionController progression;
    public CombatHealth playerHealth;
    public PlayerInteractor interactor;

    public Canvas DisplayCanvas => canvas;
    public string HealthText => healthText != null ? healthText.text : "";
    public string PromptText => promptText != null ? promptText.text : "";
    public string OutcomeText => outcomeText != null ? outcomeText.text : "";
    public string MessageText => messageText != null ? messageText.text : "";

    private Canvas canvas;
    private GameObject ownedEventSystem, outcomePanel, promptPanel, messagePanel;
    private Text healthText, objectiveText, promptText, messageText, outcomeText, areaText;
    private RectTransform healthFill;
    private Image healthFillImage;
    private Button restartButton;
    private LevelSessionController observedSession;
    private LevelProgressionController observedProgression;
    private CombatHealth observedHealth;
    private PlayerInteractor observedInteractor;
    private float messageUntil;
    private Font font;

    private void Awake() => BuildDisplay();

    private void OnEnable()
    {
        BuildDisplay();
        canvas.gameObject.SetActive(true);
        if (ownedEventSystem != null) ownedEventSystem.SetActive(true);
        observedSession = session; observedProgression = progression;
        observedHealth = playerHealth; observedInteractor = interactor;
        if (observedSession != null) observedSession.StateChanged += SessionChanged;
        if (observedProgression != null) observedProgression.Changed += ProgressChanged;
        if (observedHealth != null) observedHealth.Changed += HealthChanged;
        if (observedInteractor != null) observedInteractor.SelectionChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (observedSession != null) observedSession.StateChanged -= SessionChanged;
        if (observedProgression != null) observedProgression.Changed -= ProgressChanged;
        if (observedHealth != null) observedHealth.Changed -= HealthChanged;
        if (observedInteractor != null) observedInteractor.SelectionChanged -= Refresh;
        observedSession = null; observedProgression = null; observedHealth = null; observedInteractor = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (ownedEventSystem != null) ownedEventSystem.SetActive(false);
    }

    private void Update()
    {
        if (messageUntil > 0 && Time.unscaledTime >= messageUntil)
        {
            messageUntil = 0;
            messageText.text = "";
        }
        // UI remains accurate if a target was destroyed between input updates.
        Refresh();
    }

    public void ShowMessage(string text, float seconds = 3)
    {
        BuildDisplay();
        messageText.text = text ?? "";
        messageUntil = Time.unscaledTime + (float.IsNaN(seconds) || float.IsInfinity(seconds) ? 3 : Mathf.Clamp(seconds, .1f, 15));
    }

    private void HealthChanged(HealthChange change) => Refresh();
    private void ProgressChanged(ProgressionChange change) => Refresh();
    private void SessionChanged(LevelSessionState state)
    {
        if (state == LevelSessionState.Resetting || state == LevelSessionState.Running)
        {
            messageUntil = 0;
            messageText.text = "";
        }
        Refresh();
    }

    private void Refresh()
    {
        if (canvas == null) return;
        healthText.text = playerHealth != null ? $"Health  {playerHealth.Current} / {playerHealth.Maximum}" : "";
        healthText.color = playerHealth != null && playerHealth.Current <= playerHealth.Maximum / 3
            ? new Color(1, .48f, .42f) : new Color(.95f, .89f, .72f);
        float healthFraction = playerHealth != null && playerHealth.Maximum > 0
            ? Mathf.Clamp01((float)playerHealth.Current / playerHealth.Maximum) : 0;
        healthFill.anchorMax = new Vector2(healthFraction, 1);
        healthFillImage.color = healthFraction <= 1f / 3 ? new Color(.82f, .19f, .16f) : new Color(.58f, .12f, .13f);
        objectiveText.text = progression != null ? Objective(progression.Snapshot) : "";
        areaText.text = progression != null ? AreaName(progression.Snapshot.Room) : "SHADOWS OF THE FORSAKEN";
        bool running = session != null && session.IsRunning;
        promptText.text = running && interactor != null && interactor.SelectedTarget != null
            ? (interactor.CanInteract ? "[E] " : "[Unavailable] ") + interactor.Prompt : "";
        promptPanel.SetActive(!string.IsNullOrEmpty(promptText.text));
        messagePanel.SetActive(!string.IsNullOrEmpty(messageText.text));
        bool terminal = session != null && session.CanRestart;
        outcomePanel.SetActive(terminal);
        restartButton.interactable = terminal;
        outcomeText.text = !terminal ? "" : session.State == LevelSessionState.Completed
            ? $"Castle escaped\nTime: {Mathf.FloorToInt(session.ElapsedSeconds / 60):00}:{Mathf.FloorToInt(session.ElapsedSeconds % 60):00}" +
                (progression != null && (progression.Snapshot.CompletedObjectives & LevelObjective.BonusDiscovered) != 0
                    ? "\nHidden relic discovered" : "")
            : "You fell\nTry the castle again";
    }

    private static string AreaName(LevelRoom room)
    {
        switch (room)
        {
            case LevelRoom.Courtyard: return "COURTYARD";
            case LevelRoom.FirstEncounter: return "THE OUTER GATE";
            case LevelRoom.Puzzle: return "RUNE CHAMBER";
            case LevelRoom.ThroneRoom: return "THRONE ROOM";
            case LevelRoom.Library: return "CURSED LIBRARY";
            case LevelRoom.Catacombs: return "CATACOMBS";
            case LevelRoom.BonusRoom: return "HIDDEN RELIQUARY";
            case LevelRoom.FinalArena: return "THE LAST SANCTUM";
            case LevelRoom.Exit: return "BEYOND THE CASTLE";
            default: return "SHADOWS OF THE FORSAKEN";
        }
    }

    private static string Objective(LevelProgressSnapshot state)
    {
        var flags = state.CompletedObjectives;
        if (state.IsCompleted) return "Journey complete";
        if ((flags & LevelObjective.FirstEnemyDefeated) == 0)
            return state.Room == LevelRoom.Courtyard ? "Explore the courtyard. Continue toward the gate." : "Defeat the demon.";
        if ((flags & LevelObjective.MainPuzzleSolved) == 0) return "Find the marked lever in the western room.";
        if ((flags & LevelObjective.MinibossDefeated) == 0)
            return state.Room == LevelRoom.ThroneRoom ? "Defeat the corrupted guard." : "Return to the junction. Enter the throne room.";
        if ((flags & LevelObjective.LibraryOpened) == 0) return "Find the marked book in the library.";
        if ((flags & LevelObjective.FinalEnemyDefeated) == 0)
            return state.Room == LevelRoom.FinalArena ? "Defeat the final demon." : "Cross the catacombs. Defeat the final demon.";
        return "Reach the exit beyond the final arena.";
    }

    private void BuildDisplay()
    {
        if (canvas != null) return;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject("Level HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        var camera = session != null && session.follow != null ? session.follow.GetComponent<Camera>() : null;
        // Camera-space UI is included in actual scene-camera captures and player
        // rendering. Tests without a camera still get a usable overlay canvas.
        canvas.renderMode = camera != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = camera;
        canvas.planeDistance = camera != null ? Mathf.Max(1, camera.nearClipPlane + .1f) : 1;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;
        var status = Panel("Status", canvas.transform, new Vector2(0, 1), new Vector2(20, -20), new Vector2(420, 136));
        Ornament(status.transform, new Vector2(420, 136));
        areaText = Label("Area", status.transform, new Vector2(18, -12), new Vector2(384, 22), 15, TextAnchor.UpperLeft);
        areaText.color = new Color(.77f, .65f, .43f);
        healthText = Label("Health", status.transform, new Vector2(18, -37), new Vector2(384, 24), 19, TextAnchor.UpperLeft);
        var bar = Panel("Health track", status.transform, new Vector2(0, 1), new Vector2(18, -68), new Vector2(384, 7));
        bar.GetComponent<Image>().color = new Color(.24f, .2f, .19f, 1);
        healthFill = Rect("Health remaining", bar.transform, Vector2.zero, Vector2.zero, Vector2.zero);
        healthFill.anchorMin = Vector2.zero; healthFill.anchorMax = Vector2.one; healthFill.offsetMin = healthFill.offsetMax = Vector2.zero;
        healthFillImage = healthFill.gameObject.AddComponent<Image>(); healthFillImage.raycastTarget = false;
        objectiveText = Label("Objective", status.transform, new Vector2(18, -87), new Vector2(384, 42), 16, TextAnchor.UpperLeft);
        objectiveText.color = new Color(.9f, .89f, .83f);
        var controls = Panel("Controls", canvas.transform, new Vector2(0, 0), new Vector2(20, 18), new Vector2(494, 34));
        Label("Controls text", controls.transform, new Vector2(12, -7), new Vector2(470, 24), 15, TextAnchor.UpperLeft).text =
            "W/S Move    A/D Turn    Space Jump    LMB Attack";
        promptPanel = Panel("Interaction prompt", canvas.transform, new Vector2(.5f, 0), new Vector2(0, 70), new Vector2(600, 50));
        Ornament(promptPanel.transform, new Vector2(600, 50));
        promptText = Label("Prompt", promptPanel.transform, new Vector2(16, 0), new Vector2(568, 50), 21, TextAnchor.MiddleCenter);
        promptText.color = new Color(.96f, .88f, .67f);
        messagePanel = Panel("Message", canvas.transform, new Vector2(.5f, 1), new Vector2(0, -174), new Vector2(820, 66));
        messageText = Label("Message text", messagePanel.transform, new Vector2(16, -6), new Vector2(788, 54), 20, TextAnchor.MiddleCenter);
        outcomePanel = Panel("Outcome", canvas.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(470, 240));
        Ornament(outcomePanel.transform, new Vector2(470, 240));
        outcomeText = Label("Outcome text", outcomePanel.transform, new Vector2(20, -24), new Vector2(430, 124), 27, TextAnchor.MiddleCenter);
        outcomeText.color = new Color(.96f, .9f, .75f);
        var buttonObject = Panel("Restart", outcomePanel.transform, new Vector2(.5f, 0), new Vector2(0, 24), new Vector2(240, 54));
        buttonObject.GetComponent<Image>().color = new Color(.28f, .22f, .14f, 1);
        buttonObject.GetComponent<Image>().raycastTarget = true;
        restartButton = buttonObject.AddComponent<Button>();
        restartButton.targetGraphic = buttonObject.GetComponent<Image>();
        restartButton.onClick.AddListener(() => { if (session != null) session.RestartSession(); });
        Label("Restart text", buttonObject.transform, Vector2.zero, new Vector2(240, 54), 23, TextAnchor.MiddleCenter).text = "Restart [R]";
        outcomePanel.SetActive(false);
        promptPanel.SetActive(false); messagePanel.SetActive(false);
        if (EventSystem.current == null)
        {
            ownedEventSystem = new GameObject("Level HUD EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            ownedEventSystem.transform.SetParent(transform, false);
            ownedEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    private static void Ornament(Transform parent, Vector2 size)
    {
        // Quiet metal rules keep the HUD readable without covering the scene.
        foreach (float y in new[] { 0f, -size.y + 1 })
        {
            var line = Panel("Gilt rule", parent, new Vector2(0, 1), new Vector2(0, y), new Vector2(size.x, 1));
            line.GetComponent<Image>().color = new Color(.65f, .49f, .27f, .75f);
        }
        var accent = Panel("Gilt edge", parent, new Vector2(0, 1), Vector2.zero, new Vector2(3, size.y));
        accent.GetComponent<Image>().color = new Color(.65f, .49f, .27f, .85f);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        result.SetParent(parent, false);
        result.anchorMin = result.anchorMax = anchor;
        result.pivot = anchor;
        result.anchoredPosition = offset;
        result.sizeDelta = size;
        return result;
    }

    private static GameObject Panel(string name, Transform parent, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var rect = Rect(name, parent, anchor, offset, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(.032f, .036f, .042f, .86f);
        image.raycastTarget = false;
        return rect.gameObject;
    }

    private Text Label(string name, Transform parent, Vector2 offset, Vector2 size, int fontSize, TextAnchor alignment)
    {
        var rect = Rect(name, parent, new Vector2(0, 1), offset, size);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font; text.fontSize = fontSize; text.color = Color.white;
        text.alignment = alignment; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }
}

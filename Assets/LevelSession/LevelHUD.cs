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
    private GameObject ownedEventSystem, outcomePanel;
    private Text healthText, objectiveText, promptText, messageText, outcomeText;
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
        objectiveText.text = progression != null ? Objective(progression.Snapshot) : "";
        bool running = session != null && session.IsRunning;
        promptText.text = running && interactor != null && interactor.SelectedTarget != null
            ? (interactor.CanInteract ? "[E] " : "[Unavailable] ") + interactor.Prompt : "";
        bool terminal = session != null && session.CanRestart;
        outcomePanel.SetActive(terminal);
        restartButton.interactable = terminal;
        outcomeText.text = !terminal ? "" : session.State == LevelSessionState.Completed
            ? $"Castle escaped\nTime: {Mathf.FloorToInt(session.ElapsedSeconds / 60):00}:{Mathf.FloorToInt(session.ElapsedSeconds % 60):00}" +
                (progression != null && (progression.Snapshot.CompletedObjectives & LevelObjective.BonusDiscovered) != 0
                    ? "\nHidden relic discovered" : "")
            : "You fell\nTry the castle again";
    }

    private static string Objective(LevelProgressSnapshot state)
    {
        var flags = state.CompletedObjectives;
        if (state.IsCompleted) return "Journey complete";
        if ((flags & LevelObjective.FirstEnemyDefeated) == 0)
            return state.Room == LevelRoom.Courtyard ? "Explore the courtyard. Continue toward the gate." : "Defeat the demon.";
        if ((flags & LevelObjective.MainPuzzleSolved) == 0) return "Find the marked lever in the western room.";
        if ((flags & LevelObjective.MinibossDefeated) == 0) return "Return to the junction. Enter the throne room.";
        if ((flags & LevelObjective.LibraryOpened) == 0) return "Find the marked book in the library.";
        if ((flags & LevelObjective.FinalEnemyDefeated) == 0) return "Cross the catacombs. Defeat the final demon.";
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
        var status = Panel("Status", canvas.transform, new Vector2(0, 1), new Vector2(12, -12), new Vector2(480, 92));
        healthText = Label("Health", status.transform, new Vector2(12, -8), new Vector2(450, 28), 22, TextAnchor.UpperLeft);
        objectiveText = Label("Objective", status.transform, new Vector2(12, -40), new Vector2(450, 44), 17, TextAnchor.UpperLeft);
        var controls = Panel("Controls", canvas.transform, new Vector2(0, 0), new Vector2(12, 12), new Vector2(525, 38));
        Label("Controls text", controls.transform, new Vector2(10, -8), new Vector2(505, 26), 16, TextAnchor.UpperLeft).text =
            "W/S Move    A/D Turn    Space Jump    LMB Attack";
        var prompt = Rect("Interaction prompt", canvas.transform, new Vector2(.5f, 0), new Vector2(0, 65), new Vector2(850, 50));
        promptText = Label("Prompt", prompt, Vector2.zero, new Vector2(850, 50), 22, TextAnchor.MiddleCenter);
        var message = Rect("Message", canvas.transform, new Vector2(.5f, 1), new Vector2(0, -125), new Vector2(850, 54));
        messageText = Label("Message text", message, Vector2.zero, new Vector2(850, 54), 22, TextAnchor.MiddleCenter);
        outcomePanel = Panel("Outcome", canvas.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(440, 220));
        outcomeText = Label("Outcome text", outcomePanel.transform, new Vector2(20, -14), new Vector2(400, 118), 26, TextAnchor.MiddleCenter);
        var buttonObject = Panel("Restart", outcomePanel.transform, new Vector2(.5f, 0), new Vector2(0, 24), new Vector2(240, 54));
        buttonObject.GetComponent<Image>().color = new Color(.32f, .27f, .18f, 1);
        buttonObject.GetComponent<Image>().raycastTarget = true;
        restartButton = buttonObject.AddComponent<Button>();
        restartButton.targetGraphic = buttonObject.GetComponent<Image>();
        restartButton.onClick.AddListener(() => { if (session != null) session.RestartSession(); });
        Label("Restart text", buttonObject.transform, Vector2.zero, new Vector2(240, 54), 23, TextAnchor.MiddleCenter).text = "Restart [R]";
        outcomePanel.SetActive(false);
        if (EventSystem.current == null)
        {
            ownedEventSystem = new GameObject("Level HUD EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            ownedEventSystem.transform.SetParent(transform, false);
            ownedEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
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
        image.color = new Color(.055f, .06f, .075f, .90f);
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

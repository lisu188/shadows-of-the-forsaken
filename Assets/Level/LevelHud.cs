using System;
using System.Collections.Generic;
using ShadowsOfTheForsaken.Combat;
using ShadowsOfTheForsaken.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ShadowsOfTheForsaken.Level
{
    /// <summary>Scene-owned UI rendered by the level camera, including render-texture requests.</summary>
    [DisallowMultipleComponent]
    public sealed class LevelHud : MonoBehaviour
    {
        private static readonly Color Gold = new Color(.94f, .83f, .64f);
        private static readonly Color Parchment = new Color(.93f, .90f, .83f);
        private static readonly Color Muted = new Color(.78f, .75f, .70f);
        private static readonly Color Blood = new Color(.68f, .16f, .13f);
        private static readonly Color PanelColor = new Color(.035f, .030f, .035f, .90f);
        private ForsakenLevel level;
        private GameObject uiRoot;
        private Font font;
        private EventSystem eventSystem;
        private InputSystemUIInputModule inputModule;
        private InputActionAsset ownedUiActions;
        private readonly List<InputActionReference> actionReferences = new List<InputActionReference>();
        private Text room, health, objective, timer, prompt, feedback, enemyName, enemyCue;
        private Text endingTitle, endingBody, endingTime;
        private GameObject interactionPanel, enemyPanel, terminalPanel;
        private Button restartButton;
        private Guid restartSession;
        private bool wasTerminal;

        public Canvas HudCanvas { get; private set; }
        public Image HealthBar { get; private set; }
        public Button RestartButton => restartButton;
        public Color HealthBarColor => Blood;
        public bool Ready => isActiveAndEnabled && level != null && HudCanvas != null &&
            HudCanvas.isActiveAndEnabled && HudCanvas.renderMode == RenderMode.ScreenSpaceCamera &&
            HudCanvas.worldCamera != null && HudCanvas.worldCamera.isActiveAndEnabled &&
            HealthBar != null && HealthBar.isActiveAndEnabled &&
            eventSystem != null && eventSystem.isActiveAndEnabled && inputModule != null && inputModule.isActiveAndEnabled;

        // A pixel verifier can sample the interior of the actual filled health graphic in a camera PNG.
        public Vector2 HealthBarViewportPoint
        {
            get
            {
                if (HealthBar == null || HudCanvas == null || HudCanvas.worldCamera == null) return Vector2.zero;
                var rect = HealthBar.rectTransform.rect;
                var local = new Vector3(rect.xMin + rect.width * Mathf.Max(.01f, HealthBar.fillAmount) * .5f, rect.center.y, 0);
                return HudCanvas.worldCamera.WorldToViewportPoint(HealthBar.rectTransform.TransformPoint(local));
            }
        }

        public void Configure(ForsakenLevel owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (level == owner && uiRoot != null) { Refresh(); Canvas.ForceUpdateCanvases(); return; }
            ReleaseUi();
            level = owner;
            var camera = owner.Follow != null ? owner.Follow.GetComponent<Camera>() : null;
            var source = owner.inputActions != null ? owner.inputActions : InputSystem.actions;
            if (camera == null || source == null || source.FindActionMap("UI") == null)
            {
                Debug.LogError("LevelHud requires the level camera and the existing UI input action map.", this);
                return;
            }

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiRoot = new GameObject("Castle HUD", typeof(RectTransform));
            uiRoot.SetActive(false); // Configure the camera and actions before any UI consumer enables.
            uiRoot.transform.SetParent(transform, false);
            uiRoot.layer = 5;
            HudCanvas = uiRoot.AddComponent<Canvas>();
            HudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            HudCanvas.worldCamera = camera;
            HudCanvas.planeDistance = Mathf.Max(camera.nearClipPlane + .1f, .3f);
            HudCanvas.sortingOrder = 100;
            var scaler = uiRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            uiRoot.AddComponent<GraphicRaycaster>();
            BuildPresentation();
            BuildInput(source);
            uiRoot.SetActive(isActiveAndEnabled);
            Refresh();
            Canvas.ForceUpdateCanvases();
        }

        private void BuildPresentation()
        {
            var status = Panel(uiRoot.transform, "Journey status", new Vector2(0, 1), new Rect(22, 20, 430, 158));
            room = Label(status.transform, "Room", new Rect(18, 12, 394, 32), 24, Gold, FontStyle.Bold);
            health = Label(status.transform, "Health", new Rect(18, 48, 394, 27), 18, Parchment);
            var track = Graphic(status.transform, "Health track", new Rect(18, 80, 394, 10), new Color(.18f, .12f, .12f));
            HealthBar = Graphic(track.transform, "Health fill", new Rect(0, 0, 394, 10), Blood);
            HealthBar.type = Image.Type.Filled;
            HealthBar.fillMethod = Image.FillMethod.Horizontal;
            HealthBar.fillOrigin = (int)Image.OriginHorizontal.Left;
            HealthBar.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width,
                Texture2D.whiteTexture.height), new Vector2(.5f, .5f));
            objective = Label(status.transform, "Objective", new Rect(18, 104, 394, 43), 16, Parchment);

            var controls = Panel(uiRoot.transform, "Controls", new Vector2(0, 0), new Rect(0, -62, 1280, 62));
            StretchHorizontal(controls.rectTransform, 0, 0);
            var keys = Label(controls.transform, "Keys", new Rect(22, 16, 1045, 30), 16, Muted);
            StretchHorizontal(keys.rectTransform, 22, 214); // Timer width, right margin and a 24-unit gap.
            keys.text =
                "W/S move   A/D turn   Space jump   LMB attack   E interact";
            timer = Label(controls.transform, "Elapsed time", new Rect(1090, 16, 166, 30), 16, Gold);
            timer.rectTransform.anchorMin = timer.rectTransform.anchorMax = Vector2.one;
            timer.rectTransform.anchoredPosition = new Vector2(-190, -16);
            timer.alignment = TextAnchor.MiddleRight;

            interactionPanel = Panel(uiRoot.transform, "Interaction", new Vector2(.5f, 0), new Rect(-390, -170, 780, 90)).gameObject;
            prompt = Label(interactionPanel.transform, "Prompt", new Rect(18, 12, 744, 28), 19, Gold, FontStyle.Bold);
            feedback = Label(interactionPanel.transform, "Feedback", new Rect(18, 46, 744, 35), 16, Parchment);

            enemyPanel = Panel(uiRoot.transform, "Encounter", new Vector2(1, 1), new Rect(-490, 20, 468, 98)).gameObject;
            enemyName = Label(enemyPanel.transform, "Enemy health", new Rect(18, 12, 432, 30), 18, Parchment, FontStyle.Bold);
            enemyCue = Label(enemyPanel.transform, "Attack cue", new Rect(18, 52, 432, 32), 17, Gold);

            terminalPanel = Panel(uiRoot.transform, "Journey ending", new Vector2(.5f, .5f), new Rect(-300, -152, 600, 304)).gameObject;
            endingTitle = Label(terminalPanel.transform, "Ending title", new Rect(30, 24, 540, 40), 27, Gold, FontStyle.Bold);
            endingTitle.alignment = TextAnchor.MiddleCenter;
            endingBody = Label(terminalPanel.transform, "Ending message", new Rect(35, 87, 530, 80), 19, Parchment);
            endingBody.alignment = TextAnchor.UpperCenter;
            endingTime = Label(terminalPanel.transform, "Ending elapsed", new Rect(35, 176, 530, 30), 16, Muted);
            endingTime.alignment = TextAnchor.MiddleCenter;
            var buttonImage = Graphic(terminalPanel.transform, "Begin a new journey", new Rect(120, 234, 360, 44), new Color(.25f, .18f, .12f));
            buttonImage.raycastTarget = true;
            restartButton = buttonImage.gameObject.AddComponent<Button>();
            restartButton.targetGraphic = buttonImage;
            var colors = restartButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.15f, .95f);
            colors.pressedColor = new Color(.75f, .65f, .50f);
            colors.selectedColor = new Color(1.2f, 1.1f, .9f);
            colors.fadeDuration = .1f;
            restartButton.colors = colors;
            var buttonLabel = Label(buttonImage.transform, "Restart label", new Rect(10, 5, 340, 34), 18, Gold, FontStyle.Bold);
            buttonLabel.alignment = TextAnchor.MiddleCenter;
            buttonLabel.text = "R — Begin a new journey";
            terminalPanel.SetActive(false);
        }

        private void BuildInput(InputActionAsset source)
        {
            // The module owns a copy: enabling or destroying this HUD cannot disable gameplay actions.
            ownedUiActions = Instantiate(source);
            ownedUiActions.name = "Castle HUD UI actions";
            ownedUiActions.Disable();
            ownedUiActions.devices = source.devices;
            ownedUiActions.bindingMask = source.bindingMask;
            var events = new GameObject("Castle UI events");
            events.transform.SetParent(uiRoot.transform, false);
            eventSystem = events.AddComponent<EventSystem>();
            eventSystem.sendNavigationEvents = true;
            inputModule = events.AddComponent<InputSystemUIInputModule>();
            inputModule.actionsAsset = ownedUiActions;
            inputModule.point = Reference("Point");
            inputModule.leftClick = Reference("Click");
            inputModule.rightClick = Reference("RightClick");
            inputModule.middleClick = Reference("MiddleClick");
            inputModule.scrollWheel = Reference("ScrollWheel");
            inputModule.move = Reference("Navigate");
            inputModule.submit = Reference("Submit");
            inputModule.cancel = Reference("Cancel");
            inputModule.trackedDevicePosition = Reference("TrackedDevicePosition");
            inputModule.trackedDeviceOrientation = Reference("TrackedDeviceOrientation");
        }

        private InputActionReference Reference(string name)
        {
            var reference = InputActionReference.Create(ownedUiActions.FindAction("UI/" + name, true));
            actionReferences.Add(reference);
            return reference;
        }

        private void LateUpdate()
        {
            if (uiRoot == null || level == null || level.Progression == null) return;
            Refresh();
            Canvas.ForceUpdateCanvases();
        }

        private void Refresh()
        {
            if (HudCanvas == null || level == null || level.Progression == null || level.Combat == null) return;
            var snapshot = level.Progression.Snapshot;
            room.text = RoomName(snapshot.Room);
            health.text = "Health " + level.Combat.Health + " / " + level.Combat.MaximumHealth;
            HealthBar.fillAmount = level.Combat.MaximumHealth > 0 ? Mathf.Clamp01((float)level.Combat.Health / level.Combat.MaximumHealth) : 0;
            objective.text = Objective(snapshot);
            timer.text = "Time " + level.ElapsedSeconds.ToString("0.0") + " s";
            bool terminal = level.IsTerminal;
            terminalPanel.SetActive(terminal);
            string currentPrompt = level.Interaction != null ? level.Interaction.CurrentPrompt : "";
            string currentFeedback = level.Interaction != null ? level.Interaction.LastFeedback : "";
            interactionPanel.SetActive(!terminal && (currentPrompt.Length != 0 || currentFeedback.Length != 0));
            prompt.text = currentPrompt;
            feedback.text = currentFeedback;
            EnemyCombat currentEnemy = null;
            if (!terminal)
                foreach (var enemy in level.Enemies)
                    if (enemy != null && enemy.Room == snapshot.Room && !enemy.IsDead) { currentEnemy = enemy; break; }
            enemyPanel.SetActive(currentEnemy != null);
            if (currentEnemy != null)
            {
                enemyName.text = currentEnemy.name + "   " + currentEnemy.Health + " / " + currentEnemy.MaximumHealth;
                enemyCue.text = currentEnemy.Phase == AttackPhase.Windup ? "Attack preparing — step out of reach" :
                    currentEnemy.Phase == AttackPhase.Active ? "STRIKE" : "";
                enemyCue.color = currentEnemy.Phase == AttackPhase.Active ? new Color(1f, .45f, .29f) : Gold;
            }
            if (terminal)
            {
                endingTitle.text = level.Failed ? "THE CURSE CLAIMED YOU" : "DAWN BEYOND THE RUINS";
                bool relic = (snapshot.CompletedObjectives & LevelObjective.BonusDiscovered) != 0;
                endingBody.text = level.Failed ? "The castle returns to silence. Begin again from the courtyard." :
                    "The final curse is broken. " + (relic ? "The forgotten relic was recovered." : "The optional relic remains in the castle.");
                endingTime.text = "Elapsed: " + level.ElapsedSeconds.ToString("0.0") + " seconds";
                if (!wasTerminal || restartSession != snapshot.SessionId)
                {
                    restartSession = snapshot.SessionId;
                    Guid capturedSession = restartSession;
                    restartButton.onClick.RemoveAllListeners();
                    restartButton.onClick.AddListener(() => RestartClicked(capturedSession));
                    if (eventSystem != null && eventSystem.isActiveAndEnabled)
                        eventSystem.SetSelectedGameObject(restartButton.gameObject);
                }
            }
            else if (wasTerminal && eventSystem != null)
            {
                restartButton.onClick.RemoveAllListeners();
                eventSystem.SetSelectedGameObject(null);
            }
            wasTerminal = terminal;
        }

        private void RestartClicked(Guid capturedSession)
        {
            if (level != null && level.IsTerminal && level.Progression != null &&
                level.Progression.Snapshot.SessionId == capturedSession && Time.timeScale > 0)
                level.TryRestart();
        }

        private void OnEnable()
        {
            if (uiRoot == null) return;
            uiRoot.SetActive(true);
            Refresh();
            Canvas.ForceUpdateCanvases();
        }

        private void OnDisable()
        {
            if (uiRoot != null) uiRoot.SetActive(false);
        }

        private void OnDestroy() => ReleaseUi();

        private void ReleaseUi()
        {
            if (uiRoot != null) uiRoot.SetActive(false);
            if (restartButton != null) restartButton.onClick.RemoveAllListeners();
            if (inputModule != null) inputModule.UnassignActions();
            if (ownedUiActions != null) ownedUiActions.Disable();
            foreach (var reference in actionReferences) if (reference != null) Destroy(reference);
            actionReferences.Clear();
            if (ownedUiActions != null) Destroy(ownedUiActions);
            if (HealthBar != null && HealthBar.sprite != null) Destroy(HealthBar.sprite);
            if (uiRoot != null) Destroy(uiRoot);
            uiRoot = null;
            HudCanvas = null;
            HealthBar = null;
            ownedUiActions = null;
            inputModule = null;
            eventSystem = null;
            wasTerminal = false;
            restartSession = Guid.Empty;
        }

        private RectTransform Rectangle(Transform parent, string name, Vector2 anchor, Rect area)
        {
            var element = new GameObject(name, typeof(RectTransform));
            element.layer = 5;
            var rect = element.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
            return rect;
        }

        private Image Panel(Transform parent, string name, Vector2 anchor, Rect area)
        {
            var panel = Rectangle(parent, name, anchor, area).gameObject.AddComponent<Image>();
            panel.color = PanelColor;
            panel.raycastTarget = false;
            var edge = Graphic(panel.transform, "Gilt edge", new Rect(0, 0, area.width, 2), new Color(Gold.r, Gold.g, Gold.b, .55f));
            StretchHorizontal(edge.rectTransform, 0, 0);
            return panel;
        }

        private static void StretchHorizontal(RectTransform rect, float left, float right)
        {
            rect.anchorMin = new Vector2(0, rect.anchorMin.y);
            rect.anchorMax = new Vector2(1, rect.anchorMax.y);
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-right, rect.offsetMax.y);
        }

        private Image Graphic(Transform parent, string name, Rect area, Color color)
        {
            var graphic = Rectangle(parent, name, new Vector2(0, 1), area).gameObject.AddComponent<Image>();
            graphic.color = color;
            graphic.raycastTarget = false;
            return graphic;
        }

        private Text Label(Transform parent, string name, Rect area, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var label = Rectangle(parent, name, new Vector2(0, 1), area).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .75f);
            shadow.effectDistance = new Vector2(1, -1);
            return label;
        }

        public static string RoomName(LevelRoom room)
        {
            switch (room)
            {
                case LevelRoom.Courtyard: return "The silent courtyard";
                case LevelRoom.FirstEncounter: return "The broken gate";
                case LevelRoom.Puzzle: return "Runes of the moon";
                case LevelRoom.ThroneRoom: return "The ruined throne room";
                case LevelRoom.Library: return "The cursed library";
                case LevelRoom.Catacombs: return "The cult's catacombs";
                case LevelRoom.BonusRoom: return "The forgotten chamber";
                case LevelRoom.FinalArena: return "The last curse";
                default: return "Beyond the ruins";
            }
        }

        private static string Objective(LevelProgressSnapshot snapshot)
        {
            var flags = snapshot.CompletedObjectives;
            if (snapshot.Room == LevelRoom.Courtyard) return "Explore the courtyard and approach the castle.";
            if ((flags & LevelObjective.FirstEnemyDefeated) == 0) return "Defeat the lone demon at the broken gate.";
            if ((flags & LevelObjective.MainPuzzleSolved) == 0) return "Take the western branch. Read and activate the runes.";
            if ((flags & LevelObjective.MinibossDefeated) == 0) return "Return to the junction, then find the throne to the east.";
            if ((flags & LevelObjective.LibraryOpened) == 0) return "Find the forbidden book in the library to the north.";
            if ((flags & LevelObjective.FinalEnemyDefeated) == 0) return "Break the final curse. A concealed lever is optional.";
            return "Leave through the northern gate beyond the final arena.";
        }
    }
}

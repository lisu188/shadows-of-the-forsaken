using System;
using ShadowsOfTheForsaken.Movement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    public float rotationSpeed = 20f;
    public float gravity = 9.81f;
    public float jumpForce = 5f;
    public InputActionAsset inputActions;

    public event Action AttackRequested;
    public event Action InteractRequested;
    public bool ControlsEnabled => controlsEnabled;
    public float VerticalVelocity => motor.VerticalVelocity;

    private readonly PlayerMotor motor = new PlayerMotor();
    private readonly PlayerInputGate inputGate = new PlayerInputGate();
    private CharacterController characterController;
    private InputActionMap ownedActions;
    private InputAction moveAction, jumpAction, attackAction, interactAction;
    private MovementInput movement;
    private PlayerButtons pendingButtons;
    private bool controlsEnabled = true;
    private bool focused = true;
    private bool paused;
    private bool inputUpdated;

    private bool CanControl => isActiveAndEnabled && controlsEnabled && focused && !paused &&
        characterController != null && characterController.enabled && ownedActions != null && Time.timeScale > 0;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        focused = Application.isFocused;
    }

    private void OnEnable()
    {
        ResetMotion();
        var source = inputActions != null ? inputActions : InputSystem.actions;
        try
        {
            if (source == null) throw new InvalidOperationException("Assign inputActions or configure project-wide Input System actions.");
            var sourceMap = source.FindActionMap("Player", true);
            ownedActions = sourceMap.Clone();
            ownedActions.devices = sourceMap.devices ?? source.devices;
            ownedActions.bindingMask = sourceMap.bindingMask ?? source.bindingMask;
            moveAction = ownedActions.FindAction("Move", true);
            jumpAction = ownedActions.FindAction("Jump", true);
            attackAction = ownedActions.FindAction("Attack", true);
            interactAction = ownedActions.FindAction("Interact", true);
            if (moveAction.type != InputActionType.Value || moveAction.expectedControlType != "Vector2")
                throw new InvalidOperationException("Player/Move must be a Value action with Vector2 controls.");
            for (int i = 0; i < moveAction.bindings.Count; i++)
            {
                var binding = moveAction.bindings[i];
                if (binding.isComposite && (binding.path.StartsWith("Dpad", StringComparison.OrdinalIgnoreCase) ||
                    binding.path.StartsWith("2DVector", StringComparison.OrdinalIgnoreCase)))
                    moveAction.ChangeBinding(i).WithPath("2DVector(mode=1)");
            }
            foreach (var action in new[] { jumpAction, attackAction, interactAction })
            {
                if (action.type != InputActionType.Button)
                    throw new InvalidOperationException("Jump, Attack and Interact must be Button actions.");
                action.wantsInitialStateCheck = true;
                action.Enable();
            }
            moveAction.Enable();
            InputSystem.onAfterUpdate += SampleInput;
        }
        catch (Exception error)
        {
            Debug.LogError("PlayerMovement input configuration: " + error.Message, this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        InputSystem.onAfterUpdate -= SampleInput;
        if (ownedActions != null)
        {
            ownedActions.Disable();
            ownedActions.Dispose();
            ownedActions = null;
        }
        moveAction = jumpAction = attackAction = interactAction = null;
        ResetMotion();
    }

    private void OnDestroy()
    {
        AttackRequested = null;
        InteractRequested = null;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        focused = hasFocus;
        ResetMotion();
    }

    private void OnApplicationPause(bool isPaused)
    {
        paused = isPaused;
        ResetMotion();
    }

    public void SetControlsEnabled(bool value)
    {
        if (controlsEnabled == value) return;
        controlsEnabled = value;
        ResetMotion();
    }

    public void ResetMotion()
    {
        motor.Reset();
        inputGate.Suspend();
        movement = default;
        pendingButtons = PlayerButtons.None;
        inputUpdated = false;
    }

    private void SampleInput()
    {
        if ((InputState.currentUpdateType & (InputUpdateType.BeforeRender | InputUpdateType.Editor)) != 0) return;
        if (!CanControl)
        {
            ResetMotion();
            return;
        }
        var axes = moveAction.ReadValue<Vector2>();
        var held = PlayerButtons.None;
        var pressed = PlayerButtons.None;
        ReadButton(jumpAction, PlayerButtons.Jump, ref held, ref pressed);
        ReadButton(attackAction, PlayerButtons.Attack, ref held, ref pressed);
        ReadButton(interactAction, PlayerButtons.Interact, ref held, ref pressed);
        movement = inputGate.Sample(axes.x, axes.y, held, pressed);
        pendingButtons |= movement.Pressed;
        inputUpdated = true;
    }

    private static void ReadButton(InputAction action, PlayerButtons button, ref PlayerButtons held, ref PlayerButtons pressed)
    {
        if (action.IsPressed()) held |= button;
        if (action.WasPressedThisFrame()) pressed |= button;
    }

    private void Update() => Simulate(Time.deltaTime);

    public void Simulate(float deltaTime)
    {
        if (!CanControl || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0)
        {
            ResetMotion();
            return;
        }
        if (!inputUpdated) return;
        var buttons = pendingButtons;
        pendingButtons = PlayerButtons.None;
        var settings = new MovementSettings(Safe(speed, 5), Safe(rotationSpeed, 20), Safe(gravity, 9.81f), Safe(jumpForce, 5));
        float duration = Mathf.Min(deltaTime, 0.1f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(duration * 60f));
        float stepTime = duration / steps;
        for (int i = 0; i < steps; i++)
        {
            var input = new MovementInput(movement.Turn, movement.Forward, i == 0 ? buttons : PlayerButtons.None);
            var step = motor.Step(settings, input, characterController.isGrounded, stepTime);
            var heading = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            var offset = heading * new Vector3(step.LocalX, 0, step.LocalZ) + Vector3.up * step.LocalY;
            transform.Rotate(0, step.YawDegrees, 0, Space.World);
            var collisions = characterController.Move(offset);
            motor.ResolveCollisions((collisions & CollisionFlags.Below) != 0, (collisions & CollisionFlags.Above) != 0);
        }
        if ((buttons & PlayerButtons.Attack) != 0) Publish(AttackRequested);
        if ((buttons & PlayerButtons.Interact) != 0) Publish(InteractRequested);
    }

    private void Publish(Action listeners)
    {
        if (listeners == null) return;
        foreach (Action listener in listeners.GetInvocationList())
        {
            if (!CanControl) break;
            try { listener(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }

    private static float Safe(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, 0, 10000);
    }

    private void OnValidate()
    {
        speed = Safe(speed, 5);
        rotationSpeed = Safe(rotationSpeed, 20);
        gravity = Safe(gravity, 9.81f);
        jumpForce = Safe(jumpForce, 5);
    }
}

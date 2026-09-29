using System;

namespace ShadowsOfTheForsaken.Movement
{
    [Flags]
    public enum PlayerButtons { None = 0, Jump = 1, Attack = 2, Interact = 4 }

    public readonly struct MovementInput
    {
        public readonly float Turn;
        public readonly float Forward;
        public readonly PlayerButtons Pressed;

        public MovementInput(float turn, float forward, PlayerButtons pressed)
        {
            Turn = Axis(turn);
            Forward = Axis(forward);
            Pressed = pressed & (PlayerButtons.Jump | PlayerButtons.Attack | PlayerButtons.Interact);
        }

        private static float Axis(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(-1f, Math.Min(1f, value));
        }
    }

    public sealed class PlayerInputGate
    {
        private PlayerButtons previousHeld;
        public bool AwaitingNeutral { get; private set; } = true;

        public void Suspend()
        {
            previousHeld = PlayerButtons.None;
            AwaitingNeutral = true;
        }

        public MovementInput Sample(float turn, float forward, PlayerButtons held, PlayerButtons pressed)
        {
            var input = new MovementInput(turn, forward, held | pressed);
            if (AwaitingNeutral)
            {
                if (Math.Abs(input.Turn) < 0.01f && Math.Abs(input.Forward) < 0.01f && input.Pressed == PlayerButtons.None)
                    AwaitingNeutral = false;
                return default;
            }
            var edges = input.Pressed & ~previousHeld;
            previousHeld = held;
            return new MovementInput(input.Turn, input.Forward, edges);
        }
    }

    public readonly struct MovementSettings
    {
        public readonly float Speed;
        public readonly float RotationSpeed;
        public readonly float Gravity;
        public readonly float JumpForce;

        public MovementSettings(float speed, float rotationSpeed, float gravity, float jumpForce)
        {
            Speed = Validate(speed, nameof(speed));
            RotationSpeed = Validate(rotationSpeed, nameof(rotationSpeed));
            Gravity = Validate(gravity, nameof(gravity));
            JumpForce = Validate(jumpForce, nameof(jumpForce));
        }

        private static float Validate(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 10000f)
                throw new ArgumentOutOfRangeException(name, "Movement settings must be finite and between 0 and 10000.");
            return value;
        }
    }

    public readonly struct MovementStep
    {
        public readonly float LocalX;
        public readonly float LocalY;
        public readonly float LocalZ;
        public readonly float YawDegrees;

        public MovementStep(double x, double y, double z, double yaw)
        {
            LocalX = (float)x;
            LocalY = (float)y;
            LocalZ = (float)z;
            YawDegrees = (float)yaw;
        }
    }

    public sealed class PlayerMotor
    {
        public const float GroundStickSpeed = 2f;
        public const float MaximumFallSpeed = 60f;
        private double verticalVelocity;
        public float VerticalVelocity => (float)verticalVelocity;

        public void Reset() => verticalVelocity = 0;

        public void ResolveCollisions(bool below, bool above)
        {
            if (above && verticalVelocity > 0) verticalVelocity = 0;
            if (below && verticalVelocity < 0) verticalVelocity = -GroundStickSpeed;
        }

        public MovementStep Step(MovementSettings settings, MovementInput input, bool grounded, float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0 || deltaTime > 1)
                throw new ArgumentOutOfRangeException(nameof(deltaTime), "Step time must be finite and between 0 and 1 second.");
            if (deltaTime == 0) return default;

            double time = deltaTime;
            bool launching = grounded && verticalVelocity <= 0 &&
                (input.Pressed & PlayerButtons.Jump) != 0 && settings.JumpForce > 0;
            if (grounded && verticalVelocity <= 0)
                verticalVelocity = launching ? settings.JumpForce : -GroundStickSpeed;

            double vertical;
            if (grounded && !launching && verticalVelocity <= 0)
                vertical = -GroundStickSpeed * time;
            else
            {
                double fallingTime = settings.Gravity > 0
                    ? Math.Min(time, Math.Max(0, (verticalVelocity + MaximumFallSpeed) / settings.Gravity))
                    : time;
                vertical = verticalVelocity * fallingTime - 0.5 * settings.Gravity * fallingTime * fallingTime
                    - MaximumFallSpeed * (time - fallingTime);
                verticalVelocity = Math.Max(-MaximumFallSpeed, verticalVelocity - settings.Gravity * time);
            }

            double angularSpeed = input.Turn * settings.RotationSpeed * Math.PI / 180.0;
            double angle = angularSpeed * time;
            double distance = settings.Speed * input.Forward * time;
            double x = Math.Abs(angle) < 1e-8 ? distance * angle * 0.5 : distance * (1 - Math.Cos(angle)) / angle;
            double z = Math.Abs(angle) < 1e-8 ? distance : distance * Math.Sin(angle) / angle;
            return new MovementStep(x, vertical, z, input.Turn * settings.RotationSpeed * time);
        }
    }
}

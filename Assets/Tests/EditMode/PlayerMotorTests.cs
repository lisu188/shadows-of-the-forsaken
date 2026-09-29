using System;
using NUnit.Framework;
using ShadowsOfTheForsaken.Movement;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public class PlayerMotorTests
    {
        private static readonly MovementSettings Settings = new MovementSettings(5, 90, 9.81f, 5);

        [Test]
        public void NoInputProducesNoHorizontalMovement()
        {
            var step = new PlayerMotor().Step(Settings, default, true, 0.1f);
            Assert.That(step.LocalX, Is.Zero);
            Assert.That(step.LocalZ, Is.Zero);
            Assert.That(step.YawDegrees, Is.Zero);
            Assert.That(step.LocalY, Is.LessThan(0));
        }

        [TestCase(1f, 0.5f)]
        [TestCase(-1f, -0.5f)]
        [TestCase(0.5f, 0.25f)]
        public void ForwardAndBackwardMovementUseIndependentAxis(float forward, float expected)
        {
            var step = new PlayerMotor().Step(Settings, new MovementInput(0, forward, 0), true, 0.1f);
            Assert.That(step.LocalZ, Is.EqualTo(expected).Within(0.000001));
            Assert.That(step.LocalX, Is.Zero);
        }

        [Test]
        public void RotationDoesNotStrafeWithoutForwardInput()
        {
            var step = new PlayerMotor().Step(Settings, new MovementInput(1, 0, 0), true, 0.1f);
            Assert.That(step.YawDegrees, Is.EqualTo(9).Within(0.00001));
            Assert.That(step.LocalX, Is.Zero);
            Assert.That(step.LocalZ, Is.Zero);
        }

        [Test]
        public void GroundedJumpStartsWithExistingJumpForceAndAppliesGravityImmediately()
        {
            var motor = new PlayerMotor();
            var step = motor.Step(Settings, new MovementInput(0, 0, PlayerButtons.Jump), true, 0.1f);
            Assert.That(step.LocalY, Is.EqualTo(0.5 - 0.5 * 9.81 * 0.01).Within(0.000001));
            Assert.That(motor.VerticalVelocity, Is.EqualTo(5 - 9.81 * 0.1).Within(0.000001));
        }

        [Test]
        public void AirborneJumpCannotResetVerticalVelocity()
        {
            var motor = new PlayerMotor();
            motor.Step(Settings, default, false, 0.1f);
            motor.Step(Settings, new MovementInput(0, 0, PlayerButtons.Jump), false, 0.1f);
            Assert.That(motor.VerticalVelocity, Is.EqualTo(-1.962).Within(0.000001));
        }

        [Test]
        public void StaleGroundedFlagDoesNotRestartAscendingJump()
        {
            var motor = new PlayerMotor();
            motor.Step(Settings, new MovementInput(0, 0, PlayerButtons.Jump), true, 0.1f);
            motor.Step(Settings, new MovementInput(0, 0, PlayerButtons.Jump), true, 0.1f);
            Assert.That(motor.VerticalVelocity, Is.EqualTo(3.038).Within(0.00001));
        }

        [Test]
        public void CeilingImmediatelyCancelsAscent()
        {
            var motor = new PlayerMotor();
            motor.Step(Settings, new MovementInput(0, 0, PlayerButtons.Jump), true, 0.1f);
            motor.ResolveCollisions(false, true);
            Assert.That(motor.VerticalVelocity, Is.Zero);
            Assert.That(motor.Step(Settings, default, false, 0.1f).LocalY, Is.LessThan(0));
        }

        [Test]
        public void LandingResetsFallButCannotCancelAscent()
        {
            var motor = new PlayerMotor();
            motor.Step(Settings, default, false, 1);
            motor.ResolveCollisions(true, false);
            Assert.That(motor.VerticalVelocity, Is.EqualTo(-PlayerMotor.GroundStickSpeed));
            motor.Step(Settings, new MovementInput(0, 0, PlayerButtons.Jump), true, 0.1f);
            float before = motor.VerticalVelocity;
            motor.ResolveCollisions(true, false);
            Assert.That(motor.VerticalVelocity, Is.EqualTo(before));
        }

        [Test]
        public void FallingSpeedIsBoundedAndResetClearsIt()
        {
            var motor = new PlayerMotor();
            for (int i = 0; i < 100; i++) motor.Step(Settings, default, false, 1);
            Assert.That(motor.VerticalVelocity, Is.EqualTo(-PlayerMotor.MaximumFallSpeed));
            motor.Reset();
            Assert.That(motor.VerticalVelocity, Is.Zero);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void BallisticMotionIsFrameRateIndependent(int fps)
        {
            var motor = new PlayerMotor();
            double height = 0;
            for (int i = 0; i < fps; i++)
                height += motor.Step(Settings, new MovementInput(0, 0, i == 0 ? PlayerButtons.Jump : 0), i == 0, 1f / fps).LocalY;
            Assert.That(height, Is.EqualTo(5 - 9.81 * 0.5).Within(0.00001));
            Assert.That(motor.VerticalVelocity, Is.EqualTo(5 - 9.81).Within(0.00001));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void MovingWhileTurningFollowsTheSameArcAtEveryFrameRate(int fps)
        {
            var motor = new PlayerMotor();
            double x = 0, z = 0, heading = 0;
            for (int i = 0; i < fps; i++)
            {
                var step = motor.Step(Settings, new MovementInput(1, 1, 0), true, 1f / fps);
                x += Math.Cos(heading) * step.LocalX + Math.Sin(heading) * step.LocalZ;
                z += -Math.Sin(heading) * step.LocalX + Math.Cos(heading) * step.LocalZ;
                heading += step.YawDegrees * Math.PI / 180;
            }
            Assert.That(x, Is.EqualTo(10 / Math.PI).Within(0.00002));
            Assert.That(z, Is.EqualTo(10 / Math.PI).Within(0.00002));
            Assert.That(heading, Is.EqualTo(Math.PI / 2).Within(0.000002));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void TerminalVelocityIntegrationIsFrameRateIndependent(int fps)
        {
            var motor = new PlayerMotor();
            double height = 0;
            for (int i = 0; i < fps * 10; i++) height += motor.Step(Settings, default, false, 1f / fps).LocalY;
            double accelerating = PlayerMotor.MaximumFallSpeed / (double)Settings.Gravity;
            double expected = -0.5 * Settings.Gravity * accelerating * accelerating - PlayerMotor.MaximumFallSpeed * (10 - accelerating);
            Assert.That(height, Is.EqualTo(expected).Within(0.001));
        }

        [Test]
        public void ZeroTimeDoesNotConsumeJumpOrChangeVelocity()
        {
            var motor = new PlayerMotor();
            Assert.That(motor.Step(Settings, new MovementInput(1, 1, PlayerButtons.Jump), true, 0).LocalY, Is.Zero);
            Assert.That(motor.VerticalVelocity, Is.Zero);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(2f)]
        public void InvalidTimeIsRejectedWithoutMutation(float dt)
        {
            var motor = new PlayerMotor();
            Assert.Throws<ArgumentOutOfRangeException>(() => motor.Step(Settings, default, false, dt));
            Assert.That(motor.VerticalVelocity, Is.Zero);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(10001f)]
        public void InvalidSettingsAreRejected(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSettings(value, 90, 10, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSettings(5, value, 10, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSettings(5, 90, value, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSettings(5, 90, 10, value));
        }

        [Test]
        public void InputSanitizesAxesWithoutCouplingTurnAndForward()
        {
            var input = new MovementInput(4, -3, (PlayerButtons)255);
            Assert.That(input.Turn, Is.EqualTo(1));
            Assert.That(input.Forward, Is.EqualTo(-1));
            Assert.That(input.Pressed, Is.EqualTo(PlayerButtons.Jump | PlayerButtons.Attack | PlayerButtons.Interact));
            Assert.That(new MovementInput(float.NaN, float.PositiveInfinity, 0).Turn, Is.Zero);
            Assert.That(new MovementInput(float.NaN, float.PositiveInfinity, 0).Forward, Is.Zero);
        }

        [Test]
        public void HeldControlsCannotResumeBeforeNeutral()
        {
            var gate = new PlayerInputGate();
            Assert.That(gate.Sample(1, 1, PlayerButtons.Jump, PlayerButtons.Jump).Pressed, Is.EqualTo(PlayerButtons.None));
            Assert.That(gate.AwaitingNeutral, Is.True);
            gate.Sample(0, 0, 0, 0);
            Assert.That(gate.AwaitingNeutral, Is.False);
            Assert.That(gate.Sample(1, 1, PlayerButtons.Jump, PlayerButtons.Jump).Pressed, Is.EqualTo(PlayerButtons.Jump));
            gate.Suspend();
            var blocked = gate.Sample(1, 1, PlayerButtons.Jump, 0);
            Assert.That(blocked.Forward, Is.Zero);
            Assert.That(blocked.Pressed, Is.EqualTo(PlayerButtons.None));
            Assert.That(gate.AwaitingNeutral, Is.True);
        }

        [TestCase(PlayerButtons.Jump)]
        [TestCase(PlayerButtons.Attack)]
        [TestCase(PlayerButtons.Interact)]
        public void ButtonsFireOnceUntilRelease(PlayerButtons button)
        {
            var gate = new PlayerInputGate();
            gate.Sample(0, 0, 0, 0);
            Assert.That(gate.Sample(0, 0, button, button).Pressed, Is.EqualTo(button));
            Assert.That(gate.Sample(0, 0, button, button).Pressed, Is.EqualTo(PlayerButtons.None));
            gate.Sample(0, 0, 0, 0);
            Assert.That(gate.Sample(0, 0, button, button).Pressed, Is.EqualTo(button));
        }

        [Test]
        public void ACompleteTapBetweenFramesIsNotLost()
        {
            var gate = new PlayerInputGate();
            gate.Sample(0, 0, 0, 0);
            Assert.That(gate.Sample(0, 0, 0, PlayerButtons.Interact).Pressed, Is.EqualTo(PlayerButtons.Interact));
        }

        [Test]
        public void GateInstancesAndResetsDoNotShareState()
        {
            var first = new PlayerInputGate();
            var second = new PlayerInputGate();
            first.Sample(0, 0, 0, 0);
            Assert.That(first.AwaitingNeutral, Is.False);
            Assert.That(second.AwaitingNeutral, Is.True);
            first.Suspend();
            Assert.That(first.AwaitingNeutral, Is.True);
        }
    }
}

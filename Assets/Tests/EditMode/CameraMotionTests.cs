using System;
using NUnit.Framework;
using ShadowsOfTheForsaken.CameraRig.Core;

namespace ShadowsOfTheForsaken.Tests.EditMode
{
    public class CameraMotionTests
    {
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void StationaryTargetDampingIsFrameRateIndependent(int fps)
        {
            float position = -10f;
            for (int i = 0; i < fps; i++) position = CameraMotion.Damp(position, 5f, 2f, 1f / fps);
            Assert.That(position, Is.EqualTo(5.0 - 15.0 * Math.Exp(-2.0)).Within(0.00002));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void ReturningFromObstructionIsSmoothAndFrameRateIndependent(int fps)
        {
            float position = CameraMotion.CollisionLimit(5f, 1.05f, 0.05f);
            float previous = position;
            for (int i = 0; i < fps; i++)
            {
                position = CameraMotion.Damp(position, 5f, 2f, 1f / fps);
                Assert.That(position, Is.InRange(previous, 5f));
                previous = position;
            }
            Assert.That(position, Is.EqualTo(5.0 - 4.0 * Math.Exp(-2.0)).Within(0.00002));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void ObstructionSafetyIsNotDelayedByDamping(int fps)
        {
            float proposed = CameraMotion.Damp(5f, 5f, 0.01f, 1f / fps);
            Assert.That(CameraMotion.CollisionLimit(proposed, 1.05f, 0.05f), Is.EqualTo(1f).Within(0.000001));
        }

        [Test]
        public void IrregularTimeStepsComposeForAStationaryTarget()
        {
            float position = 0f;
            foreach (float dt in new[] { 0.01f, 0.04f, 0.15f, 0.3f, 0.5f })
                position = CameraMotion.Damp(position, 100f, 3f, dt);
            Assert.That(position, Is.EqualTo(CameraMotion.Damp(0f, 100f, 3f, 1f)).Within(0.00002));
        }

        [Test]
        public void ZeroTimeOrResponseDoesNotMove()
        {
            Assert.That(CameraMotion.Damp(3f, 10f, 2f, 0f), Is.EqualTo(3f));
            Assert.That(CameraMotion.Damp(3f, 10f, 0f, 2f), Is.EqualTo(3f));
        }

        [Test]
        public void LargeTimeStepDoesNotOvershoot()
        {
            Assert.That(CameraMotion.Damp(-2f, 5f, 100f, 100f), Is.EqualTo(5f));
        }

        [Test]
        public void ExtremeFiniteEndpointsDoNotOverflow()
        {
            float result = CameraMotion.Damp(float.MaxValue, -float.MaxValue, 2f, 0.5f);
            Assert.That(float.IsNaN(result) || float.IsInfinity(result), Is.False);
        }

        [Test]
        public void MissingHitDoesNotShortenRequestedDistance()
        {
            Assert.That(CameraMotion.CollisionLimit(5f, float.PositiveInfinity, 0.1f), Is.EqualTo(5f));
        }

        [TestCase(8f, 0.1f, 5f)]
        [TestCase(2f, 0.1f, 1.9f)]
        [TestCase(0.01f, 0.1f, 0f)]
        [TestCase(0f, 0f, 0f)]
        public void HitDistanceAndPaddingBoundCameraTravel(float hit, float padding, float expected)
        {
            Assert.That(CameraMotion.CollisionLimit(5f, hit, padding), Is.EqualTo(expected).Within(0.000001));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void RejectsInvalidTimeAndResponse(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraMotion.Blend(2f, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraMotion.Blend(value, 0.1f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void RejectsNonfinitePositions(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraMotion.Damp(value, 1f, 2f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraMotion.Damp(1f, value, 2f, 0.1f));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void RejectsInvalidRequestedDistanceAndPadding(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraMotion.CollisionLimit(value, 5f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraMotion.CollisionLimit(5f, 5f, value));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.NegativeInfinity)]
        public void RejectsInvalidHitDistance(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraMotion.CollisionLimit(5f, value, 0f));
        }
    }
}

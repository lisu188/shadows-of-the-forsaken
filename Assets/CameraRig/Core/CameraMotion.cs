using System;

namespace ShadowsOfTheForsaken.CameraRig.Core
{
    public static class CameraMotion
    {
        public static float Blend(float response, float deltaTime)
        {
            Nonnegative(response, nameof(response));
            Nonnegative(deltaTime, nameof(deltaTime));
            return (float)(1.0 - Math.Exp(-(double)response * deltaTime));
        }

        public static float Damp(float current, float target, float response, float deltaTime)
        {
            Finite(current, nameof(current));
            Finite(target, nameof(target));
            return (float)(current + ((double)target - current) * Blend(response, deltaTime));
        }

        public static float CollisionLimit(float requestedDistance, float hitDistance, float padding)
        {
            Nonnegative(requestedDistance, nameof(requestedDistance));
            Nonnegative(padding, nameof(padding));
            if (float.IsPositiveInfinity(hitDistance)) return requestedDistance;
            Nonnegative(hitDistance, nameof(hitDistance));
            return Math.Min(requestedDistance, Math.Max(0f, hitDistance - padding));
        }

        private static void Nonnegative(float value, string name)
        {
            Finite(value, name);
            if (value < 0f) throw new ArgumentOutOfRangeException(name);
        }

        private static void Finite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }
}

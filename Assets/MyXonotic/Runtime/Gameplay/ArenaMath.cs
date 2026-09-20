using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Pure, side-effect-free calculations used by movement, damage and weapons.
    /// Kept separate from MonoBehaviours specifically so they are easy to unit test
    /// without a running scene.
    /// </summary>
    public static class ArenaMath
    {
        /// Acceleration integrated over a bounded frame step, capped along wish direction.
        public static Vector3 Accelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            if (wishDir.sqrMagnitude < 0.0001f || dt <= 0f || accel <= 0f || wishSpeed <= 0f) return velocity;
            wishDir.Normalize();
            float currentSpeed = Vector3.Dot(velocity, wishDir);
            float addSpeed = wishSpeed - currentSpeed;
            if (addSpeed <= 0f) return velocity;
            float accelSpeed = accel * wishSpeed * dt;
            if (accelSpeed > addSpeed) accelSpeed = addSpeed;
            return velocity + wishDir * accelSpeed;
        }

        /// Ground friction affects horizontal speed without changing vertical velocity.
        public static Vector3 ApplyGroundFriction(Vector3 velocity, float friction, float stopSpeed, float dt)
        {
            float vertical = velocity.y;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            if (speed < 0.0001f) return new Vector3(0f, vertical, 0f);
            float control = speed < stopSpeed ? stopSpeed : speed;
            float drop = control * friction * dt;
            float newSpeed = Mathf.Max(speed - drop, 0f);
            Vector3 result = velocity * (newSpeed / speed);
            result.y = vertical;
            return result;
        }

        /// Splits raw damage between armor and health. Mutates armor by reference and
        /// returns the amount that should be subtracted from health.
        public static int ApplyArmor(int rawDamage, ref int armor, float absorbRatio)
        {
            if (rawDamage <= 0) return 0;
            if (armor <= 0 || absorbRatio <= 0f) return rawDamage;
            int absorbed = Mathf.Min(armor, Mathf.RoundToInt(rawDamage * Mathf.Clamp01(absorbRatio)));
            armor -= absorbed;
            return rawDamage - absorbed;
        }

        /// Linear falloff splash damage: full damage at distance 0, zero at/after radius.
        public static float SplashDamage(float distance, float radius, float maxDamage)
        {
            if (radius <= 0f) return distance <= 0f ? maxDamage : 0f;
            if (distance >= radius) return 0f;
            float t = 1f - Mathf.Clamp01(distance / radius);
            return maxDamage * t;
        }

        public static Vector3 KnockbackImpulse(Vector3 direction, float force)
        {
            if (direction.sqrMagnitude < 0.0001f) return Vector3.zero;
            return direction.normalized * force;
        }

        /// Knockback that fades with the same falloff curve as splash damage.
        public static Vector3 SplashKnockback(Vector3 fromCenterToTarget, float distance, float radius, float maxForce)
        {
            float scale = radius > 0f ? SplashDamage(distance, radius, 1f) : 1f;
            return KnockbackImpulse(fromCenterToTarget, maxForce * scale);
        }
    }
}

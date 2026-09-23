using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Runtime component for one imported Boil gameplay trigger volume
    /// (trigger_push / trigger_teleport / trigger_hurt). Built and configured
    /// by MyXonotic.EditorTools.BspGameplayImporter; nothing in this file
    /// reads a BSP directly.
    ///
    /// BOUNDS CAVEAT (read before trusting gameplay feel): the BoxCollider on
    /// this GameObject is the *axis-aligned bounding box* of the source brush
    /// model (BspModel.Mins/Maxs), not its actual brush volume. An L-shaped,
    /// wedge-shaped or rotated trigger brush will get a larger/smaller/offset
    /// box than the original, so trigger extents here are an original
    /// approximation, not a faithful re-derivation of Quake's brush-volume
    /// point-in-hull test. This is intentional scope for this slice (see
    /// AGENTS.md "Collision uses eligible face triangles, NOT full original
    /// brush volumes").
    ///
    /// All arc/velocity/damage math below is an independent, from-scratch
    /// approximation written for this project. No GPL Xonotic/Quake QuakeC
    /// (SV_PushEnt, trigger_push touch, trigger_hurt touch, etc.) was read
    /// or copied to write it.
    /// </summary>
    public sealed class MapTrigger : MonoBehaviour
    {
        public enum Kind
        {
            Push,
            Teleport,
            Hurt,
        }

        // --- Original-approximation tunables (no upstream value reused) ---

        /// Default apex height (Unity units) used when deriving a push/jump-pad arc.
        public const float DefaultPushApexHeight = 2.0f;

        /// Default hurt damage per tick when the source entity has no "dmg" key.
        /// dev.12: Xonotic's trigger_hurt defaults to 1000 (instant kill); the old
        /// value of 10 let players survive kill volumes under the maps for seconds.
        public const int DefaultHurtDamagePerTick = 1000;
        /// Pre-dev.12 imported markers carry 10 for "no dmg key"; treat as lethal at runtime.
        public const int LegacyDefaultHurtDamagePerTick = 10;

        /// Minimum seconds between two hurt applications to the same collider,
        /// so standing in a trigger_hurt volume does not deal damage every frame.
        public const float DefaultHurtTickInterval = 0.5f;

        /// Minimum seconds between two teleports of the same collider, guarding
        /// against an immediate re-trigger if the destination itself sits inside
        /// (or very near) another teleport/push volume.
        public const float DefaultReentryGuardSeconds = 0.5f;

        [Tooltip("Which Boil trigger classname this volume approximates.")]
        public Kind TriggerKind;

        [Tooltip("Originating entity classname, kept for diagnostics (e.g. Inspector, logs).")]
        public string SourceClass;

        [Tooltip("True if the source entity's 'target' key resolved to a target_position / " +
                 "misc_teleporter_dest entity with a parsable origin. Push/teleport triggers " +
                 "with no resolved destination are inert by design (no invented destination).")]
        public bool HasDestination;

        [Tooltip("Destination position in Unity world units (already importer-converted).")]
        public Vector3 Destination;

        [Tooltip("Destination facing yaw in Unity's Y-Euler convention (already importer-converted).")]
        public float DestinationYaw;

        public float PushApexHeight = DefaultPushApexHeight;
        public int HurtDamagePerTick = DefaultHurtDamagePerTick;
        public float HurtTickInterval = DefaultHurtTickInterval;
        public float ReentryGuardSeconds = DefaultReentryGuardSeconds;

        readonly Dictionary<Collider, float> _hurtNextAllowedTime = new Dictionary<Collider, float>();
        readonly Dictionary<Collider, float> _teleportGuardUntil = new Dictionary<Collider, float>();

        void OnTriggerEnter(Collider other)
        {
            Handle(other);
        }

        void OnTriggerStay(Collider other)
        {
            // Only trigger_hurt needs repeated application while the actor stays
            // inside the volume; push/teleport act once per entry.
            if (TriggerKind == Kind.Hurt)
            {
                Handle(other);
            }
        }

        void Handle(Collider other)
        {
            if (ArenaBootstrap.IsPaused) return;

            var actor = other.GetComponentInParent<Actor>();
            if (actor == null || actor.IsDead) return;

            switch (TriggerKind)
            {
                case Kind.Push:
                    HandlePush(other, actor);
                    break;
                case Kind.Teleport:
                    HandleTeleport(other, actor);
                    break;
                case Kind.Hurt:
                    HandleHurt(other, actor);
                    break;
            }
        }

        void HandlePush(Collider other, Actor actor)
        {
            if (!HasDestination) return;

            var cc = other.GetComponentInParent<CharacterController>();
            if (cc == null) return;

            Vector3 start = cc.transform.position;
            float gravityMagnitude = Mathf.Abs(actor.GetComponent<Player>() != null ? Player.Gravity : Bot.Gravity);
            Vector3 launchVelocity = ComputeArcVelocity(start, Destination, PushApexHeight, gravityMagnitude);

            var player = actor.GetComponent<Player>();
            if (player != null)
            {
                // Requires Player.Launch(Vector3 velocity): replaces the actor's
                // velocity outright (and clears any decaying external impulse)
                // instead of accumulating into ApplyExternalImpulse's separate,
                // ~1/6s-decaying impulse term. That distinction matters for a
                // jump pad: a real trigger_push snaps velocity to the launch arc
                // immediately, it does not blend it into whatever velocity/impulse
                // the player already had. Player.cs is owned by another worker in
                // this task and is not edited here; if Launch(Vector3) is not yet
                // present when this file is built, this call will fail to compile
                // until it lands (see BspGameplayImporter.cs header for the exact
                // wiring/ownership split).
                player.Launch(launchVelocity);
            }
            else
            {
                var bot = actor.GetComponent<Bot>();
                if (bot != null)
                {
                    bot.Launch(launchVelocity);
                }
            }
        }

        void HandleTeleport(Collider other, Actor actor)
        {
            if (!HasDestination) return;

            if (_teleportGuardUntil.TryGetValue(other, out float guardUntil) && Time.time < guardUntil)
            {
                return;
            }

            var cc = other.GetComponentInParent<CharacterController>();
            if (cc == null) return;

            _teleportGuardUntil[other] = Time.time + ReentryGuardSeconds;

            // Destination is a center-based target_position/misc_teleporter_dest
            // origin, the same convention BspSpawnPoint carries. ContentBridge's
            // FindBspSpawnPoints subtracts 24/32 from Y to turn that center-based
            // origin into a feet position for our feet-based CharacterController;
            // apply the same offset here instead of inventing a new convention, so
            // a teleported actor lands standing on the floor rather than partly
            // sunk into it or hovering.
            Vector3 feetDestination = Destination - Vector3.up * (24f / 32f);

            // Disable/move/re-enable synchronously, all inside this single
            // OnTriggerEnter call (no coroutine/yield): trigger callbacks run
            // during the physics step, which happens before this frame's
            // Update(). Leaving the controller disabled across a yielded frame
            // would let Player.Update() call CharacterController.Move() on a
            // still-disabled controller later in the same frame, which Unity
            // logs as an error. Doing everything here means the controller is
            // already repositioned and re-enabled before any Move() call runs.
            cc.enabled = false;
            cc.transform.position = feetDestination;
            Physics.SyncTransforms();
            cc.enabled = true;

            var player = actor.GetComponent<Player>();
            if (player != null)
            {
                // ResetForRespawn also zeroes velocity/impulse and view pitch, which is
                // correct here too: an original approximation of "arrive clean", not an
                // upstream-specified teleporter behavior (Xonotic's teleporter QC is not
                // reused here).
                player.ResetForRespawn(DestinationYaw);
            }
            else
            {
                var bot = actor.GetComponent<Bot>();
                if (bot != null)
                {
                    bot.ResetMotion();
                    actor.transform.rotation = Quaternion.Euler(0f, DestinationYaw, 0f);
                }
            }
        }

        void HandleHurt(Collider other, Actor actor)
        {
            float now = Time.time;
            if (_hurtNextAllowedTime.TryGetValue(other, out float nextAllowed) && now < nextAllowed)
            {
                return;
            }
            _hurtNextAllowedTime[other] = now + HurtTickInterval;

            // No knockback direction is derived from the trigger volume itself
            // (a bounding box has no meaningful "hurt surface normal" without the
            // real brush faces); environmental damage, no instigator.
            int dmg = HurtDamagePerTick <= LegacyDefaultHurtDamagePerTick ? DefaultHurtDamagePerTick : HurtDamagePerTick;
            actor.TakeDamage(dmg, Vector3.zero, null);
        }

        /// <summary>
        /// Original from-scratch projectile-motion derivation for a jump-pad-style
        /// launch: choose a vertical launch speed that reaches <paramref name="apexHeight"/>
        /// above the higher of start/destination, solve the resulting flight time to
        /// redescend to the destination's height, and derive the horizontal speed
        /// needed to cover the horizontal distance in that time. This is standard,
        /// generic kinematics (v^2 = 2*g*h, y(t) = v*t - 1/2*g*t^2), not sourced from
        /// any Quake/Xonotic engine or QuakeC implementation.
        /// </summary>
        public static Vector3 ComputeArcVelocity(Vector3 start, Vector3 destination, float apexHeight, float gravityMagnitude)
        {
            float g = Mathf.Max(gravityMagnitude, 0.01f);
            float deltaY = destination.y - start.y;
            // Apex must be above both the start and the destination for the
            // solve below to have a real (non-negative) descend time.
            float apexAboveStart = Mathf.Max(apexHeight, deltaY + 0.1f, 0.1f);

            float launchSpeed = Mathf.Sqrt(2f * g * apexAboveStart);
            float apexAboveDestination = Mathf.Max(apexAboveStart - deltaY, 0f);

            float timeUp = launchSpeed / g;
            float timeDown = Mathf.Sqrt(2f * apexAboveDestination / g);
            float totalTime = Mathf.Max(timeUp + timeDown, 0.05f);

            Vector3 horizontalDelta = new Vector3(destination.x - start.x, 0f, destination.z - start.z);
            Vector3 horizontalVelocity = horizontalDelta / totalTime;

            return horizontalVelocity + Vector3.up * launchSpeed;
        }
    }
}

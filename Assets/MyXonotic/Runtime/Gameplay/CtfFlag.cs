using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// One CTF flag. Lives at its stand (<see cref="Home"/>); an enemy touching
    /// it picks it up (carried above the head); the carrier dying drops it where
    /// they fell; a teammate touching a dropped flag returns it (or it returns by
    /// itself after <see cref="ReturnAfterSeconds"/>); the carrier touching their
    /// OWN flag while it is at home scores a capture. Classic rules approximated
    /// from the public Xonotic CTF description; no engine code.
    /// </summary>
    public sealed class CtfFlag : MonoBehaviour
    {
        public enum FlagState { AtBase, Carried, Dropped }

        public const float TouchRadius = 1.2f;
        public const float ReturnAfterSeconds = 30f;

        public Team Team;
        public Vector3 Home;
        public FlagState State { get; private set; } = FlagState.AtBase;
        public Actor Carrier { get; private set; }
        public float DroppedTimer { get; private set; }

        /// (flag, actor) events for HUD/sounds.
        public static event System.Action<CtfFlag, Actor> Taken;
        public static event System.Action<CtfFlag, Actor> Dropped;
        public static event System.Action<CtfFlag, Actor> Returned;
        public static event System.Action<CtfFlag, Actor> Captured;

        static readonly List<CtfFlag> All = new List<CtfFlag>();
        public static IReadOnlyList<CtfFlag> Flags => All;

        public static CtfFlag ForTeam(Team team)
        {
            foreach (var f in All) if (f != null && f.Team == team) return f;
            return null;
        }

        /// Flag currently carried by <paramref name="actor"/>, or null.
        public static CtfFlag CarriedBy(Actor actor)
        {
            foreach (var f in All) if (f != null && f.State == FlagState.Carried && f.Carrier == actor) return f;
            return null;
        }

        public static int Captures(Team team) => team == Team.Red ? RedCaptures : team == Team.Blue ? BlueCaptures : 0;
        public static int RedCaptures { get; private set; }
        public static int BlueCaptures { get; private set; }
        public static void ResetScores() { RedCaptures = 0; BlueCaptures = 0; }

        Transform _visual;
        float _spin;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        void OnDestroy() { All.Remove(this); }

        public void Init(Team team, Vector3 home, Transform visual)
        {
            if (!All.Contains(this)) All.Add(this); // edit-mode tests never get OnEnable
            Team = team;
            Home = home;
            _visual = visual;
            ResetToBase();
        }

        public void ResetToBase()
        {
            State = FlagState.AtBase;
            Carrier = null;
            DroppedTimer = 0f;
            transform.position = Home;
            if (_visual != null) _visual.gameObject.SetActive(true);
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            Step(Time.deltaTime);
        }

        /// Frame step (plain method for tests): follow carrier, time out drops, poll touches.
        public void Step(float dt)
        {
            _spin += 90f * dt;
            if (State == FlagState.Carried)
            {
                if (Carrier == null || Carrier.IsDead) { Drop(Carrier != null ? Carrier.transform.position : transform.position, Carrier); return; }
                transform.position = Carrier.transform.position + Vector3.up * 2.2f;
                if (_visual != null) _visual.localRotation = Quaternion.Euler(0f, _spin, 0f);
                return;
            }
            if (State == FlagState.Dropped)
            {
                DroppedTimer -= dt;
                if (DroppedTimer <= 0f) { Return(null); return; }
            }
            if (_visual != null) _visual.localRotation = Quaternion.Euler(0f, _spin, 0f);
            foreach (var a in GameState.Actors)
            {
                if (a == null || a.IsDead || a.Team == Team.None) continue;
                Vector3 p = a.transform.position + Vector3.up * 0.9f;
                if ((p - transform.position).sqrMagnitude > TouchRadius * TouchRadius) continue;
                if (Touch(a)) return;
            }
        }

        /// Applies the touch rules for <paramref name="actor"/>; returns true when the state changed.
        public bool Touch(Actor actor)
        {
            if (actor == null || actor.IsDead || actor.Team == Team.None) return false;
            if (actor.Team != Team)
            {
                if (State == FlagState.Carried) return false;
                // Enemy takes the flag (from base or from the ground).
                State = FlagState.Carried;
                Carrier = actor;
                Taken?.Invoke(this, actor);
                return true;
            }
            // Own team.
            if (State == FlagState.Dropped) { Return(actor); return true; }
            if (State == FlagState.AtBase)
            {
                var carried = CarriedBy(actor);
                if (carried != null && carried != this)
                {
                    carried.CaptureBy(actor);
                    return true;
                }
            }
            return false;
        }

        void CaptureBy(Actor scorer)
        {
            if (scorer.Team == Team.Red) RedCaptures++; else if (scorer.Team == Team.Blue) BlueCaptures++;
            Captured?.Invoke(this, scorer);
            ResetToBase();
        }

        public void Drop(Vector3 at, Actor dropper)
        {
            State = FlagState.Dropped;
            Carrier = null;
            DroppedTimer = ReturnAfterSeconds;
            // Keep the flag on solid ground near the drop point.
            Vector3 pos = at + Vector3.up * 0.5f;
            if (Physics.Raycast(pos + Vector3.up, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point + Vector3.up * 0.1f;
            transform.position = pos;
            Dropped?.Invoke(this, dropper);
        }

        public void Return(Actor returner)
        {
            ResetToBase();
            Returned?.Invoke(this, returner);
        }
    }
}

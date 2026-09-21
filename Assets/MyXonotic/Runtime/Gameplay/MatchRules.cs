using System;
using System.Collections.Generic;

namespace MyXonotic.Gameplay
{
    /// <summary>
    /// Lifecycle of one Deathmatch session. <see cref="Over"/> is terminal: once
    /// reached the only way out is <see cref="MatchRules{TParticipant}.Reset"/>
    /// followed by a fresh <see cref="MatchRules{TParticipant}.StartMatch"/>.
    /// </summary>
    public enum MatchPhase
    {
        NotStarted,
        Running,
        Paused,
        Over
    }

    /// <summary>Why a match ended. <see cref="None"/> only appears before a match is Over.</summary>
    public enum MatchEndReason
    {
        None,
        FragLimit,
        TimeLimit
    }

    /// <summary>
    /// Session limits. A value &lt;= 0 disables that limit ("zero/invalid limit"
    /// means "unbounded", never a crash or an instant match-over). At least one
    /// limit should normally be positive for the match to end on its own, but a
    /// session with both limits disabled is valid: it simply never auto-finishes
    /// (a parent could still decide to stop it through other means, outside this
    /// type's scope).
    /// </summary>
    public struct MatchConfig
    {
        public int FragLimit;
        public float TimeLimitSeconds;

        public static readonly MatchConfig Unbounded = default(MatchConfig);
    }

    /// <summary>
    /// Immutable snapshot produced exactly once, when a match transitions into
    /// <see cref="MatchPhase.Over"/>. <see cref="Winner"/> is null for a tie
    /// (two or more participants sharing the highest recorded frag total).
    /// </summary>
    public sealed class MatchResult<TParticipant> where TParticipant : class
    {
        public MatchEndReason Reason { get; }
        public TParticipant Winner { get; }
        public float ElapsedSeconds { get; }
        public bool IsTie => Winner == null;

        internal MatchResult(MatchEndReason reason, TParticipant winner, float elapsedSeconds)
        {
            Reason = reason;
            Winner = winner;
            ElapsedSeconds = elapsedSeconds;
        }
    }

    /// <summary>
    /// Pure, deterministic, UnityEngine-free offline Deathmatch state machine.
    /// Not a full Xonotic ruleset: exactly one mode (frag-limit and/or
    /// time-limit Deathmatch), no teams, no weapons/pickups/network logic of
    /// any kind. <typeparamref name="TParticipant"/> is any reference type the
    /// caller uses to identify a scoring participant (a real game normally
    /// passes its own Actor/player type; the standalone test suite passes a
    /// small plain-C# stand-in so this file can be compiled and tested with a
    /// bare `mcs`/`mono`, no Unity install required).
    ///
    /// This type never invents a score: callers report the CURRENT authoritative
    /// frag total via <see cref="ReportFrags"/> after their own scoring code has
    /// already mutated it. It only watches for the frag limit and decides when
    /// the match is over; it must never add to anyone's frags itself.
    /// </summary>
    public sealed class MatchRules<TParticipant> where TParticipant : class
    {
        public MatchPhase Phase { get; private set; } = MatchPhase.NotStarted;
        public MatchConfig Config { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public MatchResult<TParticipant> Result { get; private set; }

        public bool IsRunning => Phase == MatchPhase.Running;
        public bool IsPaused => Phase == MatchPhase.Paused;
        public bool IsOver => Phase == MatchPhase.Over;

        /// Raised exactly once per match, the moment it becomes Over.
        public event Action<MatchResult<TParticipant>> MatchOver;

        readonly Dictionary<TParticipant, int> _frags = new Dictionary<TParticipant, int>();

        /// Starts a session. Only valid from <see cref="MatchPhase.NotStarted"/>
        /// (the phase a freshly constructed instance starts in, and the phase
        /// <see cref="Reset"/> returns to): a Running/Paused/Over match is a
        /// live or terminal state and must be explicitly cleared with
        /// <see cref="Reset"/> first — this method never silently bypasses a
        /// live or terminal match, it throws <see cref="InvalidOperationException"/>
        /// instead. Known participants are seeded at 0 frags; unknown ones are
        /// added lazily by <see cref="ReportFrags"/> if they ever score.
        public void StartMatch(MatchConfig config, IEnumerable<TParticipant> participants)
        {
            if (Phase != MatchPhase.NotStarted)
            {
                throw new InvalidOperationException(
                    "MatchRules.StartMatch called while Phase == " + Phase +
                    "; call Reset() first (StartMatch never implicitly discards a live or terminal match).");
            }
            Config = Sanitize(config);
            _frags.Clear();
            if (participants != null)
            {
                foreach (var participant in participants)
                {
                    if (participant != null) _frags[participant] = 0;
                }
            }
            ElapsedSeconds = 0f;
            Result = null;
            Phase = MatchPhase.Running;
        }

        /// A limit is only honoured when it is a strictly positive, finite
        /// number; NaN, +/-Infinity, zero or negative all sanitize to 0
        /// ("disabled"), never to a crash or an instant match-over.
        static MatchConfig Sanitize(MatchConfig config) => new MatchConfig
        {
            FragLimit = config.FragLimit > 0 ? config.FragLimit : 0,
            TimeLimitSeconds = IsFinitePositive(config.TimeLimitSeconds) ? config.TimeLimitSeconds : 0f
        };

        static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        /// Toggles the Running/Paused phases. A no-op once the match is Over or
        /// before it has started; those phases are not affected by pausing.
        public void SetPaused(bool paused)
        {
            if (paused && Phase == MatchPhase.Running) Phase = MatchPhase.Paused;
            else if (!paused && Phase == MatchPhase.Paused) Phase = MatchPhase.Running;
        }

        /// Advances the match clock by <paramref name="dt"/> seconds. Only has
        /// any effect while <see cref="Phase"/> is Running (paused/over/not-started
        /// elapse no time: "pause-aware elapsed time"). A non-finite (NaN or
        /// +/-Infinity) or non-positive dt is rejected outright and never
        /// touches the timer, so a single bad frame delta cannot corrupt it.
        /// If the time limit is reached, <see cref="ElapsedSeconds"/> is
        /// clamped exactly to the configured limit (never left overshooting
        /// it) before the result is recorded. Returns true exactly when this
        /// call caused the match to become Over (time limit reached).
        public bool AdvanceTime(float dt)
        {
            if (Phase != MatchPhase.Running) return false;
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return false;

            float candidate = ElapsedSeconds + dt;
            if (float.IsNaN(candidate) || float.IsInfinity(candidate))
            {
                // Adding this dt would overflow/corrupt the timer; ignore the
                // step entirely rather than latch a broken value.
                return false;
            }
            ElapsedSeconds = candidate;

            if (Config.TimeLimitSeconds > 0f && ElapsedSeconds >= Config.TimeLimitSeconds)
            {
                ElapsedSeconds = Config.TimeLimitSeconds;
                Finish(MatchEndReason.TimeLimit, null);
                return true;
            }
            return false;
        }

        /// Records the CURRENT total frags for one participant (never adds to
        /// it) and checks the frag limit. Call this after your own scoring code
        /// has already updated the authoritative count, once per relevant
        /// participant per death, so scores are never double-counted here.
        /// Returns true exactly when this call caused the match to become Over.
        public bool ReportFrags(TParticipant participant, int currentFragTotal)
        {
            if (Phase != MatchPhase.Running || participant == null) return false;
            _frags[participant] = currentFragTotal;
            if (Config.FragLimit > 0 && currentFragTotal >= Config.FragLimit)
            {
                Finish(MatchEndReason.FragLimit, participant);
                return true;
            }
            return false;
        }

        public int GetFrags(TParticipant participant) =>
            participant != null && _frags.TryGetValue(participant, out int value) ? value : 0;

        void Finish(MatchEndReason reason, TParticipant forcedWinner)
        {
            // Terminal and idempotent: "no repeated completion". Once Over,
            // nothing above can call Finish again anyway (both callers guard on
            // Phase == Running first), but keep this defensive so Result/the
            // event can never fire twice even if that guard were ever loosened.
            if (Phase == MatchPhase.Over) return;
            Phase = MatchPhase.Over;
            TParticipant winner = forcedWinner ?? ComputeLeader();
            Result = new MatchResult<TParticipant>(reason, winner, ElapsedSeconds);
            MatchOver?.Invoke(Result);
        }

        TParticipant ComputeLeader()
        {
            TParticipant best = null;
            int bestFrags = int.MinValue;
            bool tie = false;
            foreach (var entry in _frags)
            {
                if (entry.Value > bestFrags)
                {
                    bestFrags = entry.Value;
                    best = entry.Key;
                    tie = false;
                }
                else if (entry.Value == bestFrags)
                {
                    tie = true;
                }
            }
            return tie ? null : best;
        }

        /// Clears all session state back to NotStarted (elapsed time, result,
        /// recorded frags). Call this before StartMatch for a clean restart;
        /// StartMatch alone already resets everything a *new* match needs, so
        /// Reset() is mainly useful to explicitly drop Result/Phase between an
        /// old Over match and a caller's decision to start a new one.
        public void Reset()
        {
            Phase = MatchPhase.NotStarted;
            ElapsedSeconds = 0f;
            Result = null;
            _frags.Clear();
        }
    }
}

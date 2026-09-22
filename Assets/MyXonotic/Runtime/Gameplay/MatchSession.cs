using System;
using System.Collections.Generic;

namespace MyXonotic.Gameplay
{
    /// <summary>
    /// Unity-facing façade that wires the pure <see cref="MatchRules{TParticipant}"/>
    /// (<c>MatchRules&lt;Actor&gt;</c>) Deathmatch state machine to the EXISTING
    /// <see cref="Actor"/> / <see cref="GameState"/> scoring conventions, without
    /// changing Actor, GameState, ArenaBootstrap, Hud, Player or WeaponController.
    ///
    /// Scoring is never duplicated: <c>Actor.Die()</c> (unmodified) is the only
    /// place that mutates <c>Actor.Frags</c> — it already does
    /// <c>killer.Frags++</c> on a normal kill and <c>victim.Frags--</c> when
    /// <c>killer == null</c> or <c>killer == victim</c> (suicide/environmental
    /// death), BEFORE its <c>Died</c> event (and so <see cref="GameState.AnyDeath"/>)
    /// fires. This class only READS the already-updated <c>Actor.Frags</c> from
    /// inside its <see cref="GameState.AnyDeath"/> handler and forwards the
    /// current total into <see cref="MatchRules{TParticipant}.ReportFrags"/>; it
    /// performs no frag arithmetic of its own, so a kill or suicide is scored
    /// exactly once regardless of MatchSession being attached.
    ///
    /// Not a MonoBehaviour on purpose: this class has no scene/GameObject
    /// requirement, so a parent integration can own exactly one instance
    /// however it likes (a field on ArenaBootstrap, a manually driven
    /// singleton, or its own small MonoBehaviour wrapper) without this file
    /// dictating that choice. See docs/MATCH_SESSION.md for the full contract
    /// and exact parent integration hooks.
    ///
    /// Kept deliberately lean/test-free: real-Actor integration self-tests
    /// live in the Editor-only
    /// <c>MyXonotic.EditorTools.MatchSessionRegressionTests.RunSelfTests()</c>
    /// (Assets/MyXonotic/Editor/Tests/), never shipped in a player build.
    /// </summary>
    public sealed class MatchSession
    {
        /// The underlying pure state machine. Exposed directly for callers who
        /// want lower-level access (e.g. GetFrags(actor)); the members below are
        /// convenience passthroughs plus the real-Actor scoring glue.
        public MatchRules<Actor> Rules { get; } = new MatchRules<Actor>();

        public MatchPhase Phase => Rules.Phase;
        public bool IsRunning => Rules.IsRunning;
        public bool IsPaused => Rules.IsPaused;
        public bool IsOver => Rules.IsOver;
        public float ElapsedSeconds => Rules.ElapsedSeconds;
        public MatchResult<Actor> Result => Rules.Result;

        /// Raised exactly once, the moment the match becomes Over. Use this to
        /// freeze input/HUD and show the result view; the match stays frozen
        /// (Phase == Over, ElapsedSeconds/Result stable) until Restart().
        public event Action<MatchResult<Actor>> MatchOver
        {
            add => Rules.MatchOver += value;
            remove => Rules.MatchOver -= value;
        }

        bool _subscribed;

        /// Score reported for an actor: personal frags by default; team modes
        /// install a team total (TDM: team frags, CTF: team captures) so the
        /// "frag limit" becomes the team/capture limit. Never null.
        public Func<Actor, int> ScoreOf = a => a != null ? a.Frags : 0;

        /// Re-reports <paramref name="actor"/>'s current score (e.g. after a CTF capture).
        public void ReportScore(Actor actor)
        {
            if (!Rules.IsRunning || actor == null) return;
            Rules.ReportFrags(actor, ScoreOf(actor));
        }

        // ------------------------------------------------------------- hooks

        /// Hook 1: start a match. Subscribes to GameState.AnyDeath (only after
        /// MatchRules accepts the transition) so kills/suicides reported
        /// anywhere in the scene are scored automatically. Only valid from a
        /// fresh instance or after <see cref="Restart"/>/an explicit
        /// <c>Rules.Reset()</c>; calling this again while Running/Paused/Over
        /// throws <see cref="InvalidOperationException"/> instead of silently
        /// discarding the live/terminal match — call <see cref="Restart"/> to
        /// begin a new match instead.
        public void StartMatch(MatchConfig config, IEnumerable<Actor> participants)
        {
            // Rules.StartMatch throws first if the transition is invalid, so a
            // rejected call never leaves a dangling GameState subscription.
            Rules.StartMatch(config, participants);
            Subscribe();
        }

        /// Hook 2a: sync the pause state immediately. Call this the instant the
        /// parent's own pause state changes (e.g. from whatever toggles
        /// ArenaBootstrap.IsPaused), separately from — and before — any death
        /// processing for that same frame/tick. Keeping this decoupled from
        /// AdvanceTime means an unpause takes effect immediately: a death that
        /// happens the instant after unpausing, before the next AdvanceTime
        /// call, is still scored (Phase is already Running again), not ignored.
        public void SetPaused(bool paused) => Rules.SetPaused(paused);

        /// Hook 2b: advance the match clock. Call once per frame/tick with a
        /// non-negative delta (e.g. Time.deltaTime); time elapses only while
        /// Running (call <see cref="SetPaused"/> first to reflect the current
        /// pause state — this method does not take a pause flag itself, so a
        /// pause/unpause is never deferred until the next AdvanceTime call).
        /// Returns true exactly when this call ended the match (time limit
        /// reached).
        public bool AdvanceTime(float dt) => Rules.AdvanceTime(dt);

        /// Convenience overload: syncs pause state via <see cref="SetPaused"/>
        /// and then advances time in one call. Prefer calling
        /// <see cref="SetPaused"/> directly and separately whenever the pause
        /// state changes (see its remarks) if a death could be reported
        /// between the pause change and the next per-frame AdvanceTime call.
        public bool AdvanceTime(float dt, bool isPaused)
        {
            SetPaused(isPaused);
            return AdvanceTime(dt);
        }

        /// Hook 3: scores after death. GameState.AnyDeath already carries
        /// (victim, killer); this is wired up automatically by StartMatch, but
        /// is exposed publicly in case a parent needs to feed a death through
        /// deliberately (e.g. a unit test without a live GameState subscription).
        public void ReportDeath(Actor victim, Actor killer)
        {
            if (!Rules.IsRunning) return;
            if (victim != null) Rules.ReportFrags(victim, ScoreOf(victim));
            if (killer != null && killer != victim) Rules.ReportFrags(killer, ScoreOf(killer));
        }

        /// Hook 4: match-over freeze / result view. Poll IsOver/Result, or
        /// subscribe to MatchOver, from HUD/input code to freeze gameplay and
        /// render frag counts, elapsed time, reason (FragLimit/TimeLimit) and
        /// winner (Result.Winner == null means a tie).
        // (No extra members needed: IsOver/Result/MatchOver above are hook 4.)

        /// Hook 5: clean restart. Unsubscribes the old session, clears all
        /// state, then starts a brand-new match. Safe to call at any Phase,
        /// including Over (the intended way out of a terminal match) and
        /// mid-match (an abrupt/early restart) — Reset() first is what makes
        /// the following StartMatch valid.
        public void Restart(MatchConfig config, IEnumerable<Actor> participants)
        {
            StopListening();
            Rules.Reset();
            StartMatch(config, participants);
        }

        /// Unsubscribes from GameState.AnyDeath without starting a new match.
        /// Call when tearing down a session for good (e.g. scene unload) to
        /// avoid a stale subscription outliving the match.
        public void StopListening()
        {
            if (!_subscribed) return;
            GameState.AnyDeath -= ReportDeath;
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            GameState.AnyDeath += ReportDeath;
            _subscribed = true;
        }
    }
}

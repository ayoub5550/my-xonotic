using System;
using MyXonotic.Gameplay;

/// <summary>
/// Standalone (non-Unity) test driver for the pure MatchRules&lt;T&gt; Deathmatch
/// state machine (Assets/MyXonotic/Runtime/Gameplay/MatchRules.cs). Compiles
/// and runs with plain `mcs`/`mono`, no Unity install required, because
/// MatchRules.cs itself has no UnityEngine dependency. Uses a tiny plain-C#
/// stand-in participant type instead of the real Actor (which does need
/// UnityEngine) — see Assets/MyXonotic/Runtime/Gameplay/MatchSession.cs and
/// MatchSession.RunSelfTests() for the real Actor/GameState integration
/// coverage (suicide/no-killer scoring, no double count), which needs Unity
/// and is run separately.
///
/// Exit code 0 = all checks passed; non-zero = first failure's message is
/// printed to stderr.
/// </summary>
public static class MatchRulesTests
{
    private static int _checks;

    private sealed class Fighter
    {
        public readonly string Name;
        public Fighter(string name) { Name = name; }
        public override string ToString() => Name;
    }

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static int Main(string[] args)
    {
        try
        {
            ZeroAndInvalidLimitsNeverAutoFinish();
            TimeLimitTieHasNoWinner();
            FragLimitEndsMatchImmediately();
            PauseFreezesElapsedTime();
            ResetThenStartMatchIsClean();
            TerminalStateIsStableAndCompletionIsNotRepeated();
            NegativeOrZeroDtIsIgnored();
            UnknownParticipantIsTrackedLazily();
            NonFiniteDtIsRejectedAndNeverCorruptsTimer();
            NonFiniteConfigSanitizesToDisabled();
            ElapsedTimeIsClampedExactlyToTheTimeLimit();
            StartMatchWhileRunningOrOverThrows();

            Console.WriteLine($"MatchRulesTests: {_checks} checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static void ZeroAndInvalidLimitsNeverAutoFinish()
    {
        var a = new Fighter("A");
        var b = new Fighter("B");
        var rules = new MatchRules<Fighter>();

        // Zero AND negative limits must both mean "disabled", never a crash
        // or an instant match-over.
        rules.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = -5f }, new[] { a, b });
        Assert(rules.IsRunning, "zero/negative limits must still start a running match");

        for (int i = 0; i < 50; i++) rules.AdvanceTime(10f);
        Assert(rules.IsRunning, "disabled time limit must never auto-finish the match");

        for (int i = 0; i < 1000; i++) rules.ReportFrags(a, i);
        Assert(rules.IsRunning, "disabled frag limit must never auto-finish the match");
        Assert(rules.Result == null, "an unbounded match must have no result while running");
    }

    static void TimeLimitTieHasNoWinner()
    {
        var a = new Fighter("A");
        var b = new Fighter("B");
        var rules = new MatchRules<Fighter>();
        rules.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = 10f }, new[] { a, b });

        rules.ReportFrags(a, 3);
        rules.ReportFrags(b, 3);
        bool ended = rules.AdvanceTime(11f);

        Assert(ended, "reaching the time limit must end the match on that call");
        Assert(rules.IsOver, "phase must be Over once the time limit passes");
        Assert(rules.Result.Reason == MatchEndReason.TimeLimit, "end reason must be TimeLimit");
        Assert(rules.Result.Winner == null, "equal frags at time limit must be a tie (no winner)");
        Assert(rules.Result.IsTie, "IsTie must be true for a tied result");
    }

    static void FragLimitEndsMatchImmediately()
    {
        var a = new Fighter("A");
        var b = new Fighter("B");
        var rules = new MatchRules<Fighter>();
        int overCount = 0;
        rules.MatchOver += _ => overCount++;
        rules.StartMatch(new MatchConfig { FragLimit = 5, TimeLimitSeconds = 120f }, new[] { a, b });

        rules.AdvanceTime(1f);
        for (int i = 1; i <= 4; i++)
        {
            bool ended = rules.ReportFrags(a, i);
            Assert(!ended, "must not end before the frag limit is actually reached");
        }
        bool finalEnd = rules.ReportFrags(a, 5);

        Assert(finalEnd, "reaching the frag limit exactly must end the match on that call");
        Assert(rules.IsOver, "phase must be Over once the frag limit is reached");
        Assert(rules.Result.Reason == MatchEndReason.FragLimit, "end reason must be FragLimit");
        Assert(rules.Result.Winner == a, "the participant who reached the frag limit must be declared the winner");
        Assert(overCount == 1, "MatchOver must fire exactly once");
    }

    static void PauseFreezesElapsedTime()
    {
        var a = new Fighter("A");
        var rules = new MatchRules<Fighter>();
        rules.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = 5f }, new[] { a });

        rules.AdvanceTime(3f);
        Assert(Math.Abs(rules.ElapsedSeconds - 3f) < 1e-4f, "elapsed time must accumulate while running");

        rules.SetPaused(true);
        Assert(rules.IsPaused, "SetPaused(true) must move a running match to Paused");
        bool endedWhilePaused = rules.AdvanceTime(10f);
        Assert(!endedWhilePaused, "AdvanceTime must be a no-op while paused, even past the time limit");
        Assert(Math.Abs(rules.ElapsedSeconds - 3f) < 1e-4f, "paused elapsed time must not change");

        rules.SetPaused(false);
        Assert(rules.IsRunning, "SetPaused(false) must resume a paused match");
        bool endedAfterResume = rules.AdvanceTime(3f);
        Assert(endedAfterResume, "resumed time must continue toward the original limit and finish it");
        // 3s (pre-pause) + 3s (post-resume) = 6s of running time, which is
        // past the 5s limit; completion must clamp ElapsedSeconds exactly to
        // the configured limit rather than reporting the raw (overshooting)
        // sum of running spans.
        Assert(Math.Abs(rules.ElapsedSeconds - 5f) < 1e-4f,
            "elapsed time at completion must be clamped to the time limit (min of summed running spans and the limit), never overshoot it");
    }

    static void ResetThenStartMatchIsClean()
    {
        var a = new Fighter("A");
        var b = new Fighter("B");
        var rules = new MatchRules<Fighter>();
        rules.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f }, new[] { a, b });
        rules.ReportFrags(a, 1);
        Assert(rules.IsOver, "setup: first match must finish on the frag limit");

        rules.Reset();
        Assert(rules.Phase == MatchPhase.NotStarted, "Reset must return to NotStarted");
        Assert(rules.Result == null, "Reset must clear the previous Result");
        Assert(rules.GetFrags(a) == 0, "Reset must forget previously recorded frags");

        rules.StartMatch(new MatchConfig { FragLimit = 2, TimeLimitSeconds = 0f }, new[] { a, b });
        Assert(rules.IsRunning, "a clean restart must begin a running match again");
        Assert(rules.ElapsedSeconds == 0f, "a clean restart must begin at zero elapsed time");
        Assert(rules.GetFrags(a) == 0, "a clean restart must begin with zero recorded frags");

        bool endedOnOne = rules.ReportFrags(a, 1);
        Assert(!endedOnOne, "the new match must honour its own (higher) frag limit, not the old one");
    }

    static void TerminalStateIsStableAndCompletionIsNotRepeated()
    {
        var a = new Fighter("A");
        var b = new Fighter("B");
        var rules = new MatchRules<Fighter>();
        int overCount = 0;
        rules.MatchOver += _ => overCount++;
        rules.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 3f }, new[] { a, b });

        rules.ReportFrags(a, 1);
        var firstResult = rules.Result;
        Assert(rules.IsOver, "setup: match must be over after reaching the frag limit");
        Assert(overCount == 1, "setup: MatchOver must have fired once");

        // Further frags, further time, even past the time limit: none of it
        // should change anything once the match is terminal.
        rules.ReportFrags(a, 99);
        rules.ReportFrags(b, 50);
        rules.AdvanceTime(100f);
        rules.SetPaused(true);
        rules.SetPaused(false);

        Assert(overCount == 1, "MatchOver must not fire again after the match is already Over");
        Assert(ReferenceEquals(rules.Result, firstResult), "Result must remain the same terminal snapshot object");
        Assert(rules.Result.Winner == a, "terminal winner must not change after the match is Over");
        Assert(Math.Abs(rules.ElapsedSeconds) < 1e-4f, "elapsed time recorded at completion must stay frozen");
        Assert(rules.IsOver, "phase must remain Over, never silently resume");
    }

    static void NegativeOrZeroDtIsIgnored()
    {
        var a = new Fighter("A");
        var rules = new MatchRules<Fighter>();
        rules.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = 5f }, new[] { a });

        rules.AdvanceTime(0f);
        rules.AdvanceTime(-1f);
        Assert(rules.ElapsedSeconds == 0f, "zero/negative dt must never move the match clock");
        Assert(rules.IsRunning, "zero/negative dt must never end the match");
    }

    static void UnknownParticipantIsTrackedLazily()
    {
        var a = new Fighter("A");
        var latecomer = new Fighter("Latecomer");
        var rules = new MatchRules<Fighter>();
        rules.StartMatch(new MatchConfig { FragLimit = 2, TimeLimitSeconds = 0f }, new[] { a });

        Assert(rules.GetFrags(latecomer) == 0, "an unknown participant must read as zero frags, not throw");
        bool ended = rules.ReportFrags(latecomer, 2);
        Assert(ended, "a participant not in the original seed list must still be able to reach the frag limit");
        Assert(rules.Result.Winner == latecomer, "a late-joining participant must be a valid winner");
    }

    static void NonFiniteDtIsRejectedAndNeverCorruptsTimer()
    {
        var a = new Fighter("A");
        var rules = new MatchRules<Fighter>();
        rules.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = 0f }, new[] { a });

        bool endedOnNaN = rules.AdvanceTime(float.NaN);
        Assert(!endedOnNaN, "a NaN dt must never end the match");
        Assert(rules.ElapsedSeconds == 0f, "a NaN dt must never move the match clock");
        Assert(!float.IsNaN(rules.ElapsedSeconds), "the clock itself must never become NaN from a bad dt");

        bool endedOnPosInf = rules.AdvanceTime(float.PositiveInfinity);
        Assert(!endedOnPosInf, "a +Infinity dt must never end the match");
        Assert(rules.ElapsedSeconds == 0f, "a +Infinity dt must never move the match clock");

        bool endedOnNegInf = rules.AdvanceTime(float.NegativeInfinity);
        Assert(!endedOnNegInf, "a -Infinity dt must never end the match");
        Assert(rules.ElapsedSeconds == 0f, "a -Infinity dt must never move the match clock");

        // A huge but finite dt must still be accepted and must never push the
        // clock itself into NaN/Infinity (defends against float overflow).
        bool endedOnHugeFinite = rules.AdvanceTime(float.MaxValue);
        Assert(!endedOnHugeFinite, "a huge finite dt must not end an unbounded (no time limit) match");
        Assert(!float.IsNaN(rules.ElapsedSeconds) && !float.IsInfinity(rules.ElapsedSeconds),
            "elapsed time must remain finite even after a huge finite dt");
    }

    static void NonFiniteConfigSanitizesToDisabled()
    {
        var a = new Fighter("A");
        var rules = new MatchRules<Fighter>();

        rules.StartMatch(new MatchConfig { FragLimit = -3, TimeLimitSeconds = float.NaN }, new[] { a });
        Assert(rules.Config.FragLimit == 0, "a negative frag limit must sanitize to disabled (0)");
        Assert(rules.Config.TimeLimitSeconds == 0f, "a NaN time limit must sanitize to disabled (0)");
        rules.AdvanceTime(1000f);
        Assert(rules.IsRunning, "a NaN-sanitized (disabled) time limit must never auto-finish the match");

        rules.Reset();
        rules.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = float.PositiveInfinity }, new[] { a });
        Assert(rules.Config.TimeLimitSeconds == 0f, "a +Infinity time limit must sanitize to disabled (0), not an unreachable-but-set limit");

        rules.Reset();
        rules.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = float.NegativeInfinity }, new[] { a });
        Assert(rules.Config.TimeLimitSeconds == 0f, "a -Infinity time limit must sanitize to disabled (0)");
    }

    static void ElapsedTimeIsClampedExactlyToTheTimeLimit()
    {
        var a = new Fighter("A");
        var rules = new MatchRules<Fighter>();
        rules.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = 10f }, new[] { a });

        bool ended = rules.AdvanceTime(999f); // wildly overshoots the 10s limit in one step
        Assert(ended, "a dt that overshoots the time limit must still end the match");
        Assert(rules.ElapsedSeconds == 10f,
            "elapsed time at completion must be clamped exactly to the configured time limit, never overshoot it");
        Assert(rules.Result.ElapsedSeconds == 10f, "the recorded Result.ElapsedSeconds must match the clamped value");
    }

    static void StartMatchWhileRunningOrOverThrows()
    {
        var a = new Fighter("A");
        var rules = new MatchRules<Fighter>();
        rules.StartMatch(new MatchConfig { FragLimit = 5, TimeLimitSeconds = 0f }, new[] { a });
        Assert(rules.IsRunning, "setup: first StartMatch from NotStarted must succeed");

        bool threwWhileRunning = false;
        try { rules.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f }, new[] { a }); }
        catch (InvalidOperationException) { threwWhileRunning = true; }
        Assert(threwWhileRunning, "StartMatch while Running must throw InvalidOperationException, not silently restart");
        Assert(rules.Config.FragLimit == 5, "a rejected StartMatch must not mutate the live match's config");

        rules.ReportFrags(a, 5);
        Assert(rules.IsOver, "setup: match must now be Over");

        bool threwWhileOver = false;
        try { rules.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f }, new[] { a }); }
        catch (InvalidOperationException) { threwWhileOver = true; }
        Assert(threwWhileOver, "StartMatch while Over must also throw InvalidOperationException, requiring an explicit Reset() first");

        rules.Reset();
        rules.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f }, new[] { a });
        Assert(rules.IsRunning, "StartMatch must succeed again once Reset() has returned to NotStarted");
    }
}

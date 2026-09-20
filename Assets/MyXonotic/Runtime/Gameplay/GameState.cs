using System;
using System.Collections.Generic;

namespace MyXonotic
{
    /// <summary>Tracks all live actors for the HUD/scoring readout and test drivers.</summary>
    public static class GameState
    {
        public static readonly List<Actor> Actors = new List<Actor>();

        /// victim, killer
        public static event Action<Actor, Actor> AnyDeath;

        public static void Register(Actor actor)
        {
            if (actor == null || Actors.Contains(actor)) return;
            Actors.Add(actor);
            actor.Died += HandleDeath;
        }

        static void HandleDeath(Actor victim, Actor killer) => AnyDeath?.Invoke(victim, killer);

        public static void Reset()
        {
            foreach (var a in Actors) if (a != null) a.Died -= HandleDeath;
            Actors.Clear();
        }
    }
}

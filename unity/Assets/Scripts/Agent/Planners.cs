using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>What a planner may look at when deciding.</summary>
    public class PlannerContext
    {
        public int Stage;
        public float SimTime;
        public Vector3 MosquitoPos;
        public bool Landed;
        public float FeedProgress;
        public float FeedTime;
        public HumanController Human;
        public HandController Hand;
        public List<Candidate> Candidates;
        public HashSet<string> ExcludedSkins;
        public PlannerCommand Current;
        /// <summary>HIDE scenario before the choice: the human is about to look around.</summary>
        public bool HideSituation;
        /// <summary>Stage 0 training experiment: share of commands that are MOVE_TO / HIDE to a free point.</summary>
        public float Stage0PointMix;
        /// <summary>The alert counts for planning: stage 2+, or the HIDE scenario at any stage.</summary>
        public bool AlertKnown => (Stage >= 2 || HideSituation) && Human.Alert;

        public IEnumerable<SkinZone> AllowedSkins()
        {
            foreach (var s in Human.skins)
                if (ExcludedSkins == null || !ExcludedSkins.Contains(s.id)) yield return s;
        }
    }

    /// <summary>
    /// Planners that run inside Unity. Fixed and Rule never look at
    /// SkinZone.covered or traps: in the new-object evaluation those are the
    /// cues only the image shows. Their single fallback is that a skin whose
    /// APPROACH timed out is excluded for the rest of the episode.
    /// </summary>
    public static class Planners
    {
        public const float ThreatDistance = 1.0f;

        /// <summary>Baseline: APPROACH the nearest allowed skin.</summary>
        public static PlannerCommand Fixed(PlannerContext c)
        {
            SkinZone s = Nearest(c, c.AllowedSkins());
            if (s == null)
            {
                c.ExcludedSkins?.Clear();
                s = Nearest(c, c.Human.skins);
            }
            return new PlannerCommand(Subgoal.Approach, s.id);
        }

        /// <summary>
        /// Rule planner (docs/interface.md): hand within 1 m and active -> EVADE;
        /// alert -> HIDE at the nearest hidden candidate (else the nearest one
        /// behind the gaze); straight line to the target skin blocked -> MOVE_TO
        /// the nearest candidate the mosquito can reach in a straight line and
        /// that has a clear line to the human; else APPROACH
        /// the skin farthest from the hand (stage 2+) or the nearest skin.
        /// </summary>
        public static PlannerCommand Rule(PlannerContext c)
        {
            if (c.Stage >= 2 && c.Hand.Active && Vector3.Distance(c.Hand.Position, c.MosquitoPos) < ThreatDistance)
                return new PlannerCommand(Subgoal.Evade, null);

            if (c.AlertKnown && c.Candidates.Count > 0)
            {
                Candidate? best = null;
                foreach (var k in c.Candidates)
                    if (k.Hidden && (best == null || k.Dist < best.Value.Dist)) best = k;
                if (best == null)
                {
                    foreach (var k in c.Candidates)
                    {
                        bool behind = Vector3.Dot(k.Pos - c.Human.HeadPos, c.Human.GazeDir) < 0f;
                        if (behind && (best == null || k.Dist < best.Value.Dist)) best = k;
                    }
                }
                if (best == null) best = NearestCandidate(c, false);
                if (best != null) return new PlannerCommand(Subgoal.Hide, best.Value.Id);
            }

            SkinZone skin = null;
            if (c.Stage >= 2)
            {
                float far = -1f;
                foreach (var s in c.AllowedSkins())
                {
                    float d = Vector3.Distance(s.Center, c.Hand.Position);
                    if (d > far) { far = d; skin = s; }
                }
            }
            if (skin == null) skin = Nearest(c, c.AllowedSkins());
            if (skin == null)
            {
                c.ExcludedSkins?.Clear();
                skin = Nearest(c, c.Human.skins);
            }

            bool blocked = Physics.Linecast(c.MosquitoPos, skin.Center, Layers.WorldNoHuman, QueryTriggerInteraction.Ignore);
            if (blocked && c.Candidates.Count > 0)
            {
                // Waypoint: a point the mosquito can fly to in a straight line and
                // that has a clear line to the human. Without the first check the
                // rule kept picking a point behind the same obstacle (run02).
                Candidate? open = null;
                foreach (var k in c.Candidates)
                {
                    if (!k.ClearPath || k.Dist <= SubgoalConstants.MoveToRadius) continue;
                    if (Physics.Linecast(c.MosquitoPos, k.Pos, Layers.WorldNoHuman, QueryTriggerInteraction.Ignore)) continue;
                    if (open == null || k.Dist < open.Value.Dist) open = k;
                }
                if (open == null) open = NearestCandidate(c, true);
                if (open != null && open.Value.Dist > SubgoalConstants.MoveToRadius)
                    return new PlannerCommand(Subgoal.MoveTo, open.Value.Id);
            }
            return new PlannerCommand(Subgoal.Approach, skin.id);
        }

        /// <summary>
        /// Training planner. Stage 0: APPROACH a random skin. Stage 1: APPROACH /
        /// HIDE / MOVE_TO uniformly. Stage 2+: the same, and EVADE half of the
        /// time while the hand is active.
        /// </summary>
        public static PlannerCommand Random(PlannerContext c, System.Random rng)
        {
            var skins = c.Human.skins;
            if (c.Stage <= 0 || c.Candidates.Count == 0)
            {
                // Experiment (11/24): stage 0 already mixes point subgoals in the empty room, so the skin
                // target and the subgoal target differ from the start and the subgoal has to be read.
                if (c.Stage <= 0 && c.Candidates.Count > 0 && rng.NextDouble() < c.Stage0PointMix)
                    return new PlannerCommand(rng.Next(2) == 0 ? Subgoal.MoveTo : Subgoal.Hide,
                        c.Candidates[rng.Next(c.Candidates.Count)].Id);
                return new PlannerCommand(Subgoal.Approach, skins[rng.Next(skins.Count)].id);
            }

            if (c.Stage >= 2 && c.Hand.Active && rng.NextDouble() < 0.5)
                return new PlannerCommand(Subgoal.Evade, null);

            switch (rng.Next(3))
            {
                case 0:
                    return new PlannerCommand(Subgoal.Approach, skins[rng.Next(skins.Count)].id);
                case 1:
                {
                    var hidden = new List<Candidate>();
                    foreach (var k in c.Candidates) if (k.Hidden) hidden.Add(k);
                    var pool = hidden.Count > 0 ? hidden : c.Candidates;
                    return new PlannerCommand(Subgoal.Hide, pool[rng.Next(pool.Count)].Id);
                }
                default:
                    return new PlannerCommand(Subgoal.MoveTo, c.Candidates[rng.Next(c.Candidates.Count)].Id);
            }
        }

        /// <summary>Subgoals a stage trains and checks: 0 APPROACH; 1 + HIDE, MOVE_TO; 2+ + EVADE.</summary>
        public static List<Subgoal> StageSubgoals(int stage)
        {
            var list = new List<Subgoal> { Subgoal.Approach };
            if (stage >= 1) { list.Add(Subgoal.Hide); list.Add(Subgoal.MoveTo); }
            if (stage >= 2) list.Add(Subgoal.Evade);
            return list;
        }

        /// <summary>
        /// Direct subgoal check (planner = Probe): the target for one given subgoal,
        /// drawn like the training planner draws it. APPROACH: an uncovered skin.
        /// HIDE: a candidate hidden from the head (any candidate if none is).
        /// MOVE_TO: any candidate. EVADE: no target (the hand).
        /// </summary>
        public static PlannerCommand Probe(PlannerContext c, System.Random rng, Subgoal g)
        {
            switch (g)
            {
                case Subgoal.Hide:
                case Subgoal.MoveTo:
                {
                    if (c.Candidates.Count == 0) break;
                    var pool = new List<Candidate>();
                    if (g == Subgoal.Hide) foreach (var k in c.Candidates) if (k.Hidden) pool.Add(k);
                    if (pool.Count == 0) pool.AddRange(c.Candidates);
                    return new PlannerCommand(g, pool[rng.Next(pool.Count)].Id);
                }
                case Subgoal.Evade:
                    return new PlannerCommand(Subgoal.Evade, null);
            }
            var skins = new List<SkinZone>();
            foreach (var s in c.Human.skins) if (!s.covered) skins.Add(s);
            if (skins.Count == 0) skins.AddRange(c.Human.skins);
            return new PlannerCommand(Subgoal.Approach, skins[rng.Next(skins.Count)].id);
        }

        static SkinZone Nearest(PlannerContext c, IEnumerable<SkinZone> skins)
        {
            SkinZone best = null;
            float bestD = float.MaxValue;
            foreach (var s in skins)
            {
                float d = Vector3.Distance(s.Center, c.MosquitoPos);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        static Candidate? NearestCandidate(PlannerContext c, bool requireClearPath)
        {
            Candidate? best = null;
            foreach (var k in c.Candidates)
            {
                if (requireClearPath && !k.ClearPath) continue;
                if (best == null || k.Dist < best.Value.Dist) best = k;
            }
            return best;
        }
    }
}

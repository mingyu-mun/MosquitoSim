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

            var allowed = new List<SkinZone>(c.AllowedSkins());
            if (allowed.Count == 0)
            {
                c.ExcludedSkins?.Clear();
                allowed.AddRange(c.Human.skins);
            }
            // Preference: stage 2+ the skin farthest from the hand, else the nearest.
            if (c.Stage >= 2)
                allowed.Sort((a, b) => Vector3.Distance(b.Center, c.Hand.Position).CompareTo(Vector3.Distance(a.Center, c.Hand.Position)));
            else
                allowed.Sort((a, b) => Vector3.Distance(a.Center, c.MosquitoPos).CompareTo(Vector3.Distance(b.Center, c.MosquitoPos)));

            // A skin that is a straight flight now (walls, net and the body checked): APPROACH it.
            foreach (var s in allowed)
                if (CandidatePoints.CanReachSkin(c.MosquitoPos, s)) return new PlannerCommand(Subgoal.Approach, s.id);

            // None: plan a route through the candidates (each leg a straight flight) to any of them and send
            // the mosquito to the first point of the shortest one (11/25).
            if (c.Candidates.Count > 0)
            {
                Candidate? hop = FirstHop(c, allowed);
                if (hop != null) return new PlannerCommand(Subgoal.MoveTo, hop.Value.Id);
            }
            return new PlannerCommand(Subgoal.Approach, allowed[0].id);
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
                return new PlannerCommand(Subgoal.Approach, skins[rng.Next(skins.Count)].id);

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
                {
                    // Only points the mosquito can fly to in a straight line (11/25).
                    var pool = Reachable(c);
                    if (pool.Count == 0) return new PlannerCommand(Subgoal.Approach, skins[rng.Next(skins.Count)].id);
                    return new PlannerCommand(Subgoal.MoveTo, pool[rng.Next(pool.Count)].Id);
                }
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
                    // MOVE_TO checks a straight flight: only reachable points (11/25). None reachable
                    // falls back to any point; it is logged as path_blocked and left out of the rate.
                    if (g == Subgoal.MoveTo) pool = Reachable(c);
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

        /// <summary>Candidates the mosquito can fly to in a straight line, farther than the MOVE_TO radius.</summary>
        static List<Candidate> Reachable(PlannerContext c)
        {
            var pool = new List<Candidate>();
            foreach (var k in c.Candidates)
                if (k.Reachable && k.Dist > SubgoalConstants.MoveToRadius) pool.Add(k);
            return pool;
        }

        /// <summary>
        /// Shortest route from the mosquito over the candidates to a point from which one of
        /// <paramref name="skins"/> is a straight flight, every leg a straight flight (Dijkstra over at most
        /// 10 nodes). Returns the first point; with no route, the reachable candidate nearest a skin if it is
        /// nearer than the mosquito; else null (the caller APPROACHes).
        /// </summary>
        static Candidate? FirstHop(PlannerContext c, List<SkinZone> skins)
        {
            var nodes = new List<Vector3> { c.MosquitoPos };
            foreach (var k in c.Candidates) nodes.Add(k.Pos);
            int n = nodes.Count + 1, g = n - 1; // g: "a skin is a straight flight from here"
            var dist = new float[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[0] = 0f;
            for (int it = 0; it < n; it++)
            {
                int u = -1;
                for (int i = 0; i < n; i++) if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || dist[u] == float.MaxValue) break;
                done[u] = true;
                if (u == g) break;
                for (int v = 1; v < n; v++)
                {
                    if (done[v]) continue;
                    float leg;
                    if (v == g)
                    {
                        leg = float.MaxValue;
                        foreach (var s in skins)
                            if (CandidatePoints.CanReachSkin(nodes[u], s)) leg = Mathf.Min(leg, Vector3.Distance(nodes[u], s.Center));
                        if (leg == float.MaxValue) continue;
                    }
                    else
                    {
                        // From the mosquito use the flag computed at selection; between points check now.
                        bool clear = u == 0 ? c.Candidates[v - 1].Reachable : CandidatePoints.StraightClear(nodes[u], nodes[v]);
                        if (!clear) continue;
                        leg = Vector3.Distance(nodes[u], nodes[v]);
                    }
                    float d = dist[u] + leg;
                    if (d < dist[v]) { dist[v] = d; prev[v] = u; }
                }
            }
            if (prev[g] > 0)
            {
                int v = prev[g];
                while (prev[v] != 0) v = prev[v];
                if (c.Candidates[v - 1].Dist > SubgoalConstants.MoveToRadius) return c.Candidates[v - 1];
            }
            Candidate? best = null;
            float bestD = float.MaxValue;
            foreach (var s in skins) bestD = Mathf.Min(bestD, Vector3.Distance(c.MosquitoPos, s.Center));
            foreach (var k in Reachable(c))
                foreach (var s in skins)
                {
                    float d = Vector3.Distance(k.Pos, s.Center);
                    if (d < bestD) { bestD = d; best = k; }
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

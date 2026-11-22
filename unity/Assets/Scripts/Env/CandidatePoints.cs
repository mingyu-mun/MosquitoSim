using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>A candidate point offered to the planner at one decision.</summary>
    public struct Candidate
    {
        public string Id;        // P1..P8, renumbered at every decision
        public Vector3 Pos;
        public float Dist;       // from the mosquito
        public bool Hidden;      // not visible from the human's head
        public bool ClearPath;   // straight line to the human's chest is open
        public bool NearTrap;    // within 0.2 m of a sticky trap
    }

    /// <summary>
    /// Free-space grid (0.25 m, heights 0.5/1.0/1.5/2.0 m) built at reset, and
    /// the 8 candidates chosen at each decision: up to 4 hidden points nearest
    /// to the mosquito, the 4 net sides in eval_net, up to 2 points next to a
    /// trap in the new-object evaluation, and spread points (farthest-point
    /// sampling) for the rest; at least 0.5 m apart, order shuffled.
    /// Ground-truth flags go to the rule planner and the log, not to the VLM.
    /// </summary>
    public class CandidatePoints : MonoBehaviour
    {
        public const int Count = 8;
        public const float GridStep = 0.25f;
        public const float MinSeparation = 0.5f;
        static readonly float[] k_Heights = { 0.5f, 1.0f, 1.5f, 2.0f };

        readonly List<Vector3> m_Grid = new List<Vector3>();
        public readonly List<Candidate> Current = new List<Candidate>();
        public IReadOnlyList<Vector3> Grid => m_Grid;

        public void BuildGrid(RoomArea area)
        {
            m_Grid.Clear();
            Vector3 size = area.Size;
            Vector3 o = area.transform.position;
            for (float x = -size.x / 2f + 0.2f; x <= size.x / 2f - 0.2f; x += GridStep)
            for (float z = -size.z / 2f + 0.2f; z <= size.z / 2f - 0.2f; z += GridStep)
            foreach (float y in k_Heights)
            {
                Vector3 p = o + new Vector3(x, y, z);
                if (Physics.CheckSphere(p, 0.1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (area.Net != null && area.Net.Contains(p)) continue;
                m_Grid.Add(p);
            }
        }

        public void Select(System.Random rng, Vector3 mosquito, HumanController human, NetObstacle net, CueObjects cues)
        {
            var pts = new List<Vector3>();

            // Hidden points nearest to the mosquito.
            var hidden = new List<Vector3>();
            foreach (var p in m_Grid)
            {
                Vector3 flat = p - human.transform.position;
                flat.y = 0f;
                if (flat.magnitude > 0.5f && !human.CanSee(p)) hidden.Add(p);
            }
            hidden.Sort((a, b) => Vector3.Distance(a, mosquito).CompareTo(Vector3.Distance(b, mosquito)));
            foreach (var p in hidden)
            {
                if (pts.Count >= 4) break;
                TryAdd(pts, p);
            }

            // Net sides (one of them is the open side; all treated the same).
            if (net != null && net.Enabled)
                for (int i = 0; i < 4; i++) TryAdd(pts, net.SideOutsidePoint(i, 1.2f));

            // Points next to traps.
            if (cues != null && cues.TrapPositions.Count > 0)
            {
                int added = 0;
                foreach (var p in m_Grid)
                {
                    if (added >= 2) break;
                    if (cues.NearTrap(p, 0.2f) && TryAdd(pts, p)) added++;
                }
            }

            // Spread points by farthest-point sampling.
            if (m_Grid.Count > 0)
            {
                if (pts.Count == 0) pts.Add(m_Grid[rng.Next(m_Grid.Count)]);
                while (pts.Count < Count)
                {
                    float best = -1f;
                    Vector3 bestP = Vector3.zero;
                    foreach (var p in m_Grid)
                    {
                        float dmin = float.MaxValue;
                        foreach (var q in pts) dmin = Mathf.Min(dmin, Vector3.Distance(p, q));
                        if (dmin > best) { best = dmin; bestP = p; }
                    }
                    if (best < MinSeparation) break;
                    pts.Add(bestP);
                }
            }

            // Shuffle so the ID carries no hint (P1 is not "the nearest").
            for (int i = pts.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                Vector3 tmp = pts[i];
                pts[i] = pts[j];
                pts[j] = tmp;
            }

            Current.Clear();
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i];
                Current.Add(new Candidate
                {
                    Id = "P" + (i + 1),
                    Pos = p,
                    Dist = Vector3.Distance(p, mosquito),
                    Hidden = !human.CanSee(p),
                    ClearPath = !Physics.Linecast(p, human.Chest, Layers.WorldNoHuman, QueryTriggerInteraction.Ignore),
                    NearTrap = cues != null && cues.NearTrap(p, 0.2f),
                });
            }
        }

        public bool TryGet(string id, out Candidate c)
        {
            foreach (var x in Current)
            {
                if (x.Id == id)
                {
                    c = x;
                    return true;
                }
            }
            c = default(Candidate);
            return false;
        }

        static bool TryAdd(List<Vector3> pts, Vector3 p)
        {
            if (pts.Count >= Count) return false;
            foreach (var q in pts) if (Vector3.Distance(p, q) < MinSeparation) return false;
            pts.Add(p);
            return true;
        }
    }
}

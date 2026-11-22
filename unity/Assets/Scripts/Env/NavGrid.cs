using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Free-space voxel grid of one room for routing the mosquito around
    /// furniture, the net and walls (MOVE_TO / HIDE guidance).
    ///
    /// A cell is free when a sphere of <see cref="Clearance"/> at its centre
    /// touches nothing in <see cref="Mask"/>. Paths use the 6 axis neighbours
    /// only: with Clearance > Cell / 2, a thin surface (net, curtain) between
    /// two axis neighbours always blocks at least one of them, so a path never
    /// passes through it. The human is not an obstacle here (it moves; the
    /// policy's rays see it).
    /// </summary>
    public class NavGrid
    {
        public const float Cell = 0.2f;
        public const float Clearance = 0.105f;

        /// <summary>Walls, furniture, decor, net, traps: not the human, labels, debug drawings or the mosquito model.</summary>
        public static int Mask => Layers.WorldNoHuman & ~(1 << Layers.DebugViz) & ~(1 << Layers.MosquitoModel);

        Vector3 m_Origin; // centre of cell (0, 0, 0)
        int m_Nx, m_Ny, m_Nz;
        bool[] m_Free;
        public bool Built { get; private set; }
        public int FreeCells { get; private set; }

        public void Build(Bounds room)
        {
            Vector3 min = room.min + Vector3.one * (Cell * 0.5f);
            m_Nx = Mathf.Max(1, Mathf.FloorToInt(room.size.x / Cell));
            m_Ny = Mathf.Max(1, Mathf.FloorToInt(room.size.y / Cell));
            m_Nz = Mathf.Max(1, Mathf.FloorToInt(room.size.z / Cell));
            m_Origin = min;
            m_Free = new bool[m_Nx * m_Ny * m_Nz];
            FreeCells = 0;
            int mask = Mask;
            for (int x = 0; x < m_Nx; x++)
            for (int y = 0; y < m_Ny; y++)
            for (int z = 0; z < m_Nz; z++)
            {
                bool free = !Physics.CheckSphere(Center(x, y, z), Clearance, mask, QueryTriggerInteraction.Ignore);
                m_Free[Index(x, y, z)] = free;
                if (free) FreeCells++;
            }
            Built = true;
        }

        public void Clear() { Built = false; m_Free = null; }

        int Index(int x, int y, int z) => (x * m_Ny + y) * m_Nz + z;

        Vector3 Center(int x, int y, int z) => m_Origin + new Vector3(x * Cell, y * Cell, z * Cell);

        Vector3 Center(int i)
        {
            int z = i % m_Nz;
            int y = (i / m_Nz) % m_Ny;
            int x = i / (m_Nz * m_Ny);
            return Center(x, y, z);
        }

        /// <summary>Nearest free cell to <paramref name="p"/> (searching outward up to 3 cells), or -1.</summary>
        int NearestFree(Vector3 p)
        {
            Vector3 g = (p - m_Origin) / Cell;
            int cx = Mathf.RoundToInt(g.x), cy = Mathf.RoundToInt(g.y), cz = Mathf.RoundToInt(g.z);
            int best = -1;
            float bestD = float.MaxValue;
            for (int r = 0; r <= 3 && best < 0; r++)
            for (int x = cx - r; x <= cx + r; x++)
            for (int y = cy - r; y <= cy + r; y++)
            for (int z = cz - r; z <= cz + r; z++)
            {
                if (x < 0 || y < 0 || z < 0 || x >= m_Nx || y >= m_Ny || z >= m_Nz) continue;
                int i = Index(x, y, z);
                if (!m_Free[i]) continue;
                float d = (Center(x, y, z) - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        static readonly int[] k_Dx = { 1, -1, 0, 0, 0, 0 };
        static readonly int[] k_Dy = { 0, 0, 1, -1, 0, 0 };
        static readonly int[] k_Dz = { 0, 0, 0, 0, 1, -1 };

        // Reused A* buffers (one search at a time on the main thread).
        float[] m_G;
        int[] m_Parent;
        int[] m_Stamp;
        int m_Search;
        readonly List<(float f, int i)> m_Open = new List<(float, int)>();

        /// <summary>
        /// A* from <paramref name="from"/> to <paramref name="to"/>. On success
        /// <paramref name="path"/> holds cell centres from start to goal, followed
        /// by <paramref name="to"/> itself. Returns false when either end has no
        /// free cell nearby or the goal is unreachable.
        /// </summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> path)
        {
            path.Clear();
            if (!Built) return false;
            int s = NearestFree(from), t = NearestFree(to);
            if (s < 0 || t < 0) return false;
            int n = m_Free.Length;
            if (m_G == null || m_G.Length != n)
            {
                m_G = new float[n];
                m_Parent = new int[n];
                m_Stamp = new int[n];
                m_Search = 0;
            }
            m_Search++;
            m_Open.Clear();
            Vector3 goal = Center(t);
            m_G[s] = 0f;
            m_Parent[s] = -1;
            m_Stamp[s] = m_Search;
            Push((Center(s) - goal).magnitude, s);
            bool found = false;
            while (m_Open.Count > 0)
            {
                var (f, i) = Pop();
                if (i == t) { found = true; break; }
                float gi = m_G[i];
                if (f - (Center(i) - goal).magnitude > gi + 1e-4f) continue; // stale entry
                int z = i % m_Nz, y = (i / m_Nz) % m_Ny, x = i / (m_Nz * m_Ny);
                for (int k = 0; k < 6; k++)
                {
                    int nx = x + k_Dx[k], ny = y + k_Dy[k], nz = z + k_Dz[k];
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= m_Nx || ny >= m_Ny || nz >= m_Nz) continue;
                    int j = Index(nx, ny, nz);
                    if (!m_Free[j]) continue;
                    float gj = gi + Cell;
                    if (m_Stamp[j] == m_Search && m_G[j] <= gj) continue;
                    m_Stamp[j] = m_Search;
                    m_G[j] = gj;
                    m_Parent[j] = i;
                    Push(gj + (Center(j) - goal).magnitude, j);
                }
            }
            if (!found) return false;
            for (int i = t; i >= 0; i = m_Parent[i]) path.Add(Center(i));
            path.Reverse();
            path.Add(to);
            return true;
        }

        void Push(float f, int i)
        {
            m_Open.Add((f, i));
            int c = m_Open.Count - 1;
            while (c > 0)
            {
                int p = (c - 1) / 2;
                if (m_Open[p].f <= m_Open[c].f) break;
                var tmp = m_Open[p]; m_Open[p] = m_Open[c]; m_Open[c] = tmp;
                c = p;
            }
        }

        (float f, int i) Pop()
        {
            var top = m_Open[0];
            int last = m_Open.Count - 1;
            m_Open[0] = m_Open[last];
            m_Open.RemoveAt(last);
            int c = 0;
            while (true)
            {
                int l = 2 * c + 1, r = l + 1, m = c;
                if (l < m_Open.Count && m_Open[l].f < m_Open[m].f) m = l;
                if (r < m_Open.Count && m_Open[r].f < m_Open[m].f) m = r;
                if (m == c) break;
                var tmp = m_Open[m]; m_Open[m] = m_Open[c]; m_Open[c] = tmp;
                c = m;
            }
            return top;
        }
    }

    /// <summary>
    /// Per-agent route following for point subgoals (MOVE_TO, HIDE): keeps an
    /// A* path to the target and gives the policy a nearby waypoint it can
    /// reach in a straight line, plus the remaining route length for shaping.
    /// </summary>
    public class PathGuide
    {
        public const float ReplanSeconds = 0.5f;
        public const float Lookahead = 1.5f;   // farthest waypoint offered (route length), within the 2 m rays
        public const float SightRadius = 0.02f;

        readonly List<Vector3> m_Path = new List<Vector3>();
        Vector3 m_Target;
        float m_LastPlan = -999f;
        int m_Next; // first path index not yet passed

        public bool Active { get; private set; }
        /// <summary>Point handed to the policy as the subgoal target.</summary>
        public Vector3 Waypoint { get; private set; }
        /// <summary>Route length from the mosquito to the target (straight distance when inactive).</summary>
        public float Remaining { get; private set; }
        public int Replans { get; private set; }

        public void Reset() { Active = false; m_Path.Clear(); m_LastPlan = -999f; Replans = 0; }

        public void Update(NavGrid grid, Vector3 pos, Vector3 target, float now)
        {
            bool newTarget = (target - m_Target).sqrMagnitude > 1e-6f;
            if (newTarget || now - m_LastPlan >= ReplanSeconds)
            {
                m_Target = target;
                m_LastPlan = now;
                Active = grid != null && grid.FindPath(pos, target, m_Path);
                m_Next = 1;
                Replans++;
            }
            if (!Active || m_Path.Count < 2)
            {
                Active = false;
                Waypoint = target;
                Remaining = Vector3.Distance(pos, target);
                return;
            }
            // Skip nodes already reached.
            while (m_Next < m_Path.Count - 1 && (m_Path[m_Next] - pos).sqrMagnitude < NavGrid.Cell * NavGrid.Cell * 0.25f) m_Next++;
            // Farthest node within the lookahead that the mosquito can fly to in a straight line.
            int pick = m_Next;
            float along = Vector3.Distance(pos, m_Path[m_Next]);
            for (int i = m_Next; i < m_Path.Count; i++)
            {
                if (i > m_Next) along += Vector3.Distance(m_Path[i - 1], m_Path[i]);
                if (along > Lookahead) break;
                if (Clear(pos, m_Path[i])) pick = i;
            }
            Waypoint = m_Path[pick];
            float rest = 0f;
            for (int i = pick + 1; i < m_Path.Count; i++) rest += Vector3.Distance(m_Path[i - 1], m_Path[i]);
            Remaining = Vector3.Distance(pos, Waypoint) + rest;
        }

        static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return true;
            return !Physics.SphereCast(a, SightRadius, d / len, out RaycastHit _, len, NavGrid.Mask, QueryTriggerInteraction.Ignore);
        }
    }
}

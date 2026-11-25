using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// One room: shell, furniture, human, hand, net, cue objects, candidate
    /// points and planner cameras. ResetEpisode builds everything for one
    /// episode from a seeded RNG, so evaluation episodes are identical across
    /// planner conditions.
    /// </summary>
    [RequireComponent(typeof(RoomGenerator), typeof(ObstacleSpawner), typeof(CandidatePoints))]
    [RequireComponent(typeof(CueObjects))]
    public class RoomArea : MonoBehaviour
    {
        public int roomIndex;
        public HumanController human;
        public HandController hand;
        public NetObstacle net;
        public PlannerCameras cameras;

        [Header("Spawning")]
        public float wallMargin = 0.3f;
        public float minHeight = 0.3f;
        public float maxHeight = 2.2f;
        public float freeRadius = 0.05f;

        public RoomGenerator Generator { get; private set; }
        public ObstacleSpawner Obstacles { get; private set; }
        public CandidatePoints Candidates { get; private set; }
        /// <summary>Free-space grid for routing MOVE_TO / HIDE around obstacles (stage 1+).</summary>
        public readonly NavGrid Nav = new NavGrid();
        /// <summary>Net rooms: free cells that connect to the open side.</summary>
        public bool[] NetRegion { get; private set; }
        /// <summary>Cells the mosquito may start in: the net's open side region, else the room's largest
        /// open space. Null when no grid is built (stage 0 empty room).</summary>
        public bool[] SpawnRegion { get; private set; }
        /// <summary>Auto-test report: net layouts where no side gave a way in to every skin.</summary>
        public static int NetRouteFailures, NetLayouts, NetSideSwaps;
        public CueObjects Cues { get; private set; }
        public RoomDecor Decor { get; private set; }
        public NetObstacle Net => net;

        public Vector3 Size => Generator.Size;
        public List<SkinZone> Skins => human.skins;

        void Awake()
        {
            Generator = GetComponent<RoomGenerator>();
            Obstacles = GetComponent<ObstacleSpawner>();
            Candidates = GetComponent<CandidatePoints>();
            Cues = GetComponent<CueObjects>();
            Decor = GetComponent<RoomDecor>();
            if (Decor == null) Decor = gameObject.AddComponent<RoomDecor>();
        }

        public Bounds InnerBounds => new Bounds(transform.position + Vector3.up * Size.y * 0.5f, Size);

        /// <summary>Build the room for one episode.</summary>
        public void ResetEpisode(EnvParams p, int stage, System.Random rng)
        {
            RoomType[] types = p.RoomTypes();
            NetRegion = SpawnRegion = null; // rebuilt below; cue placement must not use the last layout's
            Generator.Build(types[rng.Next(types.Length)], rng); // size and colours vary per episode
            Obstacles.Clear();
            Cues.Clear();
            Decor.Clear();
            if (net != null) net.Disable();
            Physics.SyncTransforms();

            var humanRng = new System.Random(rng.Next());
            var decorRng = new System.Random(rng.Next());
            bool walking = stage >= 2 && !p.UseNet;
            float keepAway = 0.8f;
            int clutter = 3 + decorRng.Next(4);
            if (p.UseNet && net != null)
            {
                // The net goes around the human, so the human comes first and the rest keeps clear of it.
                // The human only stands where the whole net fits inside the room (11/25); the net is turned
                // close to the room axes so it does not cut into a wall.
                m_NetYaw = 90f * rng.Next(4) + Range(rng, -10f, 10f);
                human.ResetHuman(this, humanRng, walking, p.FastHuman, p.BiteAlertChance);
                Decor.PlaceStructure(decorRng, this, human, stage >= 1);
                net.Place(human.transform.position, m_NetYaw, rng.Next(4));
                m_NetYaw = float.NaN;
                keepAway = NetObstacle.Half + 0.6f;
                if (stage >= 1)
                {
                    Obstacles.Spawn(rng, this, p.UseObstacleSetB, 3 + rng.Next(3), human, keepAway);
                    Decor.PlaceClutter(decorRng, this, human, clutter);
                }
            }
            else
            {
                // Furnish first, then the human takes a spot and a route in the floor that is left
                // (run15: routes chosen first left no room for beds in the studio layout).
                Decor.PlaceStructure(decorRng, this, null, stage >= 1);
                if (stage >= 1)
                {
                    Obstacles.Spawn(rng, this, p.UseObstacleSetB, 3 + rng.Next(3), null, keepAway);
                    // Lamps, hanging plants, shelves, clotheslines, boxes: harder to fly through.
                    Decor.PlaceClutter(decorRng, this, null, clutter);
                }
                Physics.SyncTransforms();
                human.ResetHuman(this, humanRng, walking, p.FastHuman, p.BiteAlertChance);
            }

            if (p.UseCues) Cues.Place(rng, this, human);

            hand.ResetHand(new System.Random(rng.Next()), p.FastHuman);
            if (cameras != null) cameras.PlaceOverview(this);
            Physics.SyncTransforms();
            if (stage >= 1 || p.Planner == PlannerKind.External || p.Stage0PointMix > 0f)
            {
                Candidates.BuildGrid(this);
                Nav.Build(InnerBounds);
                NetRegion = null;
                if (p.UseNet && net != null && net.Enabled) EnsureNetRoute(rng);
                SpawnRegion = NetRegion ?? Nav.LargestRegion();
            }
            else
            {
                Candidates.Current.Clear();
                Nav.Clear();
                NetRegion = SpawnRegion = null;
            }
        }

        /// <summary>Random floor point at least <paramref name="margin"/> from walls.</summary>
        public Vector3 RandomFloorPoint(System.Random rng, float margin)
        {
            float hx = Mathf.Max(0.05f, Size.x / 2f - margin);
            float hz = Mathf.Max(0.05f, Size.z / 2f - margin);
            return transform.position + new Vector3(Range(rng, -hx, hx), 0f, Range(rng, -hz, hz));
        }

        /// <summary>Floor spot where a 0.35 m wide human fits, head included (not in the L corner, furniture or clutter).</summary>
        public Vector3 RandomHumanSpot(System.Random rng)
        {
            // Net rooms: far enough from the walls for the net, and the net's box free of geometry.
            bool forNet = !float.IsNaN(m_NetYaw);
            float margin = forNet ? NetObstacle.Half + 0.15f : 0.6f;
            Vector3 p = RandomFloorPoint(rng, margin);
            for (int i = 0; i < 100; i++)
            {
                bool free = !Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.55f, 0.35f,
                    Layers.WorldNoHuman, QueryTriggerInteraction.Ignore);
                if (free && forNet)
                    free = !Physics.CheckBox(p + Vector3.up * (NetObstacle.NetHeight * 0.5f + 0.05f),
                        new Vector3(NetObstacle.Half + 0.05f, NetObstacle.NetHeight * 0.5f - 0.05f, NetObstacle.Half + 0.05f),
                        Quaternion.Euler(0f, m_NetYaw, 0f), Layers.WorldNoHuman, QueryTriggerInteraction.Ignore);
                if (free) return p;
                p = RandomFloorPoint(rng, margin);
            }
            if (forNet) NetFitFailures++;
            return p;
        }

        float m_NetYaw = float.NaN; // set while the human is placed for a net room
        /// <summary>Auto-test report: net rooms where no spot fitted the whole net.</summary>
        public static int NetFitFailures;

        /// <summary>Random point in the air, free of geometry, outside the net, away from the human.</summary>
        public Vector3 SampleFreePoint(System.Random rng, float minDistanceFromHuman, float radius = -1f)
        {
            float r = radius > 0f ? radius : freeRadius;
            Vector3 p = transform.position + Vector3.up * 1.5f;
            for (int i = 0; i < 200; i++)
            {
                Vector3 q = RandomFloorPoint(rng, wallMargin);
                p = q + Vector3.up * Range(rng, minHeight, maxHeight);
                if (Physics.CheckSphere(p, r, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (net != null && net.Contains(p)) continue;
                // The start cell itself must be open space connected to the room (or the net's way in):
                // not inside a hollow cabinet or drawer (11/25, room 0 of the net check).
                if (SpawnRegion != null && !Nav.CellInRegion(SpawnRegion, p)) continue;
                Vector3 flat = p - human.transform.position;
                flat.y = 0f;
                if (flat.magnitude < minDistanceFromHuman) continue;
                return p;
            }
            Debug.LogWarning($"{name}: no free point found");
            return p;
        }

        /// <summary>
        /// Net rooms (11/25): the open side must lead in. A side passes when the point just outside it is
        /// free, most of the free space outside the net connects to it, the point just inside connects to
        /// it, and some uncovered skin is a straight flight (body included) from just outside. The drawn side is tried
        /// first, then the others in random order; the first that passes stays open.
        /// </summary>
        void EnsureNetRoute(System.Random rng)
        {
            var order = new List<int> { net.OpenSide };
            var rest = new List<int>();
            for (int i = 0; i < 4; i++) if (i != net.OpenSide) rest.Add(i);
            while (rest.Count > 0) { int k = rng.Next(rest.Count); order.Add(rest[k]); rest.RemoveAt(k); }
            NetLayouts++;
            foreach (int side in order)
            {
                if (side != order[0]) NetSideSwaps++;
                if (side != net.OpenSide)
                {
                    net.SetOpenSide(side);
                    Nav.Build(InnerBounds);
                }
                if (NetRouteOk(out bool[] region)) { NetRegion = region; return; }
            }
            NetRouteFailures++;
            Debug.LogWarning($"{name}: no side of the net leads in to every skin");
        }

        bool NetRouteOk(out bool[] region)
        {
            region = null;
            const float y = 1.2f;
            Vector3 outside = net.OpeningOutside(y), inside = net.OpeningInside(y);
            if (!InnerBounds.Contains(outside)) return false;
            if (Physics.CheckSphere(outside, 0.1f, NavGrid.Mask, QueryTriggerInteraction.Ignore)) return false;
            region = Nav.Flood(outside);
            if (region == null || !Nav.InRegion(region, inside)) return false;
            int reach = Nav.CountCells(region, c => !net.Contains(c));
            int all = Nav.CountCells(null, c => !net.Contains(c));
            if (all == 0 || reach < 0.5f * all) return false;
            // At least one uncovered skin must be a straight flight from the point just outside the opening
            // (a candidate of the ring), so a planner can go there and then APPROACH (11/25).
            foreach (var s in human.skins)
                if (!s.covered && CandidatePoints.CanReachSkin(outside, s)) return true;
            return false;
        }

        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
    }
}

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
                human.ResetHuman(this, humanRng, walking, p.FastHuman, p.BiteAlertChance);
                Decor.PlaceStructure(decorRng, this, human, stage >= 1);
                net.Place(human.transform.position, (float)rng.NextDouble() * 360f, rng.Next(4));
                keepAway = 1.3f;
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
            }
            else
            {
                Candidates.Current.Clear();
                Nav.Clear();
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
            Vector3 p = RandomFloorPoint(rng, 0.6f);
            for (int i = 0; i < 50; i++)
            {
                if (!Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.55f, 0.35f,
                        Layers.WorldNoHuman, QueryTriggerInteraction.Ignore))
                    return p;
                p = RandomFloorPoint(rng, 0.6f);
            }
            return p;
        }

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
                Vector3 flat = p - human.transform.position;
                flat.y = 0f;
                if (flat.magnitude < minDistanceFromHuman) continue;
                return p;
            }
            Debug.LogWarning($"{name}: no free point found");
            return p;
        }

        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// The human. Stages 0-1: standing still. Stage 2+: walks between
    /// waypoints (stand, walk, and a turn on the spot when the next leg points
    /// back), becomes alert when the mosquito buzzes near the head or bites,
    /// and looks at the mosquito while alert. Stepped by the agent so that the
    /// order of updates is fixed. The phase drives the character animation.
    /// </summary>
    public class HumanController : MonoBehaviour
    {
        public Transform head;
        public List<SkinZone> skins = new List<SkinZone>();

        public const float BuzzRadius = 0.5f;
        public const float BuzzToAlert = 0.4f;
        public const float AlertSeconds = 3f;
        /// <summary>Direction changes larger than this use the turn (180°) animation.</summary>
        public const float TurnAngle = 100f;
        public const float WalkTurnRate = 240f; // deg/s for smaller changes while walking

        public enum Phase { Stand, Walk, Turn }

        [Tooltip("Length of the turn; HumanAvatar sets it to the turn clip length")]
        public float turnSeconds = 1.5f;

        public Phase CurrentPhase { get; private set; }
        public bool Turning => CurrentPhase == Phase.Turn;
        /// <summary>Waypoints of the walking route (the start spot only if standing).</summary>
        public IReadOnlyList<Vector3> Route => m_Waypoints;

        public Vector3 Velocity { get; private set; }
        public bool Walking { get; private set; }
        public bool Alert => m_AlertTimer > 0f;
        public float AlertRemaining => Mathf.Max(0f, m_AlertTimer);
        public Vector3 HeadPos => head != null ? head.position : transform.position + Vector3.up * 1.6f;
        public Vector3 Chest => transform.position + Vector3.up * 1.2f;
        public Vector3 GazeDir { get; private set; }

        System.Random m_Rng;
        readonly List<Vector3> m_Waypoints = new List<Vector3>();
        int m_Wp;
        float m_Speed, m_Pause, m_AlertTimer, m_Buzz, m_BiteTimer, m_BiteChance, m_TurnTimer;
        Quaternion m_TurnFrom, m_TurnTo;

        void Awake()
        {
            if (skins.Count == 0) skins.AddRange(GetComponentsInChildren<SkinZone>());
            if (head == null)
            {
                var h = transform.Find("Head");
                if (h != null) head = h;
            }
        }

        public SkinZone SkinById(string id)
        {
            foreach (var s in skins) if (s.id == id) return s;
            return null;
        }

        /// <summary>Place the human; set up a walking route if <paramref name="walking"/>.</summary>
        public void ResetHuman(RoomArea area, System.Random rng, bool walking, bool newBehavior, float biteChance)
        {
            m_Rng = rng;
            m_BiteChance = biteChance;
            m_AlertTimer = 0f;
            m_Buzz = 0f;
            m_BiteTimer = 0f;
            Velocity = Vector3.zero;
            Walking = walking;
            CurrentPhase = Phase.Stand;
            m_TurnTimer = 0f;
            m_Waypoints.Clear();

            Vector3 p = area.RandomHumanSpot(rng);
            transform.position = p;
            transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            GazeDir = transform.forward;
            if (!walking) m_Waypoints.Add(p);

            if (walking)
            {
                // Training: back and forth between two points at 0.5-0.9 m/s.
                // New human behaviour: a 4-point loop at 0.8-1.3 m/s.
                int n = newBehavior ? 4 : 2;
                m_Speed = newBehavior ? Range(0.8f, 1.3f) : Range(0.5f, 0.9f);
                m_Waypoints.Add(p);
                for (int i = 1; i < n; i++)
                {
                    Vector3 prev = m_Waypoints[m_Waypoints.Count - 1];
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        Vector3 q = area.RandomHumanSpot(rng);
                        if (Vector3.Distance(q, prev) < 1.0f) continue;
                        if (!PathClear(prev, q)) continue;
                        m_Waypoints.Add(q);
                        break;
                    }
                }
                if (m_Waypoints.Count < 2)
                {
                    Walking = false;
                    m_Waypoints.Clear();
                    m_Waypoints.Add(p);
                }
                m_Wp = 1;
                m_Pause = Range(0.5f, 2f);
            }
            Physics.SyncTransforms();
        }

        public void Step(float dt, Vector3 mosquitoPos, bool mosquitoLanded)
        {
            // Walking
            Velocity = Vector3.zero;
            if (!Walking)
            {
                CurrentPhase = Phase.Stand;
            }
            else if (CurrentPhase == Phase.Turn)
            {
                // Turn on the spot (the turn animation plays meanwhile).
                m_TurnTimer += dt;
                float k = Mathf.Clamp01(m_TurnTimer / Mathf.Max(0.1f, turnSeconds));
                transform.rotation = Quaternion.Slerp(m_TurnFrom, m_TurnTo, k * k * (3f - 2f * k));
                if (k >= 1f) CurrentPhase = Phase.Walk;
            }
            else if (m_Pause > 0f)
            {
                CurrentPhase = Phase.Stand;
                m_Pause -= dt;
                if (m_Pause <= 0f)
                {
                    // Leaving a waypoint: turn first if the next leg points back.
                    Vector3 d = m_Waypoints[m_Wp] - transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 1e-4f && Vector3.Angle(transform.forward, d) > TurnAngle)
                    {
                        CurrentPhase = Phase.Turn;
                        m_TurnTimer = 0f;
                        m_TurnFrom = transform.rotation;
                        m_TurnTo = Quaternion.LookRotation(d.normalized, Vector3.up);
                    }
                    else
                    {
                        CurrentPhase = Phase.Walk;
                    }
                }
            }
            else
            {
                CurrentPhase = Phase.Walk;
                Vector3 target = m_Waypoints[m_Wp];
                Vector3 d = target - transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                float step = m_Speed * dt;
                if (dist <= step)
                {
                    transform.position = target;
                    m_Wp = (m_Wp + 1) % m_Waypoints.Count;
                    m_Pause = Range(0.5f, 2f);
                    CurrentPhase = Phase.Stand;
                }
                else
                {
                    Vector3 dir = d / dist;
                    transform.position += dir * step;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(dir, Vector3.up), WalkTurnRate * dt);
                    Velocity = dir * m_Speed;
                }
            }

            // Buzzing near the head or biting makes the human alert.
            float dHead = Vector3.Distance(mosquitoPos, HeadPos);
            if (!mosquitoLanded && dHead < BuzzRadius) m_Buzz += dt;
            else m_Buzz = Mathf.Max(0f, m_Buzz - 0.5f * dt);
            if (m_Buzz >= BuzzToAlert)
            {
                m_AlertTimer = AlertSeconds;
                m_Buzz = 0f;
            }

            if (mosquitoLanded)
            {
                m_BiteTimer += dt;
                if (m_BiteTimer >= 1f)
                {
                    m_BiteTimer -= 1f;
                    if (m_Rng.NextDouble() < m_BiteChance) m_AlertTimer = AlertSeconds;
                }
            }
            else
            {
                m_BiteTimer = 0f;
            }

            if (m_AlertTimer > 0f) m_AlertTimer -= dt;
            GazeDir = Alert ? (mosquitoPos - HeadPos).normalized : transform.forward;
        }

        /// <summary>Line of sight from the head, ignoring the human's own body.</summary>
        public bool CanSee(Vector3 p)
        {
            Vector3 from = HeadPos;
            Vector3 d = p - from;
            float dist = d.magnitude;
            if (dist < 0.2f) return true;
            Vector3 dir = d / dist;
            return !Physics.Raycast(from, dir, dist - 0.01f, Layers.WorldNoHuman, QueryTriggerInteraction.Ignore);
        }

        /// <summary>A walking body (ankles to above the head) fits along a to b: furniture, lamps, towels.</summary>
        static bool PathClear(Vector3 a, Vector3 b)
        {
            Vector3 d = (b - a);
            d.y = 0f;
            return !Physics.CapsuleCast(a + Vector3.up * 0.35f, a + Vector3.up * 1.6f, 0.28f, d.normalized,
                out RaycastHit _, d.magnitude, Layers.WorldNoHuman, QueryTriggerInteraction.Ignore);
        }

        float Range(float a, float b) => a + (float)m_Rng.NextDouble() * (b - a);
    }
}

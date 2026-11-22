using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// The human. Stages 0-1: standing still. Stage 2+: walks between
    /// waypoints (stand, walk, and a turn on the spot when the next leg points
    /// back) and becomes alert when the mosquito buzzes near the head or bites.
    /// Alert (11/22): stops on the spot for 3-5 s, turns toward the buzzing
    /// (1 s) and sweeps the head left and right. The mosquito is spotted when it is within 60° of the head
    /// direction and the line of sight is clear; then the human keeps looking
    /// at it, the alert restarts, and the hand swats once it is within reach
    /// (HandController). Stepped by the agent so that the order of updates is
    /// fixed. The phase drives the character animation.
    /// </summary>
    public class HumanController : MonoBehaviour
    {
        public Transform head;
        public List<SkinZone> skins = new List<SkinZone>();

        public const float BuzzRadius = 0.5f;
        public const float BuzzToAlert = 0.4f;
        public const float AlertMinSeconds = 3f, AlertMaxSeconds = 5f;
        /// <summary>Half-angle of the field of view around the head direction.</summary>
        public const float ViewHalfAngle = 60f;
        /// <summary>Head sweep while alert: ±ScanAmplitude degrees, one side per ScanSideSeconds.</summary>
        public const float ScanAmplitude = 60f, ScanSideSeconds = 1f;
        /// <summary>Turning the body toward the buzzing when the alert starts.</summary>
        public const float SearchTurnSeconds = 1f;
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
        /// <summary>Alert and the mosquito is in view right now.</summary>
        public bool Spotted { get; private set; }
        /// <summary>The mosquito has been spotted at least once since the last reset (logging).</summary>
        public bool SpottedEver { get; private set; }
        /// <summary>HIDE scenario: alert, but frozen (no looking, no clock) until BeginSearch.</summary>
        public bool SearchSuspended { get; private set; }
        public Vector3 HeadPos => head != null ? head.position : transform.position + Vector3.up * 1.6f;
        public Vector3 Chest => transform.position + Vector3.up * 1.2f;
        public Vector3 GazeDir { get; private set; }

        System.Random m_Rng;
        readonly List<Vector3> m_Waypoints = new List<Vector3>();
        int m_Wp;
        float m_Speed, m_Pause, m_AlertTimer, m_AlertDuration, m_ScanTime, m_Buzz, m_BiteTimer, m_BiteChance, m_TurnTimer;
        float m_SearchTurnTimer = -1f; // < 0: not turning toward a sound
        Quaternion m_TurnFrom, m_TurnTo, m_SearchFrom, m_SearchTo;

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
            m_ScanTime = 0f;
            Spotted = SpottedEver = false;
            SearchSuspended = false;
            m_SearchTurnTimer = -1f;
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
            if (SearchSuspended) { Velocity = Vector3.zero; Spotted = false; return; }
            // Walking (paused on the spot while alert)
            Velocity = Vector3.zero;
            if (!Walking || Alert)
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
                StartAlert(mosquitoPos); // heard it: turn toward the buzzing
                m_Buzz = 0f;
            }

            if (mosquitoLanded)
            {
                m_BiteTimer += dt;
                if (m_BiteTimer >= 1f)
                {
                    m_BiteTimer -= 1f;
                    if (m_Rng.NextDouble() < m_BiteChance) StartAlert();
                }
            }
            else
            {
                m_BiteTimer = 0f;
            }

            // Looking: sweep the head while alert; lock on while the mosquito is in view.
            Spotted = false;
            if (Alert)
            {
                Vector3 toMosq = mosquitoPos - HeadPos;
                bool turning = m_SearchTurnTimer >= 0f;
                if (turning)
                {
                    m_SearchTurnTimer += dt;
                    float k = Mathf.Clamp01(m_SearchTurnTimer / SearchTurnSeconds);
                    transform.rotation = Quaternion.Slerp(m_SearchFrom, m_SearchTo, k * k * (3f - 2f * k));
                    if (k >= 1f) m_SearchTurnTimer = -1f;
                }
                // While turning the head faces forward; the sweep starts once the body has turned.
                Vector3 scan = turning ? transform.forward
                    : Quaternion.Euler(0f, ScanAmplitude * Mathf.Sin(Mathf.PI * m_ScanTime / ScanSideSeconds), 0f) * transform.forward;
                Vector3 look = GazeDir.sqrMagnitude > 0.5f ? GazeDir : scan;
                bool inView = Vector3.Angle(look, toMosq) <= ViewHalfAngle || Vector3.Angle(scan, toMosq) <= ViewHalfAngle;
                if (inView && CanSee(mosquitoPos))
                {
                    Spotted = SpottedEver = true;
                    GazeDir = toMosq.normalized;
                    m_AlertTimer = m_AlertDuration; // keeps watching: the alert starts over
                }
                else
                {
                    if (!turning) m_ScanTime += dt;
                    GazeDir = scan;
                }
                m_AlertTimer -= dt;
                if (m_AlertTimer <= 0f) GazeDir = transform.forward;
            }
            else
            {
                GazeDir = transform.forward;
            }
        }

        /// <summary>
        /// Become alert for 3-5 s (sampled per alert from the episode RNG). With a
        /// sound position (buzzing) the body first turns toward it over 1 s;
        /// bites raise the alert without a turn.
        /// </summary>
        public void StartAlert(Vector3? soundFrom = null)
        {
            if (!Alert) m_ScanTime = 0f;
            m_AlertDuration = Range(AlertMinSeconds, AlertMaxSeconds);
            m_AlertTimer = m_AlertDuration;
            if (soundFrom.HasValue)
            {
                Vector3 d = soundFrom.Value - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f)
                {
                    m_SearchFrom = transform.rotation;
                    m_SearchTo = Quaternion.LookRotation(d.normalized, Vector3.up);
                    m_SearchTurnTimer = 0f;
                }
            }
        }

        /// <summary>HIDE scenario: alert (so planners see it) but frozen until BeginSearch.</summary>
        public void PrepareAlert()
        {
            StartAlert();
            SearchSuspended = true;
            Velocity = Vector3.zero;
            CurrentPhase = Phase.Stand;
        }

        /// <summary>HIDE scenario: the choice is made; turn toward the buzzing and start looking.</summary>
        public void BeginSearch(Vector3 soundFrom)
        {
            SearchSuspended = false;
            Spotted = SpottedEver = false;
            StartAlert(soundFrom);
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

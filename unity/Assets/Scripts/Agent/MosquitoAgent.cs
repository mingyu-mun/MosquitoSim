using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MosquitoSim
{
    /// <summary>
    /// Subgoal-conditioned mosquito agent (interface v3).
    /// Behavior Parameters: name "Mosquito", vector obs 59, continuous actions 3.
    /// Decision Requester: period 5, take actions between decisions on.
    /// Stage 0: subgoal is always APPROACH and the target is a skin zone.
    /// </summary>
    [RequireComponent(typeof(MosquitoMotor))]
    public class MosquitoAgent : Agent
    {
        [Tooltip("Episode length in simulated seconds; sets MaxStep if it is 0")]
        public float episodeSeconds = 60f;
        public LayerMask rayMask = ~0;

        public Subgoal CurrentSubgoal { get; private set; } = Subgoal.Approach;
        public Vector3 SubgoalTarget { get; private set; }
        public SkinZone TargetSkin { get; private set; }
        public float FeedProgress => m_FeedTimer;

        MosquitoMotor m_Motor;
        RoomArea m_Area;
        readonly float[] m_Rays = new float[ObsLayout.NumRays];
        readonly float[] m_Obs = new float[ObsLayout.ObsSize];

        // Environment parameters (see configs/stage0.yaml)
        int m_Stage;
        float m_LandingRadius, m_LandingSpeed, m_FeedTime, m_TimePenaltyPerSecond;
        const float k_ProgressCoef = 0.1f;
        const float k_CollisionPenalty = 0.01f;

        float m_FeedTimer;
        float m_PrevSubgoalDist;
        bool m_Succeeded;
        int m_Collisions;
        bool m_HasEpisode;

        public override void Initialize()
        {
            m_Motor = GetComponent<MosquitoMotor>();
            m_Area = GetComponentInParent<RoomArea>();
            if (MaxStep == 0)
                MaxStep = Mathf.RoundToInt(episodeSeconds / Time.fixedDeltaTime);
        }

        public override void OnEpisodeBegin()
        {
            RecordPreviousEpisode();

            var p = Academy.Instance.EnvironmentParameters;
            m_Stage = Mathf.RoundToInt(p.GetWithDefault("stage", 0f));
            m_LandingRadius = p.GetWithDefault("landing_radius", 0.06f);
            m_LandingSpeed = p.GetWithDefault("landing_speed", 0.6f);
            m_FeedTime = p.GetWithDefault("feed_time", 1f);
            m_TimePenaltyPerSecond = p.GetWithDefault("time_penalty", 0.005f);

            m_Area.ResetArea();
            TargetSkin = m_Area.RandomSkinZone();
            m_Motor.ResetMotor(m_Area.SampleFreePoint());

            // Stage 0 trains APPROACH only (final review 1-3). Random subgoals
            // from stage 1 will come from a planner component via SetSubgoal.
            SetSubgoal(Subgoal.Approach, TargetSkin.Center);

            m_FeedTimer = 0f;
            m_Succeeded = false;
            m_Collisions = 0;
            m_HasEpisode = true;
        }

        /// <summary>Called by planners. Resets the progress baseline.</summary>
        public void SetSubgoal(Subgoal subgoal, Vector3 target)
        {
            CurrentSubgoal = subgoal;
            SubgoalTarget = target;
            m_PrevSubgoalDist = Vector3.Distance(transform.position, target);
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            Vector3 pos = transform.position;
            ObsBuilder.CastRays(pos, rayMask, m_Rays);
            var o = new ObsInput
            {
                Velocity = m_Motor.Velocity,
                TargetSkinRel = TargetSkin.Center - pos,
                SubgoalTargetRel = SubgoalTarget - pos,
                Subgoal = CurrentSubgoal,
                RemainingTime01 = MaxStep > 0 ? 1f - (float)StepCount / MaxStep : 1f,
                Landed = m_Motor.IsLanded,
                // Human motion, hand and hazard come in stage 2/3; zero until then.
            };
            ObsBuilder.Build(o, m_Rays, m_Obs);
            sensor.AddObservation(m_Obs);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var a = actions.ContinuousActions;
            var action = new Vector3(Mathf.Clamp(a[0], -1f, 1f), Mathf.Clamp(a[1], -1f, 1f), Mathf.Clamp(a[2], -1f, 1f));
            float dt = Time.fixedDeltaTime;

            MotorStepResult r = m_Motor.Step(action, dt, TargetSkin, m_LandingRadius, m_LandingSpeed);

            if (r.Collided)
            {
                AddReward(-k_CollisionPenalty);
                m_Collisions++;
            }
            AddReward(-m_TimePenaltyPerSecond * dt);

            if (CurrentSubgoal == Subgoal.Approach)
                SubgoalTarget = TargetSkin.Center; // follows a moving human

            float d = Vector3.Distance(transform.position, SubgoalTarget);
            if (!m_Motor.IsLanded)
                AddReward(k_ProgressCoef * (m_PrevSubgoalDist - d));
            m_PrevSubgoalDist = d;

            if (r.TookOff) m_FeedTimer = 0f;
            if (m_Motor.IsLanded && m_Motor.LandedZone == TargetSkin)
            {
                m_FeedTimer += dt;
                if (m_FeedTimer >= m_FeedTime)
                {
                    AddReward(1f);
                    m_Succeeded = true;
                    EndEpisode();
                }
            }
        }

        void RecordPreviousEpisode()
        {
            if (!m_HasEpisode) return;
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Mosquito/Success", m_Succeeded ? 1f : 0f);
            stats.Add("Mosquito/Collisions", m_Collisions);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            // Keyboard control for checking a scene: WASD move, Space/Shift up/down.
            var c = actionsOut.ContinuousActions;
            float x = 0f, y = 0f, z = 0f;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                z = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                y = (k.spaceKey.isPressed ? 1f : 0f) - (k.leftShiftKey.isPressed ? 1f : 0f);
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            x = Input.GetAxisRaw("Horizontal");
            z = Input.GetAxisRaw("Vertical");
            y = (Input.GetKey(KeyCode.Space) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftShift) ? 1f : 0f);
#endif
            c[0] = x;
            c[1] = y;
            c[2] = z;
        }
    }
}

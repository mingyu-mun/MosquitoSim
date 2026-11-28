using Unity.MLAgents;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>Environment split, matching the env_split parameter.</summary>
    public enum EnvSplit
    {
        Train = 0,
        Val = 1,
        EvalFamiliar = 2,
        EvalNewRoom = 3,
        EvalNewObject = 4,
        EvalNewHuman = 5,
        EvalNet = 6,
    }

    public enum RoomType
    {
        R4x3 = 0,
        R5x4 = 1,
        LShape = 2,
        Long6x3 = 3,
        Partition5x5 = 4,
    }

    /// <summary>
    /// Environment parameters for one episode, read from ML-Agents
    /// environment_parameters (all floats). See docs/interface.md.
    /// </summary>
    public struct EnvParams
    {
        public int Stage;
        public EnvSplit Split;
        public PlannerKind Planner;
        public int EvalSeed;
        public float LandingRadius;
        public float LandingSpeed;
        public float FeedTime;
        public float TimePenalty;      // per simulated second
        public float PrevStageMix;     // training only: chance of using stage - 1
        public float BiteAlertChance;  // per second of feeding, stage >= 2
        public int ProbeSubgoal;       // planner = Probe: -1 cycles through the stage's subgoals, 0-3 = Subgoal
        public bool HideScenario;      // planner = External: every episode is the HIDE scenario (11/22)
        public bool PathGuide;         // MOVE_TO / HIDE steer along an obstacle-free route (11/22, default on)
        // Training-only reward experiments (11/24). Defaults reproduce the fixed rules; evaluation ignores them.
        public bool FeedGate;          // no feeding (and no feeding reward) while the subgoal is not APPROACH
        public float AchieveBonus;     // reward for achieving HIDE / MOVE_TO / EVADE
        public float HideHold;         // HIDE: seconds to stay inside the radius
        public float HideHoldReward;   // HIDE: reward per second while inside the radius
        public float MoveToRadius;     // MOVE_TO: arrival radius
        public float ApproachTimePenalty; // extra time penalty per second while APPROACH has not landed yet (11/24)
        public bool SegmentEpisodes;   // training: one subgoal = one learning episode, the world carries on (11/24)
        public float RuleMix;          // training: share of commands chosen by the oracle rule planner instead of at random
        public float Stage0PointMix;
        public float HideRadius;        // HIDE: radius used by training (11/26; evaluation always 0.15 m)
        public float HideArriveBonus;   // HIDE: one-off reward on first entering the radius (11/26)
        public int OracleVariant;
        public bool AlertLethal;        // 11/28: see HumanController.Lethal (default on)
        public bool StrictHuman;        // 11/28: see HumanController.Strict (option, default off)
        public float SpawnMinDist;      // 11/28: the mosquito starts at least this far (horizontal) from the human       // evaluation option (11/28): Planners.OracleVariant
        public float CollisionPenalty;  // per collision step (11/26; default 0.01)   // stage 0: share of commands that are MOVE_TO / HIDE to a free point (11/24)

        public static EnvParams Read()
        {
            var p = Academy.Instance.EnvironmentParameters;
            var e = new EnvParams
            {
                Stage = Mathf.RoundToInt(p.GetWithDefault("stage", 0f)),
                Split = (EnvSplit)Mathf.RoundToInt(p.GetWithDefault("env_split", 0f)),
                Planner = (PlannerKind)Mathf.RoundToInt(p.GetWithDefault("planner", 1f)),
                EvalSeed = Mathf.RoundToInt(p.GetWithDefault("eval_seed", 0f)),
                LandingRadius = p.GetWithDefault("landing_radius", 0.06f),
                LandingSpeed = p.GetWithDefault("landing_speed", 0.6f),
                FeedTime = p.GetWithDefault("feed_time", 1f),
                TimePenalty = p.GetWithDefault("time_penalty", 0.005f),
                PrevStageMix = p.GetWithDefault("prev_stage_mix", 0.2f),
                BiteAlertChance = p.GetWithDefault("bite_alert_chance", 0.25f),
                ProbeSubgoal = Mathf.RoundToInt(p.GetWithDefault("probe_subgoal", -1f)),
                HideScenario = p.GetWithDefault("hide_scenario", 0f) > 0.5f,
                PathGuide = p.GetWithDefault("path_guide", 1f) > 0.5f,
                FeedGate = p.GetWithDefault("feed_gate", 0f) > 0.5f,
                AchieveBonus = p.GetWithDefault("achieve_bonus", 0.3f),
                HideHold = p.GetWithDefault("hide_hold", SubgoalConstants.HideHoldSeconds),
                HideHoldReward = p.GetWithDefault("hide_hold_reward", 0f),
                MoveToRadius = p.GetWithDefault("moveto_radius", SubgoalConstants.MoveToRadius),
                Stage0PointMix = p.GetWithDefault("stage0_point_mix", 0f),
                SegmentEpisodes = p.GetWithDefault("segment_episodes", 0f) > 0.5f,
                RuleMix = p.GetWithDefault("rule_mix", 0f),
                ApproachTimePenalty = p.GetWithDefault("approach_time_penalty", 0f),
                HideRadius = p.GetWithDefault("hide_radius", SubgoalConstants.HideRadius),
                HideArriveBonus = p.GetWithDefault("hide_arrive_bonus", 0f),
                CollisionPenalty = p.GetWithDefault("collision_penalty", 0.01f),
                // Oracle rule A+B confirmed 11/28 (docs/interface.md); 0 = the rule before 11/28. Training keeps 0.
                OracleVariant = Mathf.RoundToInt(p.GetWithDefault("oracle_variant", 2f)),
                // 11/28: the stricter human was tried and set aside (planners too slow for the hand); kept as an option.
                StrictHuman = p.GetWithDefault("human_strict", 0f) > 0.5f,
                AlertLethal = p.GetWithDefault("alert_lethal", 1f) > 0.5f,
                // 11/28: start farther from the human so a straight dash at it gives the human time to react (was 1 m).
                SpawnMinDist = p.GetWithDefault("spawn_min_dist", 2.5f),
            };
            // Auto test (Editor Play without Python): the only place test arguments override parameters.
            if (SimSettings.ManualPlay)
            {
                var s = SimSettings.Instance;
                e.Stage = s.stage;
                e.Split = s.envSplit;
                e.Planner = s.planner;
                e.EvalSeed = s.evalSeed;
                e.ProbeSubgoal = s.probeSubgoal;
            }
            if (AutoTestRunner.Active)
            {
                e.Stage = Mathf.RoundToInt(AutoTestRunner.F("stage", e.Stage));
                e.Split = (EnvSplit)Mathf.RoundToInt(AutoTestRunner.F("env_split", (float)e.Split));
                e.Planner = (PlannerKind)Mathf.RoundToInt(AutoTestRunner.F("planner", (float)e.Planner));
                e.EvalSeed = Mathf.RoundToInt(AutoTestRunner.F("eval_seed", e.EvalSeed));
                // 11/28 fix: evaluation splits use the evaluation values (as evaluate.py sends them), not the stage 0
                // curriculum start (feed 1 s, radius 0.06 m, 0.6 m/s). Before this, auto tests fed in 1 s.
                bool evalSplit = e.Split != EnvSplit.Train;
                e.LandingRadius = AutoTestRunner.F("landing_radius", evalSplit ? 0.02f : e.LandingRadius);
                e.LandingSpeed = AutoTestRunner.F("landing_speed", evalSplit ? 0.3f : e.LandingSpeed);
                e.FeedTime = AutoTestRunner.F("feed_time", evalSplit ? 3f : e.FeedTime);
                e.BiteAlertChance = AutoTestRunner.F("bite_alert_chance", e.BiteAlertChance);
                e.ProbeSubgoal = Mathf.RoundToInt(AutoTestRunner.F("probe_subgoal", e.ProbeSubgoal));
                e.HideScenario = AutoTestRunner.F("hide_scenario", e.HideScenario ? 1f : 0f) > 0.5f;
                e.PathGuide = AutoTestRunner.F("path_guide", e.PathGuide ? 1f : 0f) > 0.5f;
                // Training switches, so a training-mode auto test (env_split=0, planner=1) can check them.
                e.FeedGate = AutoTestRunner.F("feed_gate", e.FeedGate ? 1f : 0f) > 0.5f;
                e.HideHoldReward = AutoTestRunner.F("hide_hold_reward", e.HideHoldReward);
                e.HideHold = AutoTestRunner.F("hide_hold", e.HideHold);
                e.SegmentEpisodes = AutoTestRunner.F("segment_episodes", e.SegmentEpisodes ? 1f : 0f) > 0.5f;
                e.RuleMix = AutoTestRunner.F("rule_mix", e.RuleMix);
                e.OracleVariant = Mathf.RoundToInt(AutoTestRunner.F("oracle_variant", e.OracleVariant));
                e.StrictHuman = AutoTestRunner.F("human_strict", e.StrictHuman ? 1f : 0f) > 0.5f;
                e.AlertLethal = AutoTestRunner.F("alert_lethal", e.AlertLethal ? 1f : 0f) > 0.5f;
                e.SpawnMinDist = AutoTestRunner.F("spawn_min_dist", e.SpawnMinDist);
                e.Stage0PointMix = AutoTestRunner.F("stage0_point_mix", e.Stage0PointMix);
            }
            if (e.Split != EnvSplit.Train)
            {
                // Evaluation always judges by the fixed rules.
                e.FeedGate = false;
                e.AchieveBonus = 0.3f;
                e.HideHold = SubgoalConstants.HideHoldSeconds;
                e.HideHoldReward = 0f;
                e.MoveToRadius = SubgoalConstants.MoveToRadius;
                e.Stage0PointMix = 0f;
                e.SegmentEpisodes = false;
                e.RuleMix = 0f;
                e.ApproachTimePenalty = 0f;
                e.HideRadius = SubgoalConstants.HideRadius;
                e.HideArriveBonus = 0f;
                e.CollisionPenalty = 0.01f;
            }
            return e;
        }

        public bool IsTraining => Split == EnvSplit.Train;

        public RoomType[] RoomTypes()
        {
            if (Split == EnvSplit.EvalNewRoom)
                return new[] { RoomType.Long6x3, RoomType.Partition5x5 };
            return new[] { RoomType.R4x3, RoomType.R5x4, RoomType.LShape };
        }

        public bool UseObstacleSetB => Split == EnvSplit.EvalNewObject;
        public bool UseCues => Split == EnvSplit.EvalNewObject;
        public bool UseNet => Split == EnvSplit.EvalNet;
        public bool FastHuman => Split == EnvSplit.EvalNewHuman;

        public static string SplitName(EnvSplit s)
        {
            switch (s)
            {
                case EnvSplit.Train: return "train";
                case EnvSplit.Val: return "val";
                case EnvSplit.EvalFamiliar: return "eval_familiar";
                case EnvSplit.EvalNewRoom: return "eval_new_room";
                case EnvSplit.EvalNewObject: return "eval_new_object";
                case EnvSplit.EvalNewHuman: return "eval_new_human";
                default: return "eval_net";
            }
        }
    }

    public static class EpisodeSeed
    {
        /// <summary>
        /// Seed for a validation or evaluation episode. Depends only on the
        /// eval seed, split, room and per-room episode index, so every planner
        /// condition gets the same layouts.
        /// </summary>
        public static int Make(int evalSeed, EnvSplit split, int room, int episode)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + evalSeed;
                h = h * 31 + (int)split * 1000003;
                h = h * 31 + room * 7919;
                h = h * 31 + episode;
                return h;
            }
        }

        public static int Derive(int seed, int salt)
        {
            unchecked { return seed * 486187739 + salt * 7919 + 1; }
        }
    }
}

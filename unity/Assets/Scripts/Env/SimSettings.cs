using Unity.MLAgents;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Fixed timestep for the mosquito. At 1.5 m/s a 0.02 s step moves 3 cm,
    /// more than the 2 cm landing tolerance, so the step is 0.01 s (1.5 cm).
    /// Put one in every scene.
    /// Also the settings for pressing Play in the editor without Python
    /// (mlagents-learn or evaluate.py): which stage and split to show, which
    /// planner gives the commands, and who flies (scripted autopilot, or you
    /// with the keyboard for the mosquito you are watching).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SimSettings : MonoBehaviour
    {
        public enum Flight { Autopilot, Keyboard }

        public float fixedDeltaTime = 0.01f;

        [Header("Play in the editor without Python")]
        [Tooltip("Use the values below when no trainer is connected (ignored by training, evaluation and auto tests)")]
        public bool useManualSettings = true;
        [Range(0, 2)] public int stage = 2;
        public EnvSplit envSplit = EnvSplit.Train;
        public PlannerKind planner = PlannerKind.Rule;
        public int evalSeed;
        [Tooltip("Planner = Probe: -1 cycles through the stage's subgoals, 0 APPROACH, 1 HIDE, 2 EVADE, 3 MOVE_TO")]
        public int probeSubgoal = -1;
        [Tooltip("Autopilot: every mosquito flies a scripted straight line to its target.\n" +
                 "Keyboard: you fly the mosquito you are watching (WASD, Space up, Shift down; relative to the camera), the others use the autopilot.")]
        public Flight flight = Flight.Autopilot;
        public MosquitoView.Mode startView = MosquitoView.Mode.Chase;

        public static SimSettings Instance { get; private set; }

        /// <summary>Editor play without Python: the values above apply.</summary>
        public static bool ManualPlay =>
            Instance != null && Instance.useManualSettings && !AutoTestRunner.Active &&
            !(Academy.IsInitialized && Academy.Instance.IsCommunicatorOn);

        void Awake()
        {
            Instance = this;
            Time.fixedDeltaTime = fixedDeltaTime;
        }
    }
}

using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Fixed timestep for the mosquito. At 1.5 m/s a 0.02 s step moves 3 cm,
    /// more than the 2 cm landing tolerance, so the step is 0.01 s (1.5 cm).
    /// Put one in every scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SimSettings : MonoBehaviour
    {
        public float fixedDeltaTime = 0.01f;

        void Awake()
        {
            Time.fixedDeltaTime = fixedDeltaTime;
        }
    }
}

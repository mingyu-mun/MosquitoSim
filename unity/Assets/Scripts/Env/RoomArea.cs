using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// One training room. Holds the room size, the human and its skin zones,
    /// and samples free spawn points. Stage 0: empty box room, standing human.
    /// Room generation and obstacles (stage 1) go in RoomGenerator later.
    /// </summary>
    public class RoomArea : MonoBehaviour
    {
        [Tooltip("Inner size in metres: x width, y height, z depth")]
        public Vector3 roomSize = new Vector3(4f, 2.5f, 3f);

        public Transform human;
        public List<SkinZone> skinZones = new List<SkinZone>();

        [Header("Spawning")]
        public float wallMargin = 0.3f;
        public float minHeight = 0.3f;
        public float maxHeight = 2.2f;
        public float minDistanceFromHuman = 1.0f;
        public float freeRadius = 0.05f;
        public bool randomizeHumanYaw = true;

        void Awake()
        {
            if (human != null && skinZones.Count == 0)
                skinZones.AddRange(human.GetComponentsInChildren<SkinZone>());
        }

        public Bounds InnerBounds =>
            new Bounds(transform.position + Vector3.up * roomSize.y * 0.5f, roomSize);

        public void ResetArea()
        {
            if (human != null && randomizeHumanYaw)
                human.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Physics.SyncTransforms();
        }

        public SkinZone RandomSkinZone()
        {
            return skinZones.Count == 0 ? null : skinZones[Random.Range(0, skinZones.Count)];
        }

        /// <summary>Random point in the room, away from walls, obstacles and the human.</summary>
        public Vector3 SampleFreePoint(int maxTries = 100)
        {
            Vector3 c = transform.position;
            float hx = roomSize.x * 0.5f - wallMargin;
            float hz = roomSize.z * 0.5f - wallMargin;
            Vector3 p = c + new Vector3(0f, 1.5f, 0f);
            for (int i = 0; i < maxTries; i++)
            {
                p = c + new Vector3(Random.Range(-hx, hx), Random.Range(minHeight, maxHeight), Random.Range(-hz, hz));
                if (Physics.CheckSphere(p, freeRadius, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (human != null)
                {
                    Vector3 flat = p - human.position;
                    flat.y = 0f;
                    if (flat.magnitude < minDistanceFromHuman) continue;
                }
                return p;
            }
            Debug.LogWarning($"{name}: no free spawn point after {maxTries} tries");
            return p;
        }
    }
}

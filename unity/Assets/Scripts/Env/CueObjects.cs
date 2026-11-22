using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>Marks a sticky trap. Touching any collider under it fails the episode.</summary>
    public class StickyTrap : MonoBehaviour { }

    /// <summary>
    /// Cue objects for the new-object evaluation: things whose meaning the
    /// rule planner does not see. Skin covers (blanket, long sleeves) make a
    /// skin zone impossible to land on; sticky traps fail the episode on
    /// contact. Neither appears in the language instruction.
    /// </summary>
    public class CueObjects : MonoBehaviour
    {
        readonly List<GameObject> m_Traps = new List<GameObject>();
        readonly List<GameObject> m_Covers = new List<GameObject>();
        readonly List<SkinZone> m_CoveredZones = new List<SkinZone>();

        public readonly List<Vector3> TrapPositions = new List<Vector3>();

        public void Clear()
        {
            foreach (var c in m_Covers) if (c != null) Destroy(c);
            m_Covers.Clear();
            foreach (var z in m_CoveredZones) z.covered = false;
            m_CoveredZones.Clear();
            foreach (var t in m_Traps) t.SetActive(false);
            TrapPositions.Clear();
        }

        public void Place(System.Random rng, RoomArea area, HumanController human)
        {
            Clear();

            // Cover 1-2 skin zones, never all of them.
            var zones = new List<SkinZone>(human.skins);
            int nCover = Mathf.Min(1 + rng.Next(2), zones.Count - 1);
            for (int i = 0; i < nCover; i++)
            {
                int k = rng.Next(zones.Count);
                SkinZone z = zones[k];
                zones.RemoveAt(k);
                z.covered = true;
                m_CoveredZones.Add(z);

                var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cover.name = "Cover_" + z.id;
                cover.layer = Layers.Human;
                cover.transform.SetParent(z.transform, false);
                FitCover(cover.transform, z.Collider);
                cover.GetComponent<Renderer>().material.color = new Color(0.75f, 0.2f, 0.25f);
                m_Covers.Add(cover);
            }

            // 1-2 sticky traps: vertical yellow boards in the air near obstacles or walls.
            int nTraps = 1 + rng.Next(2);
            for (int i = 0; i < nTraps; i++)
            {
                GameObject trap = TrapObject(i);
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    Vector3 p = area.SampleFreePoint(rng, 0.8f, 0.2f);
                    trap.transform.position = p;
                    trap.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    if (Physics.CheckBox(p, new Vector3(0.14f, 0.19f, 0.03f), trap.transform.rotation, ~0,
                            QueryTriggerInteraction.Ignore))
                        continue;
                    trap.SetActive(true);
                    TrapPositions.Add(p);
                    break;
                }
            }
            Physics.SyncTransforms();
        }

        /// <summary>
        /// A box a little larger than the zone collider, in the zone's local space
        /// (works for primitive zones scaled by their transform and for bone zones
        /// sized by collider radius and height).
        /// </summary>
        static void FitCover(Transform cover, Collider c)
        {
            if (c is CapsuleCollider cap)
            {
                float d = cap.radius * 2f * 1.35f, h = cap.height * 1.05f;
                cover.localPosition = cap.center;
                cover.localRotation = Quaternion.identity;
                cover.localScale = cap.direction == 0 ? new Vector3(h, d, d) : cap.direction == 1 ? new Vector3(d, h, d) : new Vector3(d, d, h);
            }
            else if (c is SphereCollider sph)
            {
                cover.localPosition = sph.center;
                cover.localScale = Vector3.one * sph.radius * 2f * 1.3f;
            }
            else
            {
                cover.localScale = Vector3.one * 1.3f;
            }
        }

        public bool NearTrap(Vector3 p, float radius)
        {
            foreach (var t in TrapPositions)
                if (Vector3.Distance(t, p) < radius) return true;
            return false;
        }

        GameObject TrapObject(int i)
        {
            while (m_Traps.Count <= i)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "StickyTrap_" + m_Traps.Count;
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(0.25f, 0.35f, 0.01f);
                go.GetComponent<Renderer>().material.color = new Color(1f, 0.9f, 0.1f);
                go.AddComponent<StickyTrap>();
                go.SetActive(false);
                m_Traps.Add(go);
            }
            return m_Traps[i];
        }
    }
}

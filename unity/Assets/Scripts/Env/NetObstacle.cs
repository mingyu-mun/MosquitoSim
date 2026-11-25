using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Mosquito net around the human (eval_net only): four thin walls and a
    /// roof, 1.4 x 1.4 x 2.0 m, one side open. Sides: 0 = +z, 1 = +x, 2 = -z, 3 = -x
    /// in the net's local frame.
    /// </summary>
    public class NetObstacle : MonoBehaviour
    {
        public const float Half = 0.7f;
        public const float NetHeight = 2.0f;
        const float T = 0.02f;

        readonly Transform[] m_Sides = new Transform[4];
        Transform m_Roof;

        public bool Enabled { get; private set; }
        public int OpenSide { get; private set; }

        static readonly Vector3[] k_Dirs =
        {
            new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(-1f, 0f, 0f),
        };

        void EnsureBuilt()
        {
            if (m_Roof != null) return;
            var color = new Color(0.92f, 0.92f, 0.95f);
            for (int i = 0; i < 4; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "NetSide_" + i;
                go.transform.SetParent(transform, false);
                bool alongX = i % 2 == 0;
                go.transform.localPosition = k_Dirs[i] * Half + Vector3.up * NetHeight * 0.5f;
                go.transform.localScale = alongX ? new Vector3(2f * Half, NetHeight, T) : new Vector3(T, NetHeight, 2f * Half);
                go.GetComponent<Renderer>().material.color = color;
                m_Sides[i] = go.transform;
            }
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "NetRoof";
            roof.transform.SetParent(transform, false);
            roof.transform.localPosition = Vector3.up * NetHeight;
            roof.transform.localScale = new Vector3(2f * Half, T, 2f * Half);
            roof.GetComponent<Renderer>().material.color = color;
            m_Roof = roof.transform;
        }

        public void Place(Vector3 floorCenter, float yaw, int openSide)
        {
            EnsureBuilt();
            transform.position = floorCenter;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            OpenSide = openSide;
            for (int i = 0; i < 4; i++) m_Sides[i].gameObject.SetActive(i != openSide);
            m_Roof.gameObject.SetActive(true);
            gameObject.SetActive(true);
            Enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>Open another side (the net stays where it is).</summary>
        public void SetOpenSide(int openSide)
        {
            EnsureBuilt();
            OpenSide = openSide;
            for (int i = 0; i < 4; i++) m_Sides[i].gameObject.SetActive(i != openSide);
            Physics.SyncTransforms();
        }

        /// <summary>Point 0.3 m inside the open side, at height y (the way in, seen from inside).</summary>
        public Vector3 OpeningInside(float y) => transform.TransformPoint(k_Dirs[OpenSide] * (Half - 0.3f) + Vector3.up * y);

        /// <summary>Point 0.3 m outside the open side, at height y.</summary>
        public Vector3 OpeningOutside(float y) => SideOutsidePoint(OpenSide, y);

        /// <summary>
        /// Point outside corner i (0: +x+z, 1: +x-z, 2: -x-z, 3: -x+z), as far out as the side points, at
        /// height y. With the side points they ring the net, so the mosquito can go round it in straight legs.
        /// </summary>
        public Vector3 CornerOutsidePoint(int i, float y)
        {
            float a = Half + 0.3f;
            Vector3 c = i == 0 ? new Vector3(a, 0f, a) : i == 1 ? new Vector3(a, 0f, -a)
                      : i == 2 ? new Vector3(-a, 0f, -a) : new Vector3(-a, 0f, a);
            return transform.TransformPoint(c + Vector3.up * y);
        }

        public void Disable()
        {
            EnsureBuilt();
            gameObject.SetActive(false);
            Enabled = false;
        }

        /// <summary>Point 0.3 m outside the middle of side i, at height y.</summary>
        public Vector3 SideOutsidePoint(int i, float y)
        {
            return transform.TransformPoint(k_Dirs[i] * (Half + 0.3f) + Vector3.up * y);
        }

        public bool Contains(Vector3 p)
        {
            if (!Enabled) return false;
            Vector3 l = transform.InverseTransformPoint(p);
            return Mathf.Abs(l.x) < Half + 0.05f && Mathf.Abs(l.z) < Half + 0.05f && l.y < NetHeight + 0.05f;
        }
    }
}

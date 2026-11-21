using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Builds the room shell from a pool of boxes. The room origin is the
    /// floor centre; walls are 10 cm thick and 2.5 m high.
    /// Train/val/familiar: 4x3, 5x4, L (5x4 with a 2x2 block in one corner).
    /// New room: 6x3 long room, 5x5 with a 3 m partition.
    /// Each episode the floor plan is stretched by -8..+15 % per axis and the
    /// walls, floor and ceiling get colours from a palette (sometimes one
    /// accent wall), so no two episodes look the same.
    /// </summary>
    public class RoomGenerator : MonoBehaviour
    {
        public const float WallThickness = 0.1f;
        public const float Height = 2.5f;

        readonly List<Transform> m_Boxes = new List<Transform>();

        public RoomType Current { get; private set; }
        public Vector3 Size { get; private set; } = new Vector3(4f, Height, 3f);

        public static Vector3 SizeOf(RoomType type)
        {
            switch (type)
            {
                case RoomType.R5x4: return new Vector3(5f, Height, 4f);
                case RoomType.LShape: return new Vector3(5f, Height, 4f);
                case RoomType.Long6x3: return new Vector3(6f, Height, 3f);
                case RoomType.Partition5x5: return new Vector3(5f, Height, 5f);
                default: return new Vector3(4f, Height, 3f);
            }
        }

        static readonly Color[] s_Paint =
        {
            new Color(0.88f, 0.86f, 0.80f), new Color(0.93f, 0.92f, 0.90f), new Color(0.80f, 0.84f, 0.86f),
            new Color(0.86f, 0.80f, 0.72f), new Color(0.76f, 0.82f, 0.74f), new Color(0.90f, 0.82f, 0.78f),
            new Color(0.70f, 0.74f, 0.80f), new Color(0.84f, 0.84f, 0.76f),
        };
        static readonly Color[] s_Accent =
        {
            new Color(0.35f, 0.45f, 0.55f), new Color(0.55f, 0.40f, 0.35f), new Color(0.40f, 0.52f, 0.42f),
            new Color(0.60f, 0.55f, 0.40f), new Color(0.45f, 0.40f, 0.50f),
        };
        static readonly Color[] s_Floor =
        {
            new Color(0.55f, 0.40f, 0.26f), new Color(0.68f, 0.52f, 0.36f), new Color(0.40f, 0.29f, 0.20f),
            new Color(0.76f, 0.70f, 0.60f), new Color(0.55f, 0.55f, 0.55f), new Color(0.80f, 0.78f, 0.74f),
        };

        public void Build(RoomType type) => Build(type, null);

        /// <summary>Build the shell; with an RNG the size and colours vary.</summary>
        public void Build(RoomType type, System.Random rng)
        {
            Vector3 s = SizeOf(type);
            if (rng != null)
            {
                s.x = Mathf.Round(s.x * RoomPlacement.Range(rng, 0.92f, 1.15f) * 20f) / 20f;
                s.z = Mathf.Round(s.z * RoomPlacement.Range(rng, 0.92f, 1.15f) * 20f) / 20f;
            }
            const float t = WallThickness;
            int n = 0;
            SetBox(n++, "Floor", new Vector3(0f, -t / 2f, 0f), new Vector3(s.x + 2f * t, t, s.z + 2f * t));
            SetBox(n++, "Ceiling", new Vector3(0f, s.y + t / 2f, 0f), new Vector3(s.x + 2f * t, t, s.z + 2f * t));
            // North/south walls cover the corners too, so no light leaks through the seams.
            SetBox(n++, "Wall_N", new Vector3(0f, s.y / 2f, s.z / 2f + t / 2f), new Vector3(s.x + 2f * t, s.y, t));
            SetBox(n++, "Wall_S", new Vector3(0f, s.y / 2f, -s.z / 2f - t / 2f), new Vector3(s.x + 2f * t, s.y, t));
            SetBox(n++, "Wall_E", new Vector3(s.x / 2f + t / 2f, s.y / 2f, 0f), new Vector3(t, s.y, s.z));
            SetBox(n++, "Wall_W", new Vector3(-s.x / 2f - t / 2f, s.y / 2f, 0f), new Vector3(t, s.y, s.z));
            if (type == RoomType.LShape)
                SetBox(n++, "Corner", new Vector3(s.x / 2f - 1f, s.y / 2f, s.z / 2f - 1f), new Vector3(2f, s.y, 2f));
            if (type == RoomType.Partition5x5)
                SetBox(n++, "Partition", new Vector3(0f, s.y / 2f, -s.z / 2f + 1.5f), new Vector3(t, s.y, 3f));

            for (int i = n; i < m_Boxes.Count; i++)
                m_Boxes[i].gameObject.SetActive(false);

            Current = type;
            Size = s;
            if (rng != null) Style(rng);
        }

        void Style(System.Random rng)
        {
            Color wall = s_Paint[rng.Next(s_Paint.Length)];
            Color floor = s_Floor[rng.Next(s_Floor.Length)];
            int accent = rng.NextDouble() < 0.35 ? rng.Next(4) : -1;
            Color accentColor = s_Accent[rng.Next(s_Accent.Length)];
            string[] walls = { "Wall_N", "Wall_S", "Wall_E", "Wall_W" };
            foreach (var b in m_Boxes)
            {
                if (!b.gameObject.activeSelf) continue;
                var r = b.GetComponent<Renderer>();
                Color c = wall;
                if (b.name == "Floor") c = floor;
                else if (b.name == "Ceiling") c = new Color(0.95f, 0.95f, 0.93f);
                else if (accent >= 0 && b.name == walls[accent]) c = accentColor;
                r.material.color = c;
            }
        }

        void SetBox(int index, string boxName, Vector3 localCenter, Vector3 size)
        {
            while (m_Boxes.Count <= index)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(transform, false);
                go.GetComponent<Renderer>().material.color = new Color(0.85f, 0.84f, 0.8f);
                m_Boxes.Add(go.transform);
            }
            Transform b = m_Boxes[index];
            b.name = boxName;
            b.gameObject.SetActive(true);
            b.localPosition = localCenter;
            b.localRotation = Quaternion.identity;
            b.localScale = size;
        }
    }
}

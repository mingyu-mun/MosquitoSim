using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>Floor area that must stay free (in front of a door).</summary>
    public struct ClearZone
    {
        public Vector3 Center;
        public Quaternion Rot;
        public Vector2 Half; // x, z
    }

    /// <summary>
    /// Shared placement geometry for furniture and room decor. Walls are
    /// numbered 0 north (+Z), 1 south, 2 east (+X), 3 west; an item against a
    /// wall faces into the room (yaw 180, 0, 270, 90).
    /// </summary>
    public static class RoomPlacement
    {
        public const float BodyMargin = 0.35f; // half the width of the walking human

        public static float WallYaw(int side) => side == 0 ? 180f : side == 1 ? 0f : side == 2 ? 270f : 90f;

        /// <summary>Half length of wall <paramref name="side"/> (inner).</summary>
        public static float WallHalfLength(RoomArea area, int side) => side < 2 ? area.Size.x * 0.5f : area.Size.z * 0.5f;

        /// <summary>
        /// Floor point at <paramref name="u"/> along wall <paramref name="side"/>
        /// (0 = wall middle), <paramref name="inset"/> m out from the wall.
        /// </summary>
        public static Vector3 WallPoint(RoomArea area, int side, float u, float inset)
        {
            float hx = area.Size.x * 0.5f, hz = area.Size.z * 0.5f;
            Vector3 c;
            switch (side)
            {
                case 0: c = new Vector3(u, 0f, hz - inset); break;
                case 1: c = new Vector3(u, 0f, -hz + inset); break;
                case 2: c = new Vector3(hx - inset, 0f, u); break;
                default: c = new Vector3(-hx + inset, 0f, u); break;
            }
            return area.transform.position + c;
        }

        /// <summary>A box (centre, half extents, rotation) overlaps any collider.</summary>
        public static bool Blocked(Vector3 center, Vector3 half, Quaternion rot) =>
            Physics.CheckBox(center, half, rot,
                ~(1 << Layers.Labels | 1 << Layers.DebugViz | 1 << Layers.MosquitoModel | 1 << Layers.Human),
                QueryTriggerInteraction.Ignore); // the human moves; its route is checked separately

        static bool Covers(Quaternion inv, Vector3 c, Vector2 fp, Vector3 q, float margin)
        {
            Vector3 l = inv * (q - c);
            return Mathf.Abs(l.x) < fp.x * 0.5f + margin && Mathf.Abs(l.z) < fp.y * 0.5f + margin;
        }

        /// <summary>
        /// Whether a footprint (x width, y depth, rotated by <paramref name="rot"/>)
        /// covers the human's start spot (plus keepAway) or the walking route (plus
        /// half a body), the route closed as a loop.
        /// </summary>
        public static bool OnRoute(Vector3 c, Quaternion rot, Vector2 fp, HumanController human, float keepAway)
        {
            if (human == null) return false;
            var route = human.Route;
            Quaternion inv = Quaternion.Inverse(rot);
            Vector3 start = route.Count > 0 ? route[0] : human.transform.position;
            if (Covers(inv, c, fp, start, keepAway)) return true;
            for (int i = 0; i < route.Count && route.Count > 1; i++)
            {
                Vector3 a = route[i], b = route[(i + 1) % route.Count];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.2f));
                for (int k = 0; k <= n; k++)
                    if (Covers(inv, c, fp, Vector3.Lerp(a, b, k / (float)n), BodyMargin)) return true;
            }
            return false;
        }

        /// <summary>Whether a footprint overlaps any keep-clear zone (door swing areas).</summary>
        public static bool InClearZone(Vector3 c, Quaternion rot, Vector2 fp, IList<ClearZone> zones)
        {
            if (zones == null) return false;
            foreach (var z in zones)
            {
                // Separating axis test of two rectangles on the floor.
                if (RectsOverlap(c, rot, fp * 0.5f, z.Center, z.Rot, z.Half)) return true;
            }
            return false;
        }

        static bool RectsOverlap(Vector3 ca, Quaternion ra, Vector2 ha, Vector3 cb, Quaternion rb, Vector2 hb)
        {
            Vector3 ax = ra * Vector3.right, az = ra * Vector3.forward, bx = rb * Vector3.right, bz = rb * Vector3.forward;
            Vector3 d = cb - ca;
            d.y = 0f;
            foreach (var axis in new[] { ax, az, bx, bz })
            {
                float ra1 = ha.x * Mathf.Abs(Vector3.Dot(ax, axis)) + ha.y * Mathf.Abs(Vector3.Dot(az, axis));
                float rb1 = hb.x * Mathf.Abs(Vector3.Dot(bx, axis)) + hb.y * Mathf.Abs(Vector3.Dot(bz, axis));
                if (Mathf.Abs(Vector3.Dot(d, axis)) > ra1 + rb1) return false;
            }
            return true;
        }

        public static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
    
        /// <summary>
        /// The room's convex floor corners (local XZ, inset by <paramref name="inset"/>). An L-shaped
        /// room has the (+x,+z) corner cut out by a 2 m block, so that corner becomes two corners
        /// on either side of the block. Overview cameras hang in these corners.
        /// </summary>
        public static Vector2[] Corners(RoomArea area, float inset)
        {
            float hx = area.Size.x * 0.5f - inset, hz = area.Size.z * 0.5f - inset;
            bool l = area.Generator != null && area.Generator.Current == RoomType.LShape;
            if (!l) return new[] { new Vector2(hx, hz), new Vector2(-hx, hz), new Vector2(-hx, -hz), new Vector2(hx, -hz) };
            return new[] { new Vector2(hx, hz - 2f), new Vector2(-hx, hz), new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx - 2f, hz) };
        }

        /// <summary>
        /// Where the overview cameras look (world, 0.8 m up): the middle of the floor, which for an
        /// L-shaped room is the middle of the L, not of its bounding box.
        /// </summary>
        public static Vector3 OverviewLook(RoomArea area)
        {
            Vector3 s = area.Size, o = area.transform.position;
            if (area.Generator == null || area.Generator.Current != RoomType.LShape) return o + Vector3.up * 0.8f;
            float a = s.x * s.z;
            return o + new Vector3(-4f * (s.x / 2f - 1f) / (a - 4f), 0.8f, -4f * (s.z / 2f - 1f) / (a - 4f));
        }

        /// <summary>
        /// True if a tall piece at <paramref name="c"/> (radius <paramref name="r"/>) stands in an
        /// overview camera's line of sight to the middle of the room. run21: a closet halfway along
        /// a wall hid the person from one camera completely.
        /// </summary>
        public static bool BlocksOverview(RoomArea area, Vector3 c, float r)
        {
            Vector3 look = OverviewLook(area), o = area.transform.position;
            Vector2 q = new Vector2(c.x - o.x, c.z - o.z), b = new Vector2(look.x - o.x, look.z - o.z);
            foreach (var a in Corners(area, 0.15f))
            {
                Vector2 ab = new Vector2(b.x - a.x, b.y - a.y);
                float len2 = ab.x * ab.x + ab.y * ab.y;
                float k = len2 < 1e-4f ? 0f : Mathf.Clamp01(((q.x - a.x) * ab.x + (q.y - a.y) * ab.y) / len2);
                float dx = q.x - (a.x + ab.x * k), dz = q.y - (a.y + ab.y * k);
                if (dx * dx + dz * dz < (r + 0.35f) * (r + 0.35f)) return true;
            }
            return false;
        }
}
}

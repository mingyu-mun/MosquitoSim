using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// One piece of furniture made from an asset prefab (Furniture Mega Pack) at
    /// scene build. Holds what placement needs, measured at build with the item
    /// at the origin and no rotation: the box size, where the box centre is from
    /// the pivot, and which way the front faces.
    /// </summary>
    public class FurnitureItem : MonoBehaviour
    {
        public enum Placement { Wall, Free, NearTable }

        public string category;          // Bed, Sofa, Table, Chair, Closet, Drawer, Refrigerator, ...
        public bool setB;                // new-object evaluation set
        public Placement placement;
        public Vector3 size;             // axis-aligned box, metres (x width, y height, z depth)
        public Vector3 centerOffset;     // box centre minus pivot, unrotated
        [Tooltip("Yaw (deg) that turns the model's front to +Z")]
        public float frontYaw;

        public float Bottom => centerOffset.y - size.y * 0.5f;

        /// <summary>Footprint when the front faces +Z (front yaw applied).</summary>
        public Vector2 Footprint
        {
            get
            {
                bool swap = Mathf.Abs(Mathf.DeltaAngle(frontYaw, 90f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(frontYaw, 270f)) < 45f;
                return swap ? new Vector2(size.z, size.x) : new Vector2(size.x, size.z);
            }
        }

        /// <summary>Put the box centre at <paramref name="centerXZ"/> (floor height) facing <paramref name="faceYaw"/>.</summary>
        public void Place(Vector3 centerXZ, float faceYaw, float floorY)
        {
            Quaternion rot = Quaternion.Euler(0f, faceYaw + frontYaw, 0f);
            Vector3 off = rot * new Vector3(centerOffset.x, 0f, centerOffset.z);
            transform.SetPositionAndRotation(new Vector3(centerXZ.x - off.x, floorY - Bottom, centerXZ.z - off.z), rot);
        }
    }
}

using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Animated character for the human (Mixamo humanoid). Sits on the character
    /// root next to its Animator. At start it builds the physics body on the
    /// bones: the five skin zones S1..S5 (head, forearms, calves) and non-skin
    /// obstacles (torso, upper arms, thighs, neck, feet), all on the Human layer,
    /// so they move with the animation and the IK. It then hands the zones to
    /// HumanController, which no longer has primitive body parts.
    /// Animation: idle/walk blended by the walking speed; while alert the head
    /// turns to the mosquito (look-at IK); while the hand winds up or swings the
    /// right hand follows HandController's hand point (hand IK).
    /// </summary>
    [DefaultExecutionOrder(-50)] // before HumanController.Awake collects the zones
    [RequireComponent(typeof(Animator))]
    public class HumanAvatar : MonoBehaviour
    {
        public const string SpeedParam = "Speed";
        public const string TurnParam = "Turning";

        public HumanController human;
        public HandController hand;
        [Tooltip("Speed of the walk clip in m/s (from the clip at scene build)")]
        public float walkClipSpeed = 1.3f;
        [Tooltip("Length of the 180° turn clip in s (0 = no turn clip)")]
        public float turnClipLength;

        Animator m_Anim;
        float m_LookWeight, m_HandWeight;

        // Radii in metres.
        const float HeadRadius = 0.1f, ForearmRadius = 0.04f, CalfRadius = 0.055f;
        const float TorsoRadius = 0.14f, UpperArmRadius = 0.05f, ThighRadius = 0.08f, NeckRadius = 0.05f, FootRadius = 0.045f;

        void Awake()
        {
            m_Anim = GetComponent<Animator>();
            if (human == null) human = GetComponentInParent<HumanController>();
            if (turnClipLength > 0.1f) human.turnSeconds = turnClipLength;
            Build();
        }

        void Build()
        {
            human.skins.Clear();

            // Head: sphere between the head bone and the top of the skull.
            Transform head = B(HumanBodyBones.Head);
            Transform top = head.childCount > 0 ? FindTop(head) : null;
            Vector3 headCentre = top != null ? (head.position + top.position) * 0.5f : head.position + Vector3.up * 0.1f;
            var s1 = Sphere(head, "Skin_Head", headCentre, HeadRadius);
            human.skins.Add(Skin(s1, "S1", "머리"));
            human.head = s1.transform;

            human.skins.Add(Skin(Capsule(B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), "Skin_Forearm_L", ForearmRadius), "S2", "왼팔"));
            human.skins.Add(Skin(Capsule(B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), "Skin_Forearm_R", ForearmRadius), "S3", "오른팔"));
            human.skins.Add(Skin(Capsule(B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), "Skin_Calf_L", CalfRadius), "S4", "왼쪽 종아리"));
            human.skins.Add(Skin(Capsule(B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), "Skin_Calf_R", CalfRadius), "S5", "오른쪽 종아리"));

            // Clothed body parts block flight but are not landing targets.
            Capsule(B(HumanBodyBones.Hips), B(HumanBodyBones.Neck), "Body_Torso", TorsoRadius);
            Capsule(B(HumanBodyBones.Neck), head, "Body_Neck", NeckRadius);
            Capsule(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), "Body_UpperArm_L", UpperArmRadius);
            Capsule(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), "Body_UpperArm_R", UpperArmRadius);
            Capsule(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), "Body_Thigh_L", ThighRadius);
            Capsule(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), "Body_Thigh_R", ThighRadius);
            Transform toesL = B(HumanBodyBones.LeftToes), toesR = B(HumanBodyBones.RightToes);
            if (toesL != null) Capsule(B(HumanBodyBones.LeftFoot), toesL, "Body_Foot_L", FootRadius);
            if (toesR != null) Capsule(B(HumanBodyBones.RightFoot), toesR, "Body_Foot_R", FootRadius);
        }

        Transform B(HumanBodyBones b) => m_Anim.GetBoneTransform(b);

        static Transform FindTop(Transform head)
        {
            // Mixamo: mixamorig:HeadTop_End; otherwise the farthest child above the head.
            Transform best = null;
            float bestY = float.MinValue;
            foreach (Transform c in head)
            {
                if (c.name.Contains("HeadTop")) return c;
                if (c.position.y > bestY) { bestY = c.position.y; best = c; }
            }
            return best != null && best.position.y > head.position.y + 0.05f ? best : null;
        }

        static SkinZone Skin(Collider c, string id, string label)
        {
            var z = c.gameObject.AddComponent<SkinZone>();
            z.id = id;
            z.zoneName = label;
            return z;
        }

        static GameObject Part(Transform bone, string partName, Vector3 worldPos, Quaternion worldRot)
        {
            var go = new GameObject(partName) { layer = Layers.Human };
            go.transform.SetParent(bone, false);
            go.transform.SetPositionAndRotation(worldPos, worldRot);
            Vector3 s = bone.lossyScale;
            go.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z); // colliders in metres
            return go;
        }

        static SphereCollider Sphere(Transform bone, string partName, Vector3 centre, float r)
        {
            var c = Part(bone, partName, centre, Quaternion.identity).AddComponent<SphereCollider>();
            c.radius = r;
            return c;
        }

        /// <summary>Capsule from bone a to bone b (axis = local Y), attached to a.</summary>
        static CapsuleCollider Capsule(Transform a, Transform b, string partName, float r)
        {
            Vector3 d = b.position - a.position;
            float len = d.magnitude;
            Quaternion rot = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            var c = Part(a, partName, (a.position + b.position) * 0.5f, rot).AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = r;
            c.height = len + r; // round ends reach about half a radius past each joint
            return c;
        }

        void FixedUpdate()
        {
            Vector3 v = human.Velocity;
            v.y = 0f;
            m_Anim.SetFloat(SpeedParam, Mathf.Min(v.magnitude, walkClipSpeed));
            if (turnClipLength > 0.1f) m_Anim.SetBool(TurnParam, human.Turning);
            // Faster than the clip: play it faster so the feet keep up.
            m_Anim.speed = v.magnitude > walkClipSpeed ? v.magnitude / walkClipSpeed : 1f;
        }

        /// <summary>
        /// Left shoulder height minus left hand height, in metres: about 0.5 for a
        /// hanging arm, about 0 in a T-pose (auto test checks for T-poses with it).
        /// </summary>
        public float ArmDrop
        {
            get
            {
                Transform s = m_Anim.GetBoneTransform(HumanBodyBones.LeftUpperArm), h = m_Anim.GetBoneTransform(HumanBodyBones.LeftHand);
                return s != null && h != null ? s.position.y - h.position.y : 0f;
            }
        }

        public string StateName
        {
            get
            {
                var info = m_Anim.GetCurrentAnimatorStateInfo(0);
                return m_Anim.IsInTransition(0) ? "blend" : info.IsName("Turn") ? "Turn" : "Locomotion";
            }
        }

        void OnAnimatorIK(int layer)
        {
            float dt = Time.deltaTime;
            m_LookWeight = Mathf.MoveTowards(m_LookWeight, human.Alert ? 1f : 0f, 4f * dt);
            m_Anim.SetLookAtWeight(m_LookWeight, 0.2f, 0.9f, 0f, 0.6f);
            m_Anim.SetLookAtPosition(human.HeadPos + human.GazeDir);

            bool swat = hand != null && hand.Current != HandController.State.Rest;
            m_HandWeight = Mathf.MoveTowards(m_HandWeight, swat ? 1f : 0f, 8f * dt);
            if (hand != null)
            {
                m_Anim.SetIKPositionWeight(AvatarIKGoal.RightHand, m_HandWeight);
                m_Anim.SetIKPosition(AvatarIKGoal.RightHand, hand.Position);
            }
        }
    }
}

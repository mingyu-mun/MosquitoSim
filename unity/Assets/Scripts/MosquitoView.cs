// Debug view of the main camera for checking the simulation by eye.
// Three modes: Room (fixed view of the room, the old main camera), Chase
// (behind the mosquito, looking where it flies) and FirstPerson (from the
// mosquito). A yellow line and cross mark the current subgoal target (a
// LineRenderer on the DebugViz layer, which planner cameras and still shots
// skip, so the VLM input never shows it); a small HUD shows the command and state.
// Keys in Play mode: V next view (room, chase, first person, free); Q/E, [ ], comma/period or PageUp/PageDown previous/next
// room, 1-9/0 jump to room 0-9; H toggles the HUD. The HUD also has buttons.
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine;

namespace MosquitoSim
{
    [RequireComponent(typeof(Camera))]
    public class MosquitoView : MonoBehaviour
    {
        public enum Mode { Room, Chase, FirstPerson, Free }

        public Mode mode = Mode.Room;
        public int roomIndex;
        public bool showHud = true;
        public float chaseDistance = 0.08f; // close enough to see the 8 mm mosquito model
        public float chaseHeight = 0.02f;

        public static MosquitoView Instance { get; private set; }

        Camera m_Cam;
        Vector3 m_RoomPos;
        Quaternion m_RoomRot;
        float m_RoomFov, m_RoomNear;
        Vector3 m_Fwd = Vector3.forward;
        MosquitoAgent[] m_Agents = new MosquitoAgent[0];
        float m_NextFind;
        LineRenderer m_Line;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<MosquitoView>() == null) cam.gameObject.AddComponent<MosquitoView>();
        }

        public static Mode Parse(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "chase": return Mode.Chase;
                case "fp":
                case "first":
                case "firstperson": return Mode.FirstPerson;
                case "free": return Mode.Free;
                default: return Mode.Room;
            }
        }

        void Awake()
        {
            Instance = this;
            m_Cam = GetComponent<Camera>();
            m_RoomPos = transform.position;
            m_RoomRot = transform.rotation;
            m_RoomFov = m_Cam.fieldOfView;
            m_RoomNear = m_Cam.nearClipPlane;
            if (!AutoTestRunner.Active && SimSettings.Instance != null && SimSettings.Instance.useManualSettings)
                mode = SimSettings.Instance.startView;

            var go = new GameObject("TargetMarker") { layer = Layers.DebugViz };
            m_Line = go.AddComponent<LineRenderer>();
            m_Line.useWorldSpace = true;
            m_Line.positionCount = 10;
            m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.receiveShadows = false;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            m_Line.sharedMaterial = new Material(sh) { color = new Color(1f, 0.85f, 0f) };
            m_Line.startColor = m_Line.endColor = new Color(1f, 0.85f, 0f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public MosquitoAgent Agent
        {
            get
            {
                if (Time.unscaledTime >= m_NextFind || m_Agents.Length == 0)
                {
                    m_NextFind = Time.unscaledTime + 1f;
                    m_Agents = FindObjectsByType<MosquitoAgent>(FindObjectsSortMode.None);
                }
                foreach (var a in m_Agents)
                    if (a != null && a.Area != null && a.Area.roomIndex == roomIndex) return a;
                return null;
            }
        }

        static readonly KeyCode[] s_NextRoomKeys = { KeyCode.RightBracket, KeyCode.Period, KeyCode.E, KeyCode.PageDown, KeyCode.KeypadPlus };
        static readonly KeyCode[] s_PrevRoomKeys = { KeyCode.LeftBracket, KeyCode.Comma, KeyCode.Q, KeyCode.PageUp, KeyCode.KeypadMinus };

        int RoomCount
        {
            get
            {
                if (m_Agents.Length == 0) { var _ = Agent; }
                return Mathf.Max(1, m_Agents.Length);
            }
        }

        public void ChangeRoom(int delta) => roomIndex = ((roomIndex + delta) % RoomCount + RoomCount) % RoomCount;

        void Update()
        {
            // Several keys per action: bracket keys are awkward or remapped on some
            // keyboard layouts (11/21: [ and ] did not switch rooms on the user's PC).
            bool next = false, hud = false;
            int roomDelta = 0, jump = -1;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                next = k.vKey.wasPressedThisFrame;
                hud = k.hKey.wasPressedThisFrame;
                if (k.rightBracketKey.wasPressedThisFrame || k.periodKey.wasPressedThisFrame || k.eKey.wasPressedThisFrame ||
                    k.pageDownKey.wasPressedThisFrame || k.numpadPlusKey.wasPressedThisFrame) roomDelta++;
                if (k.leftBracketKey.wasPressedThisFrame || k.commaKey.wasPressedThisFrame || k.qKey.wasPressedThisFrame ||
                    k.pageUpKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) roomDelta--;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            next = Input.GetKeyDown(KeyCode.V);
            hud = Input.GetKeyDown(KeyCode.H);
            foreach (var key in s_NextRoomKeys) if (Input.GetKeyDown(key)) { roomDelta++; break; }
            foreach (var key in s_PrevRoomKeys) if (Input.GetKeyDown(key)) { roomDelta--; break; }
            // Number keys: 1..9 -> rooms 0..8, 0 -> room 9.
            for (int i = 0; i < 10; i++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                    jump = i == 0 ? 9 : i - 1;
#endif
            if (next)
            {
                mode = (Mode)(((int)mode + 1) % 4);
                m_FreeInit = false;
            }
            if (roomDelta != 0) ChangeRoom(roomDelta);
            if (jump >= 0 && jump < RoomCount) roomIndex = jump;
            if (hud) showHud = !showHud;
        }

        void LateUpdate()
        {
            var a = Agent;
            if (a == null) return;
            UpdateHeading(a, Time.deltaTime);
            if (mode == Mode.Free) FreeCamera(a);
            else Place(mode, a);
            UpdateLine(a);
        }

        // ------------------------------------------------------------ free camera

        public float freeSpeed = 1.5f;   // m/s, mouse wheel changes it
        public float lookSpeed = 3f;     // degrees per mouse unit
        float m_Yaw, m_Pitch;
        bool m_FreeInit;

        /// <summary>
        /// Free camera: hold the right mouse button to look, WASD to move, Space/Ctrl
        /// (or C) up/down, Shift for 4x speed, wheel for base speed, F jumps behind
        /// the mosquito of the shown room. Real time, so it also works when paused.
        /// </summary>
        void FreeCamera(MosquitoAgent a)
        {
            if (!m_FreeInit)
            {
                Vector3 e = transform.eulerAngles;
                m_Yaw = e.y;
                m_Pitch = e.x > 180f ? e.x - 360f : e.x;
                m_FreeInit = true;
            }
            m_Cam.fieldOfView = 70f;
            m_Cam.nearClipPlane = 0.005f;
            m_Cam.cullingMask = Layers.AllButLabels;
            float dt = Time.unscaledDeltaTime;
            Vector3 move = Vector3.zero;
            bool fast = false, jump = false;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var m = Mouse.current;
            if (m != null && m.rightButton.isPressed)
            {
                Vector2 d = m.delta.ReadValue() * 0.1f;
                m_Yaw += d.x * lookSpeed;
                m_Pitch -= d.y * lookSpeed;
            }
            if (m != null) freeSpeed = Mathf.Clamp(freeSpeed * (1f + 0.001f * m.scroll.ReadValue().y), 0.1f, 10f);
            if (k != null)
            {
                move.x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                move.z = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                move.y = (k.spaceKey.isPressed ? 1f : 0f) - (k.leftCtrlKey.isPressed || k.cKey.isPressed ? 1f : 0f);
                fast = k.leftShiftKey.isPressed;
                jump = k.fKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButton(1))
            {
                m_Yaw += Input.GetAxis("Mouse X") * lookSpeed;
                m_Pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
            }
            freeSpeed = Mathf.Clamp(freeSpeed * (1f + 0.15f * Input.mouseScrollDelta.y), 0.1f, 10f);
            move.x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            move.z = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            move.y = (Input.GetKey(KeyCode.Space) ? 1f : 0f) -
                     (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C) ? 1f : 0f);
            fast = Input.GetKey(KeyCode.LeftShift);
            jump = Input.GetKeyDown(KeyCode.F);
#endif
            m_Pitch = Mathf.Clamp(m_Pitch, -89f, 89f);
            var rot = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
            Vector3 pos = transform.position;
            if (jump)
            {
                // Behind and above the mosquito, looking at it.
                Vector3 p = a.transform.position;
                pos = p - m_Fwd * 0.4f + Vector3.up * 0.15f;
                rot = Quaternion.LookRotation(p - pos, Vector3.up);
                Vector3 e = rot.eulerAngles;
                m_Yaw = e.y;
                m_Pitch = e.x > 180f ? e.x - 360f : e.x;
            }
            pos += (rot * new Vector3(move.x, 0f, move.z) + Vector3.up * move.y) * freeSpeed * (fast ? 4f : 1f) * dt;
            transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Line from the mosquito to the target, then a 3-axis cross at the target.</summary>
        void UpdateLine(MosquitoAgent a)
        {
            Vector3 p = a.transform.position, t = a.SubgoalTarget;
            float d = Vector3.Distance(transform.position, t);
            float s = Mathf.Max(0.02f, 0.03f * d);
            // About 2 px at both ends: width grows with the distance from this camera.
            float dp = Vector3.Distance(transform.position, p);
            m_Line.startWidth = Mathf.Clamp(0.003f * dp, 0.0005f, 0.015f);
            m_Line.endWidth = Mathf.Clamp(0.003f * d, 0.0005f, 0.015f);
            m_Line.SetPosition(0, p);
            m_Line.SetPosition(1, t);
            m_Line.SetPosition(2, t + Vector3.right * s);
            m_Line.SetPosition(3, t - Vector3.right * s);
            m_Line.SetPosition(4, t);
            m_Line.SetPosition(5, t + Vector3.up * s);
            m_Line.SetPosition(6, t - Vector3.up * s);
            m_Line.SetPosition(7, t);
            m_Line.SetPosition(8, t + Vector3.forward * s);
            m_Line.SetPosition(9, t - Vector3.forward * s);
        }

        /// <summary>Heading: flight direction while moving, else toward the target.</summary>
        void UpdateHeading(MosquitoAgent a, float dt)
        {
            Vector3 v = a.Velocity;
            Vector3 desired = v.sqrMagnitude > 0.15f * 0.15f ? v.normalized : (a.SubgoalTarget - a.transform.position);
            if (desired.sqrMagnitude < 1e-6f) return;
            desired.Normalize();
            m_Fwd = Vector3.Slerp(m_Fwd, desired, 1f - Mathf.Exp(-6f * Mathf.Max(dt, 0f))).normalized;
        }

        void Place(Mode m, MosquitoAgent a)
        {
            Vector3 p = a.transform.position;
            switch (m)
            {
                case Mode.FirstPerson:
                    m_Cam.fieldOfView = 90f;
                    m_Cam.nearClipPlane = 0.004f;
                    m_Cam.cullingMask = Layers.AllButLabels & ~(1 << Layers.MosquitoModel); // not its own body
                    transform.SetPositionAndRotation(p, Quaternion.LookRotation(m_Fwd, Vector3.up));
                    break;
                case Mode.Chase:
                {
                    m_Cam.fieldOfView = 70f;
                    m_Cam.nearClipPlane = 0.002f;
                    m_Cam.cullingMask = Layers.AllButLabels;
                    Vector3 back = -m_Fwd * chaseDistance + Vector3.up * chaseHeight;
                    // Pull the camera in front of walls and furniture.
                    float d = back.magnitude;
                    if (Physics.SphereCast(p, 0.01f, back / d, out RaycastHit hit, d, Layers.AllButLabels & ~(1 << Layers.Human),
                            QueryTriggerInteraction.Ignore))
                        d = Mathf.Max(0.03f, hit.distance - 0.01f);
                    Vector3 cam = p + back.normalized * d;
                    transform.SetPositionAndRotation(cam, Quaternion.LookRotation(p + m_Fwd * 0.3f - cam, Vector3.up));
                    break;
                }
                case Mode.Free:
                    break; // pose is driven by FreeCamera
                default:
                {
                    m_Cam.fieldOfView = m_RoomFov;
                    m_Cam.nearClipPlane = m_RoomNear;
                    m_Cam.cullingMask = Layers.AllButLabels;
                    // The saved pose is for room 0; shift it to the chosen room.
                    Vector3 shift = a.Area.transform.position - RoomOrigin(0, a);
                    transform.SetPositionAndRotation(m_RoomPos + shift, m_RoomRot);
                    break;
                }
            }
        }

        Vector3 RoomOrigin(int index, MosquitoAgent fallback)
        {
            foreach (var x in m_Agents)
                if (x != null && x.Area != null && x.Area.roomIndex == index) return x.Area.transform.position;
            return fallback.Area.transform.position;
        }

        /// <summary>Render one frame of the given mode into a texture (auto test shots).</summary>
        public bool RenderTo(RenderTexture rt, Mode m)
        {
            var a = Agent;
            if (a == null) return false;
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            float fov = m_Cam.fieldOfView, near = m_Cam.nearClipPlane;
            int mask = m_Cam.cullingMask;
            Place(m, a);
            UpdateLine(a);
            CameraCapture.Render(m_Cam, rt);
            transform.SetPositionAndRotation(pos, rot);
            m_Cam.fieldOfView = fov;
            m_Cam.nearClipPlane = near;
            m_Cam.cullingMask = mask;
            return true;
        }

        static GUIStyle s_Style;

        void OnGUI()
        {
            if (!showHud) return;
            var a = Agent;
            if (a == null) return;
            if (s_Style == null)
            {
                s_Style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13 };
                s_Style.normal.textColor = Color.white;
            }
            string text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "room {0}/{14}  view {1}   V view   Q/E or [ ] or 1-9 room   H hud\n" +
                "cmd {2}   dist {3:F2} m   speed {4:F2} m/s\n" +
                "landed {5}   feed {6:F1} s   t {7:F1} s   stage {8}\n" +
                "episodes {9}  success {10}  caught {11}  trapped {12}   fly: {13}",
                roomIndex, mode, a.CurrentCommand, Vector3.Distance(a.transform.position, a.SubgoalTarget),
                a.Velocity.magnitude, a.Landed ? "yes" : "no", a.FeedTimer, a.SimTime, a.Stage,
                MosquitoAgent.Episodes, MosquitoAgent.Successes, MosquitoAgent.Caught, MosquitoAgent.Trapped,
                SimSettings.ManualPlay && SimSettings.Instance.flight == SimSettings.Flight.Keyboard ? "keyboard (WASD Space Shift)" : "autopilot/policy",
                RoomCount - 1);
            GUI.Box(new Rect(8, 8, 540, 74), text, s_Style);
            // Mouse buttons as well, in case a key does not reach the game view.
            if (GUI.Button(new Rect(8, 86, 90, 24), "< room")) ChangeRoom(-1);
            if (GUI.Button(new Rect(102, 86, 90, 24), "room >")) ChangeRoom(1);
            if (GUI.Button(new Rect(196, 86, 90, 24), "view (V)")) mode = (Mode)(((int)mode + 1) % 4);
            if (mode == Mode.Free)
                GUI.Box(new Rect(8, 114, 540, 22), "free: right-drag look, WASD move, Space/Ctrl up/down, Shift fast, wheel speed, F to mosquito", s_Style);
        }
    }
}

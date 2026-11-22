// Closed-loop auto test runner (skill: closed-loop-visual-dev).
// Runs only when AutoTest/active.txt exists (written by AutoTestWatcher) in the Editor,
// or in a build started with -autotest. Scenario: scripted flight (autopilot=1) in all
// 16 rooms, fixed camera shots of room 0, one planner-camera capture with ID labels,
// first-person and chase shots of room 0's mosquito (MosquitoView), a 1 s trace of
// room 0, observation checks, outcome counts, then Play stops.
// Args: view=room|chase|fp (game view mode), pov_every=<s> (mosquito-view shot period).
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MosquitoSim
{
    public class AutoTestRunner : MonoBehaviour
    {
        public static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AutoTest"));
        public static bool Active { get; private set; }
        public static readonly Dictionary<string, string> Args = new Dictionary<string, string>();
        static readonly List<string> s_Logs = new List<string>();
        static readonly Dictionary<string, int> s_LogCount = new Dictionary<string, int>();
        static readonly CultureInfo k_Inv = CultureInfo.InvariantCulture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            Args.Clear();
            s_Logs.Clear();
            s_LogCount.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            string flag = Path.Combine(Dir, "active.txt");
            bool allowed = Application.isEditor || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0;
            if (!allowed || !File.Exists(flag)) return;
            foreach (var line in File.ReadAllLines(flag))
            {
                int i = line.IndexOf('=');
                if (i > 0) Args[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            Active = true;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            var go = new GameObject("AutoTestRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoTestRunner>();
        }

        static string Arg(string k, string d) => Args.ContainsKey(k) ? Args[k] : d;

        public static float F(string k, float d) =>
            Args.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, k_Inv, out var f) ? f : d;

        static void OnLog(string msg, string stack, LogType type)
        {
            string key = $"[{type}] {msg}" + (type == LogType.Log ? "" : "  @ " + (stack ?? "").Split('\n')[0]);
            if (s_LogCount.ContainsKey(key)) s_LogCount[key]++;
            else { s_LogCount[key] = 1; s_Logs.Add(key); }
        }

        string m_Out;
        Camera m_Cap;
        RenderTexture m_Rt;
        Texture2D m_Tex;
        readonly List<string> m_Shots = new List<string>();
        readonly List<float> m_FrameMs = new List<float>();
        readonly StringBuilder m_Diag = new StringBuilder();
        readonly StringBuilder m_Trace = new StringBuilder();
        int m_BadObs, m_ObsChecks, m_TPoses;
        bool m_FlapShot;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Time.timeScale = F("time_scale", 1f);
            m_Out = Path.Combine(Dir, Arg("id", "run"));
            Directory.CreateDirectory(m_Out);
            m_Rt = new RenderTexture(1280, 720, 24); // the pipeline does MSAA
            m_Tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            m_Cap = new GameObject("AutoTestCam").AddComponent<Camera>();
            m_Cap.transform.SetParent(transform);
            m_Cap.enabled = false;
            m_Cap.nearClipPlane = 0.01f;
            m_Cap.cullingMask = Layers.ShotView;

            // 1) wait for the agents and the first episode
            float t0 = Time.realtimeSinceStartup;
            MosquitoAgent[] agents = null;
            while (Time.realtimeSinceStartup - t0 < 10f)
            {
                agents = FindObjectsByType<MosquitoAgent>(FindObjectsSortMode.None);
                if (agents.Length > 0 && agents[0].Area != null && agents[0].Area.Size.x > 0f) break;
                yield return null;
            }
            if (agents == null || agents.Length == 0) { Finish("MosquitoAgent 없음 (씬 확인)"); yield break; }
            System.Array.Sort(agents, (a, b) => a.Area.roomIndex.CompareTo(b.Area.roomIndex));
            yield return new WaitForSeconds(0.5f);
            Diagnose("씬", $"에이전트 {agents.Length}개, 0번 방 {agents[0].Area.Generator.Current} {Vec(agents[0].Area.Size)}, " +
                          $"stage {agents[0].Stage}, 가구 {agents[0].Area.Obstacles.Active.Count}개, 후보 격자 {agents[0].Area.Candidates.Grid.Count}점");

            // 2) fixed shots of room 0
            RoomArea room = agents[0].Area;
            {
                var sb = new StringBuilder(room.human.GetComponentInChildren<HumanAvatar>() != null ? "Mixamo 캐릭터" : "도형");
                sb.Append(", 피부 영역");
                foreach (var z in room.Skins)
                    sb.Append(string.Format(k_Inv, " {0}@{1:F2}m{2}", z.id, z.Center.y - room.transform.position.y, z.covered ? "(덮개)" : ""));
                Diagnose("사람", sb.ToString());
            }
            for (int r = 0; r < Mathf.Min(4, agents.Length); r++)
            {
                RoomArea ra = agents[r].Area;
                var names = new List<string>();
                foreach (var go in ra.Obstacles.Active) names.Add(go.name);
                Diagnose("가구", $"{r}번 방 {ra.Generator.Current} {Vec(ra.Size)} {ra.Obstacles.Layout}: " +
                                (names.Count > 0 ? string.Join(", ", names) : "없음"));
                Diagnose("장식", $"{r}번 방: " + (ra.Decor != null && ra.Decor.Placed.Count > 0 ? string.Join(", ", ra.Decor.Placed) : "없음"));
                Vector3 ro = ra.transform.position;
                Shot($"08_room{r}_{ra.Generator.Current}", ro + new Vector3(0f, 2.35f, -0.01f), ro, 110f);
            }
            // Each piece of rooms 0-3 seen from its front: the front should face into the room.
            {
                int k = 0;
                var all = new List<GameObject>();
                for (int r = 0; r < Mathf.Min(4, agents.Length); r++) all.AddRange(agents[r].Area.Obstacles.Active);
                foreach (var go in all)
                {
                    var f = go.GetComponent<FurnitureItem>();
                    if (f == null || k >= 12) continue;
                    Vector3 c = go.transform.position + go.transform.rotation * f.centerOffset;
                    Vector3 front = Quaternion.Euler(0f, go.transform.eulerAngles.y - f.frontYaw, 0f) * Vector3.forward;
                    Vector3 cam = c + front * (Mathf.Max(f.size.x, f.size.z) * 0.6f + 1.0f) + Vector3.up * 0.8f;
                    Shot($"09_front_{k}_{f.category}", cam, c, 70f);
                    k++;
                }
            }
            Vector3 o = room.transform.position;
            Shot("01_top", o + new Vector3(0f, 2.35f, -0.01f), o, 110f); // under the ceiling
            Shot("02_corner", o + new Vector3(room.Size.x * 0.45f, 2.2f, -room.Size.z * 0.45f), o + Vector3.up * 0.9f, 70f);
            // Human: the first of front/right/back/left with nothing between camera and chest.
            {
                Transform ht = room.human.transform;
                Vector3[] dirs = { ht.forward, ht.right, -ht.forward, -ht.right };
                Vector3 look = ht.position + Vector3.up * 1.0f, camPos = look;
                foreach (var d in dirs)
                {
                    camPos = ht.position + d * 1.3f + Vector3.up * 1.3f;
                    if (!Physics.Linecast(camPos, look, Layers.WorldNoHuman, QueryTriggerInteraction.Ignore)) break;
                }
                Shot("03_human", camPos, look, 60f);
            }

            // 3) planner camera capture with ID labels
            try
            {
                var cands = room.Candidates;
                if (cands.Grid.Count == 0) cands.BuildGrid(room); // stage 0 builds no grid; needed for the label test
                cands.Select(new System.Random(1), agents[0].transform.position, room.human, room.net, room.Cues);
                string[] paths = room.cameras.Capture(m_Out, "04_planner", 448, agents[0].transform.position,
                    room.human.Chest, cands.Current, room.Skins);
                foreach (var p in paths) m_Shots.Add(Path.GetFileName(p));
                Diagnose("후보", $"{cands.Current.Count}개, 가려짐 {Count(cands.Current, c => c.Hidden)}, 길 열림 {Count(cands.Current, c => c.ClearPath)}");
            }
            catch (System.Exception e) { Diagnose("플래너 카메라", "실패: " + e.Message); }

            // 3b) mosquito view (game view mode and offscreen POV shots)
            Diagnose("렌더", CameraCapture.PipelineName);
            var view = MosquitoView.Instance;
            if (view != null)
            {
                view.roomIndex = 0;
                view.mode = MosquitoView.Parse(Arg("view", "room"));
                Diagnose("모기 시점", $"게임 뷰 {view.mode}, 1인칭·추적 컷 {F("pov_every", 3f):F0}초마다");
            }
            else Diagnose("모기 시점", "MosquitoView 없음 (Camera.main 확인)");

            // 4) run, trace room 0 every second, game view every 10 s, POV every pov_every s
            float duration = F("duration", 20f);
            float start = Time.time;
            int n = 0;
            float nextShot = 0f, nextTrace = 0f, nextPov = 0.5f, povEvery = Mathf.Max(0.5f, F("pov_every", 3f));
            int pov = 0;
            while (Time.time - start < duration)
            {
                float t = Time.time - start;
                if (t >= nextTrace)
                {
                    nextTrace += 1f;
                    var a = agents[0];
                    var h = room.human;
                    var av = h.GetComponentInChildren<HumanAvatar>();
                    string pose = av != null ? string.Format(k_Inv, "{0}/{1} 팔 {2:F2}m", h.CurrentPhase, av.StateName, av.ArmDrop) : h.CurrentPhase.ToString();
                    if (av != null && av.ArmDrop < 0.25f) m_TPoses++;
                    m_Trace.AppendLine(string.Format(k_Inv,
                        "{0,5:F0}s  sim {1,5:F1}  {2,-14} 목표까지 {3,4:F2}m  착지 {4}  흡혈 {5:F1}s  경계 {6}  손 {7}  사람 {8}  누적 에피소드 {9} 성공 {10} 잡힘 {11}",
                        t, a.SimTime, a.CurrentCommand.ToString(), Vector3.Distance(a.transform.position, a.SubgoalTarget),
                        a.Landed ? "O" : "-", a.FeedTimer, a.Stage >= 2 && h.Alert ? "O" : "-",
                        a.Stage >= 2 ? room.hand.Current.ToString() : "-", pose,
                        MosquitoAgent.Episodes, MosquitoAgent.Successes, MosquitoAgent.Caught));
                    foreach (var ag in agents) CheckObs(ag);
                }
                // Wing flap: four chase frames in a row while room 0's mosquito flies.
                if (view != null && !m_FlapShot && t > 2f && !agents[0].Landed)
                {
                    m_FlapShot = true;
                    for (int f = 0; f < 4; f++)
                    {
                        if (view.RenderTo(m_Rt, MosquitoView.Mode.Chase)) SaveRt($"10_flap_{f}");
                        yield return null;
                    }
                }
                if (view != null && t >= nextPov && pov < 12)
                {
                    nextPov += povEvery;
                    if (view.RenderTo(m_Rt, MosquitoView.Mode.FirstPerson)) SaveRt($"06_fp_{pov:00}_{t:F0}s");
                    if (view.RenderTo(m_Rt, MosquitoView.Mode.Chase)) SaveRt($"07_chase_{pov:00}_{t:F0}s");
                    pov++;
                }
                if (t >= nextShot && n < 12)
                {
                    nextShot += 10f;
                    ScreenCapture.CaptureScreenshot(Path.Combine(m_Out, $"11_gameview_{n:00}_{t:F0}s.png"));
                    m_Shots.Add($"11_gameview_{n:00}_{t:F0}s.png");
                    n++;
                }
                yield return null;
            }

            Shot("05_top_end", o + new Vector3(0f, 2.35f, -0.01f), o, 110f);
            Diagnose("결과", $"에피소드 {MosquitoAgent.Episodes}, 성공 {MosquitoAgent.Successes}, 잡힘 {MosquitoAgent.Caught}, " +
                           $"트랩 {MosquitoAgent.Trapped}, 결정 {MosquitoAgent.Decisions}");
            Diagnose("관측", $"점검 {m_ObsChecks}회, 범위·NaN·원-핫 이상 {m_BadObs}회");
            Diagnose("자세", $"0번 방 사람 팔이 어깨 높이 근처(T 포즈 의심)인 초: {m_TPoses}");
            yield return new WaitForSeconds(0.5f);
            Finish(null);
        }

        void CheckObs(MosquitoAgent a)
        {
            float[] o = a.LastObservation;
            if (o == null) return;
            m_ObsChecks++;
            bool bad = o.Length != ObsLayout.ObsSize;
            float hot = 0f;
            for (int i = 0; i < o.Length && !bad; i++)
                if (float.IsNaN(o[i]) || Mathf.Abs(o[i]) > 1.5f) bad = true;
            for (int i = 0; i < ObsLayout.NumSubgoalSlots && !bad; i++) hot += o[ObsLayout.SubgoalOneHot + i];
            if (!bad && Mathf.Abs(hot - 1f) > 1e-3f) bad = true;
            if (bad)
            {
                m_BadObs++;
                if (m_BadObs <= 5) Diagnose("관측 이상", $"방 {a.Area.roomIndex}: " + string.Join(",", System.Array.ConvertAll(o, x => x.ToString("0.##", k_Inv))));
            }
        }

        void Update() => m_FrameMs.Add(Time.unscaledDeltaTime * 1000f);

        void Shot(string shotName, Vector3 pos, Vector3 lookAt, float fov)
        {
            try
            {
                m_Cap.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos, Vector3.up));
                m_Cap.fieldOfView = fov;
                CameraCapture.Render(m_Cap, m_Rt);
                SaveRt(shotName);
            }
            catch (System.Exception e) { m_Shots.Add($"{shotName} 실패: {e.Message}"); }
        }

        /// <summary>Write the current contents of m_Rt as a JPG.</summary>
        void SaveRt(string shotName)
        {
            try
            {
                var prev = RenderTexture.active;
                RenderTexture.active = m_Rt;
                m_Tex.ReadPixels(new Rect(0, 0, m_Rt.width, m_Rt.height), 0, 0);
                m_Tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(m_Out, shotName + ".jpg"), m_Tex.EncodeToJPG(85));
                m_Shots.Add(shotName + ".jpg");
            }
            catch (System.Exception e) { m_Shots.Add($"{shotName} 실패: {e.Message}"); }
        }

        void Diagnose(string label, string text) => m_Diag.AppendLine($"[{label}] {text}");

        static int Count(List<Candidate> list, System.Func<Candidate, bool> pred)
        {
            int c = 0;
            foreach (var x in list) if (pred(x)) c++;
            return c;
        }

        static string Vec(Vector3 v) => string.Format(k_Inv, "({0:F1}, {1:F1}, {2:F1})", v.x, v.y, v.z);

        void Finish(string error)
        {
            var sb = new StringBuilder($"# 자동 테스트 리포트: {Path.GetFileName(m_Out)}\n");
            sb.AppendLine($"Unity {Application.unityVersion} / 화면 {Screen.width}x{Screen.height} / timeScale {Time.timeScale}");
            sb.Append("인자: ");
            foreach (var kv in Args) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            sb.AppendLine();
            if (error != null) sb.AppendLine("오류: " + error);
            if (m_FrameMs.Count > 10)
            {
                m_FrameMs.Sort();
                float avg = 0f;
                foreach (var f in m_FrameMs) avg += f;
                avg /= m_FrameMs.Count;
                sb.AppendLine($"프레임: 평균 {1000f / avg:F1} FPS, 하위 5% {1000f / m_FrameMs[(int)(m_FrameMs.Count * 0.95f)]:F1} FPS");
            }
            sb.AppendLine("\n## 시간 흐름 (0번 방, 1초마다)").Append(m_Trace);
            sb.AppendLine("\n## 진단").Append(m_Diag);
            sb.AppendLine("\n## 스크린샷");
            foreach (var s in m_Shots) sb.AppendLine("- " + s);
            sb.AppendLine("\n## 콘솔 로그 (중복은 횟수)");
            foreach (var k in s_Logs) sb.AppendLine($"- {k}" + (s_LogCount[k] > 1 ? $"  (x{s_LogCount[k]})" : ""));
            File.WriteAllText(Path.Combine(m_Out, "report.txt"), sb.ToString(), new UTF8Encoding(false));
            Application.logMessageReceived -= OnLog;
            try { File.Delete(Path.Combine(Dir, "active.txt")); } catch { }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            if (m_Rt) m_Rt.Release();
        }
    }
}

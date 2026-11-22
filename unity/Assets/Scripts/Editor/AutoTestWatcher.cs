// Closed-loop auto test (skill: closed-loop-visual-dev).
// When AutoTest/request.txt appears: Refresh/compile -> set.* on SimSettings -> active.txt -> Play.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MosquitoSim.EditorTools
{
    [InitializeOnLoad]
    public static class AutoTestWatcher
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string StageKey = "MosquitoSim.AutoTest.Stage";
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AutoTest"));
        static string Req => Path.Combine(Dir, "request.txt");
        static double s_Next;
        static readonly List<string> s_Errors = new List<string>();

        static AutoTestWatcher()
        {
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
            CompilationPipeline.assemblyCompilationFinished -= OnAsm; CompilationPipeline.assemblyCompilationFinished += OnAsm;
            CompilationPipeline.compilationFinished -= OnDone; CompilationPipeline.compilationFinished += OnDone;
            EditorApplication.playModeStateChanged -= OnPlay; EditorApplication.playModeStateChanged += OnPlay;
            if (SessionState.GetString(StageKey, "") == "") Status("idle (에디터 스크립트 로드됨)");
        }

        static void OnAsm(string asm, CompilerMessage[] msgs)
        {
            foreach (var m in msgs) if (m.type == CompilerMessageType.Error) s_Errors.Add($"{m.file}:{m.line} {m.message}");
        }

        static void OnDone(object _)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "compile_status.txt"),
                $"time={System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\nerrors={s_Errors.Count}\n{string.Join("\n", s_Errors)}\n");
            s_Errors.Clear();
        }

        static void OnPlay(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredEditMode) return;
            var flag = Path.Combine(Dir, "active.txt");
            if (File.Exists(flag)) File.Delete(flag);
            if (SessionState.GetString(StageKey, "") == "playing") { SessionState.SetString(StageKey, ""); Status("done"); }
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < s_Next) return;
            s_Next = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(Req) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (SessionState.GetString(StageKey, "") != "playing") Status("waiting: Play 모드를 정지하면 자동 테스트를 시작합니다");
                return;
            }

            var req = Parse(File.ReadAllText(Req));
            string id = req.TryGetValue("id", out var v) ? v : "run";
            bool refresh = req.TryGetValue("refresh", out v) && v.ToLower() == "true";
            if (refresh && SessionState.GetString(StageKey, "") != "refreshed:" + id)
            {
                SessionState.SetString(StageKey, "refreshed:" + id);
                Status($"refreshing ({id})");
                AssetDatabase.Refresh();
                // Packages/manifest.json edited from outside: install the changes now.
                if (req.TryGetValue("resolve_packages", out v) && v.ToLower() == "true")
                    UnityEditor.PackageManager.Client.Resolve();
                return;
            }
            if (EditorUtility.scriptCompilationFailed)
            {
                File.Delete(Req); SessionState.SetString(StageKey, "");
                Status($"failed ({id}): 컴파일 에러 — compile_status.txt"); return;
            }

            // Build the scene on first use (or when asked) so a fresh clone runs without clicks.
            bool rebuild = req.TryGetValue("build_scene", out v) && v.ToLower() == "true";
            if (rebuild || !File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ScenePath))))
            {
                SceneBuilder.BuildWithoutPrompt();
            }
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                if (EditorSceneManager.GetActiveScene().isDirty) { File.Delete(Req); Status($"failed ({id}): 저장 안 된 씬"); return; }
                EditorSceneManager.OpenScene(ScenePath);
            }
            var setLog = ApplySettings(req);
            if (setLog.Length > 0) File.WriteAllText(Path.Combine(Dir, "settings_applied.txt"), setLog);

            var flag = new StringBuilder("id=" + id + "\n");
            foreach (var kv in req)
                if (kv.Key != "id" && kv.Key != "refresh" && kv.Key != "build_scene" && kv.Key != "resolve_packages" && !kv.Key.StartsWith("set."))
                    flag.Append($"{kv.Key}={kv.Value}\n");
            File.WriteAllText(Path.Combine(Dir, "active.txt"), flag.ToString());
            File.Delete(Req);
            SessionState.SetString(StageKey, "playing");
            Status($"playing ({id})");
            EditorApplication.EnterPlaymode();
        }

        // set.<field>=<value> on SimSettings, then save the scene (never edit scene YAML from outside).
        static string ApplySettings(Dictionary<string, string> req)
        {
            var target = Object.FindFirstObjectByType<SimSettings>();
            if (!target) return "";
            var so = new SerializedObject(target);
            var log = new StringBuilder();
            bool any = false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var kv in req)
            {
                if (!kv.Key.StartsWith("set.")) continue;
                var p = so.FindProperty(kv.Key.Substring(4));
                if (p == null) { log.AppendLine($"X {kv.Key}: 필드 없음"); continue; }
                try
                {
                    switch (p.propertyType)
                    {
                        case SerializedPropertyType.Float: p.floatValue = float.Parse(kv.Value, inv); break;
                        case SerializedPropertyType.Integer: p.intValue = int.Parse(kv.Value, inv); break;
                        case SerializedPropertyType.Boolean: p.boolValue = kv.Value == "true" || kv.Value == "1"; break;
                        case SerializedPropertyType.String: p.stringValue = kv.Value; break;
                        default: log.AppendLine($"X {kv.Key}: 형식 {p.propertyType} 미지원"); continue;
                    }
                    any = true;
                    log.AppendLine($"O {kv.Key} = {kv.Value}");
                }
                catch (System.Exception e) { log.AppendLine($"X {kv.Key}: {e.Message}"); }
            }
            if (any)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                EditorSceneManager.SaveScene(target.gameObject.scene);
            }
            return log.ToString();
        }

        static Dictionary<string, string> Parse(string t)
        {
            var d = new Dictionary<string, string>();
            foreach (var line in t.Split('\n'))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            return d;
        }

        static void Status(string s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, "status.txt"), $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}\n");
            }
            catch { }
        }

        [MenuItem("MosquitoSim/Run Auto Test (20 s, stage 0)")]
        static void RunFromMenu()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Req, $"id=manual_{System.DateTime.Now:MMdd_HHmmss}\nduration=20\nstage=0\nautopilot=1\n");
            s_Next = 0;
        }
    }
}

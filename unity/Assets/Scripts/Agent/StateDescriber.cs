using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// What a non-oracle planner may know at a decision. The VLM instruction is
    /// rendered from this view and nothing else, and the same view goes to Python
    /// as JSON ("view") for the masked rule planner, so both see exactly the
    /// same information. Never in the view: whether a candidate is hidden or has
    /// a clear path, whether a point is near a trap, whether a skin is covered,
    /// where a point is beyond its three distances (mosquito, head, floor),
    /// how long the alert lasts, or any scene geometry.
    /// </summary>
    public class PlannerView
    {
        public struct Skin { public string Id, Name; public float Dist; }
        public struct Point { public string Id; public float Dist, HeadDist, Height; } // from the mosquito, from the human's head, above the floor

        public int Stage;
        public float SimTime, EpisodeSeconds;
        public bool HumanWalking;
        public bool Alert;                       // stage 2+
        public bool HandActive;                  // stage 2+
        public float HandDist, HandClosing;      // only while the hand is active
        public bool Landed;
        public float FeedProgress, FeedTime;
        public readonly List<Skin> Skins = new List<Skin>();
        public readonly List<Point> Candidates = new List<Point>();
        public PlannerCommand Current;
        /// <summary>"hide": HIDE scenario, the human heard the mosquito and will look around once a spot is chosen.</summary>
        public string Situation = "";

        public void WriteJson(JsonBuilder j)
        {
            j.BeginObject()
                .Key("stage").Value(Stage)
                .Key("situation").Value(Situation)
                .Key("sim_time").Value(SimTime)
                .Key("episode_seconds").Value(EpisodeSeconds)
                .Key("human_walking").Value(HumanWalking)
                .Key("alert").Value(Alert)
                .Key("hand_active").Value(HandActive)
                .Key("hand_dist").Value(HandDist)
                .Key("hand_closing").Value(HandClosing)
                .Key("landed").Value(Landed)
                .Key("feed_progress").Value(FeedProgress)
                .Key("feed_time").Value(FeedTime);
            j.Key("skins").BeginArray();
            foreach (var s in Skins)
                j.BeginObject().Key("id").Value(s.Id).Key("name").Value(s.Name).Key("dist").Value(s.Dist).EndObject();
            j.EndArray();
            j.Key("candidates").BeginArray();
            foreach (var p in Candidates)
                j.BeginObject().Key("id").Value(p.Id).Key("dist").Value(p.Dist)
                    .Key("head_dist").Value(p.HeadDist).Key("height").Value(p.Height).EndObject();
            j.EndArray();
            j.Key("current_command"); Current.WriteJson(j);
            j.EndObject();
        }
    }

    /// <summary>Builds the planner view and the language instruction rendered from it.</summary>
    public static class StateDescriber
    {
        static readonly CultureInfo k_Inv = CultureInfo.InvariantCulture;

        public static PlannerView View(PlannerContext c, float episodeSeconds)
        {
            var h = c.Human;
            var v = new PlannerView
            {
                Stage = c.Stage,
                SimTime = c.SimTime,
                EpisodeSeconds = episodeSeconds,
                HumanWalking = h.Walking && h.Velocity.sqrMagnitude > 0.01f,
                Alert = c.AlertKnown,
                Situation = c.HideSituation ? "hide" : "",
                HandActive = c.Stage >= 2 && c.Hand.Active,
                Landed = c.Landed,
                FeedProgress = c.FeedProgress,
                FeedTime = c.FeedTime,
                Current = c.Current,
            };
            if (v.HandActive)
            {
                Vector3 toMosq = c.MosquitoPos - c.Hand.Position;
                v.HandDist = toMosq.magnitude;
                v.HandClosing = v.HandDist > 1e-4f ? Vector3.Dot(c.Hand.Velocity, toMosq / v.HandDist) : 0f;
            }
            foreach (var s in h.skins)
                v.Skins.Add(new PlannerView.Skin { Id = s.id, Name = s.zoneName, Dist = Vector3.Distance(s.Center, c.MosquitoPos) });
            float floorY = h.transform.position.y;
            foreach (var k in c.Candidates)
                v.Candidates.Add(new PlannerView.Point
                {
                    Id = k.Id,
                    Dist = k.Dist,
                    HeadDist = Vector3.Distance(k.Pos, h.HeadPos),
                    Height = k.Pos.y - floorY,
                });
            return v;
        }

        public static string Describe(PlannerContext c, float episodeSeconds) => Describe(View(c, episodeSeconds));

        public static string Describe(PlannerView v)
        {
            var sb = new StringBuilder();
            sb.Append("[과제] 사람에게 들키지 않고 피부에 착지해 흡혈하라.\n");
            sb.Append("[상황] ").Append(F(v.SimTime)).Append("초 / ").Append(v.EpisodeSeconds.ToString("0", k_Inv)).Append("초. ");

            sb.Append("사람: ").Append(v.HumanWalking ? "걷는 중" : "서 있음");
            // The planner learns only that the human is alert, not for how long (11/22).
            bool hide = v.Situation == "hide";
            if (v.Stage >= 2 || hide) sb.Append(v.Alert ? ", 경계 중" : ", 평온");
            sb.Append(".\n");
            if (hide)
                sb.Append("[지금] 사람이 모기 소리를 들었다. 숨을 곳을 고르면 사람이 소리 난 쪽으로 돌아서 둘러본다. " +
                          "경계가 끝날 때까지 사람 눈에 띄지 않을 후보 지점 하나를 HIDE로 골라라.\n");

            if (v.Stage >= 2)
            {
                if (v.HandActive)
                    sb.Append("손: 움직이는 중, ").Append(F(v.HandDist)).Append("m, 다가오는 속도 ").Append(F(v.HandClosing)).Append("m/s.\n");
                else
                    sb.Append("손: 쉬는 중.\n");
            }

            if (v.Landed) sb.Append("모기: 착지 중, 흡혈 ").Append(F(v.FeedProgress)).Append('/').Append(v.FeedTime.ToString("0", k_Inv)).Append("초.\n");
            else sb.Append("모기: 비행 중.\n");

            sb.Append("피부: ");
            for (int i = 0; i < v.Skins.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(v.Skins[i].Id).Append(' ').Append(v.Skins[i].Name).Append(' ').Append(F(v.Skins[i].Dist)).Append('m');
            }
            sb.Append('\n');

            if (v.Candidates.Count > 0)
            {
                sb.Append("후보 지점(이미지에 번호 표시, 모기로부터 / 사람 머리로부터 / 높이): ");
                for (int i = 0; i < v.Candidates.Count; i++)
                {
                    var p = v.Candidates[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append(p.Id).Append(' ').Append(F(p.Dist)).Append("m / ").Append(F(p.HeadDist)).Append("m / ").Append(F(p.Height)).Append('m');
                }
                sb.Append('\n');
            }

            sb.Append("직전 지시: ").Append(v.Current.ToString()).Append('\n');
            if (hide)
            {
                sb.Append("[출력] JSON으로:\n");
                sb.Append("{\"subgoal\": \"HIDE\", \"target\": \"P1~P").Append(v.Candidates.Count).Append(" 중 하나\"}\n");
                return sb.ToString();
            }
            sb.Append("[출력] 다음 중 하나를 JSON으로:\n");
            sb.Append("{\"subgoal\": \"APPROACH\", \"target\": \"S1~S5 중 하나\"}\n");
            if (v.Candidates.Count > 0)
            {
                sb.Append("{\"subgoal\": \"HIDE\", \"target\": \"P1~P").Append(v.Candidates.Count).Append(" 중 하나\"}\n");
                sb.Append("{\"subgoal\": \"MOVE_TO\", \"target\": \"P1~P").Append(v.Candidates.Count).Append(" 중 하나\"}\n");
            }
            if (v.Stage >= 2) sb.Append("{\"subgoal\": \"EVADE\"}\n");
            return sb.ToString();
        }

        static string F(float v) => v.ToString("0.0", k_Inv);
    }
}

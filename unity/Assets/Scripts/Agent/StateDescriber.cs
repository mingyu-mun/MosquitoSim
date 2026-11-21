using System.Globalization;
using System.Text;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Builds the language instruction for the VLM (and the image-free VLM).
    /// It states what the rule planner's inputs say in words, plus skin and
    /// candidate IDs with distances only: never whether a point is hidden,
    /// has a clear path, is near a trap, or whether a skin is covered.
    /// </summary>
    public static class StateDescriber
    {
        static readonly CultureInfo k_Inv = CultureInfo.InvariantCulture;

        public static string Describe(PlannerContext c, float episodeSeconds)
        {
            var sb = new StringBuilder();
            sb.Append("[과제] 사람에게 들키지 않고 피부에 착지해 흡혈하라.\n");
            sb.Append("[상황] ").Append(F(c.SimTime)).Append("초 / ").Append(episodeSeconds.ToString("0", k_Inv)).Append("초. ");

            var h = c.Human;
            sb.Append("사람: ").Append(h.Walking && h.Velocity.sqrMagnitude > 0.01f ? "걷는 중" : "서 있음");
            if (c.Stage >= 2)
                sb.Append(h.Alert ? ", 경계 상태(" + F(h.AlertRemaining) + "초 남음)" : ", 평온");
            sb.Append(".\n");

            if (c.Stage >= 2)
            {
                var hand = c.Hand;
                if (hand.Active)
                {
                    Vector3 toMosq = c.MosquitoPos - hand.Position;
                    float d = toMosq.magnitude;
                    float closing = d > 1e-4f ? Vector3.Dot(hand.Velocity, toMosq / d) : 0f;
                    sb.Append("손: 움직이는 중, ").Append(F(d)).Append("m, 다가오는 속도 ").Append(F(closing)).Append("m/s.\n");
                }
                else
                {
                    sb.Append("손: 쉬는 중.\n");
                }
            }

            if (c.Landed) sb.Append("모기: 착지 중, 흡혈 ").Append(F(c.FeedProgress)).Append('/').Append(c.FeedTime.ToString("0", k_Inv)).Append("초.\n");
            else sb.Append("모기: 비행 중.\n");

            sb.Append("피부: ");
            for (int i = 0; i < h.skins.Count; i++)
            {
                var s = h.skins[i];
                if (i > 0) sb.Append(", ");
                sb.Append(s.id).Append(' ').Append(s.zoneName).Append(' ').Append(F(Vector3.Distance(s.Center, c.MosquitoPos))).Append('m');
            }
            sb.Append('\n');

            if (c.Candidates.Count > 0)
            {
                sb.Append("후보 지점(이미지에 번호 표시): ");
                for (int i = 0; i < c.Candidates.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(c.Candidates[i].Id).Append(' ').Append(F(c.Candidates[i].Dist)).Append('m');
                }
                sb.Append('\n');
            }

            sb.Append("직전 지시: ").Append(c.Current.ToString()).Append('\n');
            sb.Append("[출력] 다음 중 하나를 JSON으로:\n");
            sb.Append("{\"subgoal\": \"APPROACH\", \"target\": \"S1~S5 중 하나\"}\n");
            if (c.Candidates.Count > 0)
            {
                sb.Append("{\"subgoal\": \"HIDE\", \"target\": \"P1~P").Append(c.Candidates.Count).Append(" 중 하나\"}\n");
                sb.Append("{\"subgoal\": \"MOVE_TO\", \"target\": \"P1~P").Append(c.Candidates.Count).Append(" 중 하나\"}\n");
            }
            if (c.Stage >= 2) sb.Append("{\"subgoal\": \"EVADE\"}\n");
            return sb.ToString();
        }

        static string F(float v) => v.ToString("0.0", k_Inv);
    }
}

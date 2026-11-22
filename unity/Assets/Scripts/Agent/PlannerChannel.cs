using System;
using System.Collections.Generic;
using Unity.MLAgents.SideChannels;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>Incoming message from Python (config or command).</summary>
    [Serializable]
    public class PlannerInMessage
    {
        public string type;          // "config" | "command"
        public int room;
        public string episode;       // command: ignored unless it matches the room's current episode
        public int decision;
        public CommandDto command;
        // config
        public string image_dir;
        public bool log;
        public bool capture_images;
        public int image_size;
    }

    /// <summary>
    /// Side channel between Unity and the Python evaluation loop
    /// (python/mosquitosim/channel.py uses the same GUID).
    /// Unity -> Python: "request" (external planner decision), "decision"
    /// (log of an in-Unity decision), "episode_end". Python -> Unity: "config",
    /// "command". mlagents-learn does not know this channel, so nothing is sent
    /// unless Python has sent a config first.
    /// </summary>
    public class PlannerChannel : SideChannel
    {
        public static readonly Guid Id = new Guid("6f1c2a5e-8d3b-4c7a-9e21-5b4d3c2a1f00");
        public static PlannerChannel Instance { get; private set; }

        public bool Configured { get; private set; }
        public bool Log { get; private set; }
        public bool CaptureImages { get; private set; }
        public string ImageDir { get; private set; } = "planner_images";
        public int ImageSize { get; private set; } = 448;

        readonly Dictionary<int, Queue<PlannerInMessage>> m_Commands = new Dictionary<int, Queue<PlannerInMessage>>();

        public PlannerChannel()
        {
            ChannelId = Id;
        }

        public static void EnsureRegistered()
        {
            if (Instance != null) return;
            Instance = new PlannerChannel();
            SideChannelManager.RegisterSideChannel(Instance);
        }

        protected override void OnMessageReceived(IncomingMessage msg)
        {
            string json = msg.ReadString();
            PlannerInMessage m;
            try
            {
                m = JsonUtility.FromJson<PlannerInMessage>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("PlannerChannel: bad message " + e.Message);
                return;
            }
            if (m == null) return;

            if (m.type == "config")
            {
                Configured = true;
                Log = m.log;
                CaptureImages = m.capture_images;
                if (!string.IsNullOrEmpty(m.image_dir)) ImageDir = m.image_dir;
                if (m.image_size > 0) ImageSize = m.image_size;
            }
            else if (m.type == "command")
            {
                if (!m_Commands.TryGetValue(m.room, out var q))
                {
                    q = new Queue<PlannerInMessage>();
                    m_Commands[m.room] = q;
                }
                q.Enqueue(m);
            }
        }

        public bool TryDequeue(int room, out PlannerInMessage m)
        {
            m = null;
            return m_Commands.TryGetValue(room, out var q) && q.Count > 0 && (m = q.Dequeue()) != null;
        }

        public void Send(string json)
        {
            if (!Configured) return;
            using (var msg = new OutgoingMessage())
            {
                msg.WriteString(json);
                QueueMessageToSend(msg);
            }
        }
    }

    /// <summary>Registers the channel before any agent initializes the Academy.</summary>
    [DefaultExecutionOrder(-1000)]
    public class PlannerChannelRegistrar : MonoBehaviour
    {
        void Awake()
        {
            PlannerChannel.EnsureRegistered();
        }
    }
}

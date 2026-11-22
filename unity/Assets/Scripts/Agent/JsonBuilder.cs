using System.Globalization;
using System.Text;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Minimal JSON writer for side-channel messages (JsonUtility cannot write
    /// dictionaries or mixed arrays). Commas are inserted automatically.
    /// </summary>
    public class JsonBuilder
    {
        readonly StringBuilder m_Sb = new StringBuilder(512);
        bool m_NeedComma;

        public JsonBuilder BeginObject() { Sep(); m_Sb.Append('{'); m_NeedComma = false; return this; }
        public JsonBuilder EndObject() { m_Sb.Append('}'); m_NeedComma = true; return this; }
        public JsonBuilder BeginArray() { Sep(); m_Sb.Append('['); m_NeedComma = false; return this; }
        public JsonBuilder EndArray() { m_Sb.Append(']'); m_NeedComma = true; return this; }

        public JsonBuilder Key(string k)
        {
            Sep();
            Str(k);
            m_Sb.Append(':');
            m_NeedComma = false;
            return this;
        }

        public JsonBuilder Value(string v) { Sep(); if (v == null) m_Sb.Append("null"); else Str(v); m_NeedComma = true; return this; }
        public JsonBuilder Value(bool v) { Sep(); m_Sb.Append(v ? "true" : "false"); m_NeedComma = true; return this; }
        public JsonBuilder Value(int v) { Sep(); m_Sb.Append(v.ToString(CultureInfo.InvariantCulture)); m_NeedComma = true; return this; }

        public JsonBuilder Value(float v)
        {
            Sep();
            if (float.IsNaN(v) || float.IsInfinity(v)) m_Sb.Append("null");
            else m_Sb.Append(v.ToString("0.####", CultureInfo.InvariantCulture));
            m_NeedComma = true;
            return this;
        }

        public JsonBuilder Value(Vector3 v)
        {
            BeginArray().Value(v.x).Value(v.y).Value(v.z).EndArray();
            return this;
        }

        public override string ToString() => m_Sb.ToString();

        void Sep()
        {
            if (m_NeedComma) m_Sb.Append(',');
        }

        void Str(string s)
        {
            m_Sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': m_Sb.Append("\\\""); break;
                    case '\\': m_Sb.Append("\\\\"); break;
                    case '\n': m_Sb.Append("\\n"); break;
                    case '\r': m_Sb.Append("\\r"); break;
                    case '\t': m_Sb.Append("\\t"); break;
                    default:
                        // ML-Agents side-channel strings are ASCII only (WriteString uses
                        // Encoding.ASCII, so every non-ASCII char would arrive as '?').
                        // Escape them; the Python json parser restores the text.
                        if (c < 0x20 || c > 0x7e) m_Sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else m_Sb.Append(c);
                        break;
                }
            }
            m_Sb.Append('"');
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// Just enough JSON for glTF headers: objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double, plus string / bool / null. RoR2 ships no Newtonsoft, so the GLB loader reads with this.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            return v;
        }

        // ---- typed access helpers (null-safe: a missing key or wrong type gives the fallback) ----

        public static Dictionary<string, object>? Obj(object? o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

        public static List<object>? Arr(object? o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out object v) ? v as List<object> : null;

        public static string? Str(object? o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out object v) ? v as string : null;

        public static int? Int(object? o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out object v) && v is double n ? (int)n : (int?)null;

        public static bool Bool(object? o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out object v) && v is bool b && b;

        public static bool Has(object? o, string key) => o is Dictionary<string, object> d && d.ContainsKey(key);

        public static float F(object v) => (float)(double)v;

        // ---- parser ----

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            char c = s[i];
            if (c == '{') return Object(s, ref i);
            if (c == '[') return Array(s, ref i);
            if (c == '"') return String(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null!; }
            return Number(s, ref i);
        }

        private static Dictionary<string, object> Object(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            for (;;)
            {
                Skip(s, ref i);
                if (s[i] == '}') { i++; return d; }
                string key = String(s, ref i);
                Skip(s, ref i);
                i++; // :
                d[key] = Value(s, ref i);
                Skip(s, ref i);
                if (s[i] == ',') i++;
            }
        }

        private static List<object> Array(string s, ref int i)
        {
            var l = new List<object>();
            i++; // [
            for (;;)
            {
                Skip(s, ref i);
                if (s[i] == ']') { i++; return l; }
                l.Add(Value(s, ref i));
                Skip(s, ref i);
                if (s[i] == ',') i++;
            }
        }

        private static string String(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            for (;;)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break; // \" \\ \/
                }
            }
        }

        private static double Number(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}

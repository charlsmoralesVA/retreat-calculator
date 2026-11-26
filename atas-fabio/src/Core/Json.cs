using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// Minimal JSON writer and reader (netstandard2.0 has no built-in JSON). Writes dictionaries, lists, strings,
    /// numbers, booleans and null; reads the same shapes back as Dictionary/List/string/decimal/bool/null.
    /// </summary>
    public static class Json
    {
        public static string Write(object v)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string s) { WriteString(sb, s); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is decimal m) { sb.Append(m.ToString(CultureInfo.InvariantCulture)); return; }
            if (v is int || v is long || v is short) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is double d) { sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is float f) { WriteValue(sb, (double)f); return; }
            if (v is DateTime dt) { WriteString(sb, dt.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + (dt.Kind == DateTimeKind.Utc ? "Z" : "")); return; }
            if (v is Enum) { WriteString(sb, v.ToString()); return; }
            if (v is IDictionary dict)
            {
                sb.Append('{'); bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(','); first = false;
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture)); sb.Append(':'); WriteValue(sb, e.Value);
                }
                sb.Append('}'); return;
            }
            if (v is IEnumerable list)
            {
                sb.Append('['); bool first = true;
                foreach (var x in list) { if (!first) sb.Append(','); first = false; WriteValue(sb, x); }
                sb.Append(']'); return;
            }
            WriteString(sb, v.ToString());
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ reader
        public static object Parse(string text)
        {
            int i = 0;
            var v = ReadValue(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException("trailing characters in JSON");
            return v;
        }

        private static void Skip(string t, ref int i) { while (i < t.Length && char.IsWhiteSpace(t[i])) i++; }

        private static object ReadValue(string t, ref int i)
        {
            Skip(t, ref i);
            if (i >= t.Length) throw new FormatException("unexpected end of JSON");
            char c = t[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal); i++;
                Skip(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return d; }
                while (true)
                {
                    Skip(t, ref i);
                    if (i >= t.Length || t[i] != '"') throw new FormatException("expected a property name");
                    string key = ReadString(t, ref i);
                    Skip(t, ref i);
                    if (i >= t.Length || t[i] != ':') throw new FormatException("expected ':'");
                    i++;
                    d[key] = ReadValue(t, ref i);
                    Skip(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == '}') { i++; return d; }
                    throw new FormatException("expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Skip(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue(t, ref i));
                    Skip(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == ']') { i++; return l; }
                    throw new FormatException("expected ',' or ']'");
                }
            }
            if (c == '"') return ReadString(t, ref i);
            if (t.Length - i >= 4 && string.CompareOrdinal(t, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (t.Length - i >= 5 && string.CompareOrdinal(t, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (t.Length - i >= 4 && string.CompareOrdinal(t, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < t.Length && "+-0123456789.eE".IndexOf(t[i]) >= 0) i++;
            if (start == i) throw new FormatException("unexpected character '" + c + "'");
            return decimal.Parse(t.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string ReadString(string t, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (i < t.Length)
            {
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) break;
                char e = t[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > t.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)int.Parse(t.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }
    }
}

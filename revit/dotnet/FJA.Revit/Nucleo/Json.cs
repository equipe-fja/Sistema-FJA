using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FJA.Revit.Nucleo
{
    /// <summary>
    /// JSON mínimo, sem dependências (o .NET Framework 4.8 do Revit 2024 não tem System.Text.Json e
    /// uma DLL extra exigiria resolvedor). Lê para Dictionary&lt;string, object&gt;, List&lt;object&gt;,
    /// string, double, bool e null; grava os mesmos tipos (e int/long/float).
    /// </summary>
    public static class Json
    {
        public static object Ler(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            int i = 0;
            object v = Valor(texto, ref i);
            return v;
        }

        public static Dictionary<string, object> LerObjeto(string texto)
        {
            try { return Ler(texto) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        static void Espacos(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Valor(string s, ref int i)
        {
            Espacos(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON incompleto");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Espacos(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Espacos(s, ref i);
                    string k = Texto(s, ref i);
                    Espacos(s, ref i);
                    if (s[i] != ':') throw new FormatException("JSON: esperava ':'");
                    i++;
                    d[k] = Valor(s, ref i);
                    Espacos(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("JSON: esperava ',' ou '}'");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Espacos(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Valor(s, ref i));
                    Espacos(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("JSON: esperava ',' ou ']'");
                }
            }
            if (c == '"') return Texto(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int ini = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == ini) throw new FormatException("JSON: valor inválido");
            return double.Parse(s.Substring(ini, i - ini), CultureInfo.InvariantCulture);
        }

        static string Texto(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON: esperava texto");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
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
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("JSON: texto sem fim");
        }

        public static string Gravar(object v)
        {
            var sb = new StringBuilder();
            Escrever(sb, v);
            return sb.ToString();
        }

        static void Escrever(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string s) { EscreverTexto(sb, s); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is double || v is float || v is int || v is long || v is decimal)
            {
                sb.Append(Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is IDictionary d)
            {
                sb.Append('{');
                bool primeiro = true;
                foreach (DictionaryEntry kv in d)
                {
                    if (!primeiro) sb.Append(',');
                    primeiro = false;
                    EscreverTexto(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Escrever(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            if (v is IEnumerable l)
            {
                sb.Append('[');
                bool primeiro = true;
                foreach (object x in l)
                {
                    if (!primeiro) sb.Append(',');
                    primeiro = false;
                    Escrever(sb, x);
                }
                sb.Append(']');
                return;
            }
            EscreverTexto(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void EscreverTexto(StringBuilder sb, string s)
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
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- leitura cômoda
        public static string Txt(this Dictionary<string, object> d, string k)
        {
            return d != null && d.TryGetValue(k, out object v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        public static List<object> Lista(this Dictionary<string, object> d, string k)
        {
            return d != null && d.TryGetValue(k, out object v) ? v as List<object> ?? new List<object>() : new List<object>();
        }

        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string k)
        {
            return d != null && d.TryGetValue(k, out object v) ? v as Dictionary<string, object> ?? new Dictionary<string, object>() : new Dictionary<string, object>();
        }
    }
}

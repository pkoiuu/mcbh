// 极简 JSON 解析器 — 在线安装器专用（避免引入第三方库，保持 exe 体积最小）
// 支持: 对象 / 数组 / 字符串 / 数字 / true / false / null
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Baihe.OnlineInstaller
{
    /// <summary>极简 JSON 解析 — 返回 Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null</summary>
    public static class SimpleJson
    {
        public static Dictionary<string, object> Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            var p = new Parser(json);
            try
            {
                var v = p.ParseValue();
                return v as Dictionary<string, object>;
            }
            catch
            {
                return null;
            }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; }

            public object ParseValue()
            {
                SkipWs();
                if (_i >= _s.Length)
                    throw new FormatException("unexpected end");
                var c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                _i++; // {
                var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                SkipWs();
                if (Peek() == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"')
                        throw new FormatException("expected key");
                    var key = ParseString();
                    SkipWs();
                    if (Peek() != ':')
                        throw new FormatException("expected :");
                    _i++;
                    var val = ParseValue();
                    obj[key] = val;
                    SkipWs();
                    var c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == '}') { _i++; return obj; }
                    throw new FormatException("expected , or }");
                }
            }

            private List<object> ParseArray()
            {
                _i++; // [
                var list = new List<object>();
                SkipWs();
                if (Peek() == ']') { _i++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWs();
                    var c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == ']') { _i++; return list; }
                    throw new FormatException("expected , or ]");
                }
            }

            private string ParseString()
            {
                _i++; // "
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    var c = _s[_i++];
                    if (c == '"')
                        return sb.ToString();
                    if (c == '\\')
                    {
                        if (_i >= _s.Length) break;
                        var e = _s[_i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_i + 4 <= _s.Length &&
                                    int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                {
                                    sb.Append((char)code);
                                    _i += 4;
                                }
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                throw new FormatException("unterminated string");
            }

            private object ParseNumber()
            {
                var start = _i;
                while (_i < _s.Length && "-+.eE0123456789".IndexOf(_s[_i]) >= 0)
                    _i++;
                var text = _s.Substring(start, _i - start);
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return l;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return d;
                throw new FormatException("bad number: " + text);
            }

            private void Expect(string word)
            {
                if (_i + word.Length > _s.Length || _s.Substring(_i, word.Length) != word)
                    throw new FormatException("bad token");
                _i += word.Length;
            }

            private char Peek()
            {
                return _i < _s.Length ? _s[_i] : '\0';
            }

            private void SkipWs()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n'))
                    _i++;
            }
        }
    }
}

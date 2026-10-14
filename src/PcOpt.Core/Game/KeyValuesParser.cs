// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Game;

/// <summary>
/// KeyValues（VDF / ACF）解析器（T7：Steam libraryfolders.vdf + appmanifest_*.acf + Epic .item）。
/// 兼容两种书写风格：
/// <list type="bullet">
/// <item>经典风格：键与字符串值均带双引号（<c>"appid"  "730"</c>）；</item>
/// <item>新风格：键不带引号（<c>libraryfolders</c> / <c>path</c>），值仍带引号。</item>
/// </list>
/// 纯函数、无 IO、无外部依赖；解析失败抛 <see cref="FormatException"/>（调用方决定降级）。
/// </summary>
public static class KeyValuesParser
{
    /// <summary>解析 KeyValues 文本为嵌套字典（叶子为 string）。</summary>
    /// <exception cref="FormatException">语法错误（括号不配对 / 引号未闭合 / 顶层多值）。</exception>
    public static Dictionary<string, object> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("KeyValues 内容为空");
        }

        var cursor = new Cursor(text);
        var root = ParseObject(cursor, isRoot: true);
        cursor.SkipWhitespaceAndComments();
        if (!cursor.IsAtEnd)
        {
            throw new FormatException($"KeyValues 根对象结束后存在多余内容（位置 {cursor.Position}）");
        }

        return root;
    }

    /// <summary>尝试解析；失败返回 null（不抛）。</summary>
    public static Dictionary<string, object>? TryParse(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : TryParseCore(text);

    private static Dictionary<string, object>? TryParseCore(string text)
    {
        try
        {
            return Parse(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, object> ParseObject(Cursor cursor, bool isRoot)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!isRoot)
        {
            cursor.SkipWhitespaceAndComments();
            cursor.Expect('{');
        }

        while (true)
        {
            cursor.SkipWhitespaceAndComments();
            if (cursor.IsAtEnd)
            {
                if (isRoot)
                {
                    return result;
                }

                throw new FormatException("KeyValues 对象缺少闭合 '}'");
            }

            if (cursor.Peek() == '}')
            {
                cursor.Read();
                return result;
            }

            var key = cursor.ReadKey();
            if (key.Length == 0)
            {
                throw new FormatException($"KeyValues 键为空（位置 {cursor.Position}）");
            }

            cursor.SkipWhitespaceAndComments();
            if (cursor.IsAtEnd)
            {
                throw new FormatException($"KeyValues 键 “{key}” 缺少值");
            }

            if (cursor.Peek() == '{')
            {
                result[key] = ParseObject(cursor, isRoot: false);
            }
            else
            {
                result[key] = cursor.ReadString();
            }
        }
    }

    /// <summary>从嵌套字典安全读取字符串；键缺失 / 类型不符返回 null。</summary>
    public static string? GetString(IReadOnlyDictionary<string, object> node, string key) =>
        node.TryGetValue(key, out var value) && value is string text ? text : null;

    /// <summary>从嵌套字典安全读取子字典；键缺失 / 类型不符返回 null。</summary>
    public static Dictionary<string, object>? GetObject(
        IReadOnlyDictionary<string, object> node,
        string key) =>
        node.TryGetValue(key, out var value) && value is Dictionary<string, object> child ? child : null;

    /// <summary>轻量字符游标。</summary>
    private sealed class Cursor
    {
        private readonly string _text;

        public Cursor(string text)
        {
            _text = text;
        }

        public int Position { get; private set; }

        public bool IsAtEnd => Position >= _text.Length;

        public char Peek() => _text[Position];

        public char Read()
        {
            var c = _text[Position];
            Position++;
            return c;
        }

        public void Expect(char expected)
        {
            if (IsAtEnd || Peek() != expected)
            {
                throw new FormatException($"KeyValues 期望 “{expected}”（位置 {Position}）");
            }

            Position++;
        }

        public void SkipWhitespaceAndComments()
        {
            while (!IsAtEnd)
            {
                var c = Peek();
                if (char.IsWhiteSpace(c))
                {
                    Position++;
                }
                else if (c == '/' && Position + 1 < _text.Length && _text[Position + 1] == '/')
                {
                    while (!IsAtEnd && Peek() != '\n' && Peek() != '\r')
                    {
                        Position++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        /// <summary>读取键：带引号则取引号内内容（支持 \" 转义），否则读到空白。</summary>
        public string ReadKey()
        {
            SkipWhitespaceAndComments();
            if (IsAtEnd)
            {
                return string.Empty;
            }

            return Peek() == '"' ? ReadQuoted() : ReadBare();
        }

        /// <summary>读取字符串值：必须带引号（KeyValues 规范）。</summary>
        public string ReadString()
        {
            SkipWhitespaceAndComments();
            if (IsAtEnd || Peek() != '"')
            {
                throw new FormatException($"KeyValues 字符串值缺少引号（位置 {Position}）");
            }

            return ReadQuoted();
        }

        private string ReadQuoted()
        {
            Expect('"');
            var builder = new System.Text.StringBuilder();
            while (!IsAtEnd)
            {
                var c = Read();
                if (c == '\\' && !IsAtEnd)
                {
                    // VDF 转义：\\ → \，\" → "，\n → 换行，其余原样保留
                    var escaped = Read();
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => escaped,
                    });
                    continue;
                }

                if (c == '"')
                {
                    return builder.ToString();
                }

                builder.Append(c);
            }

            throw new FormatException("KeyValues 字符串引号未闭合");
        }

        private string ReadBare()
        {
            var start = Position;
            while (!IsAtEnd && !char.IsWhiteSpace(Peek()) && Peek() != '{' && Peek() != '}')
            {
                Position++;
            }

            return _text[start..Position];
        }
    }
}

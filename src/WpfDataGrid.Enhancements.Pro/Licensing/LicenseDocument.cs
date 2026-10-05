using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WpfDataGrid.Enhancements.Pro.Licensing;

/// <summary>授权类型。</summary>
public enum LicenseType
{
    /// <summary>个人授权：单开发者使用。</summary>
    Personal = 0,

    /// <summary>团队授权：最多 5 名开发者。</summary>
    Team = 1,

    /// <summary>企业授权：不限开发者数量。</summary>
    Enterprise = 2
}

/// <summary>Pro 功能掩码（按位标记，控制 Pro 功能开关）。</summary>
[Flags]
public enum ProFeature : long
{
    /// <summary>无功能。</summary>
    None = 0,

    /// <summary>xlsx 导出。</summary>
    XlsxExport = 1L << 0,

    /// <summary>PDF 导出。</summary>
    PdfExport = 1L << 1,

    /// <summary>高级过滤（正则等）。</summary>
    AdvancedFilter = 1L << 2,

    /// <summary>大数据异步虚拟化。</summary>
    AsyncVirtualization = 1L << 3,

    /// <summary>全部 Pro 功能。</summary>
    All = XlsxExport | PdfExport | AdvancedFilter | AsyncVirtualization
}

/// <summary>
/// 自包含许可证文档：负载 + RSA-SHA256 签名。
/// JSON 格式为扁平对象，signature 字段携带对 canonical 负载的 PKCS#1 v1.5 签名（Base64）。
/// canonical 负载 = 固定字段顺序 + "\n" 分隔，保证签发与验证两端序列化结果完全一致。
/// </summary>
public sealed class LicenseDocument
{
    private const string CanonicalSeparator = "\n";
    private const string JsonDateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>许可证唯一 ID。</summary>
    public string LicenseId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>授权类型。</summary>
    public LicenseType LicenseType { get; set; } = LicenseType.Personal;

    /// <summary>授权名称（个人姓名 / 团队或企业名称）。</summary>
    public string HolderName { get; set; } = string.Empty;

    /// <summary>授权邮箱（用于售后识别与续费通知）。</summary>
    public string HolderEmail { get; set; } = string.Empty;

    /// <summary>颁发日期（UTC）。</summary>
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>到期日期（UTC）；null 表示永久授权。</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>功能掩码，见 <see cref="ProFeature"/>。</summary>
    public long FeatureMask { get; set; } = (long)ProFeature.All;

    /// <summary>签名（Base64 编码的 RSA-SHA256 PKCS#1 v1.5 签名值）。</summary>
    public string? Signature { get; set; }

    /// <summary>是否永久授权。</summary>
    public bool IsPermanent => ExpiresAtUtc == null;

    /// <summary>
    /// 构建 canonical 负载字符串（不含签名）。
    /// 字段顺序与格式为签发 / 验证两端的契约，禁止随意调整。
    /// </summary>
    public string BuildCanonical()
    {
        var parts = new List<string>(7)
        {
            LicenseId,
            LicenseType.ToString(),
            HolderName ?? string.Empty,
            HolderEmail ?? string.Empty,
            IssuedAtUtc.ToString(JsonDateTimeFormat, CultureInfo.InvariantCulture),
            ExpiresAtUtc.HasValue
                ? ExpiresAtUtc.Value.ToString(JsonDateTimeFormat, CultureInfo.InvariantCulture)
                : string.Empty,
            FeatureMask.ToString(CultureInfo.InvariantCulture)
        };

        return string.Join(CanonicalSeparator, parts);
    }

    /// <summary>canonical 负载的 UTF-8 字节，签名与验证均作用于该字节序列。</summary>
    public byte[] BuildCanonicalBytes() => Encoding.UTF8.GetBytes(BuildCanonical());

    /// <summary>序列化为自包含 JSON（含 signature 字段）。</summary>
    public string ToJson()
    {
        var sb = new StringBuilder(512);
        sb.Append('{');
        sb.Append("\"licenseId\":").Append(JsonQuote(LicenseId)).Append(',');
        sb.Append("\"licenseType\":").Append(JsonQuote(LicenseType.ToString())).Append(',');
        sb.Append("\"holderName\":").Append(JsonQuote(HolderName)).Append(',');
        sb.Append("\"holderEmail\":").Append(JsonQuote(HolderEmail)).Append(',');
        sb.Append("\"issuedAt\":").Append(JsonQuote(IssuedAtUtc.ToString(JsonDateTimeFormat, CultureInfo.InvariantCulture))).Append(',');
        sb.Append("\"expiresAt\":").Append(ExpiresAtUtc.HasValue ? JsonQuote(ExpiresAtUtc.Value.ToString(JsonDateTimeFormat, CultureInfo.InvariantCulture)) : "null").Append(',');
        sb.Append("\"featureMask\":").Append(FeatureMask.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"signature\":").Append(Signature != null ? JsonQuote(Signature) : "null");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>从自包含 JSON 反序列化（解析失败抛 FormatException）。</summary>
    public static LicenseDocument FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new FormatException("许可证内容为空。");

        var doc = new LicenseDocument();
        var reader = new JsonReader(json);
        reader.Expect('{');
        while (reader.Peek() != '}')
        {
            var key = reader.ReadString();
            reader.Expect(':');
            switch (key)
            {
                case "licenseId":
                    doc.LicenseId = reader.ReadStringOrNull() ?? string.Empty;
                    break;
                case "licenseType":
                    if (!Enum.TryParse(reader.ReadStringOrNull(), true, out LicenseType licenseType))
                    {
                        throw new FormatException("licenseType 无法识别：" + (reader.LastRawValue ?? string.Empty));
                    }

                    doc.LicenseType = licenseType;
                    break;
                case "holderName":
                    doc.HolderName = reader.ReadStringOrNull() ?? string.Empty;
                    break;
                case "holderEmail":
                    doc.HolderEmail = reader.ReadStringOrNull() ?? string.Empty;
                    break;
                case "issuedAt":
                    doc.IssuedAtUtc = ParseUtcDateTime(reader.ReadStringOrNull());
                    break;
                case "expiresAt":
                {
                    var expiresRaw = reader.ReadStringOrNull();
                    doc.ExpiresAtUtc = expiresRaw == null ? null : ParseUtcDateTime(expiresRaw);
                    break;
                }
                case "featureMask":
                    doc.FeatureMask = long.Parse(reader.ReadRawToken(), CultureInfo.InvariantCulture);
                    break;
                case "signature":
                    doc.Signature = reader.ReadStringOrNull();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }

            if (reader.Peek() == ',') reader.Advance();
        }

        reader.Expect('}');

        return doc;
    }

    private static DateTime ParseUtcDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("日期字段为空。");
        if (DateTime.TryParseExact(
                value,
                JsonDateTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        throw new FormatException("日期格式无效：" + value);
    }

    private static string JsonQuote(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>极简扁平 JSON 读取器（仅支持字符串 / 数字 / null / 布尔，足够解析许可证文件）。</summary>
    private sealed class JsonReader
    {
        private readonly string _text;
        private int _pos;

        /// <summary>最近一次读取的原始 token（用于错误消息）。</summary>
        public string? LastRawValue { get; private set; }

        public JsonReader(string text)
        {
            _text = text ?? string.Empty;
        }

        public char Peek()
        {
            SkipWhitespace();
            return _pos < _text.Length ? _text[_pos] : '\0';
        }

        public void Advance() => _pos++;

        public void Expect(char expected)
        {
            SkipWhitespace();
            if (_pos >= _text.Length || _text[_pos] != expected)
            {
                throw new FormatException($"期望字符 '{expected}'，实际位置 {_pos}。");
            }

            _pos++;
        }

        public string? ReadStringOrNull()
        {
            SkipWhitespace();
            if (_pos + 3 < _text.Length + 1 && string.CompareOrdinal(_text, _pos, "null", 0, 4) == 0)
            {
                _pos += 4;
                LastRawValue = "null";
                return null;
            }

            return ReadString();
        }

        public string ReadString()
        {
            SkipWhitespace();
            if (_pos >= _text.Length || _text[_pos] != '"') throw new FormatException("期望字符串。");

            _pos++;
            var sb = new StringBuilder();
            while (_pos < _text.Length)
            {
                var ch = _text[_pos++];
                if (ch == '"')
                {
                    LastRawValue = sb.ToString();
                    return sb.ToString();
                }

                if (ch == '\\' && _pos < _text.Length)
                {
                    var esc = _text[_pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_pos + 4 <= _text.Length &&
                                int.TryParse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                sb.Append((char)code);
                                _pos += 4;
                            }
                            else
                            {
                                throw new FormatException("无效的 \\u 转义。");
                            }

                            break;
                        default: throw new FormatException("无效转义字符：" + esc);
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }

            throw new FormatException("字符串未闭合。");
        }

        /// <summary>读取原始 token（数字等），不解析结构。</summary>
        public string ReadRawToken()
        {
            SkipWhitespace();
            var start = _pos;
            while (_pos < _text.Length)
            {
                var ch = _text[_pos];
                if (ch == ',' || ch == '}' || ch == ':') break;
                _pos++;
            }

            var raw = _text.Substring(start, _pos - start).Trim();
            LastRawValue = raw;
            return raw;
        }

        public void SkipValue()
        {
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == '"')
            {
                ReadString();
                return;
            }

            ReadRawToken();
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }
    }
}

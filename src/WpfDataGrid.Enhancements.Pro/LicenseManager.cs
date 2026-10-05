using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WpfDataGrid.Enhancements.Pro.Licensing;

namespace WpfDataGrid.Enhancements.Pro;

/// <summary>
/// Pro 授权校验（生产级）：离线许可证文件 + RSA-2048 签名（RSA-SHA256，PKCS#1 v1.5）。
/// 库内仅内嵌【公钥】用于验证签名，私钥只存在于签发工具与 keys/ 目录（禁止提交仓库）。
/// 无许可证时进入试用模式（可配置试用天数与行数限制）。
/// </summary>
public static class LicenseManager
{
    /// <summary>授权文件名。</summary>
    public const string LicenseFileName = "wpfdatagrid.enhancements.license";

    /// <summary>授权文件扩展名。</summary>
    public const string LicenseFileExtension = ".license";

    /// <summary>内嵌 RSA 公钥（XML 格式，RSA-2048）。由签发工具 keygen 生成后写入，私钥绝不进入本文件。internal 可写便于测试注入测试密钥。</summary>
    internal static string EmbeddedPublicKeyXml = "<RSAKeyValue><Modulus>oz3l/Cqqc9zsW1aD46CmwYjxbGrU+BUOhnNapXnXI9schu0qi94Erakb96iiFso8jFRabGRJQXfBex1WlxMo4I5YLAV0aWx73rSLtuzIg0vxfLQ/rG1QDl2YcDDBvbD6EWIoGSzkeRtxHFp6Xv+ZDjZEQWwcxtOO3vi41I9ikM/7kgZ8mTv8XDH51rYxC0npmyPYpqNBSHebyhrNw7836KZDe09WOfAkmZQEdAuffMTfFALOapiDkVTW4MkrlcycqLIZPthYED5M6tc4qUmuVX7KZWBTXI/nuaMFDdn5C6ST2/DU2en2BI6x/9U3VhHKifItHp2WKNZhDKvzzmXEaQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    private const string TrialStateFileName = "wpfdatagrid.enhancements.trial";

    private static readonly object SyncRoot = new object();
    private static LicenseDocument? _cachedDocument;
    private static LicenseStatus? _cachedStatus;

    /// <summary>授权校验状态。</summary>
    public enum LicenseStatus
    {
        /// <summary>有效（签名正确且未过期）。</summary>
        Valid = 0,

        /// <summary>无许可证文件，处于试用期（可配置天数 / 行数限制）。</summary>
        Trial = 1,

        /// <summary>无许可证文件且试用期已耗尽。</summary>
        TrialExpired = 2,

        /// <summary>许可证已过期。</summary>
        Expired = 3,

        /// <summary>签名无效或字段被篡改。</summary>
        SignatureInvalid = 4,

        /// <summary>许可证文件格式损坏。</summary>
        Corrupted = 5
    }

    /// <summary>授权错误码。</summary>
    public enum LicenseErrorCode
    {
        /// <summary>许可证文件不存在。</summary>
        FileNotFound = 0,

        /// <summary>许可证文件格式损坏。</summary>
        InvalidFormat = 1,

        /// <summary>签名无效或内容被篡改。</summary>
        SignatureInvalid = 2,

        /// <summary>许可证已过期。</summary>
        Expired = 3,

        /// <summary>当前许可证未包含该功能（功能掩码未开启）。</summary>
        FeatureNotLicensed = 4,

        /// <summary>试用期已耗尽。</summary>
        TrialExpired = 5
    }

    /// <summary>授权校验异常（携带明确错误码）。</summary>
    public sealed class LicenseException : Exception
    {
        /// <summary>错误码。</summary>
        public LicenseErrorCode ErrorCode { get; }

        public LicenseException(LicenseErrorCode errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>许可证文件路径（默认 %LocalAppData%\WpfDataGrid.Enhancements\wpfdatagrid.enhancements.license；可重定向用于测试）。</summary>
    public static string LicenseFilePath { get; set; } = BuildDefaultLicensePath();

    /// <summary>试用状态文件路径（记录首次使用日期，用于计算剩余试用天数）。</summary>
    public static string TrialStateFilePath { get; set; } = BuildDefaultTrialPath();

    /// <summary>试用期天数（无许可证时的 Pro 功能试用期限，默认 30 天）。</summary>
    public static int TrialDays { get; set; } = 30;

    /// <summary>试用期单次导出最大行数（无许可证时的数据量限制，默认 5000 行）。</summary>
    public static int TrialMaxRows { get; set; } = 5000;

    private static string BuildDefaultLicensePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "WpfDataGrid.Enhancements", LicenseFileName);
    }

    private static string BuildDefaultTrialPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "WpfDataGrid.Enhancements", TrialStateFileName);
    }

    /// <summary>当前是否已激活（有效授权）。</summary>
    public static bool IsLicensed => ValidateLicense() == LicenseStatus.Valid;

    /// <summary>
    /// 解析并验证许可证：签名完整性校验、到期检查、篡改检测。
    /// 无许可证文件时返回 <see cref="LicenseStatus.Trial"/>（首次调用会记录试用开始日期）。
    /// </summary>
    public static LicenseStatus ValidateLicense()
    {
        lock (SyncRoot)
        {
            if (_cachedDocument != null && _cachedStatus == LicenseStatus.Valid) return _cachedStatus.Value;

            if (!File.Exists(LicenseFilePath))
            {
                _cachedDocument = null;
                _cachedStatus = CheckTrialState();
                return _cachedStatus.Value;
            }

            LicenseDocument document;
            try
            {
                var json = File.ReadAllText(LicenseFilePath, Encoding.UTF8);
                document = LicenseDocument.FromJson(json);
            }
            catch (FormatException)
            {
                _cachedDocument = null;
                _cachedStatus = LicenseStatus.Corrupted;
                return _cachedStatus.Value;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _cachedDocument = null;
                _cachedStatus = LicenseStatus.Corrupted;
                return _cachedStatus.Value;
            }

            if (!VerifySignature(document))
            {
                _cachedDocument = null;
                _cachedStatus = LicenseStatus.SignatureInvalid;
                return _cachedStatus.Value;
            }

            if (document.ExpiresAtUtc.HasValue && document.ExpiresAtUtc.Value < DateTime.UtcNow)
            {
                _cachedDocument = null;
                _cachedStatus = LicenseStatus.Expired;
                return _cachedStatus.Value;
            }

            _cachedDocument = document;
            _cachedStatus = LicenseStatus.Valid;
            return _cachedStatus.Value;
        }
    }

    /// <summary>尝试获取有效许可证信息；无效 / 试用 / 不存在时返回 null。</summary>
    public static LicenseDocument? TryGetLicense()
    {
        var status = ValidateLicense();
        return status == LicenseStatus.Valid ? _cachedDocument : null;
    }

    /// <summary>
    /// 检查指定 Pro 功能是否被当前授权开启。
    /// 有效许可证按功能掩码判断；试用期（未耗尽）视为功能可用（受 <see cref="TrialDays"/> / <see cref="TrialMaxRows"/> 限制）。
    /// </summary>
    public static bool IsFeatureEnabled(ProFeature feature)
    {
        var status = ValidateLicense();
        if (status == LicenseStatus.Valid)
        {
            var document = _cachedDocument;
            return document != null && (document.FeatureMask & (long)feature) == (long)feature;
        }

        return status == LicenseStatus.Trial;
    }

    /// <summary>校验指定 Pro 功能已授权；未授权时抛出带错误码的 <see cref="LicenseException"/>。</summary>
    public static void EnsureLicensed(ProFeature feature)
    {
        var status = ValidateLicense();
        switch (status)
        {
            case LicenseStatus.Valid:
                if (!IsFeatureEnabled(feature))
                {
                    throw new LicenseException(
                        LicenseErrorCode.FeatureNotLicensed,
                        $"当前授权未包含功能：{feature}。请联系授权方升级或购买对应功能。");
                }

                return;
            case LicenseStatus.Trial:
                // 试用期允许有限使用；行数等数量限制由调用方读取 GetTrialMaxRows() 执行。
                return;
            case LicenseStatus.TrialExpired:
                throw new LicenseException(LicenseErrorCode.TrialExpired, "试用期已结束，请购买 Pro 授权后使用该功能。");
            case LicenseStatus.Expired:
                throw new LicenseException(LicenseErrorCode.Expired, "许可证已过期，请续费并重新激活。");
            case LicenseStatus.SignatureInvalid:
                throw new LicenseException(LicenseErrorCode.SignatureInvalid, "许可证签名无效或内容被篡改，请重新激活。");
            case LicenseStatus.Corrupted:
                throw new LicenseException(LicenseErrorCode.InvalidFormat, "许可证文件格式损坏，请重新获取授权文件。");
            default:
                throw new LicenseException(LicenseErrorCode.FileNotFound, "未找到许可证文件，请购买并激活 Pro 授权。");
        }
    }

    /// <summary>剩余试用天数（无许可证时）；已激活或试用已耗尽返回 0。</summary>
    public static int GetTrialRemainingDays()
    {
        var status = ValidateLicense();
        if (status != LicenseStatus.Trial) return 0;

        var start = ReadTrialStart();
        if (start == null) return TrialDays;
        var elapsed = (int)(DateTime.UtcNow.Date - start.Value).TotalDays;
        return Math.Max(0, TrialDays - elapsed);
    }

    /// <summary>试用期最大导出行数（无许可证时生效）。</summary>
    public static int GetTrialMaxRows() => TrialMaxRows;

    /// <summary>清空内部缓存（路径变更 / 文件更新后调用，便于测试）。</summary>
    public static void ResetCache()
    {
        lock (SyncRoot)
        {
            _cachedDocument = null;
            _cachedStatus = null;
        }
    }

    private static LicenseStatus CheckTrialState()
    {
        var start = ReadTrialStart();
        if (start == null)
        {
            WriteTrialStart(DateTime.UtcNow.Date);
            return LicenseStatus.Trial;
        }

        var elapsed = (int)(DateTime.UtcNow.Date - start.Value).TotalDays;
        return elapsed < TrialDays ? LicenseStatus.Trial : LicenseStatus.TrialExpired;
    }

    private static DateTime? ReadTrialStart()
    {
        try
        {
            if (!File.Exists(TrialStateFilePath)) return null;
            var raw = File.ReadAllText(TrialStateFilePath, Encoding.UTF8).Trim();
            return DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteTrialStart(DateTime date)
    {
        try
        {
            var directory = Path.GetDirectoryName(TrialStateFilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(TrialStateFilePath, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // 无法写入试用状态文件时静默降级：按首次使用当天开始计时（内存态）。
        }
    }

    private static bool VerifySignature(LicenseDocument document)
    {
        if (string.IsNullOrEmpty(document.Signature)) return false;

        try
        {
            var canonical = document.BuildCanonicalBytes();
            var signature = Convert.FromBase64String(document.Signature);

            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(EmbeddedPublicKeyXml);
                return rsa.VerifyData(canonical, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }
        catch (Exception ex) when (ex is FormatException || ex is CryptographicException)
        {
            return false;
        }
    }
}

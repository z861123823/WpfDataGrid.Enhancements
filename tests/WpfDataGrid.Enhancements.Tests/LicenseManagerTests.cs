using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WpfDataGrid.Enhancements.Pro;
using WpfDataGrid.Enhancements.Pro.Licensing;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>LicenseManager 为静态状态，且各测试会重定向文件路径，必须串行执行。</summary>
[Collection("LicenseManager")]
public class LicenseManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RSA _rsa;
    private readonly string _publicKeyXml;

    public LicenseManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LicenseManagerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _rsa = RSA.Create(2048);
        _publicKeyXml = _rsa.ToXmlString(false);

        // 注入测试公钥并重定向文件路径
        LicenseManager.EmbeddedPublicKeyXml = _publicKeyXml;
        LicenseManager.LicenseFilePath = Path.Combine(_tempDir, LicenseManager.LicenseFileName);
        LicenseManager.TrialStateFilePath = Path.Combine(_tempDir, "wpfdatagrid.enhancements.trial");
        LicenseManager.TrialDays = 30;
        LicenseManager.TrialMaxRows = 5000;
        LicenseManager.ResetCache();
    }

    public void Dispose()
    {
        LicenseManager.ResetCache();
        _rsa.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch (IOException)
            {
                // 测试临时目录清理失败不阻塞测试结果
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string WriteLicense(LicenseDocument document)
    {
        var canonical = document.BuildCanonicalBytes();
        document.Signature = Convert.ToBase64String(_rsa.SignData(canonical, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var path = Path.Combine(_tempDir, LicenseManager.LicenseFileName);
        File.WriteAllText(path, document.ToJson(), Encoding.UTF8);
        return path;
    }

    private LicenseDocument NewDocument(Action<LicenseDocument>? configure = null)
    {
        var document = new LicenseDocument
        {
            LicenseType = LicenseType.Personal,
            HolderName = "张三",
            HolderEmail = "zhangsan@example.com",
            IssuedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = null,
            FeatureMask = (long)ProFeature.All
        };
        configure?.Invoke(document);
        return document;
    }

    [Fact]
    public void ValidLicense_PassesValidation()
    {
        WriteLicense(NewDocument());

        Assert.Equal(LicenseManager.LicenseStatus.Valid, LicenseManager.ValidateLicense());
        Assert.True(LicenseManager.IsLicensed);
        var license = LicenseManager.TryGetLicense();
        Assert.NotNull(license);
        Assert.Equal(LicenseType.Personal, license!.LicenseType);
        Assert.Equal("张三", license.HolderName);
        Assert.True(license.IsPermanent);
    }

    [Theory]
    [InlineData("holderName", "李四")]
    [InlineData("holderEmail", "attacker@example.com")]
    [InlineData("licenseType", "Enterprise")]
    [InlineData("expiresAt", "\"2099-01-01T00:00:00.000Z\"")]
    public void TamperedField_InvalidatesSignature(string field, string value)
    {
        var document = NewDocument();
        var path = WriteLicense(document);

        // 重写 JSON 中目标字段（伪造篡改）
        var json = File.ReadAllText(path, Encoding.UTF8);
        var key = "\"" + field + "\":";
        var start = json.IndexOf(key, StringComparison.Ordinal);
        Assert.True(start >= 0, "字段不存在：" + field);
        start += key.Length;
        var quoted = json[start] == '"';
        var valueStart = quoted ? start + 1 : start;
        var end = json.IndexOfAny(new[] { ',', '}' }, valueStart);
        var patched = json.Substring(0, start) + (quoted ? "\"" + value + "\"" : value) + json.Substring(end);
        File.WriteAllText(path, patched, Encoding.UTF8);

        Assert.Equal(LicenseManager.LicenseStatus.SignatureInvalid, LicenseManager.ValidateLicense());
        Assert.False(LicenseManager.IsLicensed);
        Assert.Null(LicenseManager.TryGetLicense());
    }

    [Fact]
    public void MissingSignature_IsInvalid()
    {
        var document = NewDocument();
        var path = WriteLicense(document);
        var json = File.ReadAllText(path, Encoding.UTF8);
        var key = "\"signature\":";
        var start = json.IndexOf(key, StringComparison.Ordinal);
        var end = json.IndexOf('}', start);
        File.WriteAllText(path, json.Substring(0, start) + json.Substring(end), Encoding.UTF8);

        Assert.Equal(LicenseManager.LicenseStatus.SignatureInvalid, LicenseManager.ValidateLicense());
    }

    [Fact]
    public void ExpiredLicense_ReturnsExpired()
    {
        WriteLicense(NewDocument(d => d.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1)));

        Assert.Equal(LicenseManager.LicenseStatus.Expired, LicenseManager.ValidateLicense());
        Assert.False(LicenseManager.IsLicensed);
        Assert.Throws<LicenseManager.LicenseException>(() => LicenseManager.EnsureLicensed(ProFeature.XlsxExport));
    }

    [Fact]
    public void CorruptedFile_ReturnsCorrupted()
    {
        File.WriteAllText(LicenseManager.LicenseFilePath, "{ this is not json", Encoding.UTF8);

        Assert.Equal(LicenseManager.LicenseStatus.Corrupted, LicenseManager.ValidateLicense());
    }

    [Fact]
    public void FeatureMask_TogglesFeatures()
    {
        WriteLicense(NewDocument(d => d.FeatureMask = (long)(ProFeature.XlsxExport | ProFeature.AdvancedFilter)));

        Assert.Equal(LicenseManager.LicenseStatus.Valid, LicenseManager.ValidateLicense());
        Assert.True(LicenseManager.IsFeatureEnabled(ProFeature.XlsxExport));
        Assert.True(LicenseManager.IsFeatureEnabled(ProFeature.AdvancedFilter));
        Assert.False(LicenseManager.IsFeatureEnabled(ProFeature.PdfExport));
        Assert.False(LicenseManager.IsFeatureEnabled(ProFeature.AsyncVirtualization));

        // 未授权功能抛出明确错误码
        var ex = Assert.Throws<LicenseManager.LicenseException>(() => LicenseManager.EnsureLicensed(ProFeature.PdfExport));
        Assert.Equal(LicenseManager.LicenseErrorCode.FeatureNotLicensed, ex.ErrorCode);
    }

    [Fact]
    public void NoLicense_EntersTrialMode()
    {
        Assert.False(File.Exists(LicenseManager.LicenseFilePath));

        Assert.Equal(LicenseManager.LicenseStatus.Trial, LicenseManager.ValidateLicense());
        Assert.False(LicenseManager.IsLicensed);
        Assert.Null(LicenseManager.TryGetLicense());
        // 试用期内 Pro 功能视为可用
        Assert.True(LicenseManager.IsFeatureEnabled(ProFeature.XlsxExport));
        Assert.Equal(30, LicenseManager.GetTrialRemainingDays());
        Assert.Equal(5000, LicenseManager.GetTrialMaxRows());
    }

    [Fact]
    public void TrialExhausted_ReturnsTrialExpired()
    {
        LicenseManager.TrialDays = 1;
        // 试用开始日期写为 2 天前
        File.WriteAllText(
            LicenseManager.TrialStateFilePath,
            DateTime.UtcNow.Date.AddDays(-2).ToString("yyyy-MM-dd"),
            Encoding.UTF8);

        Assert.Equal(LicenseManager.LicenseStatus.TrialExpired, LicenseManager.ValidateLicense());
        Assert.False(LicenseManager.IsFeatureEnabled(ProFeature.XlsxExport));
        var ex = Assert.Throws<LicenseManager.LicenseException>(() => LicenseManager.EnsureLicensed(ProFeature.XlsxExport));
        Assert.Equal(LicenseManager.LicenseErrorCode.TrialExpired, ex.ErrorCode);
        Assert.Equal(0, LicenseManager.GetTrialRemainingDays());
    }

    [Fact]
    public void TrialStateFile_IsCreatedOnFirstUse()
    {
        Assert.False(File.Exists(LicenseManager.TrialStateFilePath));
        LicenseManager.ValidateLicense();

        Assert.True(File.Exists(LicenseManager.TrialStateFilePath));
        var content = File.ReadAllText(LicenseManager.TrialStateFilePath, Encoding.UTF8).Trim();
        Assert.Equal(DateTime.UtcNow.Date.ToString("yyyy-MM-dd"), content);
    }

    [Fact]
    public void TrialActive_DoesNotThrow_ButNotLicensed()
    {
        // 无许可证文件：进入试用模式（试用期内 Pro 功能可用，不抛异常）
        Assert.False(File.Exists(LicenseManager.LicenseFilePath));
        LicenseManager.EnsureLicensed(ProFeature.XlsxExport);
        Assert.Equal(LicenseManager.LicenseStatus.Trial, LicenseManager.ValidateLicense());
        Assert.False(LicenseManager.IsLicensed);
    }

    [Fact]
    public void TrialZeroDays_ThrowsTrialExpired()
    {
        LicenseManager.TrialDays = 0;
        // 第一次调用写入试用开始日期（当天）；第二次调用时 elapsed(0) < TrialDays(0) 为 false → 试用耗尽
        LicenseManager.ValidateLicense();
        var ex = Assert.Throws<LicenseManager.LicenseException>(() => LicenseManager.EnsureLicensed(ProFeature.XlsxExport));
        Assert.Equal(LicenseManager.LicenseErrorCode.TrialExpired, ex.ErrorCode);
    }

    /// <summary>
    /// 端到端：调用已编译的 LicenseTool 签发一张许可证，再由库使用内嵌公钥验证通过。
    /// 依赖 LicenseTool 已构建（解决方案 build 后其 DLL 位于 bin\Debug\net8.0）。
    /// </summary>
    [Fact]
    public void EndToEnd_LicenseToolSigns_AndLibraryVerifies()
    {
        // 定位 LicenseTool.dll（从测试输出目录向上回溯到仓库根）
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        var toolDll = FindToolDll(probe);
        Assert.NotNull(toolDll);

        var outPath = Path.Combine(_tempDir, "e2e_" + LicenseManager.LicenseFileName);

        var psi = new ProcessStartInfo("dotnet", $"\"{toolDll}\" sign " +
                                                 $"--key \"{PrivateKeyPath()}\" " +
                                                 $"--name \"端到端测试\" " +
                                                 $"--email \"e2e@example.com\" " +
                                                 $"--type Team " +
                                                 $"--features XlsxExport,PdfExport " +
                                                 $"--out \"{outPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"LicenseTool 退出码 {process.ExitCode}：{stdout}{stderr}");
        Assert.True(File.Exists(outPath), "许可证文件未生成");

        // 库使用内嵌公钥（当前为测试注入的公钥）验证工具签发的许可证
        LicenseManager.LicenseFilePath = outPath;
        LicenseManager.ResetCache();
        Assert.Equal(LicenseManager.LicenseStatus.Valid, LicenseManager.ValidateLicense());
        var license = LicenseManager.TryGetLicense();
        Assert.NotNull(license);
        Assert.Equal(LicenseType.Team, license!.LicenseType);
        Assert.Equal("端到端测试", license.HolderName);
        Assert.True(LicenseManager.IsFeatureEnabled(ProFeature.XlsxExport));
        Assert.False(LicenseManager.IsFeatureEnabled(ProFeature.AdvancedFilter));
    }

    private static string? FindToolDll(DirectoryInfo? dir)
    {
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src",
                "WpfDataGrid.Enhancements.LicenseTool",
                "bin",
                "Debug",
                "net8.0",
                "WpfDataGrid.Enhancements.LicenseTool.dll");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private string PrivateKeyPath()
    {
        // 端到端测试需要与测试公钥匹配的私钥：直接从当前测试 RSA 导出 PEM 写临时文件
        var pemPath = Path.Combine(_tempDir, "e2e.private.pem");
        File.WriteAllText(pemPath, ExportPkcs1Pem(_rsa), Encoding.UTF8);
        return pemPath;
    }

    private static string ExportPkcs1Pem(RSA rsa)
    {
        var der = rsa.ExportRSAPrivateKey();
        var base64 = Convert.ToBase64String(der);
        var sb = new StringBuilder("-----BEGIN RSA PRIVATE KEY-----\n");
        for (var i = 0; i < base64.Length; i += 64)
        {
            sb.Append(base64, i, Math.Min(64, base64.Length - i)).Append('\n');
        }

        sb.Append("-----END RSA PRIVATE KEY-----\n");
        return sb.ToString();
    }
}

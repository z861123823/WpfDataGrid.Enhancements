using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WpfDataGrid.Enhancements.Pro.Licensing;

namespace WpfDataGrid.Enhancements.LicenseTool;

/// <summary>
/// Pro 授权签发工具（仅供授权方内部使用，不随 NuGet 分发）。
/// 子命令：keygen / sign / verify；无子命令进入交互式签发。
/// </summary>
internal static class Program
{
    private const int RsaKeySize = 2048;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                return InteractiveSign();
            }

            switch (args[0].ToLowerInvariant())
            {
                case "keygen":
                    return KeyGen(args[1..]);
                case "sign":
                    return Sign(args[1..]);
                case "verify":
                    return Verify(args[1..]);
                case "help":
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine("未知子命令：" + args[0] + "。使用 help 查看用法。");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("错误：" + ex.Message);
            return 1;
        }
    }

    // ---------------- keygen ----------------

    private static int KeyGen(string[] args)
    {
        var keysDir = Path.Combine(FindRepositoryRoot(), "keys");
        string? embedTarget = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--keys-dir" when i + 1 < args.Length:
                    keysDir = Path.GetFullPath(args[++i]);
                    break;
                case "--embed" when i + 1 < args.Length:
                    embedTarget = Path.GetFullPath(args[++i]);
                    break;
            }
        }

        Directory.CreateDirectory(keysDir);

        using var rsa = RSA.Create(RsaKeySize);
        var privatePemPath = Path.Combine(keysDir, "wpfdatagrid.enhancements.private.pem");
        var publicXmlPath = Path.Combine(keysDir, "wpfdatagrid.enhancements.public.xml");

        // PKCS#1 私钥（BEGIN RSA PRIVATE KEY）
        var privateDer = rsa.ExportRSAPrivateKey();
        var privatePem = PemEncode("RSA PRIVATE KEY", privateDer);
        File.WriteAllText(privatePemPath, privatePem, new UTF8Encoding(false));

        // 公钥 XML（与库端 FromXmlString 兼容）
        var publicXml = rsa.ToXmlString(false);
        File.WriteAllText(publicXmlPath, publicXml, new UTF8Encoding(false));

        Console.WriteLine("密钥对已生成：");
        Console.WriteLine("  私钥（禁止提交仓库）：" + privatePemPath);
        Console.WriteLine("  公钥（可内嵌 / 分发）：" + publicXmlPath);

        if (embedTarget != null)
        {
            if (!File.Exists(embedTarget))
            {
                Console.Error.WriteLine("embed 目标不存在：" + embedTarget);
                return 2;
            }

            var source = File.ReadAllText(embedTarget, Encoding.UTF8);
            var marker = "\"<!--PUBLIC_KEY-->\"";
            if (!source.Contains(marker, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("未在目标文件找到公钥占位符 " + marker + "。");
                return 2;
            }

            var escaped = publicXml.Replace("\"", "\\\"", StringComparison.Ordinal);
            File.WriteAllText(embedTarget, source.Replace(marker, "\"" + escaped + "\"", StringComparison.Ordinal), new UTF8Encoding(false));
            Console.WriteLine("公钥已内嵌到：" + embedTarget);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("请将公钥 XML 内嵌到 LicenseManager.cs 的 EmbeddedPublicKeyXml（或使用 --embed 自动替换）。");
            Console.WriteLine();
            Console.WriteLine(publicXml);
        }

        return 0;
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WpfDataGrid.Enhancements.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        // 兜底：当前工作目录
        return Environment.CurrentDirectory;
    }

    // ---------------- sign ----------------

    private static int Sign(string[] args)
    {
        string? keyPath = null;
        var type = LicenseType.Personal;
        var name = string.Empty;
        var email = string.Empty;
        DateTime? expires = null;
        ProFeature features = ProFeature.All;
        string? outPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--key" when i + 1 < args.Length:
                    keyPath = Path.GetFullPath(args[++i]);
                    break;
                case "--type" when i + 1 < args.Length:
                    if (!Enum.TryParse(args[++i], true, out type))
                    {
                        Console.Error.WriteLine("无效授权类型，可选：Personal / Team / Enterprise。");
                        return 2;
                    }

                    break;
                case "--name" when i + 1 < args.Length:
                    name = args[++i];
                    break;
                case "--email" when i + 1 < args.Length:
                    email = args[++i];
                    break;
                case "--expires" when i + 1 < args.Length:
                    if (!DateTime.TryParseExact(
                            args[++i],
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var parsed))
                    {
                        Console.Error.WriteLine("到期日期格式应为 yyyy-MM-dd，留空表示永久。");
                        return 2;
                    }

                    expires = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    break;
                case "--features" when i + 1 < args.Length:
                    if (!TryParseFeatures(args[++i], out features))
                    {
                        return 2;
                    }

                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = Path.GetFullPath(args[++i]);
                    break;
            }
        }

        if (string.IsNullOrEmpty(keyPath))
        {
            Console.Error.WriteLine("缺少 --key <私钥 PEM 路径>。");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("缺少 --name <授权名称>。");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine("缺少 --email <授权邮箱>。");
            return 2;
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(keyPath, Encoding.UTF8));

        var document = new LicenseDocument
        {
            LicenseId = Guid.NewGuid().ToString("N"),
            LicenseType = type,
            HolderName = name,
            HolderEmail = email,
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expires,
            FeatureMask = (long)features
        };

        var canonical = document.BuildCanonicalBytes();
        var signature = rsa.SignData(canonical, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        document.Signature = Convert.ToBase64String(signature);

        if (string.IsNullOrEmpty(outPath))
        {
            outPath = Path.Combine(Environment.CurrentDirectory, "wpfdatagrid.enhancements.license");
        }

        var directory = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outPath, document.ToJson(), new UTF8Encoding(false));

        Console.WriteLine("许可证已签发：" + outPath);
        Console.WriteLine("  类型：" + document.LicenseType);
        Console.WriteLine("  名称：" + document.HolderName);
        Console.WriteLine("  邮箱：" + document.HolderEmail);
        Console.WriteLine("  颁发：" + document.IssuedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Console.WriteLine("  到期：" + (document.ExpiresAtUtc.HasValue ? document.ExpiresAtUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "永久"));
        Console.WriteLine("  功能：" + features);
        return 0;
    }

    private static bool TryParseFeatures(string raw, out ProFeature features)
    {
        features = ProFeature.None;
        foreach (var part in raw.Split(new[] { ',', '|', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse(part.Trim(), true, out ProFeature feature))
            {
                Console.Error.WriteLine("无效功能：" + part.Trim() + "。可选：" + string.Join(", ", Enum.GetNames(typeof(ProFeature))));
                return false;
            }

            features |= feature;
        }

        if (features == ProFeature.None)
        {
            Console.Error.WriteLine("功能掩码为空。");
            return false;
        }

        return true;
    }

    // ---------------- verify ----------------

    private static int Verify(string[] args)
    {
        string? keyXmlPath = null;
        string? licensePath = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--key-xml" when i + 1 < args.Length:
                    keyXmlPath = Path.GetFullPath(args[++i]);
                    break;
                case "--license" when i + 1 < args.Length:
                    licensePath = Path.GetFullPath(args[++i]);
                    break;
            }
        }

        if (string.IsNullOrEmpty(keyXmlPath) || string.IsNullOrEmpty(licensePath))
        {
            Console.Error.WriteLine("verify 需要 --key-xml <公钥XML> --license <许可证文件>。");
            return 2;
        }

        var document = LicenseDocument.FromJson(File.ReadAllText(licensePath, Encoding.UTF8));
        if (string.IsNullOrEmpty(document.Signature))
        {
            Console.Error.WriteLine("签名缺失。");
            return 2;
        }

        using var rsa = RSA.Create();
        rsa.FromXmlString(File.ReadAllText(keyXmlPath, Encoding.UTF8));
        var ok = rsa.VerifyData(document.BuildCanonicalBytes(), Convert.FromBase64String(document.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Console.WriteLine(ok ? "签名有效。" : "签名无效！");
        return ok ? 0 : 2;
    }

    // ---------------- interactive ----------------

    private static int InteractiveSign()
    {
        var keysDir = Path.Combine(FindRepositoryRoot(), "keys");
        var keyPath = Path.Combine(keysDir, "wpfdatagrid.enhancements.private.pem");

        Console.WriteLine("=== WpfDataGrid.Enhancements Pro 授权签发（交互式） ===");
        if (!File.Exists(keyPath))
        {
            Console.WriteLine("未找到私钥 " + keyPath + "，请先运行 keygen。");
            return 2;
        }

        Console.Write("授权类型 [Personal/Team/Enterprise]（默认 Personal）：");
        var typeRaw = Console.ReadLine();
        var type = LicenseType.Personal;
        if (!string.IsNullOrWhiteSpace(typeRaw) && !Enum.TryParse(typeRaw.Trim(), true, out type))
        {
            Console.Error.WriteLine("无效授权类型。");
            return 2;
        }

        Console.Write("授权名称：");
        var name = (Console.ReadLine() ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            Console.Error.WriteLine("授权名称不能为空。");
            return 2;
        }

        Console.Write("授权邮箱：");
        var email = (Console.ReadLine() ?? string.Empty).Trim();
        if (email.Length == 0)
        {
            Console.Error.WriteLine("授权邮箱不能为空。");
            return 2;
        }

        Console.Write("到期日期（yyyy-MM-dd，留空=永久）：");
        var expiresRaw = (Console.ReadLine() ?? string.Empty).Trim();
        DateTime? expires = null;
        if (expiresRaw.Length > 0)
        {
            if (!DateTime.TryParseExact(expiresRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                Console.Error.WriteLine("日期格式无效。");
                return 2;
            }

            expires = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        Console.WriteLine("功能（逗号分隔，默认全部）：XlsxExport / PdfExport / AdvancedFilter / AsyncVirtualization");
        Console.Write("功能掩码：");
        var featuresRaw = (Console.ReadLine() ?? string.Empty).Trim();
        var features = ProFeature.All;
        if (featuresRaw.Length > 0 && !TryParseFeatures(featuresRaw, out features))
        {
            return 2;
        }

        Console.Write("输出路径（默认当前目录 wpfdatagrid.enhancements.license）：");
        var outRaw = (Console.ReadLine() ?? string.Empty).Trim();
        var outPath = outRaw.Length > 0 ? Path.GetFullPath(outRaw) : Path.Combine(Environment.CurrentDirectory, "wpfdatagrid.enhancements.license");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(keyPath, Encoding.UTF8));
        var document = new LicenseDocument
        {
            LicenseId = Guid.NewGuid().ToString("N"),
            LicenseType = type,
            HolderName = name,
            HolderEmail = email,
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expires,
            FeatureMask = (long)features
        };

        var canonical = document.BuildCanonicalBytes();
        document.Signature = Convert.ToBase64String(rsa.SignData(canonical, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        var directory = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outPath, document.ToJson(), new UTF8Encoding(false));

        Console.WriteLine();
        Console.WriteLine("许可证已签发：" + outPath);
        return 0;
    }

    // ---------------- helpers ----------------

    private static string PemEncode(string label, byte[] der)
    {
        var base64 = Convert.ToBase64String(der);
        var sb = new StringBuilder(base64.Length + 64);
        sb.Append("-----BEGIN ").Append(label).Append("-----").AppendLine();
        var lineLength = 64;
        for (var i = 0; i < base64.Length; i += lineLength)
        {
            var length = Math.Min(lineLength, base64.Length - i);
            sb.Append(base64, i, length).AppendLine();
        }

        sb.Append("-----END ").Append(label).Append("-----").AppendLine();
        return sb.ToString();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("WpfDataGrid.Enhancements Pro 授权签发工具");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  LicenseTool keygen [--keys-dir <目录>] [--embed <LicenseManager.cs 路径>]");
        Console.WriteLine("      生成 RSA-2048 密钥对到 keys/ 目录；--embed 可把公钥 XML 自动内嵌进库。");
        Console.WriteLine();
        Console.WriteLine("  LicenseTool sign --key <私钥PEM> --name <名称> --email <邮箱>");
        Console.WriteLine("             [--type Personal|Team|Enterprise] [--expires yyyy-MM-dd]");
        Console.WriteLine("             [--features XlsxExport,PdfExport,...] [--out <路径>]");
        Console.WriteLine("      使用私钥签发许可证（缺省永久授权、全部功能）。");
        Console.WriteLine();
        Console.WriteLine("  LicenseTool verify --key-xml <公钥XML> --license <许可证文件>");
        Console.WriteLine("      离线验证许可证签名（工具自检用）。");
        Console.WriteLine();
        Console.WriteLine("  LicenseTool （无参数） 交互式签发。");
    }
}

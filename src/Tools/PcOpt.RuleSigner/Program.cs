// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using NSec.Cryptography;

namespace PcOpt.Tools.RuleSigner;

/// <summary>
/// 开发用规则包签名工具（阶段 2 / T20 配套）：为 <c>rules/tweaks/*.yaml</c> 生成 Ed25519 分离签名
/// （64 字节，Base64 写入同名 <c>*.sig</c>）。
/// <para>
/// 合规与安全约束：
/// <list type="bullet">
/// <item>仅用于维护者<b>离线</b>签署官方规则包；<b>不参与产品发布</b>，不随产品分发；</item>
/// <item><b>不内嵌私钥</b>：私钥从外部文件读取（默认 <c>.workbuddy/signing/ed25519-private-key.txt</c>，
/// 该目录已被 .gitignore 排除），任何情况下不写入仓库；</item>
/// <item>签名对象为 YAML 文件的<b>原始字节</b>，与 <c>RuleSignatureVerifier.Verify</c> 的验证对象严格一致。</item>
/// </list>
/// </para>
/// 用法：
/// <code>
/// dotnet run --project src/Tools/PcOpt.RuleSigner -- rules/tweaks
/// dotnet run --project src/Tools/PcOpt.RuleSigner -- rules/tweaks --key .workbuddy/signing/ed25519-private-key.txt
/// dotnet run --project src/Tools/PcOpt.RuleSigner -- rules/tweaks --verify
/// </code>
/// </summary>
public static class Program
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    private const string DefaultKeyRelativePath = ".workbuddy/signing/ed25519-private-key.txt";
    private const string RulesSubDirectory = @"rules\tweaks";
    private const string SolutionFileName = "pcopt-suite.sln";

    /// <summary>程序入口。</summary>
    /// <param name="args">[规则目录] [--key 私钥文件] [--verify]</param>
    /// <returns>进程退出码：0 成功，1 失败。</returns>
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[错误] {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var root = FindRepositoryRoot();
        var verifyOnly = args.Contains("--verify", StringComparer.OrdinalIgnoreCase);
        var keyPath = ReadOption(args, "--key") ?? Path.Combine(root, DefaultKeyRelativePath);
        var rulesDirectory = ResolveRulesDirectory(root, args);

        if (!Directory.Exists(rulesDirectory))
        {
            Console.Error.WriteLine($"[错误] 规则目录不存在：{rulesDirectory}");
            return 1;
        }

        var yamlFiles = Directory.GetFiles(rulesDirectory, "*.yaml", SearchOption.TopDirectoryOnly);
        Array.Sort(yamlFiles, StringComparer.OrdinalIgnoreCase);
        if (yamlFiles.Length == 0)
        {
            Console.Error.WriteLine($"[错误] 规则目录内没有 *.yaml：{rulesDirectory}");
            return 1;
        }

        Console.WriteLine($"规则目录：{rulesDirectory}");
        Console.WriteLine($"规则数量：{yamlFiles.Length}");

        if (verifyOnly)
        {
            return VerifyAll(yamlFiles, keyPath);
        }

        return SignAll(yamlFiles, keyPath);
    }

    private static int SignAll(string[] yamlFiles, string keyPath)
    {
        var key = LoadPrivateKey(keyPath);
        var signed = 0;
        foreach (var yamlPath in yamlFiles)
        {
            var content = File.ReadAllBytes(yamlPath);
            var signature = Algorithm.Sign(key, content);
            var sigPath = Path.ChangeExtension(yamlPath, ".sig");
            File.WriteAllText(sigPath, Convert.ToBase64String(signature) + "\n", new UTF8Encoding(false));
            signed++;
            Console.WriteLine($"  已签名 {Path.GetFileName(yamlPath)} -> {Path.GetFileName(sigPath)}");
        }

        Console.WriteLine($"完成：{signed} 个签名文件已写入。");
        return 0;
    }

    private static int VerifyAll(string[] yamlFiles, string keyPath)
    {
        var publicKeyPath = Path.Combine(FindRepositoryRoot(), "rules", "public.key");
        var publicKey = LoadPublicKey(publicKeyPath);
        var failed = 0;
        foreach (var yamlPath in yamlFiles)
        {
            var sigPath = Path.ChangeExtension(yamlPath, ".sig");
            if (!File.Exists(sigPath))
            {
                Console.Error.WriteLine($"  [失败] 缺少签名文件：{Path.GetFileName(sigPath)}");
                failed++;
                continue;
            }

            var content = File.ReadAllBytes(yamlPath);
            var signature = Convert.FromBase64String(File.ReadAllText(sigPath).Trim());
            var ok = Algorithm.Verify(publicKey, content, signature);
            Console.WriteLine($"  {(ok ? "通过" : "失败")} {Path.GetFileName(yamlPath)}");
            if (!ok)
            {
                failed++;
            }
        }

        Console.WriteLine(failed == 0
            ? $"全部通过：{yamlFiles.Length} 项（公钥：{publicKeyPath}）"
            : $"存在失败：{failed}/{yamlFiles.Length}");
        return failed == 0 ? 0 : 1;
    }

    private static Key LoadPrivateKey(string keyPath)
    {
        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException($"找不到私钥文件：{keyPath}（签名需维护者本机私钥，禁止提交到仓库）");
        }

        var raw = ReadBase64Payload(keyPath);
        return Key.Import(Algorithm, raw, KeyBlobFormat.RawPrivateKey);
    }

    private static PublicKey LoadPublicKey(string publicKeyPath)
    {
        if (!File.Exists(publicKeyPath))
        {
            throw new FileNotFoundException($"找不到公钥文件：{publicKeyPath}");
        }

        var raw = ReadBase64Payload(publicKeyPath);
        return PublicKey.Import(Algorithm, raw, KeyBlobFormat.RawPublicKey);
    }

    /// <summary>
    /// 读取文件中的 Base64 载荷：跳过以 <c>#</c> 或 <c>&gt;</c> 开头的注释/说明行与空行，
    /// 取<b>第一条</b>有效 Base64 行（私钥文件同时含"私钥 + 对应公钥"两段，取首段即可）。
    /// </summary>
    private static byte[] ReadBase64Payload(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('>'))
            {
                continue;
            }

            try
            {
                return Convert.FromBase64String(line);
            }
            catch (FormatException)
            {
                // 非 Base64 的说明行：跳过，继续找首段密钥
            }
        }

        throw new FormatException($"文件 {path} 中找不到有效的 Base64 密钥载荷。");
    }

    private static string ResolveRulesDirectory(string root, string[] args)
    {
        var positional = args.FirstOrDefault(arg => !arg.StartsWith('-') && !string.Equals(arg, "verify", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(positional))
        {
            return Path.Combine(root, RulesSubDirectory);
        }

        return Path.IsPathRooted(positional) ? positional : Path.Combine(root, positional);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>自当前目录向上查找含 <c>pcopt-suite.sln</c> 的仓库根。</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"未能从当前目录向上找到 {SolutionFileName}，请在仓库内运行本工具。");
    }
}

// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using NSec.Cryptography;
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Tests.Tweak;

/// <summary>Ed25519 测试密钥工具（临时密钥，绝不提交到仓库）。</summary>
internal static class TestKeys
{
    /// <summary>生成临时密钥对并对内容签名。</summary>
    public static (byte[] PrivateKey, byte[] PublicKey, byte[] Signature) CreateAndSign(byte[] content)
    {
        using var key = Key.Create(
            SignatureAlgorithm.Ed25519,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var signature = SignatureAlgorithm.Ed25519.Sign(key, content);
        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var privateKey = key.Export(KeyBlobFormat.RawPrivateKey);
        return (privateKey, publicKey, signature);
    }

    /// <summary>使用临时私钥对内容签名（模拟官方签名流程）。</summary>
    public static byte[] Sign(byte[] privateKey, byte[] content)
    {
        using var key = Key.Import(SignatureAlgorithm.Ed25519, privateKey, KeyBlobFormat.RawPrivateKey);
        return SignatureAlgorithm.Ed25519.Sign(key, content);
    }
}

/// <summary>
/// <see cref="RuleSignatureVerifier"/> 单元测试：未签名 / 篡改 / 错误长度 / 坏公钥均拒绝（架构 3.5）。
/// </summary>
public sealed class RuleSignatureVerifierTests
{
    private static readonly byte[] Content = System.Text.Encoding.UTF8.GetBytes(
        "id: svc.sysmain.disable\ncategory: 性能\nrisk: safe\ndisplayName: 测试规则\n");

    [Fact]
    public void Verify_ValidSignature_ShouldSucceed()
    {
        var (_, publicKey, signature) = TestKeys.CreateAndSign(Content);
        var verifier = RuleSignatureVerifier.Create(publicKey).Value!;

        var result = verifier.Verify(Content, signature);

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeEmpty();
    }

    [Fact]
    public void Verify_TamperedContent_ShouldFailWithClearError()
    {
        var (_, publicKey, signature) = TestKeys.CreateAndSign(Content);
        var verifier = RuleSignatureVerifier.Create(publicKey).Value!;
        var tampered = (byte[])Content.Clone();
        tampered[0] ^= 0xFF;

        var result = verifier.Verify(tampered, signature);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("篡改");
    }

    [Fact]
    public void Verify_UnsignedContent_ShouldFail()
    {
        var (_, publicKey, _) = TestKeys.CreateAndSign(Content);
        var verifier = RuleSignatureVerifier.Create(publicKey).Value!;

        var result = verifier.Verify(Content, Array.Empty<byte>());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("未签名");
    }

    [Fact]
    public void Verify_NullSignature_ShouldFail()
    {
        var (_, publicKey, _) = TestKeys.CreateAndSign(Content);
        var verifier = RuleSignatureVerifier.Create(publicKey).Value!;

        var result = verifier.Verify(Content, null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("未签名");
    }

    [Fact]
    public void Verify_WrongLengthSignature_ShouldFail()
    {
        var (_, publicKey, _) = TestKeys.CreateAndSign(Content);
        var verifier = RuleSignatureVerifier.Create(publicKey).Value!;

        var result = verifier.Verify(Content, new byte[16]);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("签名长度");
    }

    [Fact]
    public void Verify_SignatureFromDifferentKey_ShouldFail()
    {
        var (_, otherPublicKey, otherSignature) = TestKeys.CreateAndSign(Content);
        var (_, publicKey, _) = TestKeys.CreateAndSign(Content);
        var verifier = RuleSignatureVerifier.Create(publicKey).Value!;

        var result = verifier.Verify(Content, otherSignature);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("篡改");
    }

    [Fact]
    public void Create_EmptyKey_ShouldFail()
    {
        var result = RuleSignatureVerifier.Create(Array.Empty<byte>());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("公钥");
    }

    [Fact]
    public void Create_InvalidLengthKey_ShouldFail()
    {
        // 合法 Base64 但解码后长度错误（16 字节 ≠ Ed25519 公钥 32 字节），
        // 区别于"不是 Base64"的格式错误——此处应报"长度"问题
        var validBase64ButWrongLength = System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(new byte[16]));
        var result = RuleSignatureVerifier.Create(validBase64ButWrongLength);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("长度");
    }

    [Fact]
    public void Create_NotBase64_ShouldFail()
    {
        var result = RuleSignatureVerifier.Create(System.Text.Encoding.UTF8.GetBytes("这不是 base64 !!!"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("格式");
    }

    [Fact]
    public void CreateFromFile_ShouldParseBase64WithComments()
    {
        var (_, publicKey, signature) = TestKeys.CreateAndSign(Content);
        var filePath = Path.Combine(Path.GetTempPath(), $"pcopt-key-{Guid.NewGuid():N}.key");
        try
        {
            File.WriteAllLines(filePath, new[]
            {
                "# 测试公钥",
                Convert.ToBase64String(publicKey),
            });

            var verifier = RuleSignatureVerifier.CreateFromFile(filePath).Value!;
            var result = verifier.Verify(Content, signature);

            result.IsSuccess.Should().BeTrue();
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void CreateFromFile_MissingFile_ShouldFail()
    {
        var result = RuleSignatureVerifier.CreateFromFile(Path.Combine(Path.GetTempPath(), "pcopt-missing.key"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("读取公钥文件失败");
    }

    [Fact]
    public void Verify_OfficialRepositoryPublicKey_ShouldLoad()
    {
        // rules/public.key 随仓库发布，必须可加载（公钥格式回归测试）
        var repoRoot = FindRepositoryRoot();
        var publicKeyPath = Path.Combine(repoRoot, "rules", "public.key");

        File.Exists(publicKeyPath).Should().BeTrue();
        var result = RuleSignatureVerifier.CreateFromFile(publicKeyPath);

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "pcopt-suite.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("未找到仓库根目录（pcopt-suite.sln）");
    }
}

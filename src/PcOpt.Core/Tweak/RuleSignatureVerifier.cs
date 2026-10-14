// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace PcOpt.Core.Tweak;

/// <summary>
/// 规则包签名验证器（Ed25519 / RFC 8032，架构 3.5）。
/// 实现说明：.NET BCL 未内置 Ed25519 算法，故采用 NSec.Cryptography（BSD-3-Clause，
/// libsodium 实现）承担验签，已在 NOTICE 登记；仅使用"验签"单向能力，无私钥相关代码路径。
/// </summary>
public sealed class RuleSignatureVerifier
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    private readonly PublicKey _publicKey;

    private RuleSignatureVerifier(PublicKey publicKey)
    {
        _publicKey = publicKey;
    }

    /// <summary>Ed25519 原始公钥长度（字节）。</summary>
    public const int PublicKeySize = 32;

    /// <summary>Ed25519 分离签名长度（字节）。</summary>
    public const int SignatureSize = 64;

    /// <summary>
    /// 从公钥字节创建验证器。支持 32 字节原始公钥或其 Base64 编码（ rules/public.key 即 Base64 文本）。
    /// </summary>
    public static Result<RuleSignatureVerifier> Create(byte[] publicKeyBytes)
    {
        if (publicKeyBytes is null || publicKeyBytes.Length == 0)
        {
            return Result<RuleSignatureVerifier>.Fail("签名公钥为空");
        }

        byte[] rawKey;
        try
        {
            rawKey = publicKeyBytes.Length == PublicKeySize
                ? publicKeyBytes
                : Convert.FromBase64String(Encoding.UTF8.GetString(publicKeyBytes).Trim());
        }
        catch (FormatException)
        {
            return Result<RuleSignatureVerifier>.Fail("签名公钥格式无效：应为 32 字节原始密钥或其 Base64 编码");
        }

        if (rawKey.Length != PublicKeySize)
        {
            return Result<RuleSignatureVerifier>.Fail($"签名公钥长度无效：期望 {PublicKeySize} 字节，实际 {rawKey.Length} 字节");
        }

        try
        {
            var publicKey = PublicKey.Import(Algorithm, rawKey, KeyBlobFormat.RawPublicKey);
            return Result<RuleSignatureVerifier>.Ok(new RuleSignatureVerifier(publicKey));
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return Result<RuleSignatureVerifier>.Fail($"签名公钥导入失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 从公钥文件创建验证器。文件中以 <c>#</c> 开头的行视为注释，其余内容拼接后按 Base64 解析。
    /// </summary>
    public static Result<RuleSignatureVerifier> CreateFromFile(string publicKeyPath)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPath))
        {
            return Result<RuleSignatureVerifier>.Fail("公钥文件路径为空");
        }

        try
        {
            var lines = File.ReadAllLines(publicKeyPath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToArray();
            if (lines.Length == 0)
            {
                return Result<RuleSignatureVerifier>.Fail("公钥文件内容为空");
            }

            return Create(Encoding.UTF8.GetBytes(string.Concat(lines)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<RuleSignatureVerifier>.Fail($"读取公钥文件失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 验证规则包内容与分离签名。
    /// 未签名（签名为空）或签名与内容不符时返回失败，并给出明确错误信息（架构 3.5：拒绝执行）。
    /// </summary>
    public Result Verify(byte[] content, byte[]? signature)
    {
        if (content is null || content.Length == 0)
        {
            return Result.Fail("规则包内容为空");
        }

        if (signature is null || signature.Length == 0)
        {
            return Result.Fail("规则包未签名（缺少 .sig 签名文件），已拒绝加载");
        }

        if (signature.Length != SignatureSize)
        {
            return Result.Fail($"签名长度无效：期望 {SignatureSize} 字节，实际 {signature.Length} 字节");
        }

        try
        {
            var valid = Algorithm.Verify(_publicKey, content, signature);
            return valid
                ? Result.Ok()
                : Result.Fail("签名与规则包内容不匹配（规则包可能被篡改），已拒绝加载");
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return Result.Fail($"验签过程失败：{ex.Message}");
        }
    }
}

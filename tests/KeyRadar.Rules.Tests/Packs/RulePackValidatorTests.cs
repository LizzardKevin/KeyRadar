using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyRadar.Rules.Packs;
using NSec.Cryptography;

namespace KeyRadar.Rules.Tests.Packs;

public sealed class RulePackValidatorTests
{
    [Fact]
    public void Valid_signed_declarative_pack_is_accepted()
    {
        var package = CreatePack();

        var result = RulePackValidator.Validate(package.Stream, package.PublicKey);

        Assert.True(result.IsValid);
        Assert.Equal(RulePackValidationError.None, result.Error);
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("rules/setup.ps1")]
    [InlineData("../outside.json")]
    [InlineData("rules\\ambiguous.json")]
    public void Executable_script_and_unsafe_paths_are_rejected(string unsafePath)
    {
        var package = CreatePack(extraPath: unsafePath);

        var result = RulePackValidator.Validate(package.Stream, package.PublicKey);

        Assert.False(result.IsValid);
        Assert.Equal(RulePackValidationError.DisallowedEntry, result.Error);
    }

    [Fact]
    public void Hash_mismatch_is_rejected()
    {
        var package = CreatePack(useIncorrectHash: true);

        var result = RulePackValidator.Validate(package.Stream, package.PublicKey);

        Assert.Equal(RulePackValidationError.HashMismatch, result.Error);
    }

    [Fact]
    public void Signature_from_another_key_is_rejected()
    {
        var package = CreatePack(signWithAnotherKey: true);

        var result = RulePackValidator.Validate(package.Stream, package.PublicKey);

        Assert.Equal(RulePackValidationError.InvalidSignature, result.Error);
    }

    private static TestPack CreatePack(
        string? extraPath = null,
        bool useIncorrectHash = false,
        bool signWithAnotherKey = false)
    {
        var ruleBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"applicationId\":\"wechat\",\"variantId\":\"cn-desktop\",\"displayName\":{\"zh-CN\":\"微信\"},\"match\":{\"executables\":[\"WeChat.exe\"],\"publishers\":[],\"versionRange\":null,\"packageFamilyNames\":[],\"distribution\":null},\"hotkeys\":[]}");
        var files = new List<object>
        {
            new
            {
                path = "rules/wechat-cn-desktop.json",
                sha256 = useIncorrectHash
                    ? new string('0', 64)
                    : Convert.ToHexString(SHA256.HashData(ruleBytes)).ToLowerInvariant(),
            },
        };

        if (extraPath is not null)
        {
            files.Add(new
            {
                path = extraPath,
                sha256 = Convert.ToHexString(SHA256.HashData([0x01])).ToLowerInvariant(),
            });
        }

        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            packId = "keyradar.test",
            version = "1.0.0",
            files,
        });

        var algorithm = SignatureAlgorithm.Ed25519;
        using var trustedKey = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });
        using var alternateKey = Key.Create(algorithm);
        var signature = algorithm.Sign(signWithAnotherKey ? alternateKey : trustedKey, manifestBytes);
        var publicKey = trustedKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", manifestBytes);
            WriteEntry(archive, "rules/wechat-cn-desktop.json", ruleBytes);
            if (extraPath is not null)
            {
                WriteEntry(archive, extraPath, [0x01]);
            }

            WriteEntry(archive, "signature.ed25519", Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
        }

        stream.Position = 0;
        return new TestPack(stream, publicKey);
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var entryStream = entry.Open();
        entryStream.Write(content);
    }

    private sealed record TestPack(MemoryStream Stream, byte[] PublicKey);
}

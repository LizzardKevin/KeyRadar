namespace KeyRadar.Rules.Packs;

public static class OfficialRulePackLoader
{
    public static RulePackReadResult Load(
        string activePackPath,
        string bundledPackPath,
        ReadOnlySpan<byte> publicKeyBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activePackPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundledPackPath);

        var selectedPath = File.Exists(activePackPath) ? activePackPath : bundledPackPath;
        if (!File.Exists(selectedPath))
        {
            return RulePackReadResult.Failure(
                RulePackReadError.ValidationFailed,
                "规则不可用：未找到与 KeyRadar 一同交付的签名 .krpack，请重新下载完整 Release 或导入官方规则包。");
        }

        try
        {
            using var stream = new FileStream(
                selectedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.SequentialScan);
            var result = RulePackReader.Read(stream, publicKeyBytes);
            if (!result.IsSuccess)
            {
                return RulePackReadResult.Failure(
                    RulePackReadError.ValidationFailed,
                    "规则不可用：规则包损坏或签名校验失败，请重新下载或导入官方规则包。");
            }

            if (!string.Equals(result.Pack!.PackId, OfficialRulePack.PackId, StringComparison.Ordinal))
            {
                return RulePackReadResult.Failure(
                    RulePackReadError.ValidationFailed,
                    "规则不可用：规则包身份与 KeyRadar 官方规则不匹配。");
            }

            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RulePackReadResult.Failure(
                RulePackReadError.ValidationFailed,
                "规则不可用：规则包无法读取，请重新下载或导入官方规则包。");
        }
    }
}

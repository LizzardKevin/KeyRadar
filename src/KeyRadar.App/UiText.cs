using System.Globalization;

namespace KeyRadar;

internal static class UiText
{
    public static bool IsChinese
        => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    public static string Pick(string chinese, string english) => IsChinese ? chinese : english;

    public static string LocalizeExternal(string value)
    {
        if (IsChinese || string.IsNullOrWhiteSpace(value)) return value;

        if (value.StartsWith("ShareX 任务 ", StringComparison.Ordinal))
        {
            return "ShareX task " + value[10..];
        }

        return value switch
        {
            "规则不可用：未找到与 KeyRadar 一同交付的签名 .krpack，请重新下载完整 Release 或导入官方规则包。" =>
                "Rules unavailable: no signed .krpack delivered with KeyRadar was found. Download the complete Release again or import an official rule pack.",
            "规则不可用：规则包损坏或签名校验失败，请重新下载或导入官方规则包。" =>
                "Rules unavailable: the rule pack is damaged or its signature is invalid. Download or import the official rule pack again.",
            "规则不可用：规则包身份与 KeyRadar 官方规则不匹配。" =>
                "Rules unavailable: the rule pack identity does not match the official KeyRadar rules.",
            "规则不可用：规则包无法读取，请重新下载或导入官方规则包。" =>
                "Rules unavailable: the rule pack cannot be read. Download or import the official rule pack again.",
            "硬件软件正在运行且设备已连接；厂商 Profile 无安全公开读取接口" =>
                "Hardware software is running and the device is connected; the vendor profile has no safe public read interface",
            "ShareX HotkeysConfig.json · 白名单本机配置" =>
                "ShareX HotkeysConfig.json · whitelisted local configuration",
            "Greenshot greenshot.ini · 白名单本机配置" =>
                "Greenshot greenshot.ini · whitelisted local configuration",
            "ShareX 自定义任务" => "ShareX custom task",
            "区域截图" => "Capture region",
            "窗口截图" => "Capture window",
            "全屏截图" => "Capture fullscreen",
            "上次区域截图" => "Capture last region",
            "从剪贴板创建图像" => "Create image from clipboard",
            "活动窗口截图" => "Capture active window",
            "自定义窗口截图" => "Capture custom window",
            "当前显示器截图" => "Capture current monitor",
            "矩形区域截图" => "Capture rectangular region",
            "屏幕录制" => "Screen recording",
            "GIF 屏幕录制" => "GIF screen recording",
            "硬件映射" => "Hardware mapping",
            "功能未知" => "Function unknown",
            _ => value,
        };
    }
}

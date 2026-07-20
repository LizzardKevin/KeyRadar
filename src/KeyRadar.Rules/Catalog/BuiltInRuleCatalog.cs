using KeyRadar.Conflicts;
using KeyRadar.Shortcuts;

namespace KeyRadar.Rules;

public static class BuiltInRuleCatalog
{
    private static readonly IReadOnlyList<ApplicationRuleSet> Applications =
    [
        App(
            "wechat",
            "微信",
            ["WeChat.exe"],
            Rule("Alt+A", "截图", ShortcutScope.Global, OwnershipConfidence.Configuration),
            Rule("Ctrl+Alt+W", "打开微信", ShortcutScope.Global, OwnershipConfidence.Configuration)),
        App("wecom", "企业微信", ["WXWork.exe"]),
        App("qq", "QQ", ["QQ.exe"]),
        App("tencent-meeting", "腾讯会议", ["wemeetapp.exe"]),
        App("dingtalk", "钉钉", ["DingTalk.exe"]),
        App("feishu", "飞书", ["Feishu.exe"]),
        App("teams", "Microsoft Teams", ["ms-teams.exe", "Teams.exe"]),
        App("zoom", "Zoom", ["Zoom.exe"]),
        App("slack", "Slack", ["slack.exe"]),
        App("discord", "Discord", ["Discord.exe"]),
        App("telegram", "Telegram", ["Telegram.exe"]),
        App(
            "chrome",
            "Google Chrome",
            ["chrome.exe"],
            Rule("Ctrl+L", "定位地址栏"),
            Rule("Ctrl+Shift+T", "恢复关闭的标签页"),
            Rule("Ctrl+W", "关闭标签页"),
            Rule("F6", "切换焦点区域")),
        App("edge", "Microsoft Edge", ["msedge.exe"]),
        App("firefox", "Mozilla Firefox", ["firefox.exe"]),
        App("snipping-tool", "Windows 截图工具", ["SnippingTool.exe"]),
        App("snipaste", "Snipaste", ["Snipaste.exe"]),
        App("sharex", "ShareX", ["ShareX.exe"]),
        App("greenshot", "Greenshot", ["Greenshot.exe"]),
        App("lightshot", "Lightshot", ["Lightshot.exe"]),
        App("picpick", "PicPick", ["picpick.exe"]),
        App("obs", "OBS Studio", ["obs64.exe", "obs32.exe"]),
        App("bandicam", "Bandicam", ["bdcam.exe"]),
        App("screentogif", "ScreenToGif", ["ScreenToGif.exe"]),
        App("xbox-game-bar", "Xbox Game Bar", ["GameBar.exe"]),
        App("powertoys", "Microsoft PowerToys", ["PowerToys.exe"]),
        App("autohotkey", "AutoHotkey", ["AutoHotkey.exe", "AutoHotkey64.exe", "AutoHotkey32.exe"]),
        App("everything", "Everything", ["Everything.exe"]),
        App("listary", "Listary", ["Listary.exe"]),
        App("flow-launcher", "Flow Launcher", ["Flow.Launcher.exe"]),
        App("utools", "uTools", ["uTools.exe"]),
        App("quicker", "Quicker", ["Quicker.exe"]),
        App("ditto", "Ditto", ["Ditto.exe"]),
        App("sogou-ime", "搜狗输入法", ["SogouPY.exe", "SogouCloud.exe"]),
        App("nvidia-app", "NVIDIA App", ["NVIDIA App.exe"]),
        App("amd-adrenalin", "AMD Software: Adrenalin Edition", ["RadeonSoftware.exe"]),
        App("intel-graphics", "Intel Graphics Software", ["IntelGraphicsSoftware.exe"]),
        App("logi-options-plus", "Logi Options+", ["logioptionsplus_agent.exe"]),
        App("logitech-g-hub", "Logitech G HUB", ["lghub.exe"]),
        App("razer-synapse", "Razer Synapse", ["Razer Synapse 3.exe", "RazerAppEngine.exe"]),
        App("corsair-icue", "Corsair iCUE", ["iCUE.exe"]),
        App("vscode", "Visual Studio Code", ["Code.exe"]),
        App("visual-studio", "Visual Studio", ["devenv.exe"]),
        App("intellij-idea", "IntelliJ IDEA", ["idea64.exe", "idea.exe"]),
        App("windows-terminal", "Windows Terminal", ["WindowsTerminal.exe"]),
        App("notepad-plus-plus", "Notepad++", ["notepad++.exe"]),
        App("word", "Microsoft Word", ["WINWORD.EXE"]),
        App("excel", "Microsoft Excel", ["EXCEL.EXE"]),
        App("powerpoint", "Microsoft PowerPoint", ["POWERPNT.EXE"]),
        App("photoshop", "Adobe Photoshop", ["Photoshop.exe"]),
        App("potplayer", "PotPlayer", ["PotPlayerMini64.exe", "PotPlayerMini.exe"]),
    ];

    public static IReadOnlyList<ApplicationRuleSet> Load() => Applications;

    private static ApplicationRuleSet App(
        string id,
        string displayName,
        IReadOnlyList<string> executableNames,
        params ShortcutRule[] shortcuts) =>
        new(id, displayName, executableNames, shortcuts);

    private static ShortcutRule Rule(
        string gesture,
        string function,
        ShortcutScope scope = ShortcutScope.Application,
        OwnershipConfidence confidence = OwnershipConfidence.SystemKnown) =>
        new(ShortcutGesture.Parse(gesture), function, scope, confidence);
}

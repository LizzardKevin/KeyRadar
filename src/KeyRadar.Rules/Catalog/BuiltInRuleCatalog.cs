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
        App(
            "qq",
            "QQ",
            ["QQ.exe"],
            Rule("Ctrl+Alt+A", "截图（默认键，可在 QQ 中修改）", ShortcutScope.Global, OwnershipConfidence.Suspected),
            Rule("Ctrl+Alt+Z", "唤起 QQ（默认键，可在 QQ 中修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "tencent-meeting",
            "腾讯会议",
            ["wemeetapp.exe"],
            Rule("Alt+M", "静音或解除静音", confidence: OwnershipConfidence.Suspected),
            Rule("Alt+V", "开启或关闭视频", confidence: OwnershipConfidence.Suspected)),
        App(
            "dingtalk",
            "钉钉",
            ["DingTalk.exe"],
            Rule("Ctrl+Shift+A", "截图（默认键，可在钉钉中修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "feishu",
            "飞书",
            ["Feishu.exe"],
            Rule("Ctrl+Shift+A", "截图（默认键，可在飞书中修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "teams",
            "Microsoft Teams",
            ["ms-teams.exe", "Teams.exe"],
            Rule("Ctrl+Shift+M", "静音或解除静音", sources: ["https://support.microsoft.com/teams/meetings/mute-and-unmute-your-mic-in-microsoft-teams"]),
            Rule("Ctrl+G", "转到聊天或频道", sources: ["https://support.microsoft.com/office/use-commands-in-microsoft-teams-88f61508-284d-417f-a53d-9e082164050b"])),
        App(
            "zoom",
            "Zoom",
            ["Zoom.exe"],
            Rule("Alt+A", "静音或解除静音", sources: ["https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0067050"]),
            Rule("Alt+V", "开始或停止视频", sources: ["https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0067050"]),
            Rule("Alt+M", "为除主持人外的所有人静音（仅主持人）", sources: ["https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0067050"])),
        App(
            "slack",
            "Slack",
            ["slack.exe"],
            Rule("Ctrl+K", "打开会话", sources: ["https://slack.com/help/articles/115003340723-Navigate-Slack-with-your-keyboard"]),
            Rule("Ctrl+G", "开始搜索", sources: ["https://slack.com/help/articles/201374536-Slack-keyboard-shortcuts-and-commands"]),
            Rule("Ctrl+Shift+H", "开始、加入、离开或结束 Huddle", sources: ["https://slack.com/help/articles/201374536-Slack-keyboard-shortcuts-and-commands"])),
        App(
            "discord",
            "Discord",
            ["Discord.exe"],
            Rule("Ctrl+K", "打开快速切换器"),
            Rule("Ctrl+F", "搜索当前频道")),
        App(
            "telegram",
            "Telegram",
            ["Telegram.exe"],
            Rule("Ctrl+F", "搜索当前会话"),
            Rule("Ctrl+K", "快速选择会话", confidence: OwnershipConfidence.Suspected)),
        App(
            "chrome",
            "Google Chrome",
            ["chrome.exe"],
            Rule("Ctrl+L", "定位地址栏", sources: ["https://support.google.com/chrome/answer/157179"]),
            Rule("Ctrl+Shift+T", "恢复关闭的标签页", sources: ["https://support.google.com/chrome/answer/157179"]),
            Rule("Ctrl+W", "关闭标签页", sources: ["https://support.google.com/chrome/answer/157179"]),
            Rule("F6", "切换焦点区域", sources: ["https://support.google.com/chrome/answer/157179"])),
        App(
            "edge",
            "Microsoft Edge",
            ["msedge.exe"],
            Rule("Ctrl+L", "定位地址栏", sources: ["https://support.microsoft.com/edge/keyboard-shortcuts-in-microsoft-edge"]),
            Rule("Ctrl+Shift+T", "恢复关闭的标签页", sources: ["https://support.microsoft.com/edge/keyboard-shortcuts-in-microsoft-edge"]),
            Rule("Ctrl+W", "关闭当前标签页", sources: ["https://support.microsoft.com/edge/keyboard-shortcuts-in-microsoft-edge"]),
            Rule("F12", "打开开发人员工具", sources: ["https://support.microsoft.com/edge/keyboard-shortcuts-in-microsoft-edge"])),
        App(
            "firefox",
            "Mozilla Firefox",
            ["firefox.exe"],
            Rule("Ctrl+L", "定位地址栏", sources: ["https://support.mozilla.org/kb/keyboard-shortcuts-perform-firefox-tasks-quickly"]),
            Rule("Ctrl+Shift+T", "恢复关闭的标签页或窗口", sources: ["https://support.mozilla.org/kb/keyboard-shortcuts-perform-firefox-tasks-quickly"]),
            Rule("Ctrl+W", "关闭当前标签页", sources: ["https://support.mozilla.org/kb/keyboard-shortcuts-perform-firefox-tasks-quickly"])),
        App(
            "snipping-tool",
            "Windows 截图工具",
            ["SnippingTool.exe"],
            Rule("Ctrl+N", "新建截图"),
            Rule("Ctrl+S", "保存截图")),
        App(
            "snipaste",
            "Snipaste",
            ["Snipaste.exe"],
            Rule("F1", "截图（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected),
            Rule("F3", "贴图（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "sharex",
            "ShareX",
            ["ShareX.exe"],
            Rule("PrintScreen", "捕获整个屏幕（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected),
            Rule("Ctrl+PrintScreen", "捕获区域（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected),
            Rule("Alt+PrintScreen", "捕获活动窗口（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "greenshot",
            "Greenshot",
            ["Greenshot.exe"],
            Rule("PrintScreen", "捕获区域（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected),
            Rule("Alt+PrintScreen", "捕获活动窗口（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "lightshot",
            "Lightshot",
            ["Lightshot.exe"],
            Rule("PrintScreen", "开始截图（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "picpick",
            "PicPick",
            ["picpick.exe"],
            Rule("PrintScreen", "捕获整个屏幕（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App("obs", "OBS Studio", ["obs64.exe", "obs32.exe"]),
        App(
            "bandicam",
            "Bandicam",
            ["bdcam.exe"],
            Rule("F12", "开始或停止录制（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected),
            Rule("F11", "截图（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "screentogif",
            "ScreenToGif",
            ["ScreenToGif.exe"],
            Rule("F7", "录制或暂停"),
            Rule("F8", "停止录制")),
        App(
            "xbox-game-bar",
            "Xbox Game Bar",
            ["GameBar.exe"],
            Rule("Win+G", "打开 Xbox Game Bar", ShortcutScope.Global),
            Rule("Win+Alt+R", "开始或停止录制", ShortcutScope.Global)),
        App(
            "powertoys",
            "Microsoft PowerToys",
            ["PowerToys.exe"],
            Rule("Alt+Space", "打开 PowerToys Run（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected, ["https://learn.microsoft.com/windows/powertoys/run"]),
            Rule("Win+Shift+C", "打开颜色选取器（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected, ["https://learn.microsoft.com/windows/powertoys/color-picker"])),
        App("autohotkey", "AutoHotkey", ["AutoHotkey.exe", "AutoHotkey64.exe", "AutoHotkey32.exe"]),
        App(
            "everything",
            "Everything",
            ["Everything.exe"],
            Rule("Ctrl+F", "聚焦搜索框"),
            Rule("F5", "刷新索引结果")),
        App(
            "listary",
            "Listary",
            ["Listary.exe"],
            Rule("Win+S", "显示 Listary（默认键可能因版本而异）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "flow-launcher",
            "Flow Launcher",
            ["Flow.Launcher.exe"],
            Rule("Alt+Space", "打开 Flow Launcher（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "utools",
            "uTools",
            ["uTools.exe"],
            Rule("Alt+Space", "呼出 uTools（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "quicker",
            "Quicker",
            ["Quicker.exe"],
            Rule("Ctrl+Shift+Space", "呼出 Quicker 面板（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "ditto",
            "Ditto",
            ["Ditto.exe"],
            Rule("Ctrl+`", "打开 Ditto 剪贴板（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App("sogou-ime", "搜狗输入法", ["SogouPY.exe", "SogouCloud.exe"]),
        App(
            "nvidia-app",
            "NVIDIA App",
            ["NVIDIA App.exe"],
            Rule("Alt+Z", "打开 NVIDIA 覆盖层（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected),
            Rule("Alt+F1", "保存截图（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App(
            "amd-adrenalin",
            "AMD Software: Adrenalin Edition",
            ["RadeonSoftware.exe"],
            Rule("Alt+R", "打开 Radeon 覆盖层（默认键，可修改）", ShortcutScope.Global, OwnershipConfidence.Suspected)),
        App("intel-graphics", "Intel Graphics Software", ["IntelGraphicsSoftware.exe"]),
        App("logi-options-plus", "Logi Options+", ["logioptionsplus_agent.exe"]),
        App("logitech-g-hub", "Logitech G HUB", ["lghub.exe"]),
        App("razer-synapse", "Razer Synapse", ["Razer Synapse 3.exe", "RazerAppEngine.exe"]),
        App("corsair-icue", "Corsair iCUE", ["iCUE.exe"]),
        App(
            "vscode",
            "Visual Studio Code",
            ["Code.exe"],
            Rule("Ctrl+P", "快速打开文件", sources: ["https://code.visualstudio.com/docs/reference/default-keybindings"]),
            Rule("Ctrl+Shift+P", "显示命令面板", sources: ["https://code.visualstudio.com/docs/reference/default-keybindings"]),
            Rule("F1", "显示命令面板", sources: ["https://code.visualstudio.com/docs/reference/default-keybindings"]),
            Rule("F5", "开始调试", sources: ["https://code.visualstudio.com/docs/reference/default-keybindings"])),
        App(
            "visual-studio",
            "Visual Studio",
            ["devenv.exe"],
            Rule("Ctrl+Q", "搜索 Visual Studio"),
            Rule("F5", "启动调试"),
            Rule("Ctrl+Alt+L", "打开解决方案资源管理器")),
        App(
            "intellij-idea",
            "IntelliJ IDEA",
            ["idea64.exe", "idea.exe"],
            Rule("Ctrl+Shift+A", "查找操作"),
            Rule("Ctrl+N", "转到类"),
            Rule("Ctrl+Shift+N", "转到文件")),
        App(
            "windows-terminal",
            "Windows Terminal",
            ["WindowsTerminal.exe"],
            Rule("Ctrl+Shift+T", "新建标签页"),
            Rule("Ctrl+Shift+W", "关闭窗格或标签页"),
            Rule("Ctrl+,", "打开设置")),
        App(
            "notepad-plus-plus",
            "Notepad++",
            ["notepad++.exe"],
            Rule("Ctrl+F", "查找"),
            Rule("Ctrl+H", "替换"),
            Rule("Ctrl+G", "转到行")),
        App(
            "word",
            "Microsoft Word",
            ["WINWORD.EXE"],
            Rule("Ctrl+B", "加粗", sources: ["https://support.microsoft.com/office/use-the-keyboard-to-work-with-the-ribbon-954cd3f7-2f77-4983-978d-c09b20e31f0e"]),
            Rule("Ctrl+K", "插入超链接", sources: ["https://support.microsoft.com/office/use-the-keyboard-to-work-with-the-ribbon-954cd3f7-2f77-4983-978d-c09b20e31f0e"]),
            Rule("Ctrl+S", "保存文档", sources: ["https://support.microsoft.com/office/use-the-keyboard-to-work-with-the-ribbon-954cd3f7-2f77-4983-978d-c09b20e31f0e"])),
        App(
            "excel",
            "Microsoft Excel",
            ["EXCEL.EXE"],
            Rule("F2", "编辑活动单元格", sources: ["https://support.microsoft.com/office/keyboard-shortcuts-in-excel-1798d9d5-842a-42b8-9c99-9b7213f0040f"]),
            Rule("Ctrl+S", "保存工作簿", sources: ["https://support.microsoft.com/office/keyboard-shortcuts-in-excel-1798d9d5-842a-42b8-9c99-9b7213f0040f"]),
            Rule("Ctrl+B", "应用加粗格式", sources: ["https://support.microsoft.com/office/keyboard-shortcuts-in-excel-1798d9d5-842a-42b8-9c99-9b7213f0040f"])),
        App(
            "powerpoint",
            "Microsoft PowerPoint",
            ["POWERPNT.EXE"],
            Rule("F5", "从头开始幻灯片放映"),
            Rule("Shift+F5", "从当前幻灯片开始放映"),
            Rule("Ctrl+M", "新建幻灯片")),
        App(
            "photoshop",
            "Adobe Photoshop",
            ["Photoshop.exe"],
            Rule("Ctrl+T", "自由变换"),
            Rule("Ctrl+J", "复制图层"),
            Rule("B", "画笔工具")),
        App(
            "potplayer",
            "PotPlayer",
            ["PotPlayerMini64.exe", "PotPlayerMini.exe"],
            Rule("Space", "播放或暂停"),
            Rule("Enter", "切换全屏"),
            Rule("Ctrl+O", "打开文件")),
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
        OwnershipConfidence confidence = OwnershipConfidence.SystemKnown,
        IReadOnlyList<string>? sources = null) =>
        new(ShortcutGesture.Parse(gesture), function, scope, confidence)
        {
            Sources = sources ?? [],
        };
}

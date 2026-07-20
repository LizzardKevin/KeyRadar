# 白名单本机配置读取器

KeyRadar 只为当前正在运行、已通过规则变体匹配的应用调用对应读取器。读取器不得
枚举应用白名单目录之外的文件，不得返回完整路径、账号、窗口标题、宏文本或普通
输入内容；格式变化、文件过大、权限不足或解析失败时返回空证据并保持未知。

## ShareX

- 白名单文件：当前用户 Documents 下的 `ShareX/HotkeysConfig.json`。
- 大小上限：8 MiB；条目上限：512。
- 读取字段：`Hotkeys[].HotkeyInfo.Hotkey`、`HotkeyInfo.Win`、
  `TaskSettings.Job`。
- 忽略字段：其他所有字段，包括任务路径、上传目标与自定义参数。
- 官方结构证据：
  [SettingManager.cs](https://github.com/ShareX/ShareX/blob/master/ShareX/SettingManager.cs)、
  [HotkeySettings.cs](https://github.com/ShareX/ShareX/blob/master/ShareX/HotkeySettings.cs)、
  [HotkeyInfo.cs](https://github.com/ShareX/ShareX/blob/master/ShareX.HelpersLib/Input/HotkeyInfo.cs)。

ShareX 的自定义 Personal Folder 可能位于任意用户目录。KeyRadar v1 不读取该任意
路径；后续只有在用户明确授权某个位置后才能增加读取，不能通过全盘搜索定位。

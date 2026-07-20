# KeyRadar · 热键雷达

> 先看当前可操作、可归属的热键；需要排查时再查看完整诊断证据。

KeyRadar 是 Windows 热键占用与归属诊断工具。它在当前桌面会话中结合运行应用、已声明的规则、本机允许读取的配置与安全的 `RegisterHotKey` 探测，说明一条热键为何出现；证据不足时会保留为未知，而不会猜测注册进程。

## 当前状态

项目尚未创建 Release，也没有可下载的正式安装包。当前版本目标是首次公开 `v1.0.0`；在此之前请从源码构建，不要把版本号或文档中的未来发布资产理解为已发布。

## 正常界面与诊断

正常界面是“当前可操作/可归属热键清单”，不是原始探测日志：

- 每个规范化热键只显示一行，并按证据归入前台应用、当前硬件 Profile、后台应用、Windows 系统或未知占用等分组；同一热键的所有证据仍会聚合保留。
- 已在后台运行的应用只贡献全局或后台范围的热键；应用内热键只有应用处于前台时才纳入当前清单。Windows 系统热键按系统范围单独纳入。
- 仅 `RegisterHotKey` 探测到 `AvailableAtScanTime`、`SystemReserved` 或 `ProbeError` 的项目不显示为普通行。
- 只有 `RegisterHotKey` 占用证据的裸 `F1`–`F24`、浏览器、媒体、音量、启动和导航键不显示为普通行；`PrintScreen` 与带修饰键的组合仍可保留。这样可避免把物理按键或系统行为误呈为可归属的桌面热键。
- 完整探测集（包括上述隐藏项目）仍通过“导出诊断”以经过清洗的 `probeTelemetry` 记录保留，便于排查。

`RegisterHotKey` 探测只说明扫描瞬间能否注册标准全局组合；它不会触发、模拟、拦截或吞掉用户输入，也不能安全返回注册进程。

## 已实现边界

- 规则数量随 `rules/*.json` 的当前正式规则源动态变化，不写死应用数量。
- 可导入经验证与脱敏的声明式硬件 Profile；私有或加密的厂商数据库不猜测解析，导入项标记为“用户声明 · 未实时验证”。
- 深度确认会立即复核单条热键、当前运行应用、允许读取的本机配置和可用的原生 x64/x86 辅助探测；它不模拟按键。管理员/UAC 不能提供对其他进程热键归属的深度读取。
- 扫描可取消；取消或未完成扫描不会发布部分结果。诊断导出读取最近一次完整扫描的单一快照。

## 从源码构建

安装 .NET 10 SDK 与带 Windows C++ 工具链的 Visual Studio Build Tools，然后运行：

```powershell
dotnet restore KeyRadar.sln --locked-mode
dotnet build KeyRadar.sln -c Release --no-restore
dotnet test KeyRadar.sln -c Release --no-build
.\eng\Build-Native.ps1 -Configuration Release
```

首次 `v1.0.0` Release 未来会另行发布；届时才会提供已签名的应用与正式规则包。当前不要执行或声称已完成发布。

## 文档

- [实际占用优先扫描规格](docs/superpowers/specs/2026-07-20-keyradar-windows-occupancy-first-scan.md)
- [隐私设计](docs/PRIVACY.md)
- [规则包说明](docs/RULE_PACKS.md)
- [硬件 Profile 导入](docs/HARDWARE_PROFILES.md)
- [发布测试](docs/RELEASE_TESTING.md)
- [贡献指南](CONTRIBUTING.md)
- [安全策略](SECURITY.md)

## 许可证

[MIT](LICENSE)

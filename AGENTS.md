# KeyRadar

目的：KeyRadar 是 Windows 热键占用与归属诊断工具；先展示当前可操作或可归属的热键，再保留完整探测证据供诊断。

## 运行与验证

```powershell
dotnet restore KeyRadar.sln --locked-mode
dotnet build KeyRadar.sln -c Release --no-restore
dotnet test KeyRadar.sln -c Release --no-build
.\eng\Build-Native.ps1 -Configuration Release
```

## 技术栈

- .NET 10、C#、WinUI 3；Windows 10/11 x64 桌面应用。
- 原生 x64/x86 辅助组件只用于受控的热键复核。

## 目录与约定

- `src/`：应用、核心模型、Windows 集成、规则与更新组件；`tests/`：对应测试。
- `rules/*.json`：唯一正式规则源；不得新增编译期规则回退。
- `schemas/`：规则 Schema；`docs/`：面向用户与维护者的真实说明；`eng/`：本地构建和发布脚本。
- 面向用户的中文统一使用“热键”；API、类型和文件名保持既有技术命名。
- 证据不足时保留“未知”，不得猜测注册进程或把静态规则说成实时读取。

## 当前状态与下一步

- 尚未创建任何 Release；当前版本目标是首次公开 `v1.0.0`，仅可从源码构建。
- 普通清单是经过筛选的当前热键视图；完整 `RegisterHotKey` 探测结果仅供诊断导出。
- 厂商硬件 Profile 目前不能实时安全读取，只支持经脱敏的用户声明导入；管理员/UAC 不提供跨权限进程归属读取。
- 下一步是在不创建 Release 的前提下完成实现接线与 Windows 验证，再决定首个 `v1.0.0` 发布。

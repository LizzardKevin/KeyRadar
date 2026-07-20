# KeyRadar v1.0.0 Schema v2 与三栏界面实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目标：** 在不发布 Release、不启用发布 Actions 的前提下，把 KeyRadar v1.0.0 本地版本升级为 Schema v2、可解释应用变体匹配、分层规则包、用户规则、候选提交、三栏界面、中英文和三种主题，并生成可供用户本机验收的 EXE 与初始规则包。

**架构：** Core 统一使用 `Hotkey*` 领域模型；Rules 负责 Schema v2、版本范围、变体评分、规则来源合并和安全规则包；Windows 提供不持久化路径的进程身份；App 通过共享会话服务驱动雷达总览、热键总览和设置。用户本地规则与官方规则共用声明式解析器，但通过来源、签名状态和优先级明确区分。

**技术栈：** C#、.NET 10 LTS、WinUI 3、Windows App SDK 2.2、System.Text.Json、NSec Ed25519、xUnit、PowerShell、本地 x86/x64 原生组件。

---

## 文件结构与职责

### Core

- `src/KeyRadar.Core/Hotkeys/HotkeyGesture.cs`：解析和标准化组合。
- `src/KeyRadar.Core/Hotkeys/HotkeyModifiers.cs`：修饰键位标志。
- `src/KeyRadar.Core/Hotkeys/HotkeyBinding.cs`：应用、功能、作用范围、可信度和证据。
- `src/KeyRadar.Core/Hotkeys/HotkeyConflictClassifier.cs`：确定冲突、可能拦截和上下文重复。
- `src/KeyRadar.Core/Hotkeys/HotkeyScope.cs`：Windows、全局、后台和前台作用范围。

### Rules

- `src/KeyRadar.Rules/Models/LocalizedText.cs`：多语言文字与回退。
- `src/KeyRadar.Rules/Models/ApplicationVariantRule.cs`：一个应用变体的完整规则。
- `src/KeyRadar.Rules/Models/ApplicationMatchRule.cs`：EXE、发布者、版本范围、PFN 和发行渠道。
- `src/KeyRadar.Rules/Models/HotkeyRule.cs`：声明式热键条目。
- `src/KeyRadar.Rules/Matching/VersionRange.cs`：受限数值版本区间。
- `src/KeyRadar.Rules/Matching/ApplicationVariantMatcher.cs`：候选排除、证据评分和歧义结果。
- `src/KeyRadar.Rules/Catalogs/LayeredRuleCatalog.cs`：本地、最新官方和随附规则分层。
- `src/KeyRadar.Rules/Catalogs/RuleOverlapResolver.cs`：官方覆盖提示和非破坏性选择。
- `src/KeyRadar.Rules/Packs/RulePackReader.cs`：签名与未签名包共享的安全解包和 v2 解析。
- `src/KeyRadar.Rules/Packs/LocalRulePackStore.cs`：原子保存、导入、导出和恢复。
- `src/KeyRadar.Rules/Candidates/CandidateRuleBuilder.cs`：候选 JSON、隐私白名单和 GitHub URL。

### Windows

- `src/KeyRadar.Windows/Applications/ProcessDescriptor.cs`：扩充签名发布者、公司名、PFN 和发行标签。
- `src/KeyRadar.Windows/Applications/ProcessMetadataReader.cs`：读取上述证据，不返回持久化路径。
- `src/KeyRadar.Windows/Input/HotkeyRecordingSession.cs`：一次性、非拦截录入状态。

### App

- `src/KeyRadar.App/Services/RadarSession.cs`：扫描、匹配、合并、筛选和统计的共享状态。
- `src/KeyRadar.App/Services/LocalizationService.cs`：`zh-CN` / `en-US` 切换与资源读取。
- `src/KeyRadar.App/Services/ThemeService.cs`：System / Light / Dark 持久化。
- `src/KeyRadar.App/Pages/RadarOverviewPage.xaml(.cs)`：Hero 与四类统计、嵌入式热键总览。
- `src/KeyRadar.App/Pages/HotkeyOverviewPage.xaml(.cs)`：多维筛选、搜索和应用 Tag。
- `src/KeyRadar.App/Pages/SettingsPage.xaml(.cs)`：更新、规则库、我的规则、候选提交和诊断。
- `src/KeyRadar.App/Presentation/ApplicationGroupViewModel.cs`：应用层跳转和折叠状态。
- `src/KeyRadar.App/Presentation/HotkeyRowViewModel.cs`：用户可见热键行。
- `src/KeyRadar.App/Strings/zh-CN/Resources.resw` 与 `en-US`：界面资源。

### 规则、工具和文档

- `schemas/keyradar-rule-v2.schema.json`：唯一受支持 Schema。
- `rules/*.json`：每个应用变体一个 JSON，数量动态。
- `eng/KeyRadar.ReleaseTool/Program.cs`：按实际文件构建 `.krpack`，不验证固定数量。
- `.github/ISSUE_TEMPLATE/candidate-rule.yml`：候选规则 Issue Form。
- `eng/Validate-Rules.ps1`：本地与未来 CI 共用校验入口。
- `README.md`、`docs/RULE_PACKS.md`、`docs/PRIVACY.md`：中文优先并统一热键术语。

## Task 1：准备隔离执行环境与基线

**文件：**
- 修改：`.gitignore`
- 验证：`KeyRadar.sln`

- [ ] **Step 1：验证当前分支和工作树**

运行：

```powershell
git branch --show-current
git status --short
git worktree list
```

预期：当前分支为 `codex/keyradar-v1`，没有未提交用户文件；当前目录已经是
该功能分支的独立工作位置或通过 worktree 技能确认可安全继续。

- [ ] **Step 2：安装项目本地 .NET 10.0.302 SDK（仅当本机不存在）**

使用官方安装脚本安装到未跟踪目录 `.tools/dotnet`，不得修改 `global.json`：

```powershell
& .tools/dotnet/dotnet.exe --version
```

预期：输出 `10.0.302`。

- [ ] **Step 3：忽略本地工具目录**

在 `.gitignore` 增加：

```gitignore
.tools/
```

- [ ] **Step 4：运行现有基线测试**

运行：

```powershell
& .tools/dotnet/dotnet.exe test KeyRadar.sln -c Debug --nologo -v:minimal
```

预期：现有测试全部通过；若失败，先记录既有失败并停止实现。

- [ ] **Step 5：提交环境约束**

```powershell
git add .gitignore
git commit -m "chore: ignore repository local toolchain"
```

## Task 2：统一 Hotkey 领域术语

**文件：**
- 移动：`src/KeyRadar.Core/Shortcuts/*` → `src/KeyRadar.Core/Hotkeys/*`
- 移动：`src/KeyRadar.Core/Conflicts/ShortcutBinding.cs` → `src/KeyRadar.Core/Hotkeys/HotkeyBinding.cs`
- 移动：`src/KeyRadar.Core/Conflicts/ShortcutConflictClassifier.cs` → `src/KeyRadar.Core/Hotkeys/HotkeyConflictClassifier.cs`
- 移动：`src/KeyRadar.Core/Conflicts/ShortcutScope.cs` → `src/KeyRadar.Core/Hotkeys/HotkeyScope.cs`
- 修改：引用上述类型的 `src/**/*.cs`
- 修改：引用上述类型的 `tests/**/*.cs`
- 测试：`tests/KeyRadar.Core.Tests/Hotkeys/HotkeyGestureTests.cs`
- 测试：`tests/KeyRadar.Core.Tests/Hotkeys/HotkeyConflictClassifierTests.cs`

- [ ] **Step 1：先将测试改为新类型并验证失败**

测试使用：

```csharp
Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+A", out var gesture));
Assert.Equal("Ctrl+Alt+A", gesture.ToString());
```

运行：

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Core.Tests/KeyRadar.Core.Tests.csproj --nologo
```

预期：因 `KeyRadar.Hotkeys` 和 `Hotkey*` 类型尚不存在而编译失败。

- [ ] **Step 2：移动并重命名领域类型**

统一命名空间：

```csharp
namespace KeyRadar.Hotkeys;
```

公开类型统一为：

```csharp
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key);
public sealed record HotkeyBinding(string ApplicationId, HotkeyGesture Gesture, HotkeyScope Scope);
public enum HotkeyScope { Foreground, Background, Global, WindowsSystem }
```

- [ ] **Step 3：迁移全部引用并删除旧类型**

运行：

```powershell
rg -n -i "shortcut|快捷键" src tests
```

预期：没有产品类型、变量、测试名或界面文案残留旧术语。

- [ ] **Step 4：运行 Core、Windows 和 Rules 测试**

```powershell
& .tools/dotnet/dotnet.exe test KeyRadar.sln -c Debug --nologo -v:minimal
```

预期：全部通过。

- [ ] **Step 5：提交术语迁移**

```powershell
git add src tests
git commit -m "refactor: unify hotkey domain terminology"
```

## Task 3：实现 Schema v2 领域模型与严格解析

**文件：**
- 创建：`src/KeyRadar.Rules/Models/LocalizedText.cs`
- 创建：`src/KeyRadar.Rules/Models/ApplicationMatchRule.cs`
- 创建：`src/KeyRadar.Rules/Models/ApplicationVariantRule.cs`
- 创建：`src/KeyRadar.Rules/Models/HotkeyRule.cs`
- 创建：`src/KeyRadar.Rules/Matching/VersionRange.cs`
- 修改：`src/KeyRadar.Rules/Packs/RulePackReader.cs`
- 创建：`schemas/keyradar-rule-v2.schema.json`
- 删除：`schemas/keyradar-rule-v1.schema.json`
- 测试：`tests/KeyRadar.Rules.Tests/Models/LocalizedTextTests.cs`
- 测试：`tests/KeyRadar.Rules.Tests/Matching/VersionRangeTests.cs`
- 修改：`tests/KeyRadar.Rules.Tests/Packs/RulePackReaderTests.cs`

- [ ] **Step 1：编写多语言和版本范围失败测试**

```csharp
var text = new LocalizedText(new Dictionary<string, string> { ["zh-CN"] = "截图" });
Assert.Equal("截图", text.Resolve("en-US"));
Assert.True(VersionRange.TryParse(">=4.0 <5.0", out var range));
Assert.True(range.Contains(new Version(4, 1)));
Assert.False(range.Contains(new Version(5, 0)));
Assert.False(VersionRange.TryParse("System.IO.File.Delete('*')", out _));
```

- [ ] **Step 2：验证测试失败**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

预期：新类型不存在而编译失败。

- [ ] **Step 3：实现 v2 模型**

核心签名：

```csharp
public sealed record ApplicationVariantRule(
    string ApplicationId,
    string VariantId,
    LocalizedText DisplayName,
    ApplicationMatchRule Match,
    IReadOnlyList<HotkeyRule> Hotkeys,
    RuleProvenance Provenance);

public sealed record ApplicationMatchRule(
    IReadOnlyList<string> Executables,
    IReadOnlyList<string> Publishers,
    VersionRange? VersionRange,
    IReadOnlyList<string> PackageFamilyNames,
    string? Distribution);
```

- [ ] **Step 4：把 RulePackReader 改为只接受 Schema v2**

解析字段必须为 `schemaVersion`、`applicationId`、`variantId`、`displayName`、
`match` 和 `hotkeys`。文件身份为 `applicationId-variantId`，允许同一
`applicationId` 包含多个不同 `variantId`，拒绝重复二元身份。

- [ ] **Step 5：添加 JSON Schema 并验证测试通过**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

预期：新增测试和现有规则包安全测试全部通过。

- [ ] **Step 6：提交 Schema v2 底座**

```powershell
git add src/KeyRadar.Rules schemas tests/KeyRadar.Rules.Tests
git commit -m "feat: add declarative rule schema v2"
```

## Task 4：扩充进程身份并实现可解释变体匹配

**文件：**
- 修改：`src/KeyRadar.Windows/Applications/ProcessDescriptor.cs`
- 修改：`src/KeyRadar.Windows/Applications/ProcessMetadataReader.cs`
- 创建：`src/KeyRadar.Windows/Applications/ProcessIdentity.cs`
- 创建：`src/KeyRadar.Rules/Matching/VariantMatchEvidence.cs`
- 创建：`src/KeyRadar.Rules/Matching/ApplicationVariantMatchResult.cs`
- 创建：`src/KeyRadar.Rules/Matching/ApplicationVariantMatcher.cs`
- 删除：`src/KeyRadar.Rules/Matching/ApplicationRuleMatcher.cs`
- 测试：`tests/KeyRadar.Rules.Tests/Matching/ApplicationVariantMatcherTests.cs`
- 修改：`tests/KeyRadar.Windows.Tests/Applications/RunningApplicationScannerTests.cs`

- [ ] **Step 1：编写精确、弱证据、排除和歧义测试**

```csharp
var result = matcher.Match(identity, variants);
Assert.Equal(VariantMatchKind.Exact, result.Kind);
Assert.Equal("cn-desktop", result.Selected!.VariantId);
Assert.Contains(result.Evidence, item => item.Signal == "publisher" && item.IsMatch);
```

另写测试保证只命中 EXE 时返回 `Suspected`，两个同分候选返回 `Ambiguous`，
明确版本冲突的候选被排除。

- [ ] **Step 2：验证测试失败**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

- [ ] **Step 3：扩充进程身份**

```csharp
public sealed record ProcessDescriptor(
    int Id,
    string Name,
    string ExecutableName,
    string? Version,
    string? Publisher,
    string? CompanyName,
    string? PackageFamilyName,
    string? Distribution,
    ProcessArchitecture Architecture,
    ProcessPrivilegeLevel PrivilegeLevel);
```

`Publisher` 代表签名发布者，`CompanyName` 仅为弱证据。返回对象不得包含
完整路径。

- [ ] **Step 4：实现分阶段匹配和证据评分**

匹配器先排除已知冲突条件，再按 PFN、签名发布者、版本范围、发行标签和
EXE 顺序累积证据；不得使用系统界面语言。结果携带每条命中、缺失和冲突
证据，不只返回一个布尔值。

- [ ] **Step 5：运行 Rules 与 Windows 测试**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Windows.Tests/KeyRadar.Windows.Tests.csproj --nologo
```

- [ ] **Step 6：提交变体匹配**

```powershell
git add src/KeyRadar.Windows src/KeyRadar.Rules tests
git commit -m "feat: match application variants with explainable evidence"
```

## Task 5：实现分层规则目录与损坏回退

**文件：**
- 创建：`src/KeyRadar.Rules/Catalogs/RuleSourceKind.cs`
- 创建：`src/KeyRadar.Rules/Catalogs/LayeredRuleCatalog.cs`
- 创建：`src/KeyRadar.Rules/Catalogs/RuleOverlapResolver.cs`
- 修改：`src/KeyRadar.Rules/Packs/OfficialRulePackLoader.cs`
- 修改：`src/KeyRadar.Rules/Packs/RulePackReadResult.cs`
- 修改：`src/KeyRadar.App/RuntimeRuleCatalog.cs`
- 测试：`tests/KeyRadar.Rules.Tests/Catalogs/LayeredRuleCatalogTests.cs`
- 修改：`tests/KeyRadar.Rules.Tests/Packs/OfficialRulePackLoaderTests.cs`

- [ ] **Step 1：编写来源优先级和回退失败测试**

```csharp
Assert.Equal(RuleSourceKind.Local, merged.Selected.Provenance.Source);
Assert.Contains(merged.Shadowed, item => item.Provenance.Source == RuleSourceKind.OfficialActive);
Assert.True(load.UsedBundledFallback);
Assert.True(load.RequiresProminentWarning);
```

- [ ] **Step 2：验证测试失败**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

- [ ] **Step 3：实现分层合并**

按 `Local > OfficialActive > Bundled` 选择重叠条目，保留所有被覆盖证据。
应用重叠通过应用身份、标准化组合和兼容作用范围判断；精确变体只增加证据，
不能阻止本地临时变体与官方变体相互识别。

- [ ] **Step 4：实现无效 active 包回退**

`OfficialRulePackLoader` 先验证 active；失败后验证 bundled。bundled 成功时
返回可用目录，同时设置持久警告和下载入口状态；两者都失败时返回规则不可用。

- [ ] **Step 5：运行规则测试**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

- [ ] **Step 6：提交分层规则目录**

```powershell
git add src/KeyRadar.Rules src/KeyRadar.App/RuntimeRuleCatalog.cs tests/KeyRadar.Rules.Tests
git commit -m "feat: layer local active and bundled rule catalogs"
```

## Task 6：迁移现有规则并移除固定数量

**文件：**
- 修改：`rules/*.json`
- 修改：`eng/KeyRadar.ReleaseTool/Program.cs`
- 修改：`eng/Build-Release.ps1`
- 修改：`tests/KeyRadar.Rules.Tests/Catalog/OfficialRuleSourceTests.cs`
- 创建：`eng/Validate-Rules.ps1`

- [ ] **Step 1：先修改测试，禁止固定规则数量**

测试必须断言：

```csharp
Assert.NotEmpty(ruleFiles);
Assert.Equal(ruleFiles.Length, identities.Distinct(StringComparer.OrdinalIgnoreCase).Count());
Assert.All(documents, document => Assert.Equal(2, document.SchemaVersion));
```

测试中不得出现50或51的目录数量断言。

- [ ] **Step 2：验证测试在 v1 规则上失败**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

- [ ] **Step 3：逐个迁移规则文件**

每个文件使用：

```json
{
  "schemaVersion": 2,
  "applicationId": "chrome",
  "variantId": "stable",
  "displayName": { "zh-CN": "Chrome", "en-US": "Chrome" },
  "match": {
    "executables": ["chrome.exe"],
    "publishers": ["Google LLC"],
    "versionRange": null,
    "packageFamilyNames": [],
    "distribution": "official"
  },
  "hotkeys": []
}
```

实际热键、功能和来源从现有内容迁移，不编造版本范围或渠道证据。只有已获得
可靠区分证据时才拆分额外变体。

- [ ] **Step 4：移除 ReleaseTool 的固定数量判断**

构建器只要求规则文件非空、身份唯一、全部通过 `RulePackReader`，并验证生成
包中的变体数量等于输入文件数量，不与常量比较。

- [ ] **Step 5：实现本地规则校验入口**

`eng/Validate-Rules.ps1` 调用 ReleaseTool 的验证命令，验证 Schema、身份、
版本范围、来源、隐私字段和重复热键；不签名、不联网、不发布。

- [ ] **Step 6：运行全部规则校验**

```powershell
& .\eng\Validate-Rules.ps1
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

- [ ] **Step 7：提交规则迁移**

```powershell
git add rules schemas eng tests/KeyRadar.Rules.Tests
git commit -m "feat: migrate official rules to dynamic schema v2 catalog"
```

## Task 7：实现本地规则存储、导入导出与重叠选择

**文件：**
- 创建：`src/KeyRadar.Rules/Packs/LocalRulePackStore.cs`
- 创建：`src/KeyRadar.Rules/Packs/LocalRulePackPreview.cs`
- 创建：`src/KeyRadar.Rules/Catalogs/RuleOverlapDecision.cs`
- 测试：`tests/KeyRadar.Rules.Tests/Packs/LocalRulePackStoreTests.cs`
- 测试：`tests/KeyRadar.Rules.Tests/Catalogs/RuleOverlapResolverTests.cs`

- [ ] **Step 1：编写原子保存、安全导入和非破坏覆盖测试**

```csharp
Assert.True(store.Save(localPack).IsSuccess);
Assert.Equal("local.krpack", Path.GetFileName(store.ActivePath));
Assert.Contains(preview.Entries, entry => entry.ApplicationId == "wechat");
Assert.True(decision.DisabledLocalEntries.Count == 1);
Assert.True(File.Exists(store.ActivePath));
```

- [ ] **Step 2：验证测试失败**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

- [ ] **Step 3：实现未签名本地包**

本地包只允许 `manifest.json` 和 `rules/*.json`，与官方包共用安全限制和 v2
解析器；清单明确 `source: "local"`，不伪造 Ed25519 签名。保存使用临时文件
加原子替换，并保留一次可恢复副本。

- [ ] **Step 4：实现导入预览和重叠选择**

预览返回应用、变体、热键数量、匹配字段、签名状态和官方重叠。选择官方
只写入本地禁用清单，不删除规则；保留我的规则则维持本地优先级。

- [ ] **Step 5：运行规则测试并提交**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
git add src/KeyRadar.Rules tests/KeyRadar.Rules.Tests
git commit -m "feat: manage unsigned local rule packs safely"
```

## Task 8：实现一次性热键录入和候选提交

**文件：**
- 创建：`src/KeyRadar.Windows/Input/HotkeyRecordingSession.cs`
- 创建：`src/KeyRadar.Windows/Input/HotkeyRecordingResult.cs`
- 创建：`src/KeyRadar.Rules/Candidates/CandidateRuleBuilder.cs`
- 创建：`src/KeyRadar.Rules/Candidates/CandidateIssueUrlBuilder.cs`
- 创建：`.github/ISSUE_TEMPLATE/candidate-rule.yml`
- 测试：`tests/KeyRadar.Windows.Tests/Input/HotkeyRecordingSessionTests.cs`
- 测试：`tests/KeyRadar.Rules.Tests/Candidates/CandidateRuleBuilderTests.cs`

- [ ] **Step 1：编写普通文字丢弃、单次完成和隐私拒绝测试**

```csharp
Assert.False(session.ObserveKey(Key.A).Completed);
Assert.True(session.ObserveCombination(HotkeyModifiers.Alt, Key.A).Completed);
Assert.False(builder.TryBuild(rule with { Notes = @"C:\Users\alice\secret" }, out _));
Assert.Contains("submittedLocale", candidate.Json);
```

- [ ] **Step 2：验证测试失败**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Windows.Tests/KeyRadar.Windows.Tests.csproj --nologo
& .tools/dotnet/dotnet.exe test tests/KeyRadar.Rules.Tests/KeyRadar.Rules.Tests.csproj --nologo
```

- [ ] **Step 3：实现非拦截一次性录入状态机**

仅接受修饰键组合和允许的独立功能键；成功、取消或30秒超时后清空状态并
注销观察回调。不得把单个普通字符写入任何集合、日志或诊断。

- [ ] **Step 4：实现候选 JSON 和 GitHub Issue URL**

候选只允许应用、版本、渠道、EXE、发布者、热键、功能、范围、设置变化、
冲突现象、来源和 `submittedLocale`。URL 使用 Issue Form 字段 ID 编码，
超长内容在本地预览中明确阻止打开，避免 GitHub 414。

- [ ] **Step 5：创建结构化 Issue Form**

表单字段 ID 固定为：`application`、`version`、`distribution`、`executable`、
`publisher`、`gesture`、`function`、`scope`、`settings_changed`、`conflict`、
`sources`、`candidate_json` 和 `privacy_confirmation`。

- [ ] **Step 6：运行测试并提交**

```powershell
& .tools/dotnet/dotnet.exe test KeyRadar.sln -c Debug --nologo -v:minimal
git add src tests .github/ISSUE_TEMPLATE/candidate-rule.yml
git commit -m "feat: record and submit privacy-safe candidate hotkeys"
```

## Task 9：建立共享雷达会话、统计、搜索和筛选

**文件：**
- 创建：`src/KeyRadar.App/Services/RadarSession.cs`
- 创建：`src/KeyRadar.App/Services/RadarMetrics.cs`
- 创建：`src/KeyRadar.App/Services/HotkeyFilter.cs`
- 修改：`src/KeyRadar.App/RuntimeRuleCatalog.cs`
- 修改：`src/KeyRadar.App/Presentation/ApplicationGroupViewModel.cs`
- 移动：`src/KeyRadar.App/Presentation/ShortcutRowViewModel.cs` → `HotkeyRowViewModel.cs`
- 创建：`tests/KeyRadar.App.Tests/KeyRadar.App.Tests.csproj`
- 创建：`tests/KeyRadar.App.Tests/Services/RadarSessionTests.cs`

- [ ] **Step 1：把纯状态逻辑写成失败测试**

```csharp
Assert.Equal(1, metrics.ConflictCount);
Assert.Equal(3, metrics.AvailableCount);
Assert.Contains(filtered, row => row.Function.Contains("截图"));
Assert.Contains(filtered, row => row.ApplicationName.Contains("WeChat"));
Assert.Contains(filtered, row => row.Gesture == "Alt+A");
```

- [ ] **Step 2：验证测试失败**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.App.Tests/KeyRadar.App.Tests.csproj --nologo
```

- [ ] **Step 3：实现共享会话**

`RadarSession` 只依赖抽象扫描器、分层目录、变体匹配器和可用性探测器，
公开不可变快照和变更事件。所有页面使用同一份扫描结果，避免重复扫描。

- [ ] **Step 4：实现筛选和应用分组**

筛选支持作用范围、应用、按键类别、可信度、冲突状态和统一搜索。应用组
只包含一个跳转命令；有冲突时默认展开。

- [ ] **Step 5：加入解决方案并运行测试**

```powershell
& .tools/dotnet/dotnet.exe sln KeyRadar.sln add tests/KeyRadar.App.Tests/KeyRadar.App.Tests.csproj
& .tools/dotnet/dotnet.exe test KeyRadar.sln -c Debug --nologo -v:minimal
```

- [ ] **Step 6：提交共享会话**

```powershell
git add KeyRadar.sln src/KeyRadar.App tests/KeyRadar.App.Tests
git commit -m "feat: add shared radar session and hotkey filtering"
```

## Task 10：实现中英文资源和三种主题

**文件：**
- 创建：`src/KeyRadar.App/Services/LocalizationService.cs`
- 创建：`src/KeyRadar.App/Services/ThemeService.cs`
- 创建：`src/KeyRadar.App/Strings/zh-CN/Resources.resw`
- 创建：`src/KeyRadar.App/Strings/en-US/Resources.resw`
- 修改：`src/KeyRadar.App/App.xaml`
- 修改：`src/KeyRadar.App/App.xaml.cs`
- 测试：`tests/KeyRadar.App.Tests/Services/LocalizationServiceTests.cs`
- 测试：`tests/KeyRadar.App.Tests/Services/ThemeServiceTests.cs`

- [ ] **Step 1：编写语言回退和主题持久化失败测试**

```csharp
Assert.Equal("截图", localization.Resolve(ruleText, "en-US"));
theme.Set(AppTheme.Dark);
Assert.Equal(AppTheme.Dark, theme.Get());
```

- [ ] **Step 2：实现资源键和服务**

所有用户可见静态文字进入 `.resw`。规则文字由 `LocalizedText.Resolve` 处理。
主题值只保存 `System`、`Light` 或 `Dark`，应用到根元素 `RequestedTheme`。

- [ ] **Step 3：运行 App 测试并提交**

```powershell
& .tools/dotnet/dotnet.exe test tests/KeyRadar.App.Tests/KeyRadar.App.Tests.csproj --nologo
git add src/KeyRadar.App tests/KeyRadar.App.Tests
git commit -m "feat: add Chinese English and system-aware themes"
```

## Task 11：重构三栏 WinUI 界面

**文件：**
- 修改：`src/KeyRadar.App/MainPage.xaml`
- 修改：`src/KeyRadar.App/MainPage.xaml.cs`
- 创建：`src/KeyRadar.App/Pages/RadarOverviewPage.xaml`
- 创建：`src/KeyRadar.App/Pages/RadarOverviewPage.xaml.cs`
- 创建：`src/KeyRadar.App/Pages/HotkeyOverviewPage.xaml`
- 创建：`src/KeyRadar.App/Pages/HotkeyOverviewPage.xaml.cs`
- 创建：`src/KeyRadar.App/Pages/SettingsPage.xaml`
- 创建：`src/KeyRadar.App/Pages/SettingsPage.xaml.cs`
- 修改：`src/KeyRadar.App/ForegroundOverlayWindow.xaml(.cs)`

- [ ] **Step 1：把 MainPage 改为三个栏目导航**

使用 `NavigationView` 或等价自定义导航，只显示雷达总览、热键总览和设置。
删除隐私状态卡片和原五项导航。

- [ ] **Step 2：实现雷达总览**

Hero 卡片展示冲突或绿色正常状态；下方展示可用、前台生效和待确认三个指标。
右上角放小型重新扫描按钮。指标与列表之间必须有“热键总览”标题和“查看
全部”。

- [ ] **Step 3：实现热键总览**

左侧为可折叠维度筛选，右侧按应用变体折叠。每个应用标题层只有一个
“转到应用”；热键行不得重复跳转按钮。搜索支持功能、应用和组合。

- [ ] **Step 4：实现设置页面**

设置分区包含语言主题、程序更新、规则库与下载按钮、我的规则、候选提交和
诊断导出。无效 active 包回退时，全局和规则库区域都展示持续警告。

- [ ] **Step 5：更新前台浮窗**

浮窗使用本地化热键行，继续保证不抢焦点、不拦截、可拖动、位置持久化和
无规则时隐藏。

- [ ] **Step 6：构建 WinUI 项目**

```powershell
& .tools/dotnet/dotnet.exe build src/KeyRadar.App/KeyRadar.App.csproj -c Debug -p:Platform=x64 --nologo
```

预期：XAML 编译成功并生成 `KeyRadar.exe`。

- [ ] **Step 7：提交界面重构**

```powershell
git add src/KeyRadar.App
git commit -m "feat: build radar hotkey and settings navigation"
```

## Task 12：实现“我的规则”与官方覆盖交互

**文件：**
- 创建：`src/KeyRadar.App/Pages/MyRulesPage.xaml(.cs)`
- 创建：`src/KeyRadar.App/Dialogs/RuleImportPreviewDialog.xaml(.cs)`
- 创建：`src/KeyRadar.App/Dialogs/OfficialOverlapDialog.xaml(.cs)`
- 创建：`src/KeyRadar.App/Dialogs/CandidatePreviewDialog.xaml(.cs)`
- 修改：`src/KeyRadar.App/Pages/SettingsPage.xaml(.cs)`

- [ ] **Step 1：实现表单录入**

从 `RadarSession` 当前进程中选择应用，自动带入安全身份字段；录入按钮调用
`HotkeyRecordingSession`；表单收集功能、范围、备注和当前语言文字。

- [ ] **Step 2：实现导入导出预览**

导入前展示应用、变体、热键、未签名状态、匹配字段和官方重叠。导出只写
用户选择的 `.krpack`，不得包含路径或运行时快照。

- [ ] **Step 3：实现官方覆盖询问**

每个已被最新版官方包收录的应用显示差异，提供“使用官方规则”“保留我的
规则”和“查看差异”。选择官方只停用重叠条目。

- [ ] **Step 4：实现候选预览与浏览器跳转**

显示最终单语言 JSON 和隐私检查结果；确认后使用系统浏览器打开预填 Issue
Form，不调用发布 API。

- [ ] **Step 5：构建并提交**

```powershell
& .tools/dotnet/dotnet.exe build src/KeyRadar.App/KeyRadar.App.csproj -c Debug -p:Platform=x64 --nologo
git add src/KeyRadar.App
git commit -m "feat: add local rule authoring and official overlap review"
```

## Task 13：统一文档、规则校验和本地打包

**文件：**
- 修改：`README.md`
- 修改：`docs/RULE_PACKS.md`
- 修改：`docs/PRIVACY.md`
- 修改：`docs/RELEASE_TESTING.md`
- 修改：`CONTRIBUTING.md`
- 修改：`eng/Build-Release.ps1`
- 修改：`eng/KeyRadar.ReleaseTool/Program.cs`
- 不启用：`.github/workflows/*`

- [ ] **Step 1：中文化并统一术语**

文档说明数量动态、Schema v2、分层规则、用户规则、单语言候选和官方覆盖
流程。运行：

```powershell
rg -n -i "shortcut|快捷键|exactly 51|50 applications" README.md docs src tests eng rules
```

预期：不存在旧术语或固定首轮应用数量。

- [ ] **Step 2：验证本地规则包构建不依赖固定数量**

使用开发测试密钥运行 ReleaseTool 的规则包子命令，输出到 `artifacts/local`，
再由 `RulePackReader` 读取。只生成本地验收资产，不创建 GitHub Release。

- [ ] **Step 3：运行本地完整验证**

```powershell
& .\eng\Validate-Rules.ps1
& .tools/dotnet/dotnet.exe test KeyRadar.sln -c Release --nologo -v:minimal
& .tools/dotnet/dotnet.exe build src/KeyRadar.App/KeyRadar.App.csproj -c Release -p:Platform=x64 --nologo
```

- [ ] **Step 4：提交文档和验证工具**

```powershell
git add README.md docs CONTRIBUTING.md eng
git commit -m "docs: document schema v2 and local rule workflow"
```

## Task 14：本地运行验收与交付

**文件：**
- 验证：`src/KeyRadar.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/KeyRadar.exe`
- 验证：本地生成的 `KeyRadar-Rules-v1.0.0.krpack`

- [ ] **Step 1：启动本地 EXE 并验证三栏界面**

确认雷达总览、热键总览和设置可导航；Dashboard 与列表之间显示独立“热键
总览”标题；中英文和三种主题切换正确。

- [ ] **Step 2：验证规则故障与下载入口**

用隔离测试数据模拟无效 active 包，确认随附包继续加载、警告持续显示，
并提供“下载最新规则包”；恢复有效包后警告消失。

- [ ] **Step 3：验证微信 Alt+A**

在微信运行时确认显示 `Alt+A · 截图`、应用归属、证据、冲突对象和应用标题
层唯一跳转入口。正常按下 Alt+A 时，微信原功能继续执行。

- [ ] **Step 4：验证用户规则和候选提交**

录入一个测试热键，确认普通文字不被保存；导出和重新导入本地包；模拟官方
重叠并完成选择；打开预填 GitHub Issue Form，但不提交测试 Issue。

- [ ] **Step 5：验证性能和退出**

确认前台切换250毫秒内更新、典型进程扫描5秒内首批结果、60秒空闲 CPU
低于单核0.5%、关闭后2秒内无 KeyRadar 进程残留。

- [ ] **Step 6：检查仓库状态并提交最终修正**

```powershell
git status --short
git diff --check
git log --oneline --decorate -15
```

仅提交本轮相关文件，不添加 `artifacts/`、`.tools/` 或用户本地数据。

- [ ] **Step 7：等待用户本地验收**

向用户提供 EXE 和规则包的绝对路径。用户确认前不 push 发布自动化、不创建
Release、不启用 GitHub Actions。

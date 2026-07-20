# KeyRadar 规则包

`rules/*.json` 是正式规则的唯一维护来源。当前目录中的文件名包括 `windows-system-default.json`、`wechat-cn-desktop.json`、`chrome-stable.json`、`edge-stable.json` 以及其他以 `-default.json` 或 `-stable.json` 结尾的规则文档；不得引用或新建并行的编译期 C# 目录、回退目录或固定应用清单。规则数量由当前文件集动态决定，不是发布常量。

打包过程验证这些 JSON 文档，并生成 Ed25519 签名的 `.krpack` ZIP。应用不包含并行 C# 目录或隐藏回退。

## 归档布局

```text
manifest.json
signature.ed25519
rules/
  windows-system-default.json
  wechat-cn-desktop.json
```

仅接受两个根元数据文件及 `rules/` 下直接或递归的 JSON 文件。导入前会拒绝反斜杠、绝对路径、`.`/`..` 路径段、重复名称、可执行文件、DLL 和脚本。

归档最多 512 个条目，解压后最多 10 MiB；每个规则文件最多 512 KiB，并且必须在 `manifest.json` 以小写 SHA-256 摘要声明。

## Manifest

```json
{
  "schemaVersion": 1,
  "packId": "io.github.lizzardkevin.keyradar.official",
  "version": "1.0.0",
  "files": [
    {
      "path": "rules/wechat-cn-desktop.json",
      "sha256": "64-lowercase-hex-characters"
    }
  ]
}
```

`signature.ed25519` 是对 `manifest.json` 精确 UTF-8 字节的 Base64 Ed25519 签名。运行时正式包必须使用正式 pack ID，并通过 KeyRadar 内嵌公钥验证；未签名包或其他密钥签名的包不可作为正式规则导入。

首次 `v1.0.0` Release 未来才会将 `KeyRadar-Rules-v<version>.krpack` 与 `KeyRadar.exe` 一同交付。当前没有 Release 或可下载资产。届时，用户主动更新的规则包存于 `data/rules/active.krpack`，被替换的已验证包可仅为显式回滚保留为 `previous.krpack`；活动包无效时必须报告失败，不得静默加载编译期规则。

正式规则运行时一次只使用一个已验证包：存在时用 `active.krpack`，否则用与应用版本匹配的随附签名包。另有未签名 `local.krpack` 保存用户声明。优先级为用户本地规则、活动正式规则、随附正式规则；所有匹配证据都会保留。无法验证正式包时，界面提示“规则不可用，请重新下载/导入”，同时仍可保留真实 `RegisterHotKey` 占用诊断，但不伪造规则归属。

## 规则文档

规则文档遵循 [`schemas/keyradar-rule-v2.schema.json`](../schemas/keyradar-rule-v2.schema.json)。读取器有白名单和边界限制；规则可标识可执行文件、声明热键、命名受支持配置源，但不能运行命令、加载库、扩展环境变量或读取任意路径。

## 贡献检查表

- 使用稳定的可执行文件名和唯一的小写应用 ID。
- 在 `sources` 中链接厂商文档或配置证据。
- 为每个热键说明范围与置信度。
- 不要包含个人路径、窗口标题、用户名或真实用户配置。
- 提交规则前新增或更新目录测试。
- Windows 系统热键只维护在 `rules/windows-system-default.json`；不得编译进应用。

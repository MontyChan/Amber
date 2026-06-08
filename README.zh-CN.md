# Amber

存进琥珀里，到此为止

Amber 是一个面向本地冷存档场景的 CLI/TUI 工具，使用 7z 生成归档包，使用 SQLite 保存索引信息，让归档目录在封存之后仍然可以继续搜索、浏览和恢复。

[English README](./README.md)

## 简介

Amber 适合处理那些已经不参与日常工作、但仍然需要保留的目录，例如老项目、历史资料、归档文档、导出数据或离线交付文件

它把几件事放在一起：

- 用 7z 创建归档
- 用 SQLite 保存外部索引
- 用终端界面完成浏览、搜索和解压

## 功能

- CLI 与交互式 TUI
- 基于 7z 的归档创建
- SQLite + Dapper 外部索引
- 分离的压缩包与仅存储包
- 单文件解压与全部解压
- 按文件路径或备注搜索
- 导出 0 KB 占位目录树
- 可配置数据库路径与归档输出目录
- 可选高级压缩参数
- 中英双语终端界面

## 工作方式

每次归档最多生成两个 `.7z` 文件：

- 压缩包
- 仅存储包，用于已压缩或高熵文件

归档元数据保存在 SQLite 中，而不是只存在压缩包内部。这样在归档数量变多之后，仍然可以直接按路径、备注和记录进行检索。

默认存储根目录：

```text README.zh-CN.md
~/.amber/
```

默认路径：

- 数据库：`~/.amber/amber.db`
- 归档输出目录：`~/.amber/archives/`
- 路径配置文件：`~/.amber/locations.json`

## 依赖

- .NET8 SDK
- 7-Zip 命令行程序：`7z`、`7z.exe`、`7zz.exe` 或 `7za.exe`
- Windows 是当前主要验证平台

可选环境变量：

- `VAULT_7Z_PATH`

示例：

```powershell README.zh-CN.md
$env:VAULT_7Z_PATH = "C:\Program Files\7-Zip\7z.exe"
```

## 构建

```powershell README.zh-CN.md
dotnet build
```

## 运行

启动交互界面：

```powershell README.zh-CN.md
dotnet run
```

显式启动 UI：

```powershell README.zh-CN.md
dotnet run -- ui
```

查看帮助：

```powershell README.zh-CN.md
dotnet run -- --help
```

## CLI 示例

归档目录：

```powershell README.zh-CN.md
dotnet run -- archive "D:\ArchiveSource\Photos"
```

带备注和压缩参数归档：

```powershell README.zh-CN.md
dotnet run -- archive "D:\ArchiveSource\Photos" --note "2022 old phone dump" --tags "photo,phone" --level maximum --solid on
```

高熵文件也参与压缩：

```powershell README.zh-CN.md
dotnet run -- archive "D:\ArchiveSource\Photos" --compress-high-entropy
```

列出归档：

```powershell README.zh-CN.md
dotnet run -- list
```

搜索：

```powershell README.zh-CN.md
dotnet run -- search invoice
```

解压单个文件：

```powershell README.zh-CN.md
dotnet run -- extract 12 --file "docs/report.pdf" --out "D:\Restore"
```

## 典型流程

- 为目录创建归档，并写入备注或标签
- 之后通过索引浏览或搜索归档记录
- 需要时先导出占位目录树查看结构
- 最后选择单文件恢复或全部解压

## TUI

TUI 支持：

- 创建归档
- 浏览归档记录
- 搜索
- 单文件解压
- 全部解压
- 导出占位目录树
- 修改存储路径

## 许可证

本项目采用 Apache License 2.0，详见 `LICENSE`。

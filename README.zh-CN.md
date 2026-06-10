# Amber

面向本地冷数据场景的结构化归档与检索工具

Amber 是一个用于本地冷存档场景的 CLI/TUI 工具。项目使用 7z 生成归档包，使用 SQLite 保存归档元数据与文件索引，这样封存后的目录仍然可以继续搜索、浏览、查看结构并按需恢复。

[English README](./README.md)

## 简介

Amber 面向不再参与日常工作、但仍需长期保留和可追溯访问的目录，例如历史项目、归档文档、导出数据、离线交付材料以及照片或媒体备份。

这个工具围绕作者自己的习惯构建，功能和交互设计均以个人使用场景为准，实用程度因人而异。

项目核心由三部分组成：

- 使用 7z 生成归档包
- 使用 SQLite 保存独立于压缩包的外部索引
- 使用 CLI 与终端交互界面完成浏览、搜索、导出与恢复

和只保留压缩文件的归档方式不同，Amber 会把归档实体和索引信息分开存储。归档数量变多之后，仍然可以直接做跨归档检索，不需要逐个读取压缩包内容。

## 功能

- CLI 与交互式 TUI
- 基于 7z 的目录归档
- 基于 SQLite 与 Dapper 的外部索引
- 压缩包与仅存储包分离输出
- 单文件解压与整包恢复
- 按文件路径或归档备注搜索
- 导出 0 KB 占位目录树，用于预览归档结构
- 可配置数据库路径与归档输出目录
- 支持归档文件与数据库路径迁移
- 可选高级压缩参数
- 中英双语终端界面

## 工作方式

一次归档任务最多生成两个 `.7z` 文件：

- **压缩包：** 用于保存仍具有压缩收益的文本或结构化数据
- **仅存储包：** 用于保存已压缩或高熵文件，避免重复压缩带来的额外时间与收益损失

默认情况下，常见已压缩格式会被放入仅存储包。启用 `--compress-high-entropy` 后，此类文件也可进入压缩包。

归档元数据、文件路径、备注与标签独立持久化于 SQLite 数据库，而非写入压缩包内部。该设计使归档记录能够在不挂载压缩包的情况下完成列表展示、关键字搜索、文件定位与恢复操作。

默认存储根目录：

```text README.zh-CN.md
~/.amber/
```

默认路径：

- 数据库：`~/.amber/amber.db`
- 归档输出目录：`~/.amber/archives/`
- 路径配置文件：`~/.amber/locations.json`

## 依赖

- .NET 8 SDK
- 7-Zip 命令行程序：`7z`、`7z.exe`、`7zz.exe` 或 `7za.exe`
- Windows 为当前主要验证平台

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

- 为目标目录创建归档，并补充备注或标签
- 通过索引浏览历史归档记录或执行关键字搜索
- 需要时导出占位目录树以检查目录结构
- 根据需要执行单文件恢复或完整解压

## TUI

交互式界面支持以下操作：

- 创建归档
- 浏览归档记录
- 搜索归档内容
- 单文件解压
- 全部解压
- 导出占位目录树
- 修改存储路径

## 许可证

本项目采用 Apache License 2.0，详见 `LICENSE`。


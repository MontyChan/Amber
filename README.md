# Amber

A structured local archive and retrieval tool for cold data

Amber is a CLI/TUI application for local cold-archive workflows. It uses 7z to create archive packages and SQLite to persist archive metadata and file indexes, so archived directories remain searchable, browsable, inspectable, and recoverable after they leave the active working set.

[中文](./README.zh-CN.md)

## Overview

Amber is intended for directories that are no longer part of day-to-day work but still need to be retained and accessed later, such as legacy projects, archived documents, exported datasets, offline handoff materials, or photo and media backups.

The project combines:

- archive packaging with 7z
- external metadata indexing with SQLite
- terminal workflows for browsing, searching, exporting structure, and restoring files

Instead of treating an archive as a compressed blob with no practical index, Amber keeps package data and metadata separate. That makes it possible to search across archived material without opening each archive package individually.

## Features

- CLI and interactive TUI
- 7z-based directory archiving
- external SQLite index powered by Dapper
- separate compressed and stored archive packages
- single-file extraction and full archive restoration
- search by file path or archive note
- 0 KB placeholder tree export for structure preview
- configurable database and archive output paths
- archive package and database path migration
- optional advanced compression settings
- bilingual terminal interface in English and Chinese

## How It Works

Each archive operation can produce up to two `.7z` packages:

- a compressed package for files that still benefit from compression
- a stored package for already-compressed or high-entropy files, avoiding unnecessary recompression overhead

By default, common pre-compressed formats are routed into the stored package. With `--compress-high-entropy`, those files can also be included in the compressed package.

Archive metadata, file paths, notes, and tags are persisted in SQLite rather than embedded inside the archive package. This allows archive records to be listed, searched, inspected, and restored without mounting or enumerating package contents first.

Default storage root:

```text README.md
~/.amber/
```

Default paths:

- database: `~/.amber/amber.db`
- archive output: `~/.amber/archives/`
- locations file: `~/.amber/locations.json`

## Requirements

- .NET 8 SDK
- 7-Zip command-line executable (`7z`, `7z.exe`, `7zz.exe`, or `7za.exe`)
- Windows is the primary tested platform

Optional environment variable:

- `VAULT_7Z_PATH`

Example:

```powershell README.md
$env:VAULT_7Z_PATH = "C:\Program Files\7-Zip\7z.exe"
```

## Build

```powershell README.md
dotnet build
```

## Run

Interactive UI:

```powershell README.md
dotnet run
```

Explicit UI command:

```powershell README.md
dotnet run -- ui
```

Help:

```powershell README.md
dotnet run -- --help
```

## CLI Examples

Archive a directory:

```powershell README.md
dotnet run -- archive "D:\OldProjects\ProjectA"
```

Archive with metadata and compression options:

```powershell README.md
dotnet run -- archive "D:\OldProjects\ProjectA" --note "2023 client handoff" --tags "client,handoff" --level maximum --solid on
```

Also compress high-entropy files:

```powershell README.md
dotnet run -- archive "D:\OldProjects\ProjectA" --compress-high-entropy
```

List archives:

```powershell README.md
dotnet run -- list
```

Search:

```powershell README.md
dotnet run -- search invoice
```

Extract a single file:

```powershell README.md
dotnet run -- extract 12 --file "docs/report.pdf" --out "D:\Restore"
```

## Typical Workflow

- create an archive for a target directory and add notes or tags
- browse recorded archive entries or run keyword searches later
- export a placeholder tree when the directory structure needs to be reviewed
- restore a single file or extract the full archive when needed

## TUI

The interactive interface supports:

- archive creation
- archive browsing
- archive search
- single-file extraction
- full extraction
- placeholder tree export
- storage path updates

## License

Licensed under the Apache License, Version 2.0. See `LICENSE` for details.


# Amber

Seal it in amber, and leave it at that.

Amber is a local cold-archive CLI/TUI for directories you want to keep, index, and recover later. It uses 7z for archive packages and SQLite for metadata indexing, so archived folders stay searchable instead of disappearing into opaque compressed blobs.

[中文说明](./README.zh-CN.md)

## Overview

Amber is designed for folders that are no longer active, but still worth keeping around: old projects, exported photo libraries, archived documents, offline handoff materials, or anything else you do not want in the working set anymore.

It combines:

- archive creation with 7z
- metadata indexing with SQLite
- terminal workflows for browsing, searching, and extraction

## Features

- CLI and interactive TUI
- 7z-based archive creation
- SQLite metadata index with Dapper
- separate compressed and stored archive packages
- single-file extraction and full extraction
- file-path and note search
- 0 KB placeholder tree export
- configurable database and archive output paths
- optional advanced compression settings
- bilingual terminal interface in English and Chinese

## How It Works

Each archive run produces up to two `.7z` packages:

- a compressed package for files that still benefit from compression
- a stored package for already-compressed or high-entropy files

Archive metadata is stored outside the package in SQLite. That makes it practical to search across many archives without enumerating archive contents every time.

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

- archive a directory with a note and tags
- browse or search the recorded index later
- inspect structure with a placeholder tree if needed
- extract one file or restore the full archive to an output directory

## TUI

The TUI supports:

- archive creation
- archive browsing
- search
- single-file extraction
- full extraction
- placeholder tree export
- storage path updates

## License

Licensed under the Apache License, Version 2.0. See `LICENSE` for details.

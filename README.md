# Amber

A structured local archive and retrieval tool for cold data

Amber is a CLI/TUI application for local cold-archive workflows. It uses 7z to create archive packages and SQLite to persist archive metadata and file indexes, so archived directories can still be searched, browsed, inspected, and restored after they leave the active working set.

This tool was built around the author's own archiving workflow. Features and interactions reflect that personal use case, so your mileage may vary.

## Overview

Amber is intended for directories that are no longer part of day-to-day work but still need to be retained and accessed later, such as legacy projects, archived documents, exported datasets, offline handoff materials, or photo and media backups.

The project combines:

- archive packaging with 7z
- external metadata indexing with SQLite
- terminal workflows for browsing, searching, exporting structure, and restoring files

Instead of treating an archive as a compressed blob with no practical index, Amber keeps package data and metadata separate. This makes it easier to search across archived material without opening each archive package individually.

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

- a compressed package
- a stored package for files selected for store mode

The archive pipeline uses LZMA2. Excluding high-entropy files is not a suitable compression optimization for this workflow, so it is not presented as a recommended option.

### File Ordering and Directory Structure

Before invoking 7-Zip, Amber builds a deterministic list of relative file paths. Files assigned to the compressed package are ordered by:

1. file extension, using a case-insensitive comparison
2. parent directory, using a case-insensitive comparison
3. file size, from largest to smallest
4. full relative path, using a case-insensitive comparison

Files assigned to the store-mode package are ordered by relative path. Amber writes the resulting paths to a temporary list file and passes that file to 7-Zip while using the archived directory as the working directory.

For non-store compression levels, Amber enables LZMA2 and solid mode by default. Solid compression allows adjacent files to share compression context, but 7-Zip does not perform Amber's ordering itself. The ordering only controls the sequence in which file contents enter the archive; it does not rename files, flatten directories, or change their relative paths. Each archive entry still contains its original relative path, so extracting the package recreates the original directory structure.

The order shown in the 7-Zip file manager may differ from the physical input order because the file manager can sort its view by name, size, date, or another column. This display order does not change the stored paths or extraction result.

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

Web interface:

```powershell README.md
dotnet run -- web
```

The Web interface is served by the same Amber process. During development, the Vue client can be built with Bun:

```powershell README.md
cd Client
bun install
bun run dev
```

The regular .NET build also runs `bun install --frozen-lockfile` and `bun run build`, then copies the generated client files into the application output. Bun is required when building from source, but is not required when running a published Amber package.

## CLI Examples

Archive a directory:

```powershell README.md
dotnet run -- archive "D:\OldProjects\ProjectA"
```

Archive with metadata and compression options:

```powershell README.md
dotnet run -- archive "D:\OldProjects\ProjectA" --note "2023 client handoff" --tags "client,handoff" --level maximum --solid on
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


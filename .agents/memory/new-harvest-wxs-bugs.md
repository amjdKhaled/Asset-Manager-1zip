---
name: New-HarvestWxs PowerShell function confirmed bugs and fixes
description: Two bugs caught in New-HarvestWxs before Windows run; both fixed and validated.
---

## Bug 1: $pid collision (automatic variable)

Assigning to `$pid` inside a function collides with PowerShell's read-only `$PID`
(current process ID). Under `Set-StrictMode -Version Latest` this throws `VariableNotWritable`.

**Fix:** rename to `$parentDirId` / `$childDirId` / `$fileDirId`.

**Why:** PowerShell automatic variables are case-insensitive; `$pid`, `$PID`, `$Pid`
all refer to the same read-only binding.

## Bug 2: Intermediate directory KeyError

The original code built `$dirId` map from `$allFiles | ForEach-Object { $_.DirectoryName }`.
Directories containing ONLY subdirectories (no direct files), e.g. `wwwroot/`,
were never added to `$dirId`. When `$childMap` was built, `$dirId[$parent]` threw a
HashTable KeyError for those intermediate directories under `Set-StrictMode`.

**Fix:** replace with `Get-ChildItem -Path $SourceDir -Recurse -Directory` which
enumerates ALL subdirectories, not just file-containing ones.

**How to apply:** any harvest function that derives directory IDs from file paths
will silently fail on tree structures with intermediate-only directories.

## ID sanitization (must match WiX 4 identifier rules)

WiX 4 identifiers: `[A-Za-z_][A-Za-z0-9_.]*`, max 72 chars.
Directory names like `win-x64` contain hyphens, which are illegal.

**Fix:** `-replace '[^A-Za-z0-9]', '_'` in PowerShell (already in the function).
If replicating in Python for tests: `re.sub(r'[^A-Za-z0-9]', '_', rel)`.

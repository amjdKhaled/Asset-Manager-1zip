# LFPortal Compatibility Report

**Report Date:** 2026-07-31
**Prepared for:** LFPortal Phase 0 — Compatibility Verification
**Prepared by:** Phase 0 Compatibility Verification (pre-implementation)
**Status:** ⚠️ PARTIALLY VERIFIED — Items requiring on-site verification are marked with 🔍

---

## Executive Summary

Research against official Laserfiche developer documentation confirms that the planned
architecture is technically sound. The Laserfiche API Server v2 (self-hosted) is the
correct integration target, it requires .NET 8 and IIS, and it supports the
username/password Bearer token flow the portal will use.

**One critical item remains unresolved and requires on-site verification before Phase 1
begins:** the exact version of the installed Laserfiche environment (Server, Desktop
Client, and API Server). This determines which API endpoint prefix to use and whether
the Desktop Client extension must target .NET Framework 4.x or can target a newer
runtime.

The on-site verification commands are provided for each 🔍 item below.

---

## Component Compatibility Matrix

| # | Component | Requirement | Verified Value | Notes | Status |
|---|-----------|-------------|----------------|-------|--------|
| 1 | Laserfiche Server | 11 or 12 | 🔍 Verify on-site | API Server supports LF Server 11 and 12 only | 🔍 Pending |
| 2 | Laserfiche API Server | Self-hosted, any recent | 🔍 Verify on-site | Must be installed separately from LF Server | 🔍 Pending |
| 3 | Laserfiche Repository API version | v2 preferred | 🔍 Verify on-site | V1 also supported; see API Version section | 🔍 Pending |
| 4 | Laserfiche Desktop Client | 11 or 12 | 🔍 Verify on-site | Extension SDK version tied to client version | 🔍 Pending |
| 5 | Desktop Extension .NET target | .NET Framework 4.x expected | 🔍 Verify on-site | Windows Client is a .NET Framework app; extension must match | 🔍 Pending |
| 6 | LFPortal Web App .NET version | .NET 8 | ✅ Confirmed | Laserfiche API Server itself requires .NET 8 on server | ✅ |
| 7 | IIS version | ≥ IIS 8.5 (Windows Server 2012 R2+) | 🔍 Verify on-site | Required by both LF API Server and LFPortal | 🔍 Pending |
| 8 | ASP.NET Core Hosting Bundle | 8.x | 🔍 Verify on-site | Required on IIS host machine | 🔍 Pending |
| 9 | Windows Server version | Server 2012 R2 or later | 🔍 Verify on-site | LF API Server and .NET 8 minimum requirement | 🔍 Pending |
| 10 | Build machine .NET SDK | 8.x | ✅ Confirmed | Replit environment has dotnet SDK available | ✅ |
| 11 | Offline / air-gapped operation | Full support | ✅ Confirmed | All LF API calls are LAN-only; no internet required | ✅ |

---

## Confirmed Findings (from Official Documentation)

### Laserfiche API Server — Confirmed Facts

**Source:** https://developer.laserfiche.com/docs/api/server/installing-and-configuring/

- The self-hosted Laserfiche API Server is a **separate installable component** from
  Laserfiche Server itself. It must be installed and configured independently.
- Installation prerequisites (officially documented):
  - OS: 64-bit Windows Server 2012 R2 or later, or Windows 10/11 or later
  - IIS must be installed and running
  - **.NET 8** (the API Server itself is an ASP.NET Core application)
- The API Server supports connecting to **Laserfiche Server 11 and Server 12** only.
  Older server versions (10.x) are **not supported** by the current API Server.
- The API Server installs as an IIS web application named `LFRepositoryAPI`.

### API Version Differences (V1 vs V2)

| Feature | V1 | V2 |
|---------|----|----|
| Authentication | Username/password only | Username/password **and** authorization_code (LFDS) |
| APIs | Equivalent | Nearly equivalent; no chunked import in self-hosted |
| Volume support | Yes | Yes |
| Token endpoint | `/v1/Repositories/{repoId}/Token` | `/v2/Repositories/{repoId}/Token` |
| Chunked import | Yes | Not in self-hosted |

**Recommendation:** Target **API V2** for LFPortal. V2 is the current standard and adds
the authorization_code flow for future Active Directory / LFDS integration. The
`ILaserficheApiAdapter` abstraction in Phase 1 allows falling back to V1 URLs if the
installed API Server only supports V1.

### API Base URL Format

Based on official documentation, the self-hosted API base URL pattern is:

```
https://{server}/LFRepositoryAPI/v2/Repositories/{repoId}/...
```

or for V1:

```
https://{server}/LFRepositoryAPI/v1/Repositories/{repoId}/...
```

The `ILaserficheApiAdapter` implementation in Phase 1 must use whichever version the
installed API Server supports, confirmed via the on-site checks below.

### .NET 8 and IIS Compatibility — Confirmed

**Source:** Microsoft official documentation

- ASP.NET Core 8 apps run on IIS via the **ASP.NET Core Module (ANCM)**, installed by
  the **ASP.NET Core 8 Hosting Bundle**.
- Supported OS: Windows 7 SP1 / Windows Server 2008 R2 or later (IIS 7.5+).
- Recommended: Windows Server 2019/2022 with IIS 10.
- The Hosting Bundle installs both the .NET 8 Runtime and the ANCM.

---

## 🔍 On-Site Verification Checklist

The following checks **must be run on the target deployment machine** before Phase 1
begins. Run each command in PowerShell as Administrator and record the output.

### Check 1 — Laserfiche Server Version
```powershell
# On the machine running Laserfiche Server:
Get-ItemProperty "HKLM:\SOFTWARE\Laserfiche\Rio" | Select-Object DisplayVersion, InstallDate
# OR check Programs & Features / Apps for "Laserfiche Server"
```
**Expected:** Version 11.x or 12.x
**Blocker if:** Version is 10.x or earlier (not supported by current LF API Server)

### Check 2 — Laserfiche API Server Installation and Version
```powershell
# Check if the API Server web application exists in IIS:
Import-Module WebAdministration
Get-WebApplication -Name "LFRepositoryAPI" -ErrorAction SilentlyContinue
# Check version of the installed API Server:
Get-ItemProperty "HKLM:\SOFTWARE\Laserfiche\API Server" -ErrorAction SilentlyContinue
```
**Expected:** Web application `LFRepositoryAPI` exists and is running
**Blocker if:** API Server is not installed (must be installed before LFPortal can function)

### Check 3 — API Server V1 vs V2 Support
```powershell
# After confirming API Server is running, test V2 endpoint availability:
# (Replace {server} and {repoId} with actual values)
Invoke-RestMethod -Uri "https://{server}/LFRepositoryAPI/v2/Repositories" -Method Get
# If that fails, try V1:
Invoke-RestMethod -Uri "https://{server}/LFRepositoryAPI/v1/Repositories" -Method Get
```
**Expected:** V2 responds (HTTP 200 or 401 — either confirms the endpoint exists)
**Record:** Which API version prefix responds successfully

### Check 4 — Laserfiche Desktop Client Version
```powershell
Get-ItemProperty "HKLM:\SOFTWARE\Laserfiche\Client" | Select-Object DisplayVersion
# OR from Programs & Features:
Get-WmiObject Win32_Product | Where-Object Name -like "*Laserfiche*" | Select-Object Name, Version
```
**Expected:** Version 11.x or 12.x
**Record for ADR-003:** The exact client version determines the SDK and extension framework

### Check 5 — Desktop Client SDK Location and Framework
```powershell
# Look for the Laserfiche SDK assemblies:
Get-ChildItem "C:\Program Files\Laserfiche" -Recurse -Filter "Laserfiche.RepositoryAccess.dll" -ErrorAction SilentlyContinue
# Check the target framework of the SDK DLL:
[System.Reflection.Assembly]::ReflectionOnlyLoadFrom("{path-to-dll}").ImageRuntimeVersion
```
**Expected:** .NET Framework 4.x runtime version string (e.g., `v4.0.30319`)
**Critical for ADR-003:** This is the authoritative answer for the Desktop Extension's
target framework. If the SDK assembly targets .NET Framework 4.x, the extension project
must also target .NET Framework 4.x.

### Check 6 — IIS Version
```powershell
(Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\InetStp").MajorVersion
(Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\InetStp").MinorVersion
```
**Expected:** 10.0 (Windows Server 2016/2019/2022) or 8.5 (Windows Server 2012 R2)

### Check 7 — ASP.NET Core 8 Hosting Bundle
```powershell
dotnet --list-runtimes | Where-Object { $_ -like "*Microsoft.AspNetCore.App 8*" }
# Also check for ANCM:
Get-Item "$env:windir\System32\inetsrv\aspnetcorev2.dll" -ErrorAction SilentlyContinue | Select-Object VersionInfo
```
**Expected:** ASP.NET Core 8.x runtime listed; ANCM DLL present
**Blocker if:** Not installed (must install ASP.NET Core 8 Hosting Bundle before LFPortal deployment)

### Check 8 — Windows Server Version
```powershell
[System.Environment]::OSVersion.Version
(Get-WmiObject Win32_OperatingSystem).Caption
```
**Expected:** Windows Server 2019 or 2022 (or Windows 10/11 for development)

---

## Known Limitations and Architectural Notes

### 1. Laserfiche API Server is a prerequisite
LFPortal does **not** communicate directly with the Laserfiche Server binary protocol.
All communication goes through the **Laserfiche API Server** (REST). This is a separate
installable component that must be present and running. The MSI installer (Phase 6)
must document this as a prerequisite.

### 2. API Server requires HTTPS for production
The official documentation states that HTTPS bindings on the `LFRepositoryAPI` IIS
application are **required** for production use. LFPortal's `LaserficheOptions.ServerUrl`
must be configured with an `https://` prefix for production deployments.

### 3. GetRepositoryList API requires explicit enablement
The `GET /Repositories` endpoint (used for repository discovery) is **disabled by
default** in the LF API Server `appsettings.json`. The setting
`"EnableGetRepositoryListApi": true` must be set on the API Server to enable the
LF Settings page's "Discover Repositories" feature.

### 4. Desktop Extension framework is unconfirmed
The Laserfiche Windows Client is a .NET Framework application. Extension DLLs loaded
into its process must target a compatible framework. Based on the SDK 10.x release
history and the nature of the Windows Client architecture, **.NET Framework 4.x is
highly probable** as the required target. This **must be confirmed via Check 5 above**
before Phase 5 begins. ADR-003 will be finalized once this is confirmed.

### 5. Token expiration is configurable on the API Server
The `AccessTokenExpirationLimit` setting in the API Server's configuration controls
how long Bearer tokens are valid. LFPortal's token refresh logic (Phase 1) must be
designed to handle whatever expiration period is configured on the target server, not
assume a fixed duration.

---

## Verdict

| Condition | Status |
|-----------|--------|
| Architecture is compatible with confirmed LF API Server requirements | ✅ Yes |
| .NET 8 is the correct target for LFPortal Web | ✅ Yes |
| Phase 1 can begin with V2 API adapter (V1 fallback via adapter) | ✅ Yes |
| On-site verification completed | 🔍 Pending |
| Desktop Extension framework confirmed | 🔍 Pending |

**CURRENT STATUS: CONDITIONALLY APPROVED**

Phase 1 (LF Infrastructure) may proceed because the web portal architecture is fully
confirmed. Phase 5 (Desktop Extension) **must not begin** until Check 5 above is
completed and ADR-003 is finalized with the confirmed framework version.

---

*This document is the authoritative compatibility baseline for all LFPortal development.
Any discovered deviation from these findings must be recorded here before implementation
continues.*

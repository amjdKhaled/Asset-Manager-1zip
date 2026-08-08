# Memory Index

- [SSO OAuth2 Architecture](sso-oauth-architecture.md) — V2 token endpoint for LFDS code exchange; V1 resource URLs unchanged; loop prevention via ssoFailed param; state in IMemoryCache + session.

- [API version independence](api-version-independence.md) — build URLs only from EffectiveApiVersion; probe 401/403 = version exists; empty MSI LF_API_VERSION preserves a legacy v1 pin.

- [Token cache sign-out invalidation](token-cache-signout-invalidation.md) — invalidate cache scopes by bumping a key-embedded generation, never by tracked Remove(); explicit eviction races with in-flight Sets.

- [Phase 1 config architecture](phase1-config-architecture.md) — single writable home %ProgramData%\Dashboard; 4-layer precedence; installer file vs runtime settings file must stay separate.

- [Babel JSX tooltip pattern](babel-jsx-tooltip.md) — Extract inline tooltip functions into separate components; `} as any}` inside JSX crashes the Babel parser.
- [Unicode escapes in bash heredocs](unicode-heredocs.md) — `\uXXXX` sequences in bash/Python strings get double-escaped; write files via Python with explicit UTF-8 encoding.
- [Large file assembly safety](file-assembly.md) — When building large files from temp parts, use distinct filenames per step and verify content before appending.
- [LFPortal DI lifetime rules](lfportal-di-lifetimes.md) — Auth/credential/repo-context singletons; BearerTokenHandler transient; domain services scoped.
- [LFPortal EntryResource naming](lfportal-entry-resource.md) — Inner response record must not share name with adapter enum; use qualified namespace or rename.
- [LFPortal build gates](lfportal-build-gates.md) — ValidateDataAnnotations needs Microsoft.Extensions.Options.DataAnnotations; DPAPI needs [SupportedOSPlatform("windows")].
- [LFPortal URL ownership](lfportal-url-ownership.md) — Repository descriptors carry only the server root; the adapter owns adding the API base path exactly once.
- [LFPortal Swagger evidence](lfportal-swagger-evidence.md) — Route screenshots establish available paths; repository response fields must be confirmed from the live response body.
- [LFPortal v1 repository response](lfportal-v1-repository-response.md) — GET /Repositories returns a root JSON array with repoId, repoName, and webclientUrl.
- [LFPortal search endpoints](lfportal-search-endpoints.md) — Advanced search is /Searches (v1), NOT /Entries/Search (v2-only, returns 405); SimpleSearches returns OData inline, no polling.
- [LFPortal dashboard data sources](lfportal-dashboard-sources.md) — Dashboard uses recursive folder scan (not search expressions) + TemplateDefinitions + in-memory audit log; no external DB needed.
- [LFPortal folder-children OData params](lfportal-folder-children-no-odata.md) — This server returns HTTP 400 for any OData param ($top/$skip/$count/$select) on the Folder/children endpoint; use bare URL only, paginate in memory.
- [LFPortal field name resolution](lfportal-field-name-resolution.md) — Entry fields response may omit human-readable names; join by fieldDefinitionId with GET /FieldDefinitions (confirmed available).
- [LFPortal document viewer endpoints](lfportal-document-viewer-endpoints.md) — Typed edoc route is confirmed; page-list/image routes remain blocked until Swagger evidence arrives.
- [Dashboard rename rules](dashboard-rename-rules.md) — User-facing strings say "Dashboard"; C# namespaces/paths stay LFPortal.*; %ProgramData%\LFPortal\ has backward-compat fallback.
- [Desktop extension confirmed net48](desktop-extension-net48.md) — ADR-003 finalized: net48, SDK 10.4 ClientAutomation.dll, external-EXE button pattern; project excluded from LFPortal.sln.
- [Phase 5 dynamic repository context](phase5-dynamic-repo-context.md) — %(DatabaseName) token → ?repository= URL → session middleware → SessionAwareRepositoryContext singleton; Settings shows source badge.
- [Phase 5 WebView2 popup](phase5-webview2-popup.md) — DashboardWindow with isolated user-data folder per click; why browser was replaced; %(DatabaseName) confirmed correct.
- [LFPortal session auth](lfportal-session-auth.md) — Desktop Client login flow: session keys, credential stack, guard middleware, TryAuthenticateAsync, Change Account.
- [LFPortal Web Client button](lfportal-webclient-button.md) — Final architecture: capture-phase delegated listener only (no direct button listener); _doc=window.document; 500ms cooldown; problems solved in order.
- [Phase 6 installer architecture](phase6-installer.md) — WiX v4 MSI in installer/Dashboard.Installer/; Web Client button deployed by separate PS1, not MSI; build/publish.ps1 orchestrates full release.
- [WiX v4 Mba.Core API quirks](wix-mba-core-api.md) — Engine is a class not a property; Command is private protected; net48 traps: GetValueOrDefault, out-on-properties, PlaceholderText.
- [WiX 4.0.5 schema compat](wix4-schema-compat.md) — All confirmed WiX 3→4 renames/removals: Bitness, AllowAbsent, Secure, Custom Condition attr, NeverOverwrite on Component, appcmd CA for empty ManagedRuntimeVersion, Bundle BA as Payload.
- [New-HarvestWxs bugs fixed](new-harvest-wxs-bugs.md) — $pid collision (rename to $parentDirId); intermediate dir KeyError (use Get-ChildItem -Directory, not file.DirectoryName); WiX ID must sanitize ALL non-alnum chars.
- [WiX 4 linker action references](wix4-linker-action-refs.md) — ConfigureIIs is NOT a public WixAction symbol; After="ConfigureIIs" → WIX0094; use Before="InstallFinalize" instead.
- [WiX 4 IIS port runtime config](wix4-iis-port-runtime.md) — iis:WebAddress/@Port is compile-time only; use appcmd CA + Secure="yes" property for runtime port; Shortcut/@Arguments IS formatted.
- [WiX 4 managed BA Payload deps](wix4-ba-payload-deps.md) — WixToolset.Mba.Core.dll must be an explicit Payload; pass as -d define not backslash concat in WXS.
- [WiX 4 Mba.Host.config assemblyName](wix4-mba-host-config.md) — config MUST have <wix.bootstrapper> with assemblyName; <startup> alone → 0x80070490.
- [MSI ExeCommand trailing-backslash quoting](msi-execommand-trailing-backslash.md) — never quote "[DIRPROP]" in ExeCommand; \" escapes the quote, corrupts the path arg, rolls back install (1722).
- [Repository is runtime session context](repo-as-session-context.md) — repo never an installer setting; token cache keyed by repo + established-session id; 404/TLS/network errors propagate for classified login messages.
- [BA stale DLL guards](ba-stale-dll-guards.md) — publish.ps1 must clean Dashboard.BA/bin+obj in Step 1, compare source vs staged SHA256, and scan staged DLL for removed UI strings.
- [Smoke test WriteConfig validation](smoke-test-writecofig-validation.md) — parse WriteConfig log line for VALUES not raw stdout; Invoked: line legitimately contains both --webapp-path and --config-dir tokens.
- [Scan root entry ID discovery](scan-root-discovery.md) — always call ByPath("\\") first; default configuredRootId=1 is not guaranteed to be the root; short-circuiting on it silently returns 0 entries.
- [V2 folder-children route](v2-folder-children-route.md) — V2 uses Folder/Children (not Laserfiche.Repository.Folder/children); V1 path returns HTTP 404 on V2 servers; BuildFolderChildrenUrl is version-aware.
- [Repository JSON parser — V1 vs V2](repo-json-parser-v1-v2.md) — V2 GET /Repositories returns OData envelope {"value":[…]}; use RepositoryJsonParser.TryParse, never raw Deserialize<List<RepositoryDto>>; auto-detect must validate body shape, not just HTTP status.
- [V2 RepositoryDto field mapping](v2-dto-field-mapping.md) — V2 uses id/name/webClientUrl not repoId/repoName/webclientUrl; parser manually maps both sets in the V2 branch.
- [Web Client launch bypass](webclient-no-login.md) — "Laserfiche Web Client" NOT in GuardedSources; Web Client uses DPAPI credentials directly, no Login redirect.
- [TestConnection blank password](test-connection-blank-password.md) — blank Password field means "use stored DPAPI value"; ServerUrl+RepositoryId are the only always-required fields.
- [Auth diagnostic logging](auth-diagnostic-logging.md) — RequestTokenAsync logs effective config + sanitized LF response body at Error level; 8-char hex DiagnosticId appears in both log and UI; Uri.ToString() unescapes %20, use AbsoluteUri in tests.
- [Self-contained publish architecture](self-contained-publish.md) — Dashboard ships win-x64 self-contained (coreclr.dll bundled); ANCM V2 replaces .NET 8 runtime as the only machine prerequisite; 4 publish guards enforce the contract.
- [IIS Web Client detection](iis-webclient-detection.md) — DetectionService now checks applicationHost.config + appcmd before registry; covers non-default /Laserfiche IIS app paths.
- [SetupHelper smoke test quoting](setuphelper-smoke-quoting.md) — use Start-Process -ArgumentList array (not string) with trailing \. to avoid CommandLineToArgvW consuming --config-dir into --webapp-path.
- [Burn same-version reinstall](burn-same-version-reinstall.md) — AllowSameVersionUpgrades + NOT UPGRADINGPRODUCTCODE required, or old bundle's late uninstall strips the Web Client button (1->0).
- [Burn related-bundle BA UI](burn-related-bundle-ui.md) — BA must run headless when Relation!=None or Display==Embedded, or related-bundle execution opens a second wizard mid-Apply.
- [Installer API host selection](installer-api-host-selection.md) — never default to localhost; pick binding-host>FQDN>machine name by cert SAN match; pure ApiHostSelector is compile-linked into net8 tests.
- [WiX 4 MBA prereq package WIX6802](wix4-mba-prereq-package.md) — NetFx wixext has NO package groups in 4.0.5; define inline ExePackage with bal:PrereqPackage="yes"; ExeCommand→InstallArguments rename; Permanent="yes" avoids WIX0408.

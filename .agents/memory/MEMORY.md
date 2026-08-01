# Memory Index

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

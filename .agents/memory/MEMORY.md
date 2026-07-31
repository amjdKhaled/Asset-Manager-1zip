# Memory Index

- [Babel JSX tooltip pattern](babel-jsx-tooltip.md) — Extract inline tooltip functions into separate components; `} as any}` inside JSX crashes the Babel parser.
- [Unicode escapes in bash heredocs](unicode-heredocs.md) — `\uXXXX` sequences in bash/Python strings get double-escaped; write files via Python with explicit UTF-8 encoding.
- [Large file assembly safety](file-assembly.md) — When building large files from temp parts, use distinct filenames per step and verify content before appending.
- [LFPortal DI lifetime rules](lfportal-di-lifetimes.md) — Auth/credential/repo-context singletons; BearerTokenHandler transient; domain services scoped.
- [LFPortal EntryResource naming](lfportal-entry-resource.md) — Inner response record must not share name with adapter enum; use qualified namespace or rename.
- [LFPortal build gates](lfportal-build-gates.md) — ValidateDataAnnotations needs Microsoft.Extensions.Options.DataAnnotations; DPAPI needs [SupportedOSPlatform("windows")].
- [LFPortal URL ownership](lfportal-url-ownership.md) — Repository descriptors carry only the server root; the adapter owns adding the API base path exactly once.

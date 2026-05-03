import { useParams, Link } from "wouter";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  ArrowLeft, FileText, Tag, Calendar, User, FolderOpen,
  Hash, File, ChevronLeft, ChevronRight, AlertTriangle, ImageOff,
  Shield, Building2, Settings,
} from "lucide-react";
import { useState, useEffect, useCallback } from "react";
import { cn } from "@/lib/utils";

// ── Types ──────────────────────────────────────────────────────────────────
type LFClientConfig = {
  serverUrl: string;
  repositoryId: string;
  token: string;
};

type LFRawField = {
  fieldId: number;
  fieldName: string;
  fieldType: string;
  values: Array<{ value: string | null; position: number }>;
};

type LFEntry = {
  id: number;
  name: string;
  fullPath?: string;
  entryType?: string;
  creator?: string;
  creationTime?: string;
  lastModifiedTime?: string;
  extension?: string;
  pageCount?: number;
  electronicDocumentSize?: number;
  templateName?: string;
};

type LFPage = {
  pageNumber: number;
  width?: number;
  height?: number;
};

type FetchState<T> =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "ok"; data: T }
  | { status: "error"; message: string; notConfigured?: boolean };

// ── Helpers ────────────────────────────────────────────────────────────────
function cleanFieldValue(values: Array<{ value: string | null; position: number }>): string {
  return (values || [])
    .map((v) => (v.value === null || v.value === undefined ? "" : String(v.value).trim()))
    .filter((v) => v !== "")
    .join(", ");
}

async function getLFClientConfig(): Promise<LFClientConfig> {
  console.log("[LF Viewer] Getting client config from /api/laserfiche/status");
  const res = await fetch(`/api/laserfiche/status?_t=${Date.now()}`, {
    credentials: "include",
    cache: "no-store",
    headers: { Accept: "application/json" },
  });
  const ct = res.headers.get("content-type") || "";
  console.log("[LF Viewer] status response:", res.status, ct);

  if (!ct.includes("application/json")) {
    throw Object.assign(
      new Error("Backend returned HTML instead of JSON — check your server is running correctly."),
      { notConfigured: true }
    );
  }
  const body = await res.json() as {
    connected?: boolean; configured?: boolean;
    serverUrl?: string; repositoryId?: string; token?: string;
    message?: string; error?: string;
  };

  if (!body.configured) {
    throw Object.assign(
      new Error("Laserfiche is not configured. Go to LF Settings and enter your server URL, repository, username and password."),
      { notConfigured: true }
    );
  }
  if (!body.connected || !body.token) {
    throw Object.assign(
      new Error(body.message || "Could not connect to Laserfiche — check your credentials in LF Settings."),
      { notConfigured: true }
    );
  }
  return {
    serverUrl: body.serverUrl!,
    repositoryId: body.repositoryId!,
    token: body.token!,
  };
}

async function lfFetch<T>(cfg: LFClientConfig, path: string): Promise<T> {
  const url = `${cfg.serverUrl}/v1/Repositories/${cfg.repositoryId}/${path}`;
  console.log("[LF Viewer] Direct Laserfiche fetch:", url);
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${cfg.token}`, Accept: "application/json" },
  });
  const ct = res.headers.get("content-type") || "";
  console.log("[LF Viewer] Laserfiche response:", res.status, ct, "for", path);

  if (res.status === 401) throw new Error("Unauthorized (401) — token expired. Re-save credentials in LF Settings.");
  if (res.status === 404) throw new Error("Document not found (404) — entry ID may be wrong.");
  if (!ct.includes("application/json")) {
    throw new Error(`Laserfiche returned HTML instead of JSON (status ${res.status}) — wrong API endpoint or server down.`);
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({})) as { message?: string };
    throw new Error(body.message || `Laserfiche error ${res.status}`);
  }
  return res.json() as Promise<T>;
}

// ── Skeleton ───────────────────────────────────────────────────────────────
function DocumentDetailSkeleton() {
  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 border-b border-border px-6 py-4">
        <Skeleton className="h-4 w-24 mb-4" />
        <Skeleton className="h-7 w-2/3 mb-2" />
        <Skeleton className="h-5 w-1/3" />
      </div>
      <div className="flex-1 overflow-auto p-6">
        <div className="max-w-5xl flex gap-6">
          <div className="flex-1 space-y-3">
            <Skeleton className="h-6 w-1/4 mb-4" />
            {[1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="flex justify-between py-2 border-b border-border">
                <Skeleton className="h-4 w-1/3" />
                <Skeleton className="h-4 w-1/3" />
              </div>
            ))}
          </div>
          <div className="w-72 space-y-4">
            <Skeleton className="h-64 w-full rounded-md" />
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Page image viewer ──────────────────────────────────────────────────────
function PageViewer({ cfg, entryId }: { cfg: LFClientConfig; entryId: number }) {
  const [pagesState, setPagesState] = useState<FetchState<string[]>>({ status: "loading" });
  const [currentPage, setCurrentPage] = useState(1);
  const [imgError, setImgError] = useState(false);

  useEffect(() => {
    setPagesState({ status: "loading" });
    lfFetch<{ value?: LFPage[] }>(cfg, `Entries/${entryId}/pages`)
      .then((data) => {
        const pages = (data.value || []).map(
          (p, i) => `${cfg.serverUrl}/v1/Repositories/${cfg.repositoryId}/Entries/${entryId}/pages/${p.pageNumber ?? i + 1}/imageComponents/fullRes`
        );
        setPagesState({ status: "ok", data: pages });
      })
      .catch((err: Error) => setPagesState({ status: "error", message: err.message }));
  }, [cfg, entryId]);

  if (pagesState.status === "loading") return <Skeleton className="h-64 w-full rounded-md" />;

  if (pagesState.status === "error" || (pagesState.status === "ok" && pagesState.data.length === 0)) {
    return (
      <div className="flex flex-col items-center justify-center h-48 border border-dashed border-border rounded-md gap-2 text-muted-foreground">
        <ImageOff className="w-7 h-7" />
        <p className="text-xs text-center px-2">
          {pagesState.status === "error" ? pagesState.message : "No pages available for this document"}
        </p>
      </div>
    );
  }

  const pages = pagesState.data;
  const totalPages = pages.length;
  const pageUrl = `${pages[currentPage - 1]}&access_token=${cfg.token}`;

  return (
    <div className="space-y-3">
      <div className="border border-border rounded-md overflow-hidden bg-muted/30 flex items-center justify-center min-h-48">
        {imgError ? (
          <div className="flex flex-col items-center gap-2 text-muted-foreground py-8">
            <ImageOff className="w-7 h-7" />
            <p className="text-xs">Could not display page {currentPage}</p>
          </div>
        ) : (
          <img
            key={pageUrl}
            src={pageUrl}
            alt={`Page ${currentPage}`}
            className="max-w-full h-auto object-contain"
            onError={() => setImgError(true)}
            onLoad={() => setImgError(false)}
            data-testid={`page-image-${currentPage}`}
          />
        )}
      </div>
      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-3">
          <Button
            variant="outline"
            size="sm"
            onClick={() => { setCurrentPage((p) => Math.max(1, p - 1)); setImgError(false); }}
            disabled={currentPage === 1}
            data-testid="page-prev"
          >
            <ChevronLeft className="w-4 h-4" />
          </Button>
          <span className="text-xs text-muted-foreground">
            Page {currentPage} / {totalPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => { setCurrentPage((p) => Math.min(totalPages, p + 1)); setImgError(false); }}
            disabled={currentPage === totalPages}
            data-testid="page-next"
          >
            <ChevronRight className="w-4 h-4" />
          </Button>
        </div>
      )}
    </div>
  );
}

// ── Not-configured banner ──────────────────────────────────────────────────
function NotConfiguredBanner({ message }: { message: string }) {
  return (
    <div className="h-full flex flex-col items-center justify-center text-center px-6 gap-4">
      <AlertTriangle className="w-14 h-14 text-amber-400/70" />
      <div>
        <h2 className="text-lg font-semibold text-foreground mb-1">Could not load document</h2>
        <p className="text-sm text-muted-foreground max-w-md">{message}</p>
      </div>
      <div className="flex gap-2">
        <Link href="/laserfiche/settings">
          <Button data-testid="go-to-lf-settings">
            <Settings className="w-4 h-4 mr-2" />
            Open LF Settings
          </Button>
        </Link>
        <Link href="/archive">
          <Button variant="outline" data-testid="back-to-archive">
            <ArrowLeft className="w-4 h-4 mr-2" />
            Back to Archive
          </Button>
        </Link>
      </div>
    </div>
  );
}

// ── Main page ──────────────────────────────────────────────────────────────
export default function LFDocumentPage() {
  const params = useParams<{ entryId: string }>();
  const entryId = Number(params.entryId);

  const [cfgState, setCfgState] = useState<FetchState<LFClientConfig>>({ status: "loading" });
  const [entryState, setEntryState] = useState<FetchState<LFEntry>>({ status: "idle" });
  const [fieldsState, setFieldsState] = useState<FetchState<LFRawField[]>>({ status: "idle" });
  const [tagsState, setTagsState] = useState<FetchState<string[]>>({ status: "idle" });

  const loadAll = useCallback(async () => {
    if (!Number.isFinite(entryId)) return;

    setCfgState({ status: "loading" });
    let cfg: LFClientConfig;
    try {
      cfg = await getLFClientConfig();
      setCfgState({ status: "ok", data: cfg });
    } catch (err: any) {
      setCfgState({ status: "error", message: err.message, notConfigured: !!err.notConfigured });
      return;
    }

    // Fire all three Laserfiche calls in parallel
    setEntryState({ status: "loading" });
    setFieldsState({ status: "loading" });
    setTagsState({ status: "loading" });

    const [entryRes, fieldsRes, tagsRes] = await Promise.allSettled([
      lfFetch<LFEntry>(cfg, `Entries/${entryId}?$select=*`),
      lfFetch<{ value?: LFRawField[] }>(cfg, `Entries/${entryId}/fields?formatValue=false`),
      lfFetch<{ value?: Array<{ name?: string; tagName?: string }> }>(cfg, `Entries/${entryId}/tags`),
    ]);

    if (entryRes.status === "fulfilled") {
      setEntryState({ status: "ok", data: entryRes.value });
    } else {
      setEntryState({ status: "error", message: (entryRes.reason as Error).message });
    }

    if (fieldsRes.status === "fulfilled") {
      setFieldsState({ status: "ok", data: fieldsRes.value.value || [] });
    } else {
      setFieldsState({ status: "error", message: (fieldsRes.reason as Error).message });
    }

    if (tagsRes.status === "fulfilled") {
      const tags = (tagsRes.value.value || []).map((t) => t.name || t.tagName || "").filter(Boolean);
      setTagsState({ status: "ok", data: tags });
    } else {
      setTagsState({ status: "ok", data: [] });
    }
  }, [entryId]);

  useEffect(() => { loadAll(); }, [loadAll]);

  // ── Invalid ID ──
  if (!Number.isFinite(entryId)) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-center px-6 gap-4">
        <AlertTriangle className="w-14 h-14 text-muted-foreground/30" />
        <h2 className="text-lg font-semibold">Invalid document ID</h2>
        <Link href="/archive">
          <Button variant="outline" data-testid="back-to-archive">
            <ArrowLeft className="w-4 h-4 mr-2" />Back to Archive
          </Button>
        </Link>
      </div>
    );
  }

  // ── Laserfiche not configured ──
  if (cfgState.status === "error" && cfgState.notConfigured) {
    return <NotConfiguredBanner message={cfgState.message} />;
  }

  // ── Loading config ──
  if (cfgState.status === "loading" || cfgState.status === "idle") {
    return <DocumentDetailSkeleton />;
  }

  // ── Config error (non-503) ──
  if (cfgState.status === "error") {
    return <NotConfiguredBanner message={cfgState.message} />;
  }

  const cfg = cfgState.data;

  // ── Loading entry ──
  if (entryState.status === "loading" || entryState.status === "idle") {
    return <DocumentDetailSkeleton />;
  }

  // ── Entry fetch error ──
  if (entryState.status === "error") {
    return (
      <div className="h-full flex flex-col items-center justify-center text-center px-6 gap-4">
        <AlertTriangle className="w-14 h-14 text-amber-400/70" />
        <h2 className="text-lg font-semibold text-foreground">Could not load document</h2>
        <p className="text-sm text-muted-foreground max-w-md">{entryState.message}</p>
        <div className="flex gap-2">
          <Button onClick={loadAll} variant="outline" data-testid="retry-load">Retry</Button>
          <Link href="/archive">
            <Button variant="outline" data-testid="back-to-archive">
              <ArrowLeft className="w-4 h-4 mr-2" />Back to Archive
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  const entry = entryState.data;
  const rawFields = fieldsState.status === "ok" ? fieldsState.data : [];
  const tags = tagsState.status === "ok" ? tagsState.data : [];

  const cleanedFields = rawFields
    .map((f) => ({ ...f, cleanValue: cleanFieldValue(f.values) }))
    .filter((f) => f.cleanValue !== "");

  const isArabic = (text: string) => /[\u0600-\u06FF]/.test(text);
  const fileSizeKb = entry.electronicDocumentSize ? Math.round(entry.electronicDocumentSize / 1024) : null;
  const createdDate = entry.creationTime
    ? new Date(entry.creationTime).toLocaleDateString("en-GB", { day: "2-digit", month: "long", year: "numeric" })
    : null;

  return (
    <div className="h-full flex flex-col overflow-hidden">
      {/* ── Header ── */}
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-4">
        <Link href="/archive">
          <button
            className="flex items-center gap-1.5 text-sm text-muted-foreground mb-3 hover:text-foreground transition-colors"
            data-testid="back-to-archive"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
            Back to Archive
          </button>
        </Link>

        <div className="flex flex-wrap items-center gap-2 mb-2">
          <span className="text-xs text-muted-foreground flex items-center gap-1">
            <Hash className="w-3 h-3" />
            Entry {entry.id}
          </span>
          {entry.extension && (
            <Badge variant="secondary" className="text-xs uppercase" data-testid="doc-extension">
              {entry.extension}
            </Badge>
          )}
          {entry.templateName && (
            <Badge variant="outline" className="text-xs" data-testid="doc-template">
              {entry.templateName}
            </Badge>
          )}
          {entry.pageCount != null && (
            <span className="text-xs text-muted-foreground">
              {entry.pageCount} {entry.pageCount === 1 ? "page" : "pages"}
            </span>
          )}
        </div>

        <h1
          className={cn("text-xl font-semibold text-foreground leading-tight", isArabic(entry.name) && "font-arabic")}
          dir={isArabic(entry.name) ? "rtl" : "ltr"}
          data-testid="doc-title"
        >
          {entry.name}
        </h1>
        {entry.fullPath && entry.fullPath !== entry.name && (
          <p className="text-xs text-muted-foreground mt-1 flex items-center gap-1" data-testid="doc-path">
            <FolderOpen className="w-3 h-3" />
            {entry.fullPath}
          </p>
        )}

        {/* Tags */}
        {tags.length > 0 && (
          <div className="flex flex-wrap gap-1.5 mt-3" data-testid="doc-tags">
            {tags.map((tag) => (
              <Badge key={tag} variant="outline" className="text-xs flex items-center gap-1">
                <Tag className="w-3 h-3" />
                {tag}
              </Badge>
            ))}
          </div>
        )}
      </div>

      {/* ── Body ── */}
      <div className="flex-1 overflow-auto">
        <div className="max-w-5xl px-6 py-5 flex gap-6">

          {/* Left: document info + metadata */}
          <div className="flex-1 min-w-0 space-y-5">

            {/* Quick info */}
            <div className="bg-card border border-card-border rounded-md">
              <div className="px-4 py-3 border-b border-border flex items-center gap-2">
                <FileText className="w-4 h-4 text-primary" />
                <span className="text-sm font-medium">Document Info</span>
              </div>
              <div className="divide-y divide-border px-4">
                {createdDate && (
                  <div className="flex items-center gap-3 py-2.5">
                    <Calendar className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                    <div>
                      <p className="text-xs text-muted-foreground">Created</p>
                      <p className="text-sm font-medium" data-testid="doc-created">{createdDate}</p>
                    </div>
                  </div>
                )}
                {entry.creator && (
                  <div className="flex items-center gap-3 py-2.5">
                    <User className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                    <div>
                      <p className="text-xs text-muted-foreground">Creator</p>
                      <p className="text-sm font-medium" data-testid="doc-creator">{entry.creator}</p>
                    </div>
                  </div>
                )}
                {entry.extension && (
                  <div className="flex items-center gap-3 py-2.5">
                    <File className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                    <div>
                      <p className="text-xs text-muted-foreground">File Type</p>
                      <p className="text-sm font-medium uppercase">{entry.extension}</p>
                    </div>
                  </div>
                )}
                {fileSizeKb != null && (
                  <div className="flex items-center gap-3 py-2.5">
                    <FolderOpen className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                    <div>
                      <p className="text-xs text-muted-foreground">File Size</p>
                      <p className="text-sm font-medium">
                        {fileSizeKb >= 1024 ? `${(fileSizeKb / 1024).toFixed(1)} MB` : `${fileSizeKb} KB`}
                      </p>
                    </div>
                  </div>
                )}
              </div>
            </div>

            {/* Metadata fields */}
            <div className="bg-card border border-card-border rounded-md">
              <div className="px-4 py-3 border-b border-border flex items-center gap-2">
                <Hash className="w-4 h-4 text-primary" />
                <span className="text-sm font-medium">Metadata Fields</span>
                {fieldsState.status === "ok" && (
                  <span className="ml-auto text-xs text-muted-foreground">{cleanedFields.length} fields</span>
                )}
              </div>

              {fieldsState.status === "loading" && (
                <div className="p-4 space-y-2">
                  {[1, 2, 3, 4].map((i) => (
                    <div key={i} className="flex justify-between py-1.5 border-b border-border">
                      <Skeleton className="h-4 w-1/3" />
                      <Skeleton className="h-4 w-1/2" />
                    </div>
                  ))}
                </div>
              )}

              {fieldsState.status === "error" && (
                <div className="px-4 py-3 text-xs text-red-600 flex items-start gap-2">
                  <AlertTriangle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                  {fieldsState.message}
                </div>
              )}

              {fieldsState.status === "ok" && cleanedFields.length === 0 && (
                <div className="px-4 py-6 text-center text-sm text-muted-foreground">
                  No metadata fields found for this document.
                </div>
              )}

              {fieldsState.status === "ok" && cleanedFields.length > 0 && (
                <div className="divide-y divide-border px-4" data-testid="metadata-fields">
                  {cleanedFields.map((field) => (
                    <div
                      key={field.fieldId}
                      className="flex items-start justify-between gap-4 py-2.5"
                      data-testid={`metadata-field-${field.fieldId}`}
                    >
                      <span className="text-xs text-muted-foreground flex-shrink-0 w-2/5 pt-0.5">
                        {field.fieldName}
                      </span>
                      <span
                        className={cn("text-sm text-foreground text-right break-all flex-1", isArabic(field.cleanValue) && "font-arabic")}
                        dir={isArabic(field.cleanValue) ? "rtl" : "ltr"}
                      >
                        {field.cleanValue}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          {/* Right: image preview */}
          <div className="w-80 flex-shrink-0">
            <div className="bg-card border border-card-border rounded-md">
              <div className="px-4 py-3 border-b border-border flex items-center gap-2">
                <FileText className="w-4 h-4 text-primary" />
                <span className="text-sm font-medium">Document Preview</span>
              </div>
              <div className="p-4">
                <PageViewer cfg={cfg} entryId={entryId} />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "wouter";
import { type Document } from "@shared/schema";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  FileText, FileCheck, Scroll, TrendingUp, Shield, Building2,
  Clock, Tag, Search, ChevronRight, Folder, FolderOpen,
  ArrowLeft, Image as ImageIcon, FileDown, Eye, Trash2,
  LayoutGrid, LayoutList,
} from "lucide-react";
import { cn } from "@/lib/utils";

const classificationColor = (cls: string) => {
  switch (cls) {
    case "Top Secret": return "text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-950/30 border-red-200 dark:border-red-800";
    case "Confidential": return "text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-950/30 border-amber-200 dark:border-amber-800";
    default: return "text-primary bg-primary/5 border-primary/20";
  }
};

const statusColor = (status: string) => {
  switch (status) {
    case "Active": case "Published": return "text-emerald-600 dark:text-emerald-400";
    case "Completed": case "Closed": return "text-slate-500";
    case "Under Review": return "text-amber-600 dark:text-amber-400";
    case "Approved": return "text-blue-600 dark:text-blue-400";
    default: return "text-muted-foreground";
  }
};

const docIcon = (type: string) => {
  switch (type?.toLowerCase()) {
    case "contract": return FileCheck;
    case "report": return FileText;
    case "memo": case "policy": return Scroll;
    case "plan": case "program": return TrendingUp;
    default: return FileText;
  }
};

function DocCard({ doc }: { doc: Document }) {
  const Icon = docIcon(doc.docType);
  return (
    <div className="bg-card border border-card-border rounded-md p-4 hover-elevate" data-testid={`archive-doc-${doc.id}`}>
      <div className="flex items-start gap-3">
        <div className="w-9 h-9 rounded-md bg-primary/10 flex items-center justify-center flex-shrink-0 mt-0.5">
          <Icon className="w-4 h-4 text-primary" />
        </div>
        <div className="flex-1 min-w-0">
          <button type="button" className="text-left w-full" data-testid={`button-open-document-card-${doc.id}`}>
            <h3 className="text-sm font-semibold text-foreground leading-tight hover:text-primary transition-colors cursor-pointer line-clamp-1 mb-0.5">
              {doc.title}
            </h3>
          </button>
          {doc.titleAr && (
            <p className="text-xs text-muted-foreground leading-tight line-clamp-1 mb-2 font-arabic" dir="rtl">{doc.titleAr}</p>
          )}
          <div className="flex flex-wrap gap-1 mb-3">
            <Badge variant="outline" className={cn("text-xs border py-0", classificationColor(doc.classification))}>
              {doc.classification}
            </Badge>
            <Badge variant="secondary" className="text-xs py-0">{doc.docType}</Badge>
            {doc.securityLevel !== "Public" && (
              <Badge variant="outline" className="text-xs py-0">
                <Shield className="w-2.5 h-2.5 mr-1" />
                {doc.securityLevel}
              </Badge>
            )}
          </div>
          <div className="flex items-center justify-between gap-2">
            <div className="flex flex-wrap gap-x-3 gap-y-1">
              <span className="text-xs text-muted-foreground flex items-center gap-1">
                <Building2 className="w-3 h-3" />
                {doc.department.split(" ").slice(-2).join(" ")}
              </span>
              <span className="text-xs text-muted-foreground flex items-center gap-1">
                <Clock className="w-3 h-3" />
                {doc.year}
              </span>
              <span className={cn("text-xs font-medium", statusColor(doc.workflowStatus))}>
                {doc.workflowStatus}
              </span>
            </div>
            <Button size="icon" variant="ghost" className="h-7 w-7 flex-shrink-0" data-testid={`archive-view-${doc.id}`}>
              <ChevronRight className="w-3.5 h-3.5" />
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}

type LaserficheFileEntry = {
  id: number;
  name: string;
  entryType: string;
  fullPath: string;
  creator?: string;
  creationTime?: string;
  lastModifiedTime?: string;
  extension?: string;
  pageCount?: number;
  isElectronicDocument?: boolean;
};

type LaserfichePreview = {
  folderId: number;
  children: LaserficheFileEntry[];
  fieldDefinitions?: Array<{ id: number; name: string; fieldType?: string; isRequired?: boolean }>;
};

type TrailItem = { id: number; name: string };

const parseFolderId = (value: string): number | null => {
  const trimmed = value.trim();
  if (!/^\d+$/.test(trimmed)) return null;
  const parsed = Number(trimmed);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
};

type LaserficheDetails = {
  value?: Array<{
    fieldId: number;
    fieldName: string;
    fieldType: string;
    isMultiValue: boolean;
    isRequired: boolean;
    hasMoreValues: boolean;
    groupId: number;
    values: Array<{ value: string | null; position: number }>;
  }>;
  fieldDefinitions?: Array<{ id: number; name: string; fieldType?: string; isRequired?: boolean }>;
};

type LaserficheSummary = { content: string; contentAr: string };

type DocumentAnalysis = {
  entryId: number;
  title: string;
  createdDate: string | null;
  fullPath: string;
  metadata: Record<string, unknown>;
  content: string;
  summary: LaserficheSummary;
};

type LaserficheRawFieldValue = { value?: unknown; [key: string]: unknown };

function normalizeLaserficheFieldValue(input: unknown): string {
  if (input === null || input === undefined) return "";
  if (input instanceof Date) return input.toISOString();
  if (typeof input === "object") {
    const raw = input as LaserficheRawFieldValue;
    if ("value" in raw) {
      if (raw.value === null || raw.value === undefined) return "";
      return normalizeLaserficheFieldValue(raw.value);
    }
    try { return JSON.stringify(raw); } catch { return ""; }
  }
  return String(input);
}

function formatLaserficheFieldValues(values: unknown[]): string {
  return values.map((item) => normalizeLaserficheFieldValue(item)).filter((v) => v !== "").join(", ");
}

// ── Smart inline document viewer ──────────────────────────────────────────────
function SmartViewer({ entry, onClose }: { entry: LaserficheFileEntry; onClose: () => void }) {
  const contentUrl = `/api/laserfiche/entries/${entry.id}/content`;
  const ext = (entry.extension || "").toLowerCase().replace(/^\./, "");
  const isPdf = ext === "pdf";
  const isImage = ["jpg", "jpeg", "png", "gif", "tiff", "tif", "bmp", "webp"].includes(ext);
  const isOffice = ["doc", "docx", "xls", "xlsx", "ppt", "pptx"].includes(ext);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [fileUrl, setFileUrl] = useState<string | null>(null);
  const isEmpty = !fileUrl;

  let viewerContent: JSX.Element;
  if (isLoading) {
    viewerContent = (
      <div className="w-full h-full flex items-center justify-center">
        <div className="w-8 h-8 rounded-full border-2 border-primary/30 border-t-primary animate-spin" />
      </div>
    );
  } else if (loadError) {
    viewerContent = (
      <div className="w-full h-full flex flex-col items-center justify-center gap-2 text-center px-8">
        <p className="text-sm font-medium text-destructive">Failed to load preview</p>
        <p className="text-xs text-muted-foreground">{loadError}</p>
      </div>
    );
  } else if (isEmpty) {
    viewerContent = <div className="w-full h-full flex items-center justify-center text-muted-foreground text-sm">No document available</div>;
  } else if (isPdf && fileUrl) {
    viewerContent = <iframe src={fileUrl} className="w-full h-full border-none" title={entry.name} data-testid="viewer-iframe-pdf" />;
  } else if (isImage && fileUrl) {
    viewerContent = (
      <div className="w-full h-full overflow-auto flex items-start justify-center p-4">
        <img src={fileUrl} alt={entry.name} className="max-w-full h-auto rounded shadow-sm" data-testid="viewer-img" />
      </div>
    );
  } else if (isOffice && fileUrl) {
    viewerContent = (
      <div className="flex flex-col h-full">
        <iframe src={fileUrl} className="w-full flex-1 border-none" title={entry.name} data-testid="viewer-iframe-office" />
        <div className="flex-shrink-0 px-4 py-2 border-t border-border bg-background flex items-center gap-2">
          <Eye className="w-3.5 h-3.5 text-muted-foreground" />
          <span className="text-xs text-muted-foreground">If your browser cannot preview this file, open it in a compatible viewer.</span>
        </div>
      </div>
    );
  } else if (fileUrl) {
    viewerContent = (
      <div className="w-full h-full flex items-center justify-center text-muted-foreground text-sm" data-testid="viewer-unsupported">
        No preview available for this file type
      </div>
    );
  } else {
    viewerContent = (
      <div className="flex flex-col items-center justify-center h-full gap-4 text-center px-8">
        <div className="w-14 h-14 rounded-full bg-muted flex items-center justify-center">
          <FileDown className="w-7 h-7 text-muted-foreground" />
        </div>
        <div>
          <p className="text-sm font-medium text-foreground mb-1">No preview available</p>
        </div>
        <a href={contentUrl} download={entry.name || `document-${entry.id}`} data-testid="viewer-download-fallback">
          <Button variant="outline" size="sm" className="gap-1.5">
            Download file
          </Button>
        </a>
      </div>
    );
  }

  useEffect(() => {
    let disposed = false;
    let objectUrl: string | null = null;

    const loadFile = async () => {
      setIsLoading(true);
      setLoadError(null);
      try {
        const response = await fetch(contentUrl);
        if (response.status === 204) {
          setFileUrl(null);
          throw new Error("No document available");
        }
        if (!response.ok) throw new Error(`Failed to load document (${response.status})`);
        const blob = await response.blob();
        objectUrl = URL.createObjectURL(blob);
        if (!disposed) {
          setFileUrl(objectUrl);
        } else {
          URL.revokeObjectURL(objectUrl);
          objectUrl = null;
        }
      } catch (error) {
        if (!disposed) {
          setFileUrl(null);
          setLoadError(error instanceof Error ? error.message : "Failed to load document");
        }
      } finally {
        if (!disposed) setIsLoading(false);
      }
    };

    loadFile();

    return () => {
      disposed = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [contentUrl]);
  const absoluteContentUrl = typeof window === "undefined" ? contentUrl : `${window.location.origin}${contentUrl}`;
  const officeViewerUrl = `https://view.officeapps.live.com/op/embed.aspx?src=${encodeURIComponent(absoluteContentUrl)}`;

  return (
    <div className="h-full flex flex-col bg-card border border-card-border rounded-md overflow-hidden" data-testid="doc-viewer-panel">
      {/* Viewer toolbar */}
      <div className="flex-shrink-0 px-4 py-2.5 border-b border-border flex items-center gap-2">
        <button
          type="button"
          onClick={onClose}
          className="flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
          data-testid="viewer-close"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          Back
        </button>
        <div className="w-px h-4 bg-border" />
        <div className="flex items-center gap-1.5 flex-1 min-w-0">
          {isPdf ? <FileText className="w-3.5 h-3.5 text-primary flex-shrink-0" /> :
            isImage ? <ImageIcon className="w-3.5 h-3.5 text-primary flex-shrink-0" /> :
              <FileDown className="w-3.5 h-3.5 text-primary flex-shrink-0" />}
          <span className="text-xs font-medium text-foreground truncate">{entry.name}</span>
          {ext && (
            <Badge variant="secondary" className="text-xs uppercase flex-shrink-0">{ext}</Badge>
          )}
        </div>
      </div>

      {/* Viewer body */}
      <div className="flex-1 overflow-hidden bg-muted/20">
        {isLoading ? (
          <div className="w-full h-full flex items-center justify-center">
            <div className="w-8 h-8 rounded-full border-2 border-primary/30 border-t-primary animate-spin" />
          </div>
        ) : loadError ? (
          <div className="w-full h-full flex flex-col items-center justify-center gap-2 text-center px-8">
            <p className="text-sm font-medium text-destructive">Failed to load preview</p>
            <p className="text-xs text-muted-foreground">{loadError}</p>
          </div>
        ) : isEmpty ? (
          <div className="w-full h-full flex items-center justify-center text-muted-foreground text-sm">
            No document available
          </div>
        ) : fileUrl ? (
          isPdf ? (
            <iframe src={fileUrl} className="w-full h-full border-none" title={entry.name} data-testid="viewer-iframe-pdf" />
          ) : isImage ? (
            <div className="w-full h-full overflow-auto flex items-start justify-center p-4">
              <img src={fileUrl} alt={entry.name} className="max-w-full h-auto rounded shadow-sm" data-testid="viewer-img" />
            </div>
          ) : isOffice ? (
            <div className="flex flex-col h-full">
              <iframe src={officeViewerUrl} className="w-full flex-1 border-none" title={entry.name} data-testid="viewer-iframe-office" />
              <div className="flex-shrink-0 px-4 py-2 border-t border-border bg-background flex items-center gap-2">
                <Eye className="w-3.5 h-3.5 text-muted-foreground" />
                <span className="text-xs text-muted-foreground">If your browser cannot preview this file, use Download above and save as PDF.</span>
              </div>
            </div>
          ) : (
            <iframe src={fileUrl} className="w-full h-full border-none" title={entry.name} data-testid="viewer-iframe-fallback" />
          )
        ) : (
          <div className="flex flex-col items-center justify-center h-full gap-4 text-center px-8">
            <div className="w-14 h-14 rounded-full bg-muted flex items-center justify-center">
              <FileDown className="w-7 h-7 text-muted-foreground" />
            </div>
            <div>
              <p className="text-sm font-medium text-foreground mb-1">No preview available</p>
            </div>
            <a href={contentUrl} download={entry.name || `document-${entry.id}`} data-testid="viewer-download-fallback">
              <Button variant="outline" size="sm" className="gap-1.5">
                Download file
              </Button>
            </a>
          </div>
        )}
      </div>
    </div>
  );
}

export default function ArchivePage() {
  const [, setLocation] = useLocation();
  const [localSearch, setLocalSearch] = useState("");
  const [selectedFolderFilter, setSelectedFolderFilter] = useState("all");
  const [selectedFolderId, setSelectedFolderId] = useState("1");
  const [viewMode, setViewMode] = useState<"archive" | "laserfiche">("archive");
  const [layoutView, setLayoutView] = useState<"grid" | "list">("grid");
  const [trail, setTrail] = useState<TrailItem[]>([]);
  const [selectedEntryId, setSelectedEntryId] = useState<number | null>(null);
  const [details, setDetails] = useState<LaserficheDetails | null>(null);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [detailsError, setDetailsError] = useState<string | null>(null);
  const [analysisByEntryId, setAnalysisByEntryId] = useState<Record<number, DocumentAnalysis>>({});
  const [analysisLoadingEntryId, setAnalysisLoadingEntryId] = useState<number | null>(null);
  const [analysisError, setAnalysisError] = useState<string | null>(null);
  const [viewerEntry, setViewerEntry] = useState<LaserficheFileEntry | null>(null);
  const [openNotice, setOpenNotice] = useState<string | null>(null);
  const [discoveringRoots, setDiscoveringRoots] = useState(false);
  const [rootCandidates, setRootCandidates] = useState<Array<{ id: number; name: string }>>([]);
  const currentFolderId = selectedFolderId;

  const { data: lfDocsData, isLoading } = useQuery<{
    repositoryId?: string;
    repositoryName?: string;
    folderCount?: number;
    documentCount?: number;
    apiEndpoints?: string[];
    failedCalls?: Array<{ endpoint: string; error: string }>;
    documents: Array<{
      id: number;
      name: string;
      path: string;
      folderName: string;
      repositoryId?: string;
      repositoryName?: string;
      metadata?: Record<string, string[]>;
      extension?: string | null;
      pageCount?: number | null;
      isElectronicDocument?: boolean;
    }>;
  }>({
    queryKey: ["/api/lf/documents", "all"],
    queryFn: async () => {
      console.info("[ArchivePage] loading all Laserfiche documents", { endpoint: "/api/lf/documents" });
      const res = await fetch("/api/lf/documents", { credentials: "include" });
      console.info("[ArchivePage] Laserfiche documents API response", { status: res.status, ok: res.ok });
      if (!res.ok) throw new Error("Failed to load Laserfiche documents");
      const payload = await res.json();
      console.info("[ArchivePage] all Laserfiche documents loaded", {
        repositoryId: payload.repositoryId,
        folderCount: payload.folderCount,
        documentCount: payload.documentCount ?? payload.documents?.length ?? 0,
        apiEndpoints: payload.apiEndpoints,
        failedCalls: payload.failedCalls,
      });
      return payload;
    },
  });

  const folderFilters = useMemo(() => {
    const unique = new Map<string, { id: string; name: string }>();
    for (const doc of lfDocsData?.documents || []) {
      if (doc.folderName && !unique.has(doc.folderName)) {
        unique.set(doc.folderName, { id: doc.folderName, name: doc.folderName });
      }
    }
    return Array.from(unique.values());
  }, [lfDocsData]);

  const { data: preview, isLoading: previewLoading, error: previewError, refetch: refetchPreview } = useQuery<LaserfichePreview>({
    queryKey: ["/api/laserfiche/folders", currentFolderId, "children"],
    enabled: true,
  });

  const filtered = (lfDocsData?.documents || []).filter((d) => {
    const folderMatch = selectedFolderFilter === "all" || d.folderName === selectedFolderFilter;
    const searchMatch = !localSearch || d.name.toLowerCase().includes(localSearch.toLowerCase()) || d.path.toLowerCase().includes(localSearch.toLowerCase());
    return folderMatch && searchMatch;
  });

  const folders = useMemo(() => (preview?.children || []).filter(i => i.entryType?.toLowerCase().includes("folder")), [preview]);
  const files = useMemo(() => (preview?.children || []).filter(i => !i.entryType?.toLowerCase().includes("folder")), [preview]);
  const { data: lfSearchData } = useQuery<{ results: Array<{ id: number; name: string; path: string }> }>({
    queryKey: ["/api/laserfiche/search", currentFolderId, localSearch],
    enabled: viewMode === "laserfiche" && localSearch.trim().length > 0,
    queryFn: async () => {
      const res = await fetch("/api/laserfiche/search", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ query: localSearch, folderId: Number(currentFolderId) || 1 }),
      });
      if (!res.ok) throw new Error("Search failed");
      return res.json();
    },
  });
  const filesToRender = useMemo(() => {
    if (viewMode !== "laserfiche") return files;
    if (!localSearch.trim()) return files;
    const mapped = (lfSearchData?.results || []).map((r) => ({
      id: r.id,
      name: r.name,
      fullPath: r.path,
      entryType: "ElectronicDocument",
      isElectronicDocument: true,
    }));
    return mapped as LaserficheFileEntry[];
  }, [viewMode, localSearch, files, lfSearchData]);

  const openFolder = async (folderId: string, folderName?: string) => {
    setSelectedFolderId(folderId);
    setTrail((current) => {
      const index = current.findIndex((item) => item.id === parsedFolderId);
      if (index >= 0) return current.slice(0, index + 1);
      return [...current, { id: parsedFolderId, name: folderName || `Folder ${normalizedFolderId}` }];
    });
  };

  const discoverRoots = async () => {
    setDiscoveringRoots(true);
    try {
      const res = await fetch("/api/lf/root-candidates", { credentials: "include" });
      if (!res.ok) throw new Error("Failed to discover root folders");
      const payload = await res.json();
      setRootCandidates(Array.isArray(payload?.candidates) ? payload.candidates : []);
    } finally {
      setDiscoveringRoots(false);
    }
  };

  const openTrail = (index: number) => {
    const next = trail[index];
    if (!next) return;
    setSelectedFolderId(String(next.id));
    setActiveFolderId(next.id);
    setTrail(trail.slice(0, index + 1));
  };

  const openDocument = async (entryId: number) => {
    setSelectedEntryId(entryId);
    setDetailsLoading(true);
    setDetailsError(null);
    try {
      const data = await loadLaserficheFields(entryId);
      setDetails(data);
    } catch (error) {
      setDetails(null);
      setDetailsError(error instanceof Error ? error.message : "Could not load Laserfiche fields.");
    } finally {
      setDetailsLoading(false);
    }
  };

  const openViewer = async (file: LaserficheFileEntry) => {
    setOpenNotice(null);
    if (file.isElectronicDocument === false) {
      setOpenNotice("This entry has no electronic file, so it cannot be opened in the document viewer.");
      return;
    }
    try {
      const probe = await fetch(`/api/laserfiche/entries/${file.id}/content`, { method: "HEAD" });
      if (!probe.ok) {
        if (probe.status === 404) {
          setOpenNotice("This entry has no electronic file, so it cannot be opened in the document viewer.");
          return;
        }
        setOpenNotice("Could not open this document right now. Please try again.");
        return;
      }
      setLocation(`/lf-document/${file.id}`);
    } catch {
      setOpenNotice("Could not open this document right now. Please try again.");
    }
  };

  const closeViewer = () => setViewerEntry(null);

  const handleAI = async (file: LaserficheFileEntry) => {
    let contextText = `Document ID: ${file.id}\nName: ${file.name}\nPath: ${file.fullPath || "-"}`;
    try {
      const payload = await loadLaserficheFields(file.id);
      const fields = Array.isArray(payload?.value) ? payload.value : [];
      const map: Record<string, string> = {};
      for (const f of fields) {
        const vals = formatLaserficheFieldValues(f?.values || []);
        if (f?.fieldName && vals) map[f.fieldName] = vals;
      }
      contextText = [
        `Document ID: ${file.id}`,
        `Name: ${file.name}`,
        `Path: ${file.fullPath || "-"}`,
        `Title: ${map["Title"] || map["العنوان"] || "-"}`,
        `Department: ${map["Department"] || map["الجهة"] || "-"}`,
        `Type: ${map["Document Type"] || map["نوع المستند"] || "-"}`,
        `Status: ${map["Workflow Status"] || map["الحالة"] || "-"}`,
      ].join("\n");
    } catch {}

    const nextDoc = {
      entryId: file.id,
      name: file.name,
      fullPath: file.fullPath,
      fileUrl: `/api/laserfiche/entries/${file.id}/content`,
      contextText,
    };
    localStorage.setItem("ai_document", JSON.stringify(nextDoc));
    setLocation(`/chat?entryId=${file.id}`);
  };
  const analyzeDocument = async (file: LaserficheFileEntry) => {
    setSelectedEntryId(file.id);
    setAnalysisError(null);
    if (analysisByEntryId[file.id]) return;

    setAnalysisLoadingEntryId(file.id);
    try {
      const metadata = details?.value?.reduce<Record<string, string>>((acc, field) => {
        const formatted = formatLaserficheFieldValues(field.values || []);
        if (formatted) acc[field.fieldName] = formatted;
        return acc;
      }, {}) || {};

      const res = await fetch("/api/analyze-document", {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        credentials: "include",
        body: JSON.stringify({ entryId: file.id, name: file.name, fullPath: file.fullPath, metadata }),
      });

      const payload = await res.json().catch(() => null);
      if (!res.ok || !payload) throw new Error(payload?.error || `Analyze failed: ${res.status}`);
      setAnalysisByEntryId((current) => ({ ...current, [file.id]: payload as DocumentAnalysis }));
    } catch (error) {
      setAnalysisError(error instanceof Error ? error.message : "Document analysis failed.");
    } finally {
      setAnalysisLoadingEntryId(null);
    }
  };

  const fieldEntries = details?.value || [];
  const fieldDefinitions = details?.fieldDefinitions || [];

  const loadLaserficheFields = async (entryId: number) => {
    const endpoint = `/api/laserfiche/entries/${entryId}/fields`;
    try {
      const res = await fetch(endpoint, { headers: { Accept: "application/json" }, credentials: "include" });
      const contentType = res.headers.get("content-type") || "";
      const payload = await res.json().catch(() => null);

      if (!res.ok) {
        if (res.status === 401) throw new Error("Failed to load Laserfiche fields: 401 Unauthorized. Re-open LF Settings and save valid credentials.");
        throw new Error(payload?.error || `Failed to load Laserfiche fields: ${res.status}`);
      }
      if (!contentType.includes("application/json") || !payload || !Array.isArray(payload.value)) {
        throw new Error("Metadata API returned HTML/non-JSON. Verify backend is running and LF is configured.");
      }
      return { value: payload.value, fieldDefinitions: payload.fieldDefinitions || [] } as LaserficheDetails;
    } catch (error) {
      const lastError = error instanceof Error ? error.message : "Could not load metadata.";
      throw new Error(lastError);
    }
  };

  return (
    <div className="h-full flex flex-col overflow-hidden">
      {/* Header */}
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-5">
        <div className="mb-4">
          <h1 className="text-xl font-semibold text-foreground">Document Archive</h1>
          <p className="text-sm text-muted-foreground mt-0.5 font-arabic" dir="rtl">أرشيف المستندات الحكومية</p>
        </div>
        <div className="flex items-center gap-2 mb-3">
          <Badge variant="outline" className="text-xs" data-testid="archive-repository">
            Repository: {lfDocsData?.repositoryName || lfDocsData?.repositoryId || "Laserfiche"}
          </Badge>
          {lfDocsData && (
            <Badge variant="secondary" className="text-xs" data-testid="archive-folder-document-counts">
              {lfDocsData.folderCount ?? 0} folders · {lfDocsData.documentCount ?? lfDocsData.documents.length} documents
            </Badge>
          )}
        </div>
        <div className="flex flex-wrap gap-3">
          <div className="relative flex-1 min-w-48">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-muted-foreground" />
            <Input
              value={localSearch}
              onChange={e => setLocalSearch(e.target.value)}
              placeholder="Filter documents..."
              className="pl-8 h-8 text-sm"
              data-testid="archive-search"
            />
          </div>
          <Select value={selectedFolderFilter} onValueChange={setSelectedFolderFilter}>
            <SelectTrigger className="h-8 text-xs w-48" data-testid="archive-folder-filter">
              <SelectValue placeholder="All Folders" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Folders</SelectItem>
              {folderFilters.map(f => <SelectItem key={f.id} value={f.id}>{f.name}</SelectItem>)}
            </SelectContent>
          </Select>
          {lfDocsData && (
            <span className="flex items-center text-xs text-muted-foreground">
              {filtered?.length ?? 0} of {lfDocsData.documents.length} documents
            </span>
          )}
          <div className="flex items-center gap-1 ml-auto">
            <Button
              variant={layoutView === "grid" ? "default" : "outline"}
              size="icon"
              className="h-8 w-8"
              onClick={() => setLayoutView("grid")}
              data-testid="archive-view-grid"
              title="Grid view"
            >
              <LayoutGrid className="w-4 h-4" />
            </Button>
            <Button
              variant={layoutView === "list" ? "default" : "outline"}
              size="icon"
              className="h-8 w-8"
              onClick={() => setLayoutView("list")}
              data-testid="archive-view-list"
              title="List view"
            >
              <LayoutList className="w-4 h-4" />
            </Button>
          </div>
        </div>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-hidden px-6 py-5">
        <div className="h-full grid grid-cols-1 gap-5">

          {/* LEFT — Document grid OR inline viewer */}
          <div className="overflow-auto min-h-0">
            {viewerEntry ? (
              <div className="h-full">
                <SmartViewer entry={viewerEntry} onClose={closeViewer} />
              </div>
            ) : isLoading ? (
              layoutView === "grid" ? (
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
                  {[1, 2, 3, 4, 5, 6].map(i => <Skeleton key={i} className="h-28 rounded-md" />)}
                </div>
              ) : (
                <div className="space-y-2">
                  {[1, 2, 3, 4, 5, 6].map(i => <Skeleton key={i} className="h-12 rounded-md" />)}
                </div>
              )
            ) : filtered && filtered.length > 0 ? (
              layoutView === "grid" ? (
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 max-w-6xl">
                  {filtered.map((doc) => (
                    <button
                      key={doc.id}
                      type="button"
                      onClick={() => setLocation(`/lf-document/${doc.id}`)}
                      className="border border-border rounded-md p-3 bg-card text-left hover:bg-muted/20 transition-colors focus:outline-none focus:ring-2 focus:ring-primary/40"
                      data-testid={`archive-card-open-${doc.id}`}
                    >
                      <p className="text-lg font-semibold truncate">{doc.name}</p>
                      <p className="text-sm text-muted-foreground truncate mt-1">{doc.path}</p>
                      <div className="mt-3 flex items-center gap-2 flex-wrap">
                        <Badge variant="secondary">Entry #{doc.id}</Badge>
                        <Badge variant="outline">{doc.folderName}</Badge>
                        <Badge variant="outline">{doc.repositoryName || doc.repositoryId || lfDocsData?.repositoryName || lfDocsData?.repositoryId || "Laserfiche"}</Badge>
                      </div>
                      {doc.metadata && Object.keys(doc.metadata).length > 0 && (
                        <div className="mt-3 grid gap-1" data-testid={`archive-metadata-${doc.id}`}>
                          {Object.entries(doc.metadata).slice(0, 3).map(([key, values]) => (
                            <p key={key} className="text-xs text-muted-foreground truncate">
                              <span className="font-medium text-foreground">{key}:</span> {values.join(", ") || "-"}
                            </p>
                          ))}
                        </div>
                      )}
                      <div className="mt-3">
                        <div className="w-full h-px bg-border mb-3" />
                        <Button
                          type="button"
                          size="lg"
                          className="w-full h-10 text-sm"
                          onClick={(e) => {
                            e.stopPropagation();
                            handleAI({ id: doc.id, name: doc.name, fullPath: doc.path, entryType: "ElectronicDocument", isElectronicDocument: true } as LaserficheFileEntry);
                          }}
                        >
                          AI Assistant
                        </Button>
                      </div>
                    </button>
                  ))}
                </div>
              ) : (
                <div className="overflow-hidden rounded-lg border border-border">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="bg-muted/50">
                        <th className="text-left px-3 py-2.5 font-semibold">Name</th>
                        <th className="text-left px-3 py-2.5 font-semibold">Path</th>
                        <th className="text-left px-3 py-2.5 font-semibold">Folder</th>
                        <th className="text-left px-3 py-2.5 font-semibold w-20">ID</th>
                        <th className="text-left px-3 py-2.5 font-semibold w-28">Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filtered.map((doc) => (
                        <tr
                          key={doc.id}
                          className="border-t border-border hover:bg-muted/30 transition-colors"
                          data-testid={`archive-list-row-${doc.id}`}
                        >
                          <td className="px-3 py-2.5">
                            <button
                              type="button"
                              onClick={() => setLocation(`/lf-document/${doc.id}`)}
                              className="text-left font-medium text-foreground hover:text-primary transition-colors"
                              data-testid={`archive-list-open-${doc.id}`}
                            >
                              {doc.name}
                            </button>
                          </td>
                          <td className="px-3 py-2.5 text-muted-foreground truncate max-w-[200px]" title={doc.path}>{doc.path}</td>
                          <td className="px-3 py-2.5">
                            <Badge variant="outline" className="text-xs">{doc.folderName}</Badge>
                          </td>
                          <td className="px-3 py-2.5 text-muted-foreground">#{doc.id}</td>
                          <td className="px-3 py-2.5">
                            <Button
                              type="button"
                              size="sm"
                              variant="ghost"
                              className="h-7 text-xs"
                              onClick={() => handleAI({ id: doc.id, name: doc.name, fullPath: doc.path, entryType: "ElectronicDocument", isElectronicDocument: true } as LaserficheFileEntry)}
                              data-testid={`archive-list-ai-${doc.id}`}
                            >
                              AI
                            </Button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )
            ) : (
              <div className="flex flex-col items-center justify-center h-64 text-center">
                <FileText className="w-12 h-12 text-muted-foreground/30 mb-4" />
                <h3 className="font-medium text-foreground mb-1">No documents found</h3>
                <p className="text-sm text-muted-foreground">Try adjusting your filters.</p>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

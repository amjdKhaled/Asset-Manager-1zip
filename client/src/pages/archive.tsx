import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "wouter";
import { type Document } from "@shared/schema";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  FileText, FileCheck, Scroll, TrendingUp, Shield, Building2,
  Clock, Tag, Search, ChevronRight, Folder, FolderOpen, ExternalLink
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

type LaserfichePreview = {
  folderId: number;
  children: Array<{
    id: number;
    name: string;
    entryType: string;
    fullPath: string;
    creator?: string;
    creationTime?: string;
    lastModifiedTime?: string;
    extension?: string;
    pageCount?: number;
  }>;
  fieldDefinitions?: Array<{
    id: number;
    name: string;
    fieldType?: string;
    isRequired?: boolean;
  }>;
};

type TrailItem = {
  id: number;
  name: string;
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
    values: Array<{
      value: string | null;
      position: number;
    }>;
  }>;
  fieldDefinitions?: Array<{
    id: number;
    name: string;
    fieldType?: string;
    isRequired?: boolean;
  }>;
};

type LaserficheSummary = {
  content: string;
  contentAr: string;
};

type DocumentAnalysis = {
  entryId: number;
  title: string;
  createdDate: string | null;
  fullPath: string;
  metadata: Record<string, unknown>;
  content: string;
  summary: LaserficheSummary;
};


type LaserficheRawFieldValue = {
  value?: unknown;
  [key: string]: unknown;
};

function normalizeLaserficheFieldValue(input: unknown): string {
  if (input === null || input === undefined) return "";
  if (input instanceof Date) return input.toISOString();

  if (typeof input === "object") {
    const raw = input as LaserficheRawFieldValue;
    if ("value" in raw) {
      if (raw.value === null || raw.value === undefined) return "";
      return normalizeLaserficheFieldValue(raw.value);
    }

    try {
      return JSON.stringify(raw);
    } catch {
      return "";
    }
  }

  return String(input);
}

function formatLaserficheFieldValues(values: unknown[]): string {
  return values
    .map((item) => normalizeLaserficheFieldValue(item))
    .filter((value) => value !== "")
    .join(", ");
}

export default function ArchivePage() {
  const [localSearch, setLocalSearch] = useState("");
  const [filterType, setFilterType] = useState("all");
  const [filterDept, setFilterDept] = useState("all");
  const [selectedFolderId, setSelectedFolderId] = useState("1");
  const [trail, setTrail] = useState<TrailItem[]>([{ id: 1, name: "Repository" }]);
  const [selectedEntryId, setSelectedEntryId] = useState<number | null>(null);
  const [details, setDetails] = useState<LaserficheDetails | null>(null);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [detailsError, setDetailsError] = useState<string | null>(null);
  const [analysisByEntryId, setAnalysisByEntryId] = useState<Record<number, DocumentAnalysis>>({});
  const [analysisLoadingEntryId, setAnalysisLoadingEntryId] = useState<number | null>(null);
  const [analysisError, setAnalysisError] = useState<string | null>(null);

  const { data: docs, isLoading } = useQuery<Document[]>({
    queryKey: ["/api/documents"],
  });

  const { data: preview, isLoading: previewLoading, error: previewError, refetch: refetchPreview } = useQuery<LaserfichePreview>({
    queryKey: ["/api/laserfiche/folders", selectedFolderId, "children"],
    enabled: true,
  });


  const filtered = docs?.filter(d => {
    const matchesSearch = !localSearch || d.title.toLowerCase().includes(localSearch.toLowerCase()) || (d.titleAr || "").includes(localSearch);
    const matchesType = filterType === "all" || d.docType === filterType;
    const matchesDept = filterDept === "all" || d.department === filterDept;
    return matchesSearch && matchesType && matchesDept;
  });

  const departments = docs ? Array.from(new Set(docs.map(d => d.department))) : [];
  const docTypes = docs ? Array.from(new Set(docs.map(d => d.docType))) : [];

  const folders = useMemo(() => {
    const items = preview?.children || [];
    return items.filter((item) => item.entryType?.toLowerCase().includes("folder"));
  }, [preview]);

  const files = useMemo(() => {
    const items = preview?.children || [];
    return items.filter((item) => !item.entryType?.toLowerCase().includes("folder"));
  }, [preview]);

  const openFolder = async (folderId: string, folderName?: string) => {
    setSelectedFolderId(folderId);
    setTrail((current) => {
      const index = current.findIndex((item) => String(item.id) === folderId);
      if (index >= 0) return current.slice(0, index + 1);
      return [...current, { id: Number(folderId), name: folderName || `Folder ${folderId}` }];
    });
    await refetchPreview();
  };

  const openTrail = async (index: number) => {
    const next = trail[index];
    if (!next) return;
    setSelectedFolderId(String(next.id));
    setTrail(trail.slice(0, index + 1));
    await refetchPreview();
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

  const openEdoc = (entryId: number) => {
    // Backend streams the file with Bearer token auth — browser opens it directly.
    // No fetch needed: window.open navigates to the proxy endpoint which handles auth.
    window.open(`/api/laserfiche/entries/${entryId}/open`, "_blank", "noopener,noreferrer");
  };

  const analyzeDocument = async (file: LaserfichePreview["children"][number]) => {
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
        body: JSON.stringify({
          entryId: file.id,
          name: file.name,
          fullPath: file.fullPath,
          metadata,
        }),
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
      console.log("[Laserfiche] Fetching metadata", { entryId, endpoint });
      const res = await fetch(endpoint, {
        headers: {
          Accept: "application/json",
        },
        credentials: "include",
      });

        const contentType = res.headers.get("content-type") || "";
        console.log("[Laserfiche] Metadata response status", { entryId, endpoint, status: res.status, contentType });
        const payload = await res.json().catch(() => null);

        if (!res.ok) {
          if (res.status === 401) {
            throw new Error("Failed to load Laserfiche fields: 401 Unauthorized from Laserfiche. Re-open Laserfiche Settings and save valid username/password to refresh server token.");
          }
          throw new Error(payload?.error || `Failed to load Laserfiche fields: ${res.status}`);
        }

        if (!contentType.includes("application/json") || !payload || !Array.isArray(payload.value)) {
          throw new Error("Metadata API returned HTML/non-JSON (frontend route hit). Verify VITE_BACKEND_URL points to backend API server.");
        }

      console.log("[Laserfiche] Metadata payload received", { entryId, count: payload.value.length });
      return {
        value: payload.value,
        fieldDefinitions: payload.fieldDefinitions || [],
      } as LaserficheDetails;
    } catch (error) {
      const lastError = error instanceof Error ? error.message : "Could not load metadata.";
      console.error("[Laserfiche] Metadata fetch failed", { entryId, endpoint, error: lastError });
      throw new Error(`${lastError} Root cause: backend Laserfiche token acquisition failed or expired.`);
    }
  };

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-5">
        <div className="mb-4">
          <h1 className="text-xl font-semibold text-foreground">Document Archive</h1>
          <p className="text-sm text-muted-foreground mt-0.5 font-arabic" dir="rtl">أرشيف المستندات الحكومية</p>
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
          <Select value={filterType} onValueChange={setFilterType}>
            <SelectTrigger className="h-8 text-xs w-36" data-testid="archive-filter-type">
              <SelectValue placeholder="All Types" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Types</SelectItem>
              {docTypes.map(t => <SelectItem key={t} value={t}>{t}</SelectItem>)}
            </SelectContent>
          </Select>
          <Select value={filterDept} onValueChange={setFilterDept}>
            <SelectTrigger className="h-8 text-xs w-48" data-testid="archive-filter-dept">
              <SelectValue placeholder="All Departments" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Departments</SelectItem>
              {departments.map(d => <SelectItem key={d} value={d}>{d.split(" ").slice(-2).join(" ")}</SelectItem>)}
            </SelectContent>
          </Select>
          {docs && (
            <span className="flex items-center text-xs text-muted-foreground">
              {filtered?.length ?? 0} of {docs.length} documents
            </span>
          )}
        </div>
      </div>

      <div className="flex-1 overflow-hidden px-6 py-5">
        <div className="h-full grid grid-cols-1 lg:grid-cols-[1fr_360px] gap-5">
          <div className="overflow-auto">
            {isLoading ? (
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
                {[1, 2, 3, 4, 5, 6].map(i => <Skeleton key={i} className="h-28 rounded-md" />)}
              </div>
            ) : filtered && filtered.length > 0 ? (
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-3 max-w-5xl">
                {filtered.map(doc => <DocCard key={doc.id} doc={doc} />)}
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center h-64 text-center">
                <FileText className="w-12 h-12 text-muted-foreground/30 mb-4" />
                <h3 className="font-medium text-foreground mb-1">No documents found</h3>
                <p className="text-sm text-muted-foreground">Try adjusting your filters.</p>
              </div>
            )}
          </div>

          <div className="overflow-auto">
            <div className="bg-card border border-card-border rounded-md h-full flex flex-col">
              <div className="px-4 py-3 border-b border-border flex items-center gap-2">
                <Folder className="w-4 h-4 text-primary" />
                <h2 className="text-sm font-semibold">Laserfiche Repository</h2>
              </div>
              <div className="p-4 border-b border-border space-y-2">
                <label className="text-xs font-medium text-muted-foreground">Current Folder ID</label>
                <div className="flex gap-2">
                  <Input value={selectedFolderId} onChange={(e) => setSelectedFolderId(e.target.value)} data-testid="input-folder-id" />
                  <Button type="button" onClick={() => openFolder(selectedFolderId)} data-testid="button-open-folder">
                    Open
                  </Button>
                </div>
                <div className="flex items-center gap-1 text-xs text-muted-foreground overflow-x-auto">
                  {trail.map((item, index) => (
                    <button
                      key={`${item.id}-${index}`}
                      type="button"
                      className="hover:text-foreground"
                      onClick={() => openTrail(index)}
                      data-testid={`trail-folder-${item.id}`}
                    >
                      {index > 0 && <span className="mx-1">/</span>}
                      {item.name}
                    </button>
                  ))}
                </div>
              </div>
              <div className="flex-1 overflow-auto p-3">
                {previewLoading ? (
                  <Skeleton className="h-64 w-full" />
                ) : previewError ? (
                  <div className="text-xs text-red-600">Could not load folders.</div>
                ) : preview ? (
                  <div className="space-y-4">
                    <div>
                      <p className="text-xs text-muted-foreground mb-2">Folders</p>
                      <div className="space-y-1">
                        {folders.map((folder) => (
                          <button
                            key={folder.id}
                            type="button"
                            onClick={() => openFolder(String(folder.id), folder.name)}
                            className="w-full flex items-center gap-2 px-2 py-1.5 rounded-md hover:bg-muted text-left"
                            data-testid={`folder-row-${folder.id}`}
                          >
                            <FolderOpen className="w-4 h-4 text-amber-500" />
                            <span className="text-sm">{folder.name}</span>
                          </button>
                        ))}
                        {folders.length === 0 && <div className="text-xs text-muted-foreground">No folders.</div>}
                      </div>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground mb-2">Files</p>
                      <div className="divide-y divide-border rounded-md border border-border">
                        {files.map((file) => (
                          <div
                            key={file.id}
                            className={cn(
                              "w-full px-3 py-2 flex items-center justify-between gap-2 text-left hover:bg-muted",
                              selectedEntryId === file.id && "bg-primary/5"
                            )}
                            data-testid={`file-row-${file.id}`}
                          >
                            <button
                              type="button"
                              className="min-w-0 text-left flex-1"
                              onClick={() => openDocument(file.id)}
                              data-testid={`button-metadata-${file.id}`}
                            >
                              <p className="text-sm font-medium truncate">{file.name}</p>
                              <p className="text-xs text-muted-foreground truncate">{file.fullPath}</p>
                            </button>
                            <div className="flex items-center gap-1.5 flex-shrink-0">
                              <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                className="h-7 text-xs px-2"
                                onClick={() => openDocument(file.id)}
                                data-testid={`button-metadata-panel-${file.id}`}
                              >
                                Metadata
                              </Button>
                              <Button
                                type="button"
                                variant="outline"
                                size="sm"
                                className="h-7 text-xs px-2 gap-1"
                                onClick={() => openEdoc(file.id)}
                                data-testid={`button-open-document-${file.id}`}
                              >
                                <ExternalLink className="w-3 h-3" />
                                Open
                              </Button>
                            </div>
                          </div>
                        ))}
                        {files.length === 0 && <div className="px-3 py-2 text-xs text-muted-foreground">No files.</div>}
                      </div>
                    </div>
                    {selectedEntryId && (
                      <div className="border border-border rounded-md p-3 space-y-3">
                        <div className="flex items-center gap-2">
                          <FileText className="w-4 h-4 text-primary" />
                          <p className="text-sm font-semibold">Document Details</p>
                        </div>
                        {detailsLoading ? (
                          <Skeleton className="h-40 w-full" />
                        ) : detailsError ? (
                          <div className="text-xs text-red-600">{detailsError}</div>
                        ) : fieldEntries.length > 0 ? (
                          <div className="space-y-3">
                            <div className="grid grid-cols-1 gap-2 text-xs">
                              {fieldEntries.map((field) => (
                                <div key={field.fieldId} className="flex items-start justify-between gap-3 border-b border-border pb-1.5 last:border-b-0">
                                  <span className="text-muted-foreground shrink-0">{field.fieldName}</span>
                                  <span className="text-foreground text-right break-all">
                                    {formatLaserficheFieldValues(field.values || []) || "—"}
                                  </span>
                                </div>
                              ))}
                            </div>
                          </div>
                        ) : fieldDefinitions.length > 0 ? (
                          <div className="space-y-2">
                            <p className="text-xs text-muted-foreground">No field values on this document. Showing available repository metadata fields:</p>
                            <div className="grid grid-cols-1 gap-2 text-xs">
                              {fieldDefinitions.slice(0, 20).map((field) => (
                                <div key={field.id} className="flex items-start justify-between gap-3 border-b border-border pb-1.5 last:border-b-0">
                                  <span className="text-muted-foreground shrink-0">{field.name}</span>
                                  <span className="text-foreground text-right break-all">{field.fieldType || "Field"}</span>
                                </div>
                              ))}
                            </div>
                          </div>
                        ) : (
                          <div className="text-xs text-muted-foreground">Select a file to view document details.</div>
                        )}
                      </div>
                    )}
                    {selectedEntryId && (
                      <div className="border border-border rounded-md p-3 space-y-3">
                        <div className="flex items-center gap-2">
                          <span className="text-base">🤖</span>
                          <p className="text-sm font-semibold">AI Analysis</p>
                        </div>
                        {analysisLoadingEntryId === selectedEntryId ? (
                          <Skeleton className="h-24 w-full" />
                        ) : analysisError ? (
                          <div className="text-xs text-red-600">{analysisError}</div>
                        ) : analysisByEntryId[selectedEntryId] ? (
                          <div className="space-y-2 text-xs">
                            <p className="text-muted-foreground">{analysisByEntryId[selectedEntryId].summary.content}</p>
                            {analysisByEntryId[selectedEntryId].content ? null : (
                              <p className="text-amber-600">No document content was available; summary is based on metadata.</p>
                            )}
                          </div>
                        ) : (
                          <div className="text-xs text-muted-foreground">Click Analyze to run AI analysis for this document.</div>
                        )}
                      </div>
                    )}
                  </div>
                ) : (
                  <div className="text-xs text-muted-foreground">Click open to load the selected folder.</div>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

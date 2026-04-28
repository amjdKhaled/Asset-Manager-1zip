import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { type Document } from "@shared/schema";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  FileText, FileCheck, Scroll, TrendingUp, Shield, Building2,
  Clock, Tag, Search, Filter, ChevronRight, Eye, Server, Database, User, Lock, FileSearch
} from "lucide-react";
import { Link } from "wouter";
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
          <Link href={`/document/${doc.id}`}>
            <h3 className="text-sm font-semibold text-foreground leading-tight hover:text-primary transition-colors cursor-pointer line-clamp-1 mb-0.5">
              {doc.title}
            </h3>
          </Link>
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
            <Link href={`/document/${doc.id}`}>
              <Button size="icon" variant="ghost" className="h-7 w-7 flex-shrink-0" data-testid={`archive-view-${doc.id}`}>
                <ChevronRight className="w-3.5 h-3.5" />
              </Button>
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}

type LaserfichePreview = {
  entry: {
    id: number;
    name: string;
    entryType: string;
    fullPath: string;
    creator?: string;
    creationTime?: string;
    lastModifiedTime?: string;
    extension?: string;
    pageCount?: number;
  };
  fields: Record<string, string>;
};

export default function ArchivePage() {
  const [localSearch, setLocalSearch] = useState("");
  const [filterType, setFilterType] = useState("all");
  const [filterDept, setFilterDept] = useState("all");
  const [laserficheEntryId, setLaserficheEntryId] = useState("1");
  const [showLaserfichePreview, setShowLaserfichePreview] = useState(false);

  const { data: docs, isLoading } = useQuery<Document[]>({
    queryKey: ["/api/documents"],
  });

  const { data: preview, isLoading: previewLoading, error: previewError, refetch: refetchPreview } = useQuery<LaserfichePreview>({
    queryKey: ["/api/laserfiche/preview", laserficheEntryId],
    enabled: false,
  });

  const filtered = docs?.filter(d => {
    const matchesSearch = !localSearch || d.title.toLowerCase().includes(localSearch.toLowerCase()) || (d.titleAr || "").includes(localSearch);
    const matchesType = filterType === "all" || d.docType === filterType;
    const matchesDept = filterDept === "all" || d.department === filterDept;
    return matchesSearch && matchesType && matchesDept;
  });

  const departments = docs ? [...new Set(docs.map(d => d.department))] : [];
  const docTypes = docs ? [...new Set(docs.map(d => d.docType))] : [];

  const openPreview = async () => {
    setShowLaserfichePreview(true);
    await refetchPreview();
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
            <div className="bg-card border border-card-border rounded-md p-4 space-y-4">
              <div className="flex items-center gap-2">
                <Eye className="w-4 h-4 text-primary" />
                <h2 className="text-sm font-semibold">Laserfiche Preview</h2>
              </div>
              <p className="text-xs text-muted-foreground">
                Preview an entry from the same repo saved in LF Settings.
              </p>
              <div className="space-y-2">
                <label className="text-xs font-medium text-muted-foreground">Entry ID</label>
                <Input
                  value={laserficheEntryId}
                  onChange={(e) => setLaserficheEntryId(e.target.value)}
                  placeholder="1"
                  data-testid="input-laserfiche-entry-id"
                />
                <Button
                  type="button"
                  onClick={openPreview}
                  className="w-full"
                  data-testid="button-preview-laserfiche"
                >
                  <FileSearch className="w-4 h-4 mr-2" />
                  Preview Entry
                </Button>
              </div>

              {showLaserfichePreview && (
                <div className="border border-border rounded-md p-3 space-y-3">
                  {previewLoading ? (
                    <Skeleton className="h-28 w-full" />
                  ) : previewError ? (
                    <div className="text-xs text-red-600">Could not load preview.</div>
                  ) : preview ? (
                    <>
                      <div className="space-y-1">
                        <p className="text-sm font-medium">{preview.entry.name}</p>
                        <p className="text-xs text-muted-foreground break-all">{preview.entry.fullPath}</p>
                      </div>
                      <div className="space-y-1 text-xs">
                        <div className="flex justify-between gap-2"><span>Type</span><span>{preview.entry.entryType}</span></div>
                        <div className="flex justify-between gap-2"><span>Creator</span><span>{preview.entry.creator || "—"}</span></div>
                        <div className="flex justify-between gap-2"><span>Pages</span><span>{preview.entry.pageCount || "—"}</span></div>
                      </div>
                      {Object.keys(preview.fields).length > 0 && (
                        <div className="space-y-2">
                          <p className="text-xs font-medium text-muted-foreground">Fields</p>
                          <div className="space-y-1">
                            {Object.entries(preview.fields).map(([key, value]) => (
                              <div key={key} className="flex justify-between gap-2 text-xs">
                                <span className="text-muted-foreground">{key}</span>
                                <span className="text-foreground text-right break-all">{value}</span>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}
                    </>
                  ) : (
                    <div className="text-xs text-muted-foreground">Click preview to load an entry.</div>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

import { useParams, Link } from "wouter";
import { useQuery } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  ArrowLeft, FileText, Tag, Calendar, User, FolderOpen,
  Hash, File, ChevronLeft, ChevronRight, AlertTriangle, ImageOff,
  ServerOff, Settings, Info, Download, Eye, ExternalLink
} from "lucide-react";
import { useState } from "react";
import { cn } from "@/lib/utils";

type LFDocumentDetail = {
  id: number;
  name: string;
  path: string;
  createdDate: string | null;
  creator: string | null;
  extension: string | null;
  pageCount: number | null;
  metadata: Array<{ fieldId: number; fieldName: string; fieldType: string; value: string }>;
  tags: string[];
};

type LFDocumentPages = {
  entryId: number;
  pageCount: number;
  pages: string[];
};

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
          <div className="w-80 space-y-4">
            <Skeleton className="h-96 w-full rounded-md" />
          </div>
        </div>
      </div>
    </div>
  );
}

/** Inline document viewer: tries iframe first, falls back to page images */
function DocumentViewer({ entryId, extension }: { entryId: number; extension: string | null }) {
  const [showImages, setShowImages] = useState(false);
  const isPdf = extension?.toLowerCase() === "pdf";
  const isDoc = /^docx?$/i.test(extension || "");

  const edocUrl = `/api/laserfiche/entries/${entryId}/content`;

  if (!showImages && (isPdf || isDoc || !extension)) {
    return (
      <div className="space-y-3">
        {/* Inline viewer */}
        <div className="border border-border rounded-md overflow-hidden bg-muted/30">
          <iframe
            src={edocUrl}
            className="w-full"
            style={{ height: "600px" }}
            title={`Document ${entryId}`}
            data-testid="doc-iframe"
            onError={() => setShowImages(true)}
          />
        </div>
        <div className="flex gap-2 flex-wrap">
          <a href={edocUrl} download>
            <Button variant="outline" size="sm" className="gap-1.5" data-testid="download-edoc">
              <Download className="w-3.5 h-3.5" />
              Download
            </Button>
          </a>
          <a href={edocUrl} target="_blank" rel="noreferrer">
            <Button variant="ghost" size="sm" className="gap-1.5" data-testid="open-edoc-tab">
              <ExternalLink className="w-3.5 h-3.5" />
              Open in new tab
            </Button>
          </a>
          <Button variant="ghost" size="sm" className="gap-1.5" onClick={() => setShowImages(true)} data-testid="switch-to-images">
            <Eye className="w-3.5 h-3.5" />
            Page images
          </Button>
        </div>
      </div>
    );
  }

  return <PageImageViewer entryId={entryId} edocUrl={edocUrl} extension={extension} onSwitchToDoc={() => setShowImages(false)} />;
}

function PageImageViewer({ entryId, edocUrl, extension, onSwitchToDoc }: {
  entryId: number;
  edocUrl: string;
  extension: string | null;
  onSwitchToDoc: () => void;
}) {
  const [currentPage, setCurrentPage] = useState(1);
  const [imgError, setImgError] = useState(false);

  const { data: pageData, isLoading, error } = useQuery<LFDocumentPages>({
    queryKey: ["/api/document", entryId, "image"],
    queryFn: async () => {
      const res = await fetch(`/api/document/${entryId}/image`);
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.error || `Failed to load pages: ${res.status}`);
      }
      return res.json();
    },
  });

  if (isLoading) {
    return <Skeleton className="h-80 w-full rounded-md" />;
  }

  if (error || !pageData || pageData.pages.length === 0) {
    return (
      <div className="space-y-3">
        <div className="flex flex-col items-center justify-center h-48 border border-dashed border-border rounded-md gap-2 text-muted-foreground">
          <ImageOff className="w-8 h-8" />
          <p className="text-sm">No page images available</p>
        </div>
        <div className="flex gap-2 flex-wrap">
          <a href={edocUrl} download>
            <Button variant="outline" size="sm" className="gap-1.5" data-testid="download-edoc-fallback">
              <Download className="w-3.5 h-3.5" />
              Download file
            </Button>
          </a>
          {/^pdf$/i.test(extension || "") && (
            <Button variant="ghost" size="sm" className="gap-1.5" onClick={onSwitchToDoc} data-testid="switch-to-doc">
              <Eye className="w-3.5 h-3.5" />
              Try inline viewer
            </Button>
          )}
        </div>
      </div>
    );
  }

  const totalPages = pageData.pages.length;
  const pageUrl = pageData.pages[currentPage - 1];

  return (
    <div className="space-y-3">
      <div className="border border-border rounded-md overflow-hidden bg-muted/30 flex items-center justify-center min-h-64">
        {imgError ? (
          <div className="flex flex-col items-center justify-center h-64 gap-2 text-muted-foreground">
            <ImageOff className="w-8 h-8" />
            <p className="text-sm">Could not display page {currentPage}</p>
          </div>
        ) : (
          <img
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
            variant="outline" size="sm"
            onClick={() => { setCurrentPage((p) => Math.max(1, p - 1)); setImgError(false); }}
            disabled={currentPage === 1}
            data-testid="page-prev"
          >
            <ChevronLeft className="w-4 h-4" />
          </Button>
          <span className="text-sm text-muted-foreground">Page {currentPage} of {totalPages}</span>
          <Button
            variant="outline" size="sm"
            onClick={() => { setCurrentPage((p) => Math.min(totalPages, p + 1)); setImgError(false); }}
            disabled={currentPage === totalPages}
            data-testid="page-next"
          >
            <ChevronRight className="w-4 h-4" />
          </Button>
        </div>
      )}

      <div className="flex gap-2 flex-wrap border-t border-border pt-2">
        <a href={edocUrl} download>
          <Button variant="outline" size="sm" className="gap-1.5" data-testid="download-edoc-images">
            <Download className="w-3.5 h-3.5" />
            Download
          </Button>
        </a>
        <a href={edocUrl} target="_blank" rel="noreferrer">
          <Button variant="ghost" size="sm" className="gap-1.5" data-testid="open-edoc-tab-images">
            <ExternalLink className="w-3.5 h-3.5" />
            Open in new tab
          </Button>
        </a>
      </div>
    </div>
  );
}

/** Turn a raw backend error into a user-friendly one-liner */
function friendlyError(msg: string): string {
  if (!msg) return "Unknown error";
  if (/not configured/i.test(msg)) return "Laserfiche is not configured. Go to LF Settings to set it up.";
  if (/html page instead of json/i.test(msg) || /Unexpected token.*DOCTYPE/i.test(msg))
    return "The Laserfiche server returned a login/error page. Check your server URL or authentication settings.";
  if (/authentication failed/i.test(msg)) return "Laserfiche authentication failed. Verify your username and password.";
  if (/fetch failed|ECONNREFUSED|ENOTFOUND/i.test(msg)) return "Cannot reach the Laserfiche server. Check the server URL and network.";
  if (/404/i.test(msg)) return "This entry was not found in Laserfiche (may have been moved or deleted).";
  if (msg.length > 120) return msg.slice(0, 120) + "…";
  return msg;
}

function ErrorState({ entryId, error }: { entryId: number; error: Error | null }) {
  const msg = error ? error.message : "";
  const isNotConfigured = /not configured/i.test(msg);
  const isNetworkError = /fetch failed|ECONNREFUSED|ENOTFOUND/i.test(msg);
  const showSettingsLink = isNotConfigured || /html page|authentication failed|ECONN|ENOTFOUND/i.test(msg) || isNetworkError;
  const friendly = friendlyError(msg);

  return (
    <div className="h-full flex flex-col items-center justify-center text-center px-6 gap-4">
      {isNotConfigured || isNetworkError
        ? <ServerOff className="w-14 h-14 text-muted-foreground/30" />
        : <AlertTriangle className="w-14 h-14 text-muted-foreground/30" />
      }
      <div className="space-y-1.5">
        <h2 className="text-lg font-semibold text-foreground">
          {isNotConfigured ? "Laserfiche not configured" : "Could not load document"}
        </h2>
        <p className="text-sm text-muted-foreground max-w-sm">{friendly}</p>
      </div>
      <div className="flex items-center gap-2 flex-wrap justify-center">
        <Link href="/archive">
          <Button variant="outline" size="sm" data-testid="back-to-archive">
            <ArrowLeft className="w-4 h-4 mr-1.5" />
            Back to Archive
          </Button>
        </Link>
        {showSettingsLink && (
          <Link href="/laserfiche/settings">
            <Button variant="default" size="sm" data-testid="go-to-lf-settings">
              <Settings className="w-4 h-4 mr-1.5" />
              LF Settings
            </Button>
          </Link>
        )}
      </div>
      {!isNotConfigured && msg && (
        <details className="max-w-sm w-full text-left">
          <summary className="text-xs text-muted-foreground cursor-pointer flex items-center gap-1">
            <Info className="w-3 h-3" /> Technical details
          </summary>
          <div className="mt-1 bg-muted rounded-md px-3 py-2 text-xs font-mono text-muted-foreground break-all">
            {msg}
          </div>
        </details>
      )}
    </div>
  );
}

export default function LFDocumentPage() {
  const params = useParams<{ entryId: string }>();
  const entryId = Number(params.entryId);

  const { data: doc, isLoading, error } = useQuery<LFDocumentDetail>({
    queryKey: ["/api/document", entryId],
    queryFn: async () => {
      const res = await fetch(`/api/document/${entryId}`);
      if (!res.ok) {
        const ct = res.headers.get("content-type") || "";
        if (/json/i.test(ct)) {
          const body = await res.json().catch(() => ({}));
          throw new Error(body.error || `Failed to load document: ${res.status}`);
        }
        throw new Error(`Server returned non-JSON response (status ${res.status}). Laserfiche may not be reachable.`);
      }
      return res.json();
    },
    enabled: Number.isFinite(entryId) && entryId > 0,
  });

  if (!Number.isFinite(entryId) || entryId <= 0) {
    return <ErrorState entryId={0} error={new Error("Invalid document ID in URL.")} />;
  }

  if (isLoading) return <DocumentDetailSkeleton />;
  if (error || !doc) return <ErrorState entryId={entryId} error={error as Error | null} />;

  const createdDate = doc.createdDate
    ? new Date(doc.createdDate).toLocaleDateString("en-GB", { day: "2-digit", month: "long", year: "numeric" })
    : null;

  const isArabic = (text: string) => /[\u0600-\u06FF]/.test(text);

  return (
    <div className="h-full flex flex-col overflow-hidden">
      {/* Header */}
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

        <div className="flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <div className="flex flex-wrap items-center gap-2 mb-2">
              <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <Hash className="w-3.5 h-3.5" />
                <span>Entry {doc.id}</span>
              </div>
              {doc.extension && (
                <Badge variant="secondary" className="text-xs uppercase" data-testid="doc-extension">
                  {doc.extension}
                </Badge>
              )}
              {doc.pageCount != null && (
                <span className="text-xs text-muted-foreground">
                  {doc.pageCount} {doc.pageCount === 1 ? "page" : "pages"}
                </span>
              )}
            </div>
            <h1
              className={cn(
                "text-xl font-semibold text-foreground leading-tight",
                isArabic(doc.name) && "font-arabic text-right"
              )}
              dir={isArabic(doc.name) ? "rtl" : "ltr"}
              data-testid="doc-title"
            >
              {doc.name}
            </h1>
            {doc.path && doc.path !== doc.name && (
              <p className="text-xs text-muted-foreground mt-1 flex items-center gap-1" data-testid="doc-path">
                <FolderOpen className="w-3 h-3" />
                {doc.path}
              </p>
            )}
          </div>

          {/* Header actions */}
          <div className="flex items-center gap-2 flex-shrink-0">
            <a href={`/api/laserfiche/entries/${entryId}/content`} download>
              <Button variant="outline" size="sm" className="gap-1.5" data-testid="header-download">
                <Download className="w-3.5 h-3.5" />
                Download
              </Button>
            </a>
          </div>
        </div>

        {doc.tags.length > 0 && (
          <div className="flex flex-wrap gap-1.5 mt-3" data-testid="doc-tags">
            {doc.tags.map((tag) => (
              <Badge key={tag} variant="outline" className="text-xs flex items-center gap-1">
                <Tag className="w-3 h-3" />
                {tag}
              </Badge>
            ))}
          </div>
        )}
      </div>

      {/* Body */}
      <div className="flex-1 overflow-auto">
        <div className="max-w-6xl px-6 py-5 flex gap-6">

          {/* Left: Metadata */}
          <div className="w-72 flex-shrink-0 space-y-5">
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
                      <p className="text-sm font-medium">{createdDate}</p>
                    </div>
                  </div>
                )}
                {doc.creator && (
                  <div className="flex items-center gap-3 py-2.5">
                    <User className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                    <div>
                      <p className="text-xs text-muted-foreground">Creator</p>
                      <p className="text-sm font-medium">{doc.creator}</p>
                    </div>
                  </div>
                )}
                {doc.extension && (
                  <div className="flex items-center gap-3 py-2.5">
                    <File className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                    <div>
                      <p className="text-xs text-muted-foreground">File Type</p>
                      <p className="text-sm font-medium uppercase">{doc.extension}</p>
                    </div>
                  </div>
                )}
              </div>
            </div>

            {doc.metadata.length > 0 && (
              <div className="bg-card border border-card-border rounded-md">
                <div className="px-4 py-3 border-b border-border flex items-center gap-2">
                  <Hash className="w-4 h-4 text-primary" />
                  <span className="text-sm font-medium">Metadata Fields</span>
                  <span className="ml-auto text-xs text-muted-foreground">{doc.metadata.length}</span>
                </div>
                <div className="divide-y divide-border px-4" data-testid="metadata-fields">
                  {doc.metadata.map((field) => (
                    <div
                      key={field.fieldId}
                      className="flex items-start justify-between gap-4 py-2.5"
                      data-testid={`metadata-field-${field.fieldId}`}
                    >
                      <span className="text-xs text-muted-foreground flex-shrink-0 w-2/5 pt-0.5">
                        {field.fieldName}
                      </span>
                      <span
                        className={cn(
                          "text-xs text-foreground text-right break-all flex-1",
                          isArabic(field.value) && "font-arabic"
                        )}
                        dir={isArabic(field.value) ? "rtl" : "ltr"}
                      >
                        {field.value}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {doc.metadata.length === 0 && (
              <div className="text-center py-8 text-muted-foreground text-sm border border-dashed border-border rounded-md">
                No metadata fields found.
              </div>
            )}
          </div>

          {/* Right: Document Viewer */}
          <div className="flex-1 min-w-0 space-y-4">
            <div className="bg-card border border-card-border rounded-md">
              <div className="px-4 py-3 border-b border-border flex items-center gap-2">
                <Eye className="w-4 h-4 text-primary" />
                <span className="text-sm font-medium">Document Preview</span>
                {doc.extension && (
                  <Badge variant="outline" className="text-xs uppercase ml-auto">{doc.extension}</Badge>
                )}
              </div>
              <div className="p-4">
                <DocumentViewer entryId={entryId} extension={doc.extension} />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

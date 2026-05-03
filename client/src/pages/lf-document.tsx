import { useParams, Link } from "wouter";
import { useQuery } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  ArrowLeft, FileText, Tag, Calendar, User, FolderOpen,
  Hash, File, ChevronLeft, ChevronRight, AlertTriangle, ImageOff
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
          <div className="w-72 space-y-4">
            <Skeleton className="h-64 w-full rounded-md" />
          </div>
        </div>
      </div>
    </div>
  );
}

function PageViewer({ entryId }: { entryId: number }) {
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
    return (
      <div className="space-y-2">
        <Skeleton className="h-80 w-full rounded-md" />
        <Skeleton className="h-8 w-40 mx-auto rounded-md" />
      </div>
    );
  }

  if (error || !pageData || pageData.pages.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-48 border border-dashed border-border rounded-md gap-2 text-muted-foreground">
        <ImageOff className="w-8 h-8" />
        <p className="text-sm">No pages available for preview</p>
        {error && <p className="text-xs text-red-500">{(error as Error).message}</p>}
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
            variant="outline"
            size="sm"
            onClick={() => { setCurrentPage((p) => Math.max(1, p - 1)); setImgError(false); }}
            disabled={currentPage === 1}
            data-testid="page-prev"
          >
            <ChevronLeft className="w-4 h-4" />
          </Button>
          <span className="text-sm text-muted-foreground">
            Page {currentPage} of {totalPages}
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

export default function LFDocumentPage() {
  const params = useParams<{ entryId: string }>();
  const entryId = Number(params.entryId);

  const { data: doc, isLoading, error } = useQuery<LFDocumentDetail>({
    queryKey: ["/api/document", entryId],
    queryFn: async () => {
      const res = await fetch(`/api/document/${entryId}`);
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.error || `Failed to load document: ${res.status}`);
      }
      return res.json();
    },
    enabled: Number.isFinite(entryId),
  });

  if (isLoading) return <DocumentDetailSkeleton />;

  if (error || !doc) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-center px-6 gap-4">
        <AlertTriangle className="w-14 h-14 text-muted-foreground/30" />
        <h2 className="text-lg font-semibold text-foreground">Document not found</h2>
        <p className="text-sm text-muted-foreground max-w-sm">
          {error ? (error as Error).message : "The requested document could not be loaded from Laserfiche."}
        </p>
        <Link href="/archive">
          <Button variant="outline" data-testid="back-to-archive">
            <ArrowLeft className="w-4 h-4 mr-2" />
            Back to Archive
          </Button>
        </Link>
      </div>
    );
  }

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
        </div>

        {/* Tags */}
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
        <div className="max-w-5xl px-6 py-5 flex gap-6">

          {/* Left: Metadata */}
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

            {/* Metadata fields */}
            {doc.metadata.length > 0 && (
              <div className="bg-card border border-card-border rounded-md">
                <div className="px-4 py-3 border-b border-border flex items-center gap-2">
                  <Hash className="w-4 h-4 text-primary" />
                  <span className="text-sm font-medium">Metadata Fields</span>
                  <span className="ml-auto text-xs text-muted-foreground">{doc.metadata.length} fields</span>
                </div>
                <div className="divide-y divide-border px-4" data-testid="metadata-fields">
                  {doc.metadata.map((field) => (
                    <div
                      key={field.fieldId}
                      className="flex items-start justify-between gap-4 py-2.5"
                      data-testid={`metadata-field-${field.fieldId}`}
                    >
                      <span className="text-xs text-muted-foreground flex-shrink-0 w-1/3 pt-0.5">
                        {field.fieldName}
                      </span>
                      <span
                        className={cn(
                          "text-sm text-foreground text-right break-all flex-1",
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
              <div className="text-center py-10 text-muted-foreground text-sm border border-dashed border-border rounded-md">
                No metadata fields found for this document.
              </div>
            )}
          </div>

          {/* Right: Document Preview */}
          <div className="w-80 flex-shrink-0 space-y-4">
            <div className="bg-card border border-card-border rounded-md">
              <div className="px-4 py-3 border-b border-border flex items-center gap-2">
                <FileText className="w-4 h-4 text-primary" />
                <span className="text-sm font-medium">Document Preview</span>
              </div>
              <div className="p-4">
                <PageViewer entryId={entryId} />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

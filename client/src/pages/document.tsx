import { useParams, Link } from "wouter";
import { useQuery } from "@tanstack/react-query";
import { type Document } from "@shared/schema";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import {
  ArrowLeft, FileText, FileCheck, Scroll, TrendingUp, Shield,
  Building2, Clock, Tag, User, Hash, Files, HardDrive, Calendar,
  Globe, Download, Share2, Eye
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
    case "Active": return "text-emerald-700 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/30";
    case "Approved": return "text-blue-700 dark:text-blue-400 bg-blue-50 dark:bg-blue-950/30";
    case "Published": return "text-emerald-700 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/30";
    case "Completed": return "text-slate-600 dark:text-slate-400 bg-slate-50 dark:bg-slate-950/30";
    case "Under Review": return "text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-950/30";
    case "Closed": return "text-red-700 dark:text-red-400 bg-red-50 dark:bg-red-950/30";
    case "Distributed": return "text-purple-700 dark:text-purple-400 bg-purple-50 dark:bg-purple-950/30";
    default: return "text-muted-foreground bg-muted";
  }
};

function MetaRow({ icon: Icon, label, value, valueDir }: {
  icon: any; label: string; value: string | number | null | undefined; valueDir?: "rtl" | "ltr";
}) {
  if (!value) return null;
  return (
    <div className="flex items-start gap-3 py-2.5">
      <Icon className="w-4 h-4 text-muted-foreground flex-shrink-0 mt-0.5" />
      <div className="flex-1 min-w-0">
        <p className="text-xs text-muted-foreground mb-0.5">{label}</p>
        <p className="text-sm text-foreground font-medium leading-tight" dir={valueDir}>{String(value)}</p>
      </div>
    </div>
  );
}

function DocumentSkeleton() {
  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-4">
        <Skeleton className="h-5 w-32 mb-3" />
        <Skeleton className="h-7 w-2/3 mb-2" />
        <Skeleton className="h-5 w-1/2" />
      </div>
      <div className="flex-1 overflow-auto p-6">
        <div className="max-w-5xl flex gap-6">
          <div className="flex-1 space-y-4">
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-5/6" />
            <Skeleton className="h-4 w-4/5" />
          </div>
          <div className="w-72 space-y-3">
            <Skeleton className="h-32 w-full rounded-md" />
            <Skeleton className="h-48 w-full rounded-md" />
          </div>
        </div>
      </div>
    </div>
  );
}

export default function DocumentPage() {
  const params = useParams();
  const id = params.id;

  const { data: doc, isLoading, error } = useQuery<Document>({
    queryKey: ["/api/documents", id],
    enabled: !!id,
  });

  if (isLoading) return <DocumentSkeleton />;

  if (error || !doc) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-center px-6">
        <FileText className="w-16 h-16 text-muted-foreground/30 mb-4" />
        <h2 className="text-lg font-semibold text-foreground mb-2">Document not found</h2>
        <p className="text-sm text-muted-foreground mb-4">The requested document could not be found in the archive.</p>
        <Link href="/">
          <Button variant="outline">
            <ArrowLeft className="w-4 h-4 mr-2" />
            Back to Search
          </Button>
        </Link>
      </div>
    );
  }

  const isArabicTitle = /[\u0600-\u06FF]/.test(doc.titleAr || "");
  const createdDate = doc.createdAt ? new Date(doc.createdAt).toLocaleDateString("en-GB", { day: "2-digit", month: "long", year: "numeric" }) : "—";

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-4">
        <Link href="/">
          <button className="flex items-center gap-1.5 text-sm text-muted-foreground mb-3 hover:text-foreground transition-colors" data-testid="back-to-search">
            <ArrowLeft className="w-3.5 h-3.5" />
            Back to Search
          </button>
        </Link>

        <div className="flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <div className="flex flex-wrap gap-1.5 mb-2">
              <Badge variant="outline" className={cn("text-xs border", classificationColor(doc.classification))} data-testid="doc-classification">
                <Shield className="w-3 h-3 mr-1" />
                {doc.classification}
              </Badge>
              <Badge variant="outline" className="text-xs" data-testid="doc-security">{doc.securityLevel}</Badge>
              <span className={cn("text-xs px-2 py-0.5 rounded-md font-medium", statusColor(doc.workflowStatus))} data-testid="doc-status">
                {doc.workflowStatus}
              </span>
            </div>
            <h1 className="text-xl font-semibold text-foreground leading-tight mb-1" data-testid="doc-title">{doc.title}</h1>
            {doc.titleAr && (
              <p className="text-base text-muted-foreground font-arabic leading-tight" dir="rtl" data-testid="doc-title-ar">{doc.titleAr}</p>
            )}
          </div>
          <div className="flex items-center gap-2 flex-shrink-0">
            <Button variant="outline" size="sm" data-testid="share-doc">
              <Share2 className="w-4 h-4 mr-1.5" />
              Share
            </Button>
            <Button variant="outline" size="sm" data-testid="download-doc">
              <Download className="w-4 h-4 mr-1.5" />
              Download
            </Button>
          </div>
        </div>
      </div>

      <div className="flex-1 overflow-auto">
        <div className="max-w-5xl px-6 py-5 flex gap-6">
          <div className="flex-1 min-w-0 space-y-5">
            <div className="bg-card border border-card-border rounded-md">
              <div className="px-5 py-3 border-b border-border flex items-center gap-2">
                <Globe className="w-4 h-4 text-primary" />
                <span className="text-sm font-medium">Document Content (English)</span>
              </div>
              <div className="px-5 py-4">
                <p className="text-sm text-foreground leading-relaxed" data-testid="doc-content">{doc.content}</p>
              </div>
            </div>

            {doc.contentAr && (
              <div className="bg-card border border-card-border rounded-md">
                <div className="px-5 py-3 border-b border-border flex items-center gap-2">
                  <Globe className="w-4 h-4 text-primary" />
                  <span className="text-sm font-medium" dir="rtl">محتوى الوثيقة (العربية)</span>
                </div>
                <div className="px-5 py-4">
                  <p className="text-sm text-foreground leading-relaxed font-arabic text-right" dir="rtl" data-testid="doc-content-ar">{doc.contentAr}</p>
                </div>
              </div>
            )}

            {(doc.tags && doc.tags.length > 0) && (
              <div className="bg-card border border-card-border rounded-md px-5 py-4">
                <div className="flex items-center gap-2 mb-3">
                  <Tag className="w-4 h-4 text-muted-foreground" />
                  <span className="text-sm font-medium">Tags</span>
                </div>
                <div className="flex flex-wrap gap-1.5">
                  {doc.tags.map(tag => (
                    <span key={tag} className="text-xs bg-muted text-muted-foreground px-2.5 py-1 rounded-md font-mono" data-testid={`tag-${tag}`}>
                      {tag}
                    </span>
                  ))}
                </div>
              </div>
            )}
          </div>

          <div className="w-72 flex-shrink-0 space-y-4">
            <div className="bg-card border border-card-border rounded-md">
              <div className="px-4 py-3 border-b border-border">
                <span className="text-sm font-medium">Document Details</span>
              </div>
              <div className="px-4 divide-y divide-border">
                <MetaRow icon={Hash} label="Laserfiche ID" value={doc.laserficheId} />
                <MetaRow icon={Building2} label="Department" value={doc.department} />
                {doc.departmentAr && (
                  <MetaRow icon={Globe} label="الجهة" value={doc.departmentAr} valueDir="rtl" />
                )}
                <MetaRow icon={FileText} label="Document Type" value={doc.docType} />
                <MetaRow icon={User} label="Author" value={doc.author} />
                {doc.authorAr && (
                  <MetaRow icon={Globe} label="المؤلف" value={doc.authorAr} valueDir="rtl" />
                )}
                <MetaRow icon={Calendar} label="Created" value={createdDate} />
                <MetaRow icon={Clock} label="Year" value={doc.year} />
              </div>
            </div>

            <div className="bg-card border border-card-border rounded-md">
              <div className="px-4 py-3 border-b border-border">
                <span className="text-sm font-medium">File Information</span>
              </div>
              <div className="px-4 divide-y divide-border">
                <MetaRow icon={Files} label="Pages" value={doc.pageCount ? `${doc.pageCount} pages` : null} />
                <MetaRow
                  icon={HardDrive}
                  label="File Size"
                  value={doc.fileSizeKb ? `${doc.fileSizeKb >= 1024 ? (doc.fileSizeKb / 1024).toFixed(1) + " MB" : doc.fileSizeKb + " KB"}` : null}
                />
              </div>
            </div>

            <div className="bg-card border border-card-border rounded-md px-4 py-3">
              <div className="flex items-center gap-2 mb-2">
                <Eye className="w-4 h-4 text-muted-foreground" />
                <span className="text-sm font-medium">Indexed in</span>
              </div>
              <div className="space-y-1.5">
                <div className="flex items-center justify-between text-xs">
                  <span className="text-muted-foreground">Vector Index</span>
                  <span className="text-emerald-600 font-medium">Active</span>
                </div>
                <div className="flex items-center justify-between text-xs">
                  <span className="text-muted-foreground">Keyword Index</span>
                  <span className="text-emerald-600 font-medium">Active</span>
                </div>
                <div className="flex items-center justify-between text-xs">
                  <span className="text-muted-foreground">Arabic NLP</span>
                  <span className="text-emerald-600 font-medium">Processed</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

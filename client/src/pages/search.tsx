import { useState, useRef, useEffect } from "react";
import { useMutation } from "@tanstack/react-query";
import { apiRequest } from "@/lib/queryClient";
import { type SmartSearchResponse, type UnifiedResult } from "@shared/schema";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { useToast } from "@/hooks/use-toast";
import {
  Search, SlidersHorizontal, FileText, FileCheck, Scroll,
  Clock, Shield, Building2, Tag, ChevronRight, Zap, Brain,
  X, Filter, TrendingUp, AlertCircle, Globe, FolderOpen, Code, Download,
  Sparkles, Layers, Bot, Fingerprint, Calendar
} from "lucide-react";
import { Link } from "wouter";
import { cn } from "@/lib/utils";

const DEPARTMENTS = [
  "Ministry of Finance",
  "Ministry of Public Works",
  "Ministry of Communications",
  "Ministry of Human Resources",
  "Ministry of Digital Economy",
  "Ministry of Environment and Water",
  "National Cybersecurity Authority",
  "General Authority for Government Procurement",
  "Riyadh Municipality",
];

const CLASSIFICATIONS = ["Official", "Confidential", "Top Secret"];
const SECURITY_LEVELS = ["Public", "Internal", "Restricted", "Classified"];
const DOC_TYPES = ["Contract", "Report", "Memo", "Policy", "Tender", "Plan", "Program"];

const EXAMPLE_QUERIES = [
  { text: "معاملات تجديد عقود الصيانة لعام 2023", lang: "ar" },
  { text: "maintenance contract renewal 2023", lang: "en" },
  { text: "جميع العقود لعام 2023", lang: "ar" },
  { text: "budget report infrastructure", lang: "en" },
  { text: "سياسة الموارد البشرية العمل عن بعد", lang: "ar" },
  { text: "all contracts with Ahmed", lang: "en" },
  { text: "تقرير الميزانية السنوية للبنية التحتية", lang: "ar" },
  { text: "digital transformation implementation plan", lang: "en" },
];

const docTypeIcon = (type: string) => {
  switch (type?.toLowerCase()) {
    case "contract": return FileCheck;
    case "report": return FileText;
    case "memo": return Scroll;
    case "plan": return TrendingUp;
    default: return FileText;
  }
};

const securityBadgeVariant = (level: string) => {
  switch (level) {
    case "Classified": return "destructive";
    case "Restricted": return "secondary";
    default: return "outline";
  }
};

const classificationColor = (cls: string) => {
  switch (cls) {
    case "Top Secret": return "text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-950/30 border-red-200 dark:border-red-800";
    case "Confidential": return "text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-950/30 border-amber-200 dark:border-amber-800";
    default: return "text-primary bg-primary/5 border-primary/20";
  }
};

const matchReasonIcon = (reason: string) => {
  switch (reason) {
    case "semantic": return Brain;
    case "keyword": return Search;
    case "metadata": return Tag;
    case "laserfiche": return Bot;
    case "laserfiche-metadata": return Fingerprint;
    case "title-match": return FileText;
    case "year-match": return Calendar;
    default: return Sparkles;
  }
};

const matchReasonLabel = (reason: string) => {
  switch (reason) {
    case "semantic": return "Semantic";
    case "keyword": return "Keyword";
    case "metadata": return "Metadata";
    case "laserfiche": return "Laserfiche";
    case "laserfiche-metadata": return "LF Metadata";
    case "title-match": return "Title";
    case "year-match": return "Year";
    default: return reason;
  }
};

const matchReasonColor = (reason: string) => {
  switch (reason) {
    case "semantic": return "bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-950/30 dark:text-emerald-400 dark:border-emerald-800";
    case "keyword": return "bg-sky-50 text-sky-700 border-sky-200 dark:bg-sky-950/30 dark:text-sky-400 dark:border-sky-800";
    case "metadata": return "bg-violet-50 text-violet-700 border-violet-200 dark:bg-violet-950/30 dark:text-violet-400 dark:border-violet-800";
    case "laserfiche": return "bg-orange-50 text-orange-700 border-orange-200 dark:bg-orange-950/30 dark:text-orange-400 dark:border-orange-800";
    case "laserfiche-metadata": return "bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-950/30 dark:text-amber-400 dark:border-amber-800";
    case "title-match": return "bg-teal-50 text-teal-700 border-teal-200 dark:bg-teal-950/30 dark:text-teal-400 dark:border-teal-800";
    default: return "bg-muted text-muted-foreground border-border";
  }
};


const getLaserficheEntryId = (result: UnifiedResult) => {
  const id = result.laserficheId || result.id.replace(/^lf-/, "");
  return String(id).replace(/^LF-/, "");
};

const getLaserficheViewerRoute = (result: UnifiedResult) => `/lf-document/${getLaserficheEntryId(result)}`;

const logLaserficheNavigation = (result: UnifiedResult, route: string) => {
  console.info("[LaserficheSearch] generated document viewer route", {
    route,
    resultId: result.id,
    laserficheId: result.laserficheId,
    entryId: getLaserficheEntryId(result),
    title: result.title,
  });
};

function ScoreBar({ score, label }: { score: number; label: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className="text-xs text-muted-foreground w-16 shrink-0">{label}</span>
      <div className="flex-1 h-1.5 bg-muted rounded-full overflow-hidden">
        <div className="h-full bg-primary rounded-full transition-all duration-500" style={{ width: `${Math.round(score * 100)}%` }} />
      </div>
      <span className="text-xs font-mono text-muted-foreground w-8 text-right">{Math.round(score * 100)}%</span>
    </div>
  );
}

function MatchReasonBadge({ reason, detail }: { reason: string; detail?: string }) {
  const Icon = matchReasonIcon(reason);
  return (
    <span className={cn("inline-flex items-center gap-1 text-[10px] px-1.5 py-0.5 rounded border", matchReasonColor(reason))}>
      <Icon className="w-3 h-3" />
      {matchReasonLabel(reason)}
      {detail && <span className="opacity-70">· {detail}</span>}
    </span>
  );
}

function LocalResultCard({ result }: { result: UnifiedResult }) {
  const Icon = docTypeIcon(result.docType);
  const isArabicTitle = result.titleAr ? /[\u0600-\u06FF]/.test(result.titleAr) : false;

  return (
    <div className="bg-card border border-card-border rounded-md p-5 hover-elevate transition-all" data-testid={`result-card-${result.id}`}>
      <div className="flex items-start gap-4">
        <div className="w-10 h-10 rounded-md bg-primary/10 flex items-center justify-center flex-shrink-0 mt-0.5">
          <Icon className="w-5 h-5 text-primary" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-start justify-between gap-3 mb-2">
            <div className="flex-1 min-w-0">
              <Link href={`/document/${result.id}`}>
                <h3 className="font-semibold text-foreground leading-tight hover:text-primary transition-colors cursor-pointer line-clamp-1 mb-0.5" data-testid={`result-title-${result.id}`}>
                  {result.title}
                </h3>
              </Link>
              {result.titleAr && (
                <p className="text-sm text-muted-foreground leading-tight line-clamp-1" dir="rtl">{result.titleAr}</p>
              )}
            </div>
            <div className="flex items-center gap-1.5 flex-shrink-0">
              <div className="flex items-center gap-1 bg-primary/10 text-primary text-xs font-mono px-2 py-0.5 rounded-md">
                <TrendingUp className="w-3 h-3" />
                {Math.round(result.score * 100)}%
              </div>
            </div>
          </div>

          <div className="flex flex-wrap gap-1.5 mb-2">
            <Badge variant="outline" className={cn("text-xs border", classificationColor(result.classification))}>
              {result.classification}
            </Badge>
            <Badge variant="outline" className="text-xs"><Shield className="w-3 h-3 mr-1" />{result.securityLevel}</Badge>
            <Badge variant="outline" className="text-xs"><Building2 className="w-3 h-3 mr-1" />{result.department.split(" ").slice(-1)[0]}</Badge>
            <Badge variant="outline" className="text-xs"><Clock className="w-3 h-3 mr-1" />{result.year || "N/A"}</Badge>
            <Badge variant="secondary" className="text-xs">{result.docType}</Badge>
          </div>

          <p className="text-sm text-muted-foreground leading-relaxed mb-3 line-clamp-2">{result.snippet}</p>

          {result.matchReasons.length > 0 && (
            <div className="flex flex-wrap gap-1 mb-3">
              {result.matchReasons.map((m, i) => (
                <MatchReasonBadge key={i} reason={m.reason} detail={m.detail} />
              ))}
            </div>
          )}

          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              {result.laserficheId && <span>{result.laserficheId}</span>}
              {result.pageCount && <span>{result.pageCount} pages</span>}
              {result.fileSizeKb && <span>{(result.fileSizeKb / 1024).toFixed(1)} MB</span>}
            </div>
            <Link href={`/document/${result.id}`}>
              <Button size="sm" variant="outline" className="h-7 text-xs" data-testid={`view-doc-${result.id}`}>
                View <ChevronRight className="w-3 h-3 ml-1" />
              </Button>
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}

function LFResultCard({ result }: { result: UnifiedResult }) {
  const viewerRoute = getLaserficheViewerRoute(result);

  return (
    <div className="bg-card border border-card-border rounded-md p-5 hover:bg-muted/20 transition-colors" data-testid={`result-card-${result.id}`}>
      <div className="flex items-start gap-3">
        <div className="w-10 h-10 rounded-md bg-primary/10 flex items-center justify-center flex-shrink-0">
          <FolderOpen className="w-5 h-5 text-primary" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-start justify-between gap-3 mb-1">
            <div className="flex-1 min-w-0">
              <Link href={viewerRoute}>
                <p
                  className="text-sm font-medium text-foreground truncate hover:text-primary transition-colors cursor-pointer"
                  data-testid={`result-title-${result.id}`}
                  onClick={() => logLaserficheNavigation(result, viewerRoute)}
                >
                  {result.title}
                </p>
              </Link>
              <p className="text-xs text-muted-foreground truncate">{result.snippet}</p>
            </div>
            <div className="flex items-center gap-1 bg-orange-50 text-orange-700 text-xs font-mono px-2 py-0.5 rounded-md dark:bg-orange-950/30 dark:text-orange-400">
              <Bot className="w-3 h-3" />
              {Math.round(result.score * 100)}%
            </div>
          </div>

          <div className="flex flex-wrap gap-1.5 mb-2">
            <Badge variant="outline" className="text-xs"><Building2 className="w-3 h-3 mr-1" />{result.department}</Badge>
            <Badge variant="secondary" className="text-xs">{result.docType}</Badge>
            <Badge variant="outline" className="text-xs">LF #{result.laserficheId}</Badge>
          </div>

          {result.matchReasons.length > 0 && (
            <div className="flex flex-wrap gap-1 mb-2">
              {result.matchReasons.map((m, i) => (
                <MatchReasonBadge key={i} reason={m.reason} detail={m.detail} />
              ))}
            </div>
          )}

          {result.metadata && Object.keys(result.metadata).length > 0 && (
            <div className="mt-2 grid gap-1">
              {Object.entries(result.metadata).slice(0, 4).map(([k, v]) => (
                <p key={k} className="text-xs text-muted-foreground">
                  <span className="font-medium text-foreground">{k}:</span> {v.join(", ") || "-"}
                </p>
              ))}
            </div>
          )}
          <div className="mt-2 flex gap-2">
            <Link href={viewerRoute}>
              <Button
                size="sm"
                variant="outline"
                data-testid={`view-lf-doc-${getLaserficheEntryId(result)}`}
                onClick={() => logLaserficheNavigation(result, viewerRoute)}
              >
                View <ChevronRight className="w-3 h-3 ml-1" />
              </Button>
            </Link>
            {result.previewUrl && <Button size="sm" variant="outline" asChild><a href={result.previewUrl} target="_blank" rel="noreferrer">Preview</a></Button>}
            {result.downloadUrl && <Button size="sm" asChild><a href={result.downloadUrl} target="_blank" rel="noreferrer"><Download className="w-3 h-3 mr-1" />Download</a></Button>}
          </div>
        </div>
      </div>
    </div>
  );
}

function ResultCard({ result }: { result: UnifiedResult }) {
  if (result.type === "laserfiche") {
    return <LFResultCard result={result} />;
  }
  return <LocalResultCard result={result} />;
}

function FilterPanel({ filters, setFilters, onClose }: { filters: any; setFilters: (f: any) => void; onClose: () => void }) {
  const updateFilter = (key: string, value: string) => {
    setFilters((prev: any) => ({ ...prev, [key]: value === "all" ? undefined : value }));
  };
  const activeCount = Object.values(filters).filter(Boolean).length;

  return (
    <div className="bg-card border border-card-border rounded-md p-4 space-y-4">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Filter className="w-4 h-4 text-muted-foreground" />
          <span className="font-medium text-sm">Filters</span>
          {activeCount > 0 && <Badge variant="secondary" className="text-xs">{activeCount} active</Badge>}
        </div>
        <div className="flex items-center gap-1">
          {activeCount > 0 && (
            <Button variant="ghost" size="sm" onClick={() => setFilters({})} className="h-7 text-xs text-muted-foreground px-2" data-testid="clear-filters">Clear all</Button>
          )}
          <Button variant="ghost" size="icon" onClick={onClose} className="h-7 w-7" data-testid="close-filters"><X className="w-3.5 h-3.5" /></Button>
        </div>
      </div>
      <Separator />
      <div className="space-y-3">
        <FilterSelect label="Department" labelAr="الجهة" value={filters.department || "all"} onChange={(v) => updateFilter("department", v)} options={DEPARTMENTS} testId="filter-department" />
        <FilterSelect label="Classification" labelAr="التصنيف" value={filters.classification || "all"} onChange={(v) => updateFilter("classification", v)} options={CLASSIFICATIONS} testId="filter-classification" />
        <FilterSelect label="Security Level" labelAr="مستوى الأمان" value={filters.securityLevel || "all"} onChange={(v) => updateFilter("securityLevel", v)} options={SECURITY_LEVELS} testId="filter-security" />
        <FilterSelect label="Document Type" labelAr="نوع الوثيقة" value={filters.docType || "all"} onChange={(v) => updateFilter("docType", v)} options={DOC_TYPES} testId="filter-doctype" />
        <FilterSelect label="Year" labelAr="السنة" value={filters.yearFrom ? filters.yearFrom.toString() : "all"} onChange={(v) => {
          if (v === "all") { setFilters((prev: any) => ({ ...prev, yearFrom: undefined, yearTo: undefined })); }
          else { setFilters((prev: any) => ({ ...prev, yearFrom: parseInt(v), yearTo: parseInt(v) })); }
        }} options={["2022", "2023", "2024"]} testId="filter-year" />
      </div>
    </div>
  );
}

function FilterSelect({ label, labelAr, value, onChange, options, testId }: { label: string; labelAr: string; value: string; onChange: (v: string) => void; options: string[]; testId: string }) {
  return (
    <div>
      <div className="flex items-baseline justify-between mb-1">
        <label className="text-xs font-medium text-foreground">{label}</label>
        <span className="text-xs text-muted-foreground" dir="rtl">{labelAr}</span>
      </div>
      <Select value={value} onValueChange={onChange}>
        <SelectTrigger className="h-8 text-xs" data-testid={testId}><SelectValue placeholder={`All ${label}s`} /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All {label}s</SelectItem>
          {options.map(o => <SelectItem key={o} value={o}>{o}</SelectItem>)}
        </SelectContent>
      </Select>
    </div>
  );
}

export default function SearchPage() {
  const [query, setQuery] = useState("");
  const [showFilters, setShowFilters] = useState(false);
  const [filters, setFilters] = useState<any>({});
  const [results, setResults] = useState<SmartSearchResponse | null>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const { toast } = useToast();

  const isArabic = /[\u0600-\u06FF]/.test(query);
  const activeFilterCount = Object.values(filters).filter(Boolean).length;

  const smartSearchMutation = useMutation({
    mutationFn: async () => {
      const res = await apiRequest("POST", "/api/smart-search", {
        query: query.trim(),
        filters: Object.keys(filters).length > 0 ? filters : undefined,
        page: 1,
        limit: 10,
      });
      return res.json() as Promise<SmartSearchResponse>;
    },
    onSuccess: (data) => setResults(data),
    onError: () => toast({ title: "Search failed", description: "Please try again.", variant: "destructive" }),
  });

  const handleSearch = () => {
    if (!query.trim()) return;
    smartSearchMutation.mutate();
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSearch();
    }
  };

  const handleExampleClick = (q: string) => {
    setQuery(q);
    setTimeout(() => inputRef.current?.focus(), 0);
  };

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-5">
        <div className="max-w-4xl">
          <div className="mb-4">
            <h1 className="text-xl font-semibold text-foreground">Smart Search</h1>
            <p className="text-sm text-muted-foreground mt-0.5" dir="rtl">البحث الذكي في أرشيف المستندات الحكومية</p>
          </div>

          <div className="relative">
            <div className={cn("flex items-start gap-3 bg-card border-2 rounded-md px-4 py-3 transition-colors", "focus-within:border-primary border-card-border")}>
              <div className="flex-shrink-0 mt-0.5">
                {isArabic ? (
                  <Globe className="w-5 h-5 text-primary" />
                ) : (
                  <Search className="w-5 h-5 text-muted-foreground" />
                )}
              </div>
              <textarea
                ref={inputRef}
                value={query}
                onChange={e => setQuery(e.target.value)}
                onKeyDown={handleKeyDown}
                dir={isArabic ? "rtl" : "ltr"}
                placeholder="Search anything — documents, departments, years, topics... | ابحث بأي شكل — وثائق، جهات، سنوات، موضوعات..."
                className={cn("flex-1 bg-transparent text-foreground placeholder:text-muted-foreground resize-none outline-none text-sm leading-relaxed min-h-[52px] max-h-[120px]", isArabic && "font-arabic text-base")}
                rows={2}
                data-testid="search-input"
              />
              <div className="flex items-end gap-2 flex-shrink-0">
                <button
                  onClick={() => setShowFilters(!showFilters)}
                  data-testid="toggle-filters"
                  className={cn("flex items-center gap-1.5 h-8 px-2.5 rounded-md text-xs font-medium border transition-colors", showFilters || activeFilterCount > 0 ? "bg-primary/10 text-primary border-primary/30" : "bg-muted text-muted-foreground border-transparent hover-elevate")}
                >
                  <SlidersHorizontal className="w-3.5 h-3.5" />
                  {activeFilterCount > 0 && <span className="text-xs bg-primary text-primary-foreground rounded-full w-4 h-4 flex items-center justify-center">{activeFilterCount}</span>}
                </button>
                <Button onClick={handleSearch} disabled={!query.trim() || smartSearchMutation.isPending} className="h-8" data-testid="search-button">
                  {smartSearchMutation.isPending ? (
                    <span className="flex items-center gap-1.5">
                      <span className="w-3 h-3 border-2 border-primary-foreground/30 border-t-primary-foreground rounded-full animate-spin" />
                      Searching...
                    </span>
                  ) : (
                    <span className="flex items-center gap-1.5"><Sparkles className="w-3.5 h-3.5" />Search</span>
                  )}
                </Button>
              </div>
            </div>

            {query && (
              <button
                onClick={() => { setQuery(""); setResults(null); }}
                className="absolute right-[140px] top-3 text-muted-foreground hover:text-foreground transition-colors"
                data-testid="clear-query"
              >
                <X className="w-4 h-4" />
              </button>
            )}
          </div>

          {!results && !smartSearchMutation.isPending && (
            <div className="mt-3 flex flex-wrap gap-2">
              <span className="text-xs text-muted-foreground self-center">Try:</span>
              {EXAMPLE_QUERIES.map(eq => (
                <button
                  key={eq.text}
                  onClick={() => handleExampleClick(eq.text)}
                  data-testid={`example-query-${eq.lang}`}
                  className={cn("text-xs px-2.5 py-1 rounded-md border border-border bg-card text-muted-foreground hover-elevate transition-colors", eq.lang === "ar" && "font-arabic")}
                  dir={eq.lang === "ar" ? "rtl" : "ltr"}
                >
                  {eq.text}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>

      <div className="flex-1 overflow-auto">
        <div className="max-w-5xl px-6 py-5 flex gap-5">
          {showFilters && (
            <div className="w-64 flex-shrink-0">
              <FilterPanel filters={filters} setFilters={setFilters} onClose={() => setShowFilters(false)} />
            </div>
          )}

          <div className="flex-1 min-w-0">
            {smartSearchMutation.isPending && (
              <div className="space-y-4">
                <div className="flex items-center gap-3 mb-2">
                  <span className="w-4 h-4 border-2 border-primary/30 border-t-primary rounded-full animate-spin" />
                  <span className="text-sm text-muted-foreground">Smart search running...</span>
                </div>
                {[1, 2, 3].map(i => (
                  <div key={i} className="bg-card border border-card-border rounded-md p-5">
                    <div className="flex gap-4">
                      <Skeleton className="w-10 h-10 rounded-md flex-shrink-0" />
                      <div className="flex-1 space-y-2">
                        <Skeleton className="h-4 w-3/4" />
                        <Skeleton className="h-3 w-1/2" />
                        <div className="flex gap-1.5 py-1"><Skeleton className="h-5 w-20 rounded-full" /><Skeleton className="h-5 w-16 rounded-full" /><Skeleton className="h-5 w-24 rounded-full" /></div>
                        <Skeleton className="h-3 w-full" />
                        <Skeleton className="h-3 w-4/5" />
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}

            {results && !smartSearchMutation.isPending && (
              <div>
                <div className="flex items-center justify-between gap-3 mb-4">
                  <div>
                    <p className="text-sm text-foreground font-medium">
                      {results.total} results for{" "}
                      <span className="text-primary font-semibold" dir={results.queryLanguage === "ar" ? "rtl" : "ltr"}>"{results.query}"</span>
                    </p>
                    <div className="flex items-center gap-2 mt-1">
                      <span className="text-xs text-muted-foreground flex items-center gap-1">
                        <Layers className="inline w-3 h-3" />{results.processingTimeMs}ms
                      </span>
                      <span className="text-xs text-muted-foreground">·</span>
                      <span className="text-xs text-muted-foreground">Intent: {results.intent}</span>
                      <span className="text-xs text-muted-foreground">·</span>
                      <span className="text-xs text-muted-foreground">{results.enginesUsed.join(" + ")}</span>
                      {results.lfConnected && (
                        <Badge variant="outline" className="text-[10px] h-4 px-1 border-orange-200 text-orange-700 dark:border-orange-800 dark:text-orange-400">
                          <Bot className="w-2.5 h-2.5 mr-0.5" />LF connected
                        </Badge>
                      )}
                    </div>
                    {results.laserficheCommand && (
                      <div className="flex items-center gap-1.5 mt-1">
                        <Code className="w-3 h-3 text-muted-foreground" />
                        <code className="text-xs text-primary font-mono">{results.laserficheCommand}</code>
                      </div>
                    )}
                  </div>
                  {results.total === 0 && <Badge variant="outline" className="text-xs">No matches</Badge>}
                </div>

                {results.total === 0 ? (
                  <div className="flex flex-col items-center justify-center py-16 text-center">
                    <AlertCircle className="w-12 h-12 text-muted-foreground/30 mb-4" />
                    <h3 className="font-medium text-foreground mb-1">No documents found</h3>
                    <p className="text-sm text-muted-foreground max-w-sm">Try different search terms or adjust filters.</p>
                    <Button variant="outline" size="sm" className="mt-4" onClick={() => { setQuery(""); setResults(null); }}>Clear search</Button>
                  </div>
                ) : (
                  <div className="space-y-3">
                    {results.results.map((result) => (
                      <ResultCard key={result.id} result={result} />
                    ))}
                  </div>
                )}
              </div>
            )}

            {!results && !smartSearchMutation.isPending && (
              <div className="flex flex-col items-center justify-center py-20 text-center">
                <div className="w-16 h-16 rounded-full bg-primary/10 flex items-center justify-center mb-4">
                  <Sparkles className="w-8 h-8 text-primary" />
                </div>
                <h3 className="font-semibold text-foreground text-lg mb-2">Smart AI Search</h3>
                <p className="text-sm text-muted-foreground max-w-md mb-2">
                  Type anything in Arabic or English. Our AI automatically routes your query to the best search engine
                  — semantic, keyword, metadata, or Laserfiche — and shows you why each result matched.
                </p>
                <p className="text-sm text-muted-foreground font-arabic" dir="rtl">
                  اكتب أي شيء بالعربية أو الإنجليزية. يوجد البحث الذكي استفسارك تلقائياً للمحرك المناسب
                </p>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

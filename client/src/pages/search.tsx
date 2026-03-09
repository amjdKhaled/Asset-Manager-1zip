import { useState, useRef, useEffect } from "react";
import { useMutation } from "@tanstack/react-query";
import { apiRequest, queryClient } from "@/lib/queryClient";
import { type SearchResponse, type SearchResult } from "@shared/schema";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { useToast } from "@/hooks/use-toast";
import {
  Search, SlidersHorizontal, FileText, FileCheck, FileX, Scroll,
  Clock, Shield, Building2, Tag, ChevronRight, Zap, Brain, Layers,
  X, Filter, TrendingUp, AlertCircle, Globe
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
const SEARCH_TYPES = [
  { value: "hybrid", label: "Hybrid Search", labelAr: "بحث مختلط", icon: Layers, description: "Combines semantic + keyword" },
  { value: "semantic", label: "Semantic Search", labelAr: "بحث دلالي", icon: Brain, description: "AI meaning-based matching" },
  { value: "keyword", label: "Keyword Search", labelAr: "بحث نصي", icon: Search, description: "Exact term matching" },
];

const EXAMPLE_QUERIES = [
  { text: "معاملات تجديد عقود الصيانة لعام 2023", lang: "ar" },
  { text: "maintenance contract renewal 2023", lang: "en" },
  { text: "تقرير الميزانية السنوية للبنية التحتية", lang: "ar" },
  { text: "digital transformation implementation plan", lang: "en" },
  { text: "سياسة الموارد البشرية العمل عن بعد", lang: "ar" },
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

function ScoreBar({ score, label }: { score: number; label: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className="text-xs text-muted-foreground w-16 shrink-0">{label}</span>
      <div className="flex-1 h-1.5 bg-muted rounded-full overflow-hidden">
        <div
          className="h-full bg-primary rounded-full transition-all duration-500"
          style={{ width: `${Math.round(score * 100)}%` }}
        />
      </div>
      <span className="text-xs font-mono text-muted-foreground w-8 text-right">{Math.round(score * 100)}%</span>
    </div>
  );
}

function ResultCard({ result, index }: { result: SearchResult; index: number }) {
  const [expanded, setExpanded] = useState(false);
  const Icon = docTypeIcon(result.document.docType);
  const isArabicTitle = /[\u0600-\u06FF]/.test(result.document.titleAr || "");

  return (
    <div
      className="bg-card border border-card-border rounded-md p-5 hover-elevate transition-all"
      data-testid={`result-card-${result.document.id}`}
    >
      <div className="flex items-start gap-4">
        <div className="w-10 h-10 rounded-md bg-primary/10 flex items-center justify-center flex-shrink-0 mt-0.5">
          <Icon className="w-5 h-5 text-primary" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-start justify-between gap-3 mb-2">
            <div className="flex-1 min-w-0">
              <Link href={`/document/${result.document.id}`}>
                <h3 className="font-semibold text-foreground leading-tight hover:text-primary transition-colors cursor-pointer line-clamp-1 mb-0.5" data-testid={`result-title-${result.document.id}`}>
                  {result.document.title}
                </h3>
              </Link>
              {result.document.titleAr && (
                <p className="text-sm text-muted-foreground leading-tight line-clamp-1" dir="rtl">
                  {result.document.titleAr}
                </p>
              )}
            </div>
            <div className="flex items-center gap-1.5 flex-shrink-0">
              <div className="flex items-center gap-1 bg-primary/10 text-primary text-xs font-mono px-2 py-0.5 rounded-md">
                <TrendingUp className="w-3 h-3" />
                {Math.round(result.score * 100)}%
              </div>
            </div>
          </div>

          <div className="flex flex-wrap gap-1.5 mb-3">
            <Badge variant="outline" className={cn("text-xs border", classificationColor(result.document.classification))}>
              {result.document.classification}
            </Badge>
            <Badge variant="outline" className="text-xs">
              <Shield className="w-3 h-3 mr-1" />
              {result.document.securityLevel}
            </Badge>
            <Badge variant="outline" className="text-xs">
              <Building2 className="w-3 h-3 mr-1" />
              {result.document.department.split(" ").slice(-1)[0]}
            </Badge>
            <Badge variant="outline" className="text-xs">
              <Clock className="w-3 h-3 mr-1" />
              {result.document.year || new Date(result.document.createdAt || "").getFullYear()}
            </Badge>
            <Badge variant="secondary" className="text-xs">{result.document.docType}</Badge>
          </div>

          <p className="text-sm text-muted-foreground leading-relaxed mb-3 line-clamp-2">
            {result.snippet}
          </p>

          {result.matchedTerms.length > 0 && (
            <div className="flex flex-wrap gap-1 mb-3">
              {result.matchedTerms.slice(0, 5).map(term => (
                <span key={term} className="text-xs bg-primary/10 text-primary px-1.5 py-0.5 rounded font-mono">
                  {term}
                </span>
              ))}
            </div>
          )}

          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              <span>{result.document.laserficheId}</span>
              {result.document.pageCount && <span>{result.document.pageCount} pages</span>}
              {result.document.fileSizeKb && <span>{(result.document.fileSizeKb / 1024).toFixed(1)} MB</span>}
            </div>
            <div className="flex items-center gap-2">
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setExpanded(!expanded)}
                data-testid={`expand-scores-${result.document.id}`}
                className="h-7 px-2 text-xs text-muted-foreground"
              >
                Score Details
                <ChevronRight className={cn("w-3 h-3 ml-1 transition-transform", expanded && "rotate-90")} />
              </Button>
              <Link href={`/document/${result.document.id}`}>
                <Button size="sm" variant="outline" className="h-7 text-xs" data-testid={`view-doc-${result.document.id}`}>
                  View
                  <ChevronRight className="w-3 h-3 ml-1" />
                </Button>
              </Link>
            </div>
          </div>

          {expanded && (
            <div className="mt-3 pt-3 border-t border-border space-y-1.5">
              <ScoreBar score={result.scoreBreakdown.semantic} label="Semantic" />
              <ScoreBar score={result.scoreBreakdown.keyword} label="Keyword" />
              <ScoreBar score={result.scoreBreakdown.metadata} label="Metadata" />
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function FilterPanel({ filters, setFilters, onClose }: {
  filters: any;
  setFilters: (f: any) => void;
  onClose: () => void;
}) {
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
          {activeCount > 0 && (
            <Badge variant="secondary" className="text-xs">{activeCount} active</Badge>
          )}
        </div>
        <div className="flex items-center gap-1">
          {activeCount > 0 && (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setFilters({})}
              className="h-7 text-xs text-muted-foreground px-2"
              data-testid="clear-filters"
            >
              Clear all
            </Button>
          )}
          <Button variant="ghost" size="icon" onClick={onClose} className="h-7 w-7" data-testid="close-filters">
            <X className="w-3.5 h-3.5" />
          </Button>
        </div>
      </div>

      <Separator />

      <div className="space-y-3">
        <FilterSelect
          label="Department"
          labelAr="الجهة"
          value={filters.department || "all"}
          onChange={(v) => updateFilter("department", v)}
          options={DEPARTMENTS}
          testId="filter-department"
        />
        <FilterSelect
          label="Classification"
          labelAr="التصنيف"
          value={filters.classification || "all"}
          onChange={(v) => updateFilter("classification", v)}
          options={CLASSIFICATIONS}
          testId="filter-classification"
        />
        <FilterSelect
          label="Security Level"
          labelAr="مستوى الأمان"
          value={filters.securityLevel || "all"}
          onChange={(v) => updateFilter("securityLevel", v)}
          options={SECURITY_LEVELS}
          testId="filter-security"
        />
        <FilterSelect
          label="Document Type"
          labelAr="نوع الوثيقة"
          value={filters.docType || "all"}
          onChange={(v) => updateFilter("docType", v)}
          options={DOC_TYPES}
          testId="filter-doctype"
        />
        <FilterSelect
          label="Year"
          labelAr="السنة"
          value={filters.yearFrom ? filters.yearFrom.toString() : "all"}
          onChange={(v) => {
            if (v === "all") {
              setFilters((prev: any) => ({ ...prev, yearFrom: undefined, yearTo: undefined }));
            } else {
              setFilters((prev: any) => ({ ...prev, yearFrom: parseInt(v), yearTo: parseInt(v) }));
            }
          }}
          options={["2022", "2023", "2024"]}
          testId="filter-year"
        />
      </div>
    </div>
  );
}

function FilterSelect({ label, labelAr, value, onChange, options, testId }: {
  label: string; labelAr: string; value: string;
  onChange: (v: string) => void; options: string[]; testId: string;
}) {
  return (
    <div>
      <div className="flex items-baseline justify-between mb-1">
        <label className="text-xs font-medium text-foreground">{label}</label>
        <span className="text-xs text-muted-foreground" dir="rtl">{labelAr}</span>
      </div>
      <Select value={value} onValueChange={onChange}>
        <SelectTrigger className="h-8 text-xs" data-testid={testId}>
          <SelectValue placeholder={`All ${label}s`} />
        </SelectTrigger>
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
  const [searchType, setSearchType] = useState<"hybrid" | "semantic" | "keyword">("hybrid");
  const [showFilters, setShowFilters] = useState(false);
  const [filters, setFilters] = useState<any>({});
  const [results, setResults] = useState<SearchResponse | null>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const { toast } = useToast();

  const isArabic = /[\u0600-\u06FF]/.test(query);
  const activeFilterCount = Object.values(filters).filter(Boolean).length;

  const searchMutation = useMutation({
    mutationFn: async () => {
      const res = await apiRequest("POST", "/api/search", {
        query: query.trim(),
        searchType,
        filters: Object.keys(filters).length > 0 ? filters : undefined,
        page: 1,
        limit: 10,
      });
      return res.json() as Promise<SearchResponse>;
    },
    onSuccess: (data) => {
      setResults(data);
      queryClient.invalidateQueries({ queryKey: ["/api/audit-logs"] });
    },
    onError: () => {
      toast({ title: "Search failed", description: "Please try again.", variant: "destructive" });
    },
  });

  const handleSearch = () => {
    if (!query.trim()) return;
    searchMutation.mutate();
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSearch();
    }
  };

  const handleExampleClick = (q: string) => {
    setQuery(q);
    setTimeout(() => {
      if (inputRef.current) {
        inputRef.current.focus();
      }
    }, 0);
  };

  const selectedSearchType = SEARCH_TYPES.find(s => s.value === searchType)!;
  const SearchTypeIcon = selectedSearchType.icon;

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-5">
        <div className="max-w-4xl">
          <div className="mb-4">
            <h1 className="text-xl font-semibold text-foreground">Semantic Document Search</h1>
            <p className="text-sm text-muted-foreground mt-0.5" dir="rtl">البحث الدلالي في أرشيف المستندات الحكومية</p>
          </div>

          <div className="flex gap-2 mb-3">
            {SEARCH_TYPES.map(st => {
              const Icon = st.icon;
              return (
                <button
                  key={st.value}
                  onClick={() => setSearchType(st.value as any)}
                  data-testid={`search-type-${st.value}`}
                  className={cn(
                    "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium transition-colors border",
                    searchType === st.value
                      ? "bg-primary text-primary-foreground border-primary"
                      : "bg-card text-muted-foreground border-card-border hover-elevate"
                  )}
                >
                  <Icon className="w-3.5 h-3.5" />
                  {st.label}
                </button>
              );
            })}
          </div>

          <div className="relative">
            <div className={cn(
              "flex items-start gap-3 bg-card border-2 rounded-md px-4 py-3 transition-colors",
              "focus-within:border-primary border-card-border"
            )}>
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
                placeholder="Search documents in Arabic or English... | ابحث بالعربية أو الإنجليزية"
                className={cn(
                  "flex-1 bg-transparent text-foreground placeholder:text-muted-foreground resize-none outline-none text-sm leading-relaxed min-h-[52px] max-h-[120px]",
                  isArabic && "font-arabic text-base"
                )}
                rows={2}
                data-testid="search-input"
              />
              <div className="flex items-end gap-2 flex-shrink-0">
                <button
                  onClick={() => setShowFilters(!showFilters)}
                  data-testid="toggle-filters"
                  className={cn(
                    "flex items-center gap-1.5 h-8 px-2.5 rounded-md text-xs font-medium border transition-colors",
                    showFilters || activeFilterCount > 0
                      ? "bg-primary/10 text-primary border-primary/30"
                      : "bg-muted text-muted-foreground border-transparent hover-elevate"
                  )}
                >
                  <SlidersHorizontal className="w-3.5 h-3.5" />
                  {activeFilterCount > 0 && <span className="text-xs bg-primary text-primary-foreground rounded-full w-4 h-4 flex items-center justify-center">{activeFilterCount}</span>}
                </button>
                <Button
                  onClick={handleSearch}
                  disabled={!query.trim() || searchMutation.isPending}
                  className="h-8"
                  data-testid="search-button"
                >
                  {searchMutation.isPending ? (
                    <span className="flex items-center gap-1.5">
                      <span className="w-3 h-3 border-2 border-primary-foreground/30 border-t-primary-foreground rounded-full animate-spin" />
                      Searching...
                    </span>
                  ) : (
                    <span className="flex items-center gap-1.5">
                      <Zap className="w-3.5 h-3.5" />
                      Search
                    </span>
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

          {!results && !searchMutation.isPending && (
            <div className="mt-3 flex flex-wrap gap-2">
              <span className="text-xs text-muted-foreground self-center">Try:</span>
              {EXAMPLE_QUERIES.map(eq => (
                <button
                  key={eq.text}
                  onClick={() => handleExampleClick(eq.text)}
                  data-testid={`example-query-${eq.lang}`}
                  className={cn(
                    "text-xs px-2.5 py-1 rounded-md border border-border bg-card text-muted-foreground hover-elevate transition-colors",
                    eq.lang === "ar" && "font-arabic"
                  )}
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
            {searchMutation.isPending && (
              <div className="space-y-4">
                <div className="flex items-center gap-3 mb-2">
                  <span className="w-4 h-4 border-2 border-primary/30 border-t-primary rounded-full animate-spin" />
                  <span className="text-sm text-muted-foreground">Running {selectedSearchType.label.toLowerCase()}...</span>
                </div>
                {[1, 2, 3].map(i => (
                  <div key={i} className="bg-card border border-card-border rounded-md p-5">
                    <div className="flex gap-4">
                      <Skeleton className="w-10 h-10 rounded-md flex-shrink-0" />
                      <div className="flex-1 space-y-2">
                        <Skeleton className="h-4 w-3/4" />
                        <Skeleton className="h-3 w-1/2" />
                        <div className="flex gap-1.5 py-1">
                          <Skeleton className="h-5 w-20 rounded-full" />
                          <Skeleton className="h-5 w-16 rounded-full" />
                          <Skeleton className="h-5 w-24 rounded-full" />
                        </div>
                        <Skeleton className="h-3 w-full" />
                        <Skeleton className="h-3 w-4/5" />
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}

            {results && !searchMutation.isPending && (
              <div>
                <div className="flex items-center justify-between gap-3 mb-4">
                  <div>
                    <p className="text-sm text-foreground font-medium">
                      {results.total} results for{" "}
                      <span className="text-primary font-semibold" dir={isArabic ? "rtl" : "ltr"}>"{results.query}"</span>
                    </p>
                    <p className="text-xs text-muted-foreground mt-0.5">
                      <SearchTypeIcon className="inline w-3 h-3 mr-1" />
                      {selectedSearchType.label} · {results.processingTimeMs}ms
                    </p>
                  </div>
                  {results.total === 0 && (
                    <Badge variant="outline" className="text-xs">No matches</Badge>
                  )}
                </div>

                {results.total === 0 ? (
                  <div className="flex flex-col items-center justify-center py-16 text-center">
                    <AlertCircle className="w-12 h-12 text-muted-foreground/30 mb-4" />
                    <h3 className="font-medium text-foreground mb-1">No documents found</h3>
                    <p className="text-sm text-muted-foreground max-w-sm">
                      Try different search terms or switch to Hybrid search for broader coverage.
                    </p>
                    <Button variant="outline" size="sm" className="mt-4" onClick={() => { setQuery(""); setResults(null); }}>
                      Clear search
                    </Button>
                  </div>
                ) : (
                  <div className="space-y-3">
                    {results.results.map((result, index) => (
                      <ResultCard key={result.document.id} result={result} index={index} />
                    ))}
                  </div>
                )}
              </div>
            )}

            {!results && !searchMutation.isPending && (
              <div className="flex flex-col items-center justify-center py-20 text-center">
                <div className="w-16 h-16 rounded-full bg-primary/10 flex items-center justify-center mb-4">
                  <Brain className="w-8 h-8 text-primary" />
                </div>
                <h3 className="font-semibold text-foreground text-lg mb-2">AI-Powered Semantic Search</h3>
                <p className="text-sm text-muted-foreground max-w-md mb-2">
                  Search across government archives using natural language in Arabic or English.
                  The system understands meaning, not just keywords.
                </p>
                <p className="text-sm text-muted-foreground font-arabic" dir="rtl">
                  ابحث في الأرشيف الحكومي بالعربية أو الإنجليزية
                </p>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

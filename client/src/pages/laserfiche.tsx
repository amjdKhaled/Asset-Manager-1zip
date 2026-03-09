import { useState } from "react";
import { useQuery, useMutation } from "@tanstack/react-query";
import { apiRequest, queryClient } from "@/lib/queryClient";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { Skeleton } from "@/components/ui/skeleton";
import { useToast } from "@/hooks/use-toast";
import {
  Database, CheckCircle, XCircle, AlertCircle, RefreshCw, Search,
  FileText, FolderOpen, ArrowRight, Zap, Code, Globe, Shield,
  Download, ChevronRight, Copy, Check, Layers, Brain
} from "lucide-react";
import { cn } from "@/lib/utils";

type LFStatus = {
  connected: boolean;
  configured: boolean;
  serverUrl?: string;
  repositoryId?: string;
  username?: string;
  message: string;
};

type LFEntry = {
  id: number;
  name: string;
  entryType: string;
  fullPath: string;
  creator: string;
  creationTime?: string;
  extension?: string;
  pageCount?: number;
};

type LFSearchResult = {
  entries: LFEntry[];
  total: number;
  searchCommand: string;
  nlTranslation: { command: string; explanation: string; extractedTerms: string[] };
  query: string;
};

type NLTranslation = {
  command: string;
  explanation: string;
  extractedTerms: string[];
};

const EXAMPLE_NL_QUERIES = [
  { text: "عطني جميع المعاملات اللتي تحتوي على اسم سلمان", lang: "ar" },
  { text: "جميع العقود لعام 2023", lang: "ar" },
  { text: "المعاملات المتعلقة بالصيانة", lang: "ar" },
  { text: "all contracts with Ahmed", lang: "en" },
  { text: "documents from 2022 about budget", lang: "en" },
];

function ConfigStep({ num, title, titleAr, content }: { num: number; title: string; titleAr: string; content: React.ReactNode }) {
  return (
    <div className="flex gap-4">
      <div className="w-7 h-7 rounded-full bg-primary text-primary-foreground flex items-center justify-center text-xs font-bold flex-shrink-0 mt-0.5">
        {num}
      </div>
      <div className="flex-1 min-w-0">
        <p className="text-sm font-semibold text-foreground">{title}</p>
        <p className="text-xs text-muted-foreground font-arabic mb-2" dir="rtl">{titleAr}</p>
        {content}
      </div>
    </div>
  );
}

function CodeBlock({ code, label }: { code: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  const copy = () => {
    navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };
  return (
    <div className="relative group">
      {label && <span className="text-xs text-muted-foreground mb-1 block">{label}</span>}
      <div className="bg-muted rounded-md px-3 py-2 font-mono text-xs text-foreground overflow-x-auto pr-8">
        {code}
      </div>
      <button
        onClick={copy}
        className="absolute right-2 top-1/2 -translate-y-1/2 opacity-0 group-hover:opacity-100 transition-opacity p-1 rounded text-muted-foreground hover:text-foreground"
      >
        {copied ? <Check className="w-3 h-3 text-emerald-500" /> : <Copy className="w-3 h-3" />}
      </button>
    </div>
  );
}

export default function LaserfichePage() {
  const [nlQuery, setNlQuery] = useState("");
  const [lfQuery, setLfQuery] = useState("");
  const [searchMode, setSearchMode] = useState<"nl" | "lf">("nl");
  const { toast } = useToast();

  const { data: status, isLoading: statusLoading, refetch: refetchStatus } = useQuery<LFStatus>({
    queryKey: ["/api/laserfiche/status"],
  });

  const translateMutation = useMutation({
    mutationFn: async (q: string) => {
      const res = await apiRequest("POST", "/api/laserfiche/translate", { query: q });
      return res.json() as Promise<NLTranslation>;
    },
  });

  const searchMutation = useMutation({
    mutationFn: async (params: { query?: string; searchCommand?: string }) => {
      const res = await apiRequest("POST", "/api/laserfiche/search", params);
      return res.json() as Promise<LFSearchResult>;
    },
    onError: (err: any) => {
      toast({ title: "Search failed", description: err.message, variant: "destructive" });
    },
  });

  const syncMutation = useMutation({
    mutationFn: async () => {
      const res = await apiRequest("POST", "/api/laserfiche/sync", { folderId: 1, limit: 50 });
      return res.json();
    },
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ["/api/documents"] });
      toast({ title: `Sync complete`, description: `Imported ${data.imported} documents from Laserfiche.` });
    },
    onError: (err: any) => {
      toast({ title: "Sync failed", description: err.message, variant: "destructive" });
    },
  });

  const handleSearch = () => {
    if (searchMode === "nl" && nlQuery.trim()) {
      searchMutation.mutate({ query: nlQuery.trim() });
    } else if (searchMode === "lf" && lfQuery.trim()) {
      searchMutation.mutate({ searchCommand: lfQuery.trim(), query: lfQuery.trim() });
    }
  };

  const handleTranslate = () => {
    if (nlQuery.trim()) {
      translateMutation.mutate(nlQuery.trim());
    }
  };

  const isArabic = /[\u0600-\u06FF]/.test(nlQuery);

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-2 mb-1">
              <Database className="w-5 h-5 text-primary" />
              <h1 className="text-xl font-semibold text-foreground">Laserfiche Integration</h1>
            </div>
            <p className="text-sm text-muted-foreground font-arabic" dir="rtl">ربط منصة البحث بنظام Laserfiche</p>
          </div>
          <div className="flex items-center gap-2">
            {statusLoading ? (
              <Skeleton className="h-7 w-28 rounded-full" />
            ) : status?.connected ? (
              <Badge variant="outline" className="text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/30 border-emerald-200 gap-1.5">
                <CheckCircle className="w-3 h-3" />
                Connected
              </Badge>
            ) : status?.configured ? (
              <Badge variant="outline" className="text-red-600 border-red-200 gap-1.5">
                <XCircle className="w-3 h-3" />
                Auth Failed
              </Badge>
            ) : (
              <Badge variant="outline" className="text-amber-600 border-amber-200 gap-1.5">
                <AlertCircle className="w-3 h-3" />
                Not Configured
              </Badge>
            )}
            <Button variant="outline" size="sm" onClick={() => refetchStatus()} data-testid="refresh-status">
              <RefreshCw className="w-3.5 h-3.5 mr-1.5" />
              Refresh
            </Button>
            {status?.connected && (
              <Button
                size="sm"
                onClick={() => syncMutation.mutate()}
                disabled={syncMutation.isPending}
                data-testid="sync-laserfiche"
              >
                <Download className="w-3.5 h-3.5 mr-1.5" />
                {syncMutation.isPending ? "Syncing..." : "Sync Documents"}
              </Button>
            )}
          </div>
        </div>
      </div>

      <div className="flex-1 overflow-auto">
        <div className="max-w-5xl px-6 py-5">
          <Tabs defaultValue={status?.connected ? "search" : "setup"}>
            <TabsList className="mb-5">
              <TabsTrigger value="search" data-testid="tab-search">
                <Search className="w-3.5 h-3.5 mr-1.5" />
                NL Search
              </TabsTrigger>
              <TabsTrigger value="translate" data-testid="tab-translate">
                <Brain className="w-3.5 h-3.5 mr-1.5" />
                Query Translator
              </TabsTrigger>
              <TabsTrigger value="setup" data-testid="tab-setup">
                <Code className="w-3.5 h-3.5 mr-1.5" />
                Setup Guide
              </TabsTrigger>
            </TabsList>

            <TabsContent value="search" className="space-y-4">
              <div className="bg-card border border-card-border rounded-md p-5">
                <div className="flex items-center gap-2 mb-3">
                  <Search className="w-4 h-4 text-primary" />
                  <h2 className="text-sm font-semibold">Search Laserfiche Directly</h2>
                  <span className="text-xs text-muted-foreground font-arabic" dir="rtl">البحث مباشرة في Laserfiche</span>
                </div>

                <div className="flex gap-2 mb-3">
                  <button
                    onClick={() => setSearchMode("nl")}
                    className={cn(
                      "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium border transition-colors",
                      searchMode === "nl"
                        ? "bg-primary text-primary-foreground border-primary"
                        : "bg-card text-muted-foreground border-card-border hover-elevate"
                    )}
                    data-testid="mode-nl"
                  >
                    <Globe className="w-3 h-3" />
                    Natural Language
                  </button>
                  <button
                    onClick={() => setSearchMode("lf")}
                    className={cn(
                      "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium border transition-colors",
                      searchMode === "lf"
                        ? "bg-primary text-primary-foreground border-primary"
                        : "bg-card text-muted-foreground border-card-border hover-elevate"
                    )}
                    data-testid="mode-lf"
                  >
                    <Code className="w-3 h-3" />
                    LF Command
                  </button>
                </div>

                {searchMode === "nl" ? (
                  <div className="space-y-3">
                    <div className={cn(
                      "flex items-start gap-3 bg-background border-2 rounded-md px-4 py-3 focus-within:border-primary border-border transition-colors"
                    )}>
                      <Globe className="w-4 h-4 text-muted-foreground mt-0.5 flex-shrink-0" />
                      <textarea
                        value={nlQuery}
                        onChange={e => setNlQuery(e.target.value)}
                        dir={isArabic ? "rtl" : "ltr"}
                        placeholder="e.g. عطني جميع المعاملات اللتي تحتوي على اسم سلمان في المعاملة"
                        className={cn(
                          "flex-1 bg-transparent text-foreground placeholder:text-muted-foreground resize-none outline-none text-sm leading-relaxed min-h-[52px]",
                          isArabic && "font-arabic text-base"
                        )}
                        rows={2}
                        data-testid="nl-search-input"
                      />
                    </div>

                    <div className="flex flex-wrap gap-1.5">
                      <span className="text-xs text-muted-foreground self-center">Examples:</span>
                      {EXAMPLE_NL_QUERIES.map(eq => (
                        <button
                          key={eq.text}
                          onClick={() => setNlQuery(eq.text)}
                          className={cn(
                            "text-xs px-2 py-1 rounded-md border border-border bg-muted text-muted-foreground hover-elevate",
                            eq.lang === "ar" && "font-arabic"
                          )}
                          dir={eq.lang === "ar" ? "rtl" : "ltr"}
                        >
                          {eq.text}
                        </button>
                      ))}
                    </div>

                    <div className="flex items-center gap-2">
                      <Button
                        onClick={handleSearch}
                        disabled={!nlQuery.trim() || searchMutation.isPending || !status?.connected}
                        data-testid="lf-search-button"
                      >
                        <Zap className="w-3.5 h-3.5 mr-1.5" />
                        {searchMutation.isPending ? "Searching..." : "Search Laserfiche"}
                      </Button>
                      {!status?.connected && (
                        <span className="text-xs text-amber-600">Configure Laserfiche connection first</span>
                      )}
                    </div>
                  </div>
                ) : (
                  <div className="space-y-3">
                    <div className="bg-background border-2 border-border focus-within:border-primary rounded-md px-4 py-3 transition-colors">
                      <input
                        value={lfQuery}
                        onChange={e => setLfQuery(e.target.value)}
                        placeholder={`{LF:Basic~="search term"} or {[]:[Field]="value"}`}
                        className="w-full bg-transparent text-foreground font-mono text-sm outline-none placeholder:text-muted-foreground"
                        data-testid="lf-command-input"
                      />
                    </div>
                    <Button
                      onClick={handleSearch}
                      disabled={!lfQuery.trim() || searchMutation.isPending || !status?.connected}
                      data-testid="lf-command-search"
                    >
                      <Search className="w-3.5 h-3.5 mr-1.5" />
                      {searchMutation.isPending ? "Searching..." : "Run LF Command"}
                    </Button>
                  </div>
                )}
              </div>

              {searchMutation.data && (
                <div className="bg-card border border-card-border rounded-md overflow-hidden">
                  <div className="px-4 py-3 border-b border-border bg-muted/30">
                    <div className="flex items-center justify-between gap-3">
                      <div>
                        <p className="text-sm font-medium text-foreground">
                          {searchMutation.data.total} results from Laserfiche
                        </p>
                        <div className="flex items-center gap-1.5 mt-1">
                          <Code className="w-3 h-3 text-muted-foreground" />
                          <code className="text-xs text-primary font-mono">{searchMutation.data.searchCommand}</code>
                        </div>
                      </div>
                      {searchMutation.data.nlTranslation && (
                        <div className="text-right">
                          <p className="text-xs text-muted-foreground">Extracted terms</p>
                          <div className="flex flex-wrap gap-1 justify-end mt-1">
                            {searchMutation.data.nlTranslation.extractedTerms.map(t => (
                              <span key={t} className="text-xs bg-primary/10 text-primary px-1.5 py-0.5 rounded font-mono">{t}</span>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                  </div>

                  {searchMutation.data.entries.length === 0 ? (
                    <div className="p-8 text-center">
                      <FileText className="w-10 h-10 text-muted-foreground/30 mx-auto mb-3" />
                      <p className="text-sm text-muted-foreground">No documents found matching your query</p>
                    </div>
                  ) : (
                    <div className="divide-y divide-border">
                      {searchMutation.data.entries.map(entry => (
                        <div key={entry.id} className="px-4 py-3 flex items-start gap-3 hover:bg-muted/20 transition-colors" data-testid={`lf-entry-${entry.id}`}>
                          <div className="w-8 h-8 rounded-md bg-primary/10 flex items-center justify-center flex-shrink-0">
                            {entry.entryType === "Folder" ? (
                              <FolderOpen className="w-4 h-4 text-primary" />
                            ) : (
                              <FileText className="w-4 h-4 text-primary" />
                            )}
                          </div>
                          <div className="flex-1 min-w-0">
                            <p className="text-sm font-medium text-foreground truncate">{entry.name}</p>
                            <p className="text-xs text-muted-foreground truncate">{entry.fullPath}</p>
                            <div className="flex flex-wrap gap-1.5 mt-1">
                              <span className="text-xs text-muted-foreground">ID: {entry.id}</span>
                              {entry.creator && <span className="text-xs text-muted-foreground">· {entry.creator}</span>}
                              {entry.extension && <Badge variant="outline" className="text-xs py-0">{entry.extension.toUpperCase()}</Badge>}
                              {entry.pageCount && <span className="text-xs text-muted-foreground">{entry.pageCount} pages</span>}
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {!status?.connected && (
                <div className="bg-amber-50 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-800 rounded-md p-4">
                  <div className="flex items-start gap-3">
                    <AlertCircle className="w-4 h-4 text-amber-600 flex-shrink-0 mt-0.5" />
                    <div>
                      <p className="text-sm font-medium text-amber-800 dark:text-amber-300">Laserfiche Not Connected</p>
                      <p className="text-xs text-amber-700 dark:text-amber-400 mt-1">
                        Follow the Setup Guide tab to configure your Laserfiche credentials. Once connected, you can search directly against your Laserfiche repository.
                      </p>
                    </div>
                  </div>
                </div>
              )}
            </TabsContent>

            <TabsContent value="translate" className="space-y-4">
              <div className="bg-card border border-card-border rounded-md p-5">
                <div className="flex items-center gap-2 mb-1">
                  <Brain className="w-4 h-4 text-primary" />
                  <h2 className="text-sm font-semibold">Natural Language → Laserfiche Query Translator</h2>
                </div>
                <p className="text-xs text-muted-foreground mb-4 font-arabic" dir="rtl">
                  حول استعلامك بالعربية أو الإنجليزية إلى أمر بحث Laserfiche
                </p>

                <div className="space-y-3">
                  <div className={cn(
                    "flex items-start gap-3 bg-background border-2 border-border focus-within:border-primary rounded-md px-4 py-3 transition-colors"
                  )}>
                    <Globe className="w-4 h-4 text-muted-foreground mt-0.5 flex-shrink-0" />
                    <textarea
                      value={nlQuery}
                      onChange={e => setNlQuery(e.target.value)}
                      dir={isArabic ? "rtl" : "ltr"}
                      placeholder="Type a natural language query in Arabic or English..."
                      className={cn(
                        "flex-1 bg-transparent text-foreground placeholder:text-muted-foreground resize-none outline-none text-sm leading-relaxed min-h-[60px]",
                        isArabic && "font-arabic text-base"
                      )}
                      rows={2}
                      data-testid="translate-input"
                    />
                  </div>

                  <div className="flex flex-wrap gap-2">
                    {EXAMPLE_NL_QUERIES.map(eq => (
                      <button
                        key={eq.text}
                        onClick={() => setNlQuery(eq.text)}
                        className={cn(
                          "text-xs px-2 py-1 rounded-md border border-border bg-muted text-muted-foreground hover-elevate",
                          eq.lang === "ar" && "font-arabic"
                        )}
                        dir={eq.lang === "ar" ? "rtl" : "ltr"}
                      >
                        {eq.text}
                      </button>
                    ))}
                  </div>

                  <Button
                    onClick={handleTranslate}
                    disabled={!nlQuery.trim() || translateMutation.isPending}
                    data-testid="translate-button"
                  >
                    <ArrowRight className="w-3.5 h-3.5 mr-1.5" />
                    {translateMutation.isPending ? "Translating..." : "Translate to LF Command"}
                  </Button>
                </div>

                {translateMutation.data && (
                  <div className="mt-4 space-y-3">
                    <Separator />
                    <div>
                      <p className="text-xs text-muted-foreground mb-1.5">Generated Laserfiche Search Command</p>
                      <CodeBlock code={translateMutation.data.command} />
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground mb-1.5">Explanation</p>
                      <p className="text-sm text-foreground">{translateMutation.data.explanation}</p>
                    </div>
                    {translateMutation.data.extractedTerms.length > 0 && (
                      <div>
                        <p className="text-xs text-muted-foreground mb-1.5">Extracted Search Terms</p>
                        <div className="flex flex-wrap gap-1.5">
                          {translateMutation.data.extractedTerms.map(t => (
                            <span key={t} className="text-xs bg-primary/10 text-primary px-2 py-0.5 rounded font-mono">{t}</span>
                          ))}
                        </div>
                      </div>
                    )}
                    <div className="bg-muted/50 rounded-md p-3">
                      <p className="text-xs text-muted-foreground mb-2">Example usage in your app:</p>
                      <CodeBlock
                        code={`POST /api/laserfiche/search\n{\n  "query": "${nlQuery}"\n}`}
                      />
                    </div>
                  </div>
                )}
              </div>

              <div className="bg-card border border-card-border rounded-md p-5">
                <h3 className="text-sm font-semibold mb-3">Laserfiche Search Command Reference</h3>
                <div className="space-y-2">
                  {[
                    { label: "Full-text search", cmd: `{LF:Basic~="سلمان"}`, desc: "Search within document content" },
                    { label: "Field search", cmd: `{[]:[Field Name]="value"}`, desc: "Search by metadata field value" },
                    { label: "Date range", cmd: `{LF:Modified>="2023-01-01"} & {LF:Modified<="2023-12-31"}`, desc: "Filter by modification date" },
                    { label: "Entry name", cmd: `{LF:Name~="contract"}`, desc: "Search by document title/name" },
                    { label: "Combine conditions", cmd: `{LF:Basic~="صيانة"} & {LF:Modified>="2023-01-01"}`, desc: "AND multiple conditions with &" },
                    { label: "Template filter", cmd: `{LF:Template="Contract Template"}`, desc: "Filter by Laserfiche template" },
                  ].map(item => (
                    <div key={item.label} className="flex items-start gap-3 py-1.5">
                      <div className="min-w-28 text-xs text-muted-foreground pt-0.5">{item.label}</div>
                      <div className="flex-1 min-w-0">
                        <code className="text-xs text-primary font-mono block truncate">{item.cmd}</code>
                        <p className="text-xs text-muted-foreground mt-0.5">{item.desc}</p>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </TabsContent>

            <TabsContent value="setup" className="space-y-5">
              <div className="bg-card border border-card-border rounded-md p-5">
                <div className="flex items-center gap-2 mb-4">
                  <Shield className="w-4 h-4 text-primary" />
                  <h2 className="text-sm font-semibold">Connection Setup Guide</h2>
                </div>

                <div className="space-y-5">
                  <ConfigStep
                    num={1}
                    title="Install Laserfiche API Server (Self-Hosted)"
                    titleAr="تثبيت خادم Laserfiche API"
                    content={
                      <div className="space-y-2 text-xs text-muted-foreground">
                        <p>Download and install the Laserfiche API Server on your Windows Server:</p>
                        <CodeBlock code="https://developer.laserfiche.com/docs/api/server/" label="Documentation" />
                        <p className="mt-1">After installation, the API is accessible at:</p>
                        <CodeBlock code="https://{your-server}/LFRepositoryAPI/swagger/index.html" label="API Explorer" />
                      </div>
                    }
                  />

                  <Separator />

                  <ConfigStep
                    num={2}
                    title="Add Required Environment Secrets"
                    titleAr="إضافة المتغيرات البيئية المطلوبة"
                    content={
                      <div className="space-y-2">
                        <p className="text-xs text-muted-foreground">In the Replit Secrets tab, add these four secrets:</p>
                        <div className="space-y-1.5">
                          {[
                            { key: "LF_SERVER_URL", example: "https://your-server/LFRepositoryAPI", desc: "Base URL of your Laserfiche API Server" },
                            { key: "LF_REPO_ID", example: "YourRepoName", desc: "Your Laserfiche repository ID/name" },
                            { key: "LF_USERNAME", example: "admin", desc: "Laserfiche username (repo or domain user)" },
                            { key: "LF_PASSWORD", example: "••••••••", desc: "Laserfiche password (stored securely)" },
                          ].map(s => (
                            <div key={s.key} className="bg-muted rounded-md px-3 py-2">
                              <div className="flex items-center justify-between gap-2 mb-0.5">
                                <code className="text-xs font-mono font-semibold text-primary">{s.key}</code>
                                <span className="text-xs text-muted-foreground font-mono truncate">{s.example}</span>
                              </div>
                              <p className="text-xs text-muted-foreground">{s.desc}</p>
                            </div>
                          ))}
                        </div>
                      </div>
                    }
                  />

                  <Separator />

                  <ConfigStep
                    num={3}
                    title="Test Connection"
                    titleAr="اختبار الاتصال"
                    content={
                      <div className="space-y-2 text-xs text-muted-foreground">
                        <p>After adding secrets, restart the application and click "Refresh" above. You should see a green "Connected" badge.</p>
                        <div className="bg-muted rounded-md px-3 py-2">
                          <p className="font-mono text-xs">GET /api/laserfiche/status</p>
                          <p className="text-xs text-muted-foreground mt-1">Returns connection health and server details</p>
                        </div>
                      </div>
                    }
                  />

                  <Separator />

                  <ConfigStep
                    num={4}
                    title="Sync Documents to Search Index"
                    titleAr="مزامنة الوثائق مع فهرس البحث"
                    content={
                      <div className="space-y-2 text-xs text-muted-foreground">
                        <p>Once connected, click "Sync Documents" to import Laserfiche documents into the semantic search index. The system will:</p>
                        <ul className="list-disc list-inside space-y-1 ml-1">
                          <li>Browse your Laserfiche repository folders</li>
                          <li>Extract document metadata and field values</li>
                          <li>Index documents for Arabic + English semantic search</li>
                          <li>Make them searchable via the main Search page</li>
                        </ul>
                      </div>
                    }
                  />

                  <Separator />

                  <ConfigStep
                    num={5}
                    title="Search Directly or via Semantic Search"
                    titleAr="البحث مباشرة أو عبر البحث الدلالي"
                    content={
                      <div className="space-y-2 text-xs text-muted-foreground">
                        <p>Two search modes are available:</p>
                        <div className="grid grid-cols-2 gap-2">
                          <div className="bg-muted rounded-md p-2">
                            <p className="font-medium text-foreground mb-1">NL → LF Search</p>
                            <p>Arabic/English queries translated to Laserfiche commands and executed live</p>
                          </div>
                          <div className="bg-muted rounded-md p-2">
                            <p className="font-medium text-foreground mb-1">Semantic Search</p>
                            <p>Synced documents become searchable via the AI semantic search on the main page</p>
                          </div>
                        </div>
                      </div>
                    }
                  />
                </div>
              </div>

              {status && (
                <div className={cn(
                  "rounded-md p-4 border",
                  status.connected
                    ? "bg-emerald-50 dark:bg-emerald-950/20 border-emerald-200 dark:border-emerald-800"
                    : "bg-muted border-border"
                )}>
                  <div className="flex items-start gap-3">
                    {status.connected ? (
                      <CheckCircle className="w-4 h-4 text-emerald-600 flex-shrink-0 mt-0.5" />
                    ) : (
                      <AlertCircle className="w-4 h-4 text-muted-foreground flex-shrink-0 mt-0.5" />
                    )}
                    <div>
                      <p className="text-sm font-medium text-foreground">Status: {status.message}</p>
                      {status.serverUrl && (
                        <p className="text-xs text-muted-foreground mt-1 font-mono">{status.serverUrl}</p>
                      )}
                    </div>
                  </div>
                </div>
              )}
            </TabsContent>
          </Tabs>
        </div>
      </div>
    </div>
  );
}

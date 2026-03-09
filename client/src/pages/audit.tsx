import { useQuery } from "@tanstack/react-query";
import { type AuditLog } from "@shared/schema";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Shield, Search, Globe, Clock, User, Layers, Brain, Filter } from "lucide-react";
import { cn } from "@/lib/utils";

const searchTypeBadge = (type: string | null) => {
  switch (type) {
    case "semantic": return { label: "Semantic", icon: Brain, className: "text-purple-700 dark:text-purple-400 bg-purple-50 dark:bg-purple-950/30 border-purple-200 dark:border-purple-800" };
    case "keyword": return { label: "Keyword", icon: Search, className: "text-blue-700 dark:text-blue-400 bg-blue-50 dark:bg-blue-950/30 border-blue-200 dark:border-blue-800" };
    default: return { label: "Hybrid", icon: Layers, className: "text-emerald-700 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/30 border-emerald-200 dark:border-emerald-800" };
  }
};

function AuditRow({ log, index }: { log: AuditLog; index: number }) {
  const isAr = log.queryLanguage === "ar" || /[\u0600-\u06FF]/.test(log.query);
  const badge = searchTypeBadge(log.searchType);
  const BadgeIcon = badge.icon;
  const searchedAt = log.searchedAt ? new Date(log.searchedAt) : null;

  return (
    <tr className="border-b border-border hover:bg-muted/30 transition-colors" data-testid={`audit-row-${log.id}`}>
      <td className="px-4 py-3 text-xs text-muted-foreground font-mono">{index + 1}</td>
      <td className="px-4 py-3 max-w-xs">
        <p
          className={cn("text-sm text-foreground leading-tight truncate", isAr && "font-arabic")}
          dir={isAr ? "rtl" : "ltr"}
          title={log.query}
          data-testid={`audit-query-${log.id}`}
        >
          {log.query}
        </p>
        {isAr && (
          <span className="inline-flex items-center gap-1 text-xs text-muted-foreground mt-0.5">
            <Globe className="w-3 h-3" />
            Arabic
          </span>
        )}
      </td>
      <td className="px-4 py-3">
        <span className={cn("inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-md border font-medium", badge.className)}>
          <BadgeIcon className="w-3 h-3" />
          {badge.label}
        </span>
      </td>
      <td className="px-4 py-3">
        <div className="flex items-center gap-1.5">
          <User className="w-3.5 h-3.5 text-muted-foreground" />
          <span className="text-sm text-foreground">{log.username || "—"}</span>
        </div>
      </td>
      <td className="px-4 py-3 text-xs text-muted-foreground">
        {log.department || "—"}
      </td>
      <td className="px-4 py-3 text-center">
        <span className="text-sm font-mono text-foreground">{log.resultsCount ?? "—"}</span>
      </td>
      <td className="px-4 py-3 text-xs text-muted-foreground font-mono">
        {log.ipAddress || "—"}
      </td>
      <td className="px-4 py-3 text-xs text-muted-foreground whitespace-nowrap">
        {searchedAt ? (
          <div>
            <p>{searchedAt.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" })}</p>
            <p className="text-muted-foreground/70">{searchedAt.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" })}</p>
          </div>
        ) : "—"}
      </td>
    </tr>
  );
}

export default function AuditPage() {
  const { data: logs, isLoading } = useQuery<AuditLog[]>({
    queryKey: ["/api/audit-logs"],
  });

  const arabicCount = logs?.filter(l => l.queryLanguage === "ar" || /[\u0600-\u06FF]/.test(l.query)).length || 0;
  const hybridCount = logs?.filter(l => !l.searchType || l.searchType === "hybrid").length || 0;
  const semanticCount = logs?.filter(l => l.searchType === "semantic").length || 0;

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-2 mb-1">
              <Shield className="w-5 h-5 text-primary" />
              <h1 className="text-xl font-semibold text-foreground">Audit Log</h1>
            </div>
            <p className="text-sm text-muted-foreground font-arabic" dir="rtl">سجل التدقيق - جميع عمليات البحث</p>
          </div>
          {logs && (
            <div className="flex items-center gap-2">
              <div className="text-right">
                <p className="text-xs text-muted-foreground">Total Entries</p>
                <p className="text-2xl font-bold text-foreground" data-testid="audit-total">{logs.length}</p>
              </div>
            </div>
          )}
        </div>

        {logs && (
          <div className="flex flex-wrap gap-3 mt-4">
            <div className="flex items-center gap-1.5 bg-card border border-card-border rounded-md px-3 py-2">
              <Globe className="w-3.5 h-3.5 text-primary" />
              <span className="text-xs text-muted-foreground">Arabic queries:</span>
              <span className="text-xs font-semibold text-foreground">{arabicCount}</span>
            </div>
            <div className="flex items-center gap-1.5 bg-card border border-card-border rounded-md px-3 py-2">
              <Layers className="w-3.5 h-3.5 text-primary" />
              <span className="text-xs text-muted-foreground">Hybrid:</span>
              <span className="text-xs font-semibold text-foreground">{hybridCount}</span>
            </div>
            <div className="flex items-center gap-1.5 bg-card border border-card-border rounded-md px-3 py-2">
              <Brain className="w-3.5 h-3.5 text-primary" />
              <span className="text-xs text-muted-foreground">Semantic:</span>
              <span className="text-xs font-semibold text-foreground">{semanticCount}</span>
            </div>
            <div className="flex items-center gap-1.5 bg-card border border-card-border rounded-md px-3 py-2 text-xs text-muted-foreground">
              <Clock className="w-3.5 h-3.5" />
              All searches logged for compliance
            </div>
          </div>
        )}
      </div>

      <div className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="px-6 py-5 space-y-3">
            {[1, 2, 3, 4, 5].map(i => (
              <Skeleton key={i} className="h-12 w-full rounded-md" />
            ))}
          </div>
        ) : !logs || logs.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-64 text-center px-6">
            <Shield className="w-12 h-12 text-muted-foreground/30 mb-4" />
            <h3 className="font-medium text-foreground mb-1">No audit entries yet</h3>
            <p className="text-sm text-muted-foreground">Search activity will appear here automatically.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[900px]">
              <thead>
                <tr className="border-b border-border bg-muted/30">
                  <th className="px-4 py-2.5 text-left text-xs font-medium text-muted-foreground w-10">#</th>
                  <th className="px-4 py-2.5 text-left text-xs font-medium text-muted-foreground">Query</th>
                  <th className="px-4 py-2.5 text-left text-xs font-medium text-muted-foreground">Type</th>
                  <th className="px-4 py-2.5 text-left text-xs font-medium text-muted-foreground">User</th>
                  <th className="px-4 py-2.5 text-left text-xs font-medium text-muted-foreground">Department</th>
                  <th className="px-4 py-2.5 text-center text-xs font-medium text-muted-foreground">Results</th>
                  <th className="px-4 py-2.5 text-left text-xs font-medium text-muted-foreground">IP Address</th>
                  <th className="px-4 py-2.5 text-left text-xs font-medium text-muted-foreground">Searched At</th>
                </tr>
              </thead>
              <tbody>
                {logs.map((log, index) => (
                  <AuditRow key={log.id} log={log} index={index} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

import { useQuery } from "@tanstack/react-query";
import { useState, useMemo } from "react";
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Cell,
  PieChart, Pie, Legend, LineChart, Line,
} from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useToast } from "@/hooks/use-toast";
import {
  RefreshCw, Layers, CheckCircle2, AlertCircle,
  Database, Search,
  Activity, Wifi, WifiOff, User, Timer, Globe, Clock,
  ShieldCheck, ShieldAlert, AlertTriangle,
  FileSpreadsheet, FileCode, Printer,
} from "lucide-react";
// Export libraries are loaded dynamically when export buttons are clicked
// to avoid startup failures if packages are missing from local node_modules

// ═══════════════════════════════════════════════════════════════════════════
// Types
// ═══════════════════════════════════════════════════════════════════════════

type DocEntry = {
  id: number;
  name: string;
  fullPath: string;
  templateName: string;
  creator: string;
  creationTime?: string;
  lastModifiedTime?: string;
};

type DashboardStats = {
  repositoryId: string | null;
  isLive: boolean;
  totalFolders: number;
  totalDocuments: number;
  totalTemplates: number;
  docsWithTemplate: number;
  docsWithoutTemplate: number;
  templateStats: Array<{ name: string; count: number }>;
  rootFolders: Array<{ name: string; documents: number; folders: number }>;
  allFolders: Array<{ name: string; documents: number; folders: number }>;
  recentDocs: DocEntry[];
  modifiedDocs: DocEntry[];
  totalSearches: number;
  searchesByDay: Array<{ date: string; count: number }>;
  topSearches: Array<{ query: string; count: number }>;
  health?: {
    status: "connected" | "disconnected" | "reconnecting";
    repositoryId: string | null;
    serverUrl: string;
    username: string;
    lastRefresh: string;
    scanDurationMs: number;
    tokenDurationMs: number;
  };
};

// ═══════════════════════════════════════════════════════════════════════════
// Design tokens
// ═══════════════════════════════════════════════════════════════════════════

const PALETTE = [
  "#3B82F6", "#14B8A6", "#F59E0B", "#8B5CF6", "#EF4444", "#22C55E", "#F97316", "#06B6D4",
  "#EC4899", "#84CC16", "#A855F7", "#F43F5E", "#10B981", "#FBBF24", "#60A5FA", "#6366F1",
  "#0EA5E9", "#D946EF", "#C2410C", "#0891B2", "#7C3AED", "#E11D48", "#2563EB", "#16A34A",
];
const OTHERS_COLOR = "#94A3B8";
const TOP_N = 15;

function computeYTicks(maxValue: number): number[] {
  if (!maxValue || maxValue <= 0) return [0, 1];
  if (maxValue <= 10) { const t: number[] = []; for (let i = 0; i <= maxValue; i++) t.push(i); return t; }
  const rs = maxValue / 5, mag = Math.pow(10, Math.floor(Math.log10(rs))), n = rs / mag;
  let step = n <= 1 ? mag : n <= 2 ? 2 * mag : n <= 7.5 ? 5 * mag : 10 * mag;
  const ceil = Math.ceil(maxValue / step) * step; const t: number[] = [];
  for (let v = 0; v <= ceil; v += step) t.push(v); return t;
}
function truncateLabel(name: string, maxLen: number): string { return name.length > maxLen ? name.slice(0, maxLen - 1) + "…" : name; }
function formatDuration(ms: number): string { if (ms < 1000) return `${ms}ms`; return `${(ms / 1000).toFixed(1)}s`; }
function formatTimeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  if (diff < 60_000) return "Just now"; if (diff < 3600_000) return `${Math.round(diff / 60_000)}m ago`;
  if (diff < 86400_000) return `${Math.round(diff / 3600_000)}h ago`; return `${Math.round(diff / 86400_000)}d ago`;
}
function formatDate(iso?: string): string {
  if (!iso) return "N/A"; const d = new Date(iso);
  return d.toLocaleString("en-US", { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
}
function isWithinDateRange(iso: string, from?: string, to?: string): boolean {
  const ts = new Date(iso).getTime(); if (from && ts < new Date(from).getTime()) return false; if (to && ts > new Date(to + "T23:59:59").getTime()) return false; return true;
}
function countDocsByPeriod(docs: DocEntry[], field: "creationTime" | "lastModifiedTime"): { today: number; thisWeek: number; thisMonth: number } {
  const now = new Date(), ts = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const ws = ts - (now.getDay() * 86400_000), ms = new Date(now.getFullYear(), now.getMonth(), 1).getTime();
  let t = 0, w = 0, m = 0;
  for (const d of docs) {
    const x = new Date(d[field] || d.creationTime || "").getTime(); if (!x) continue;
    if (x >= ts) t++; if (x >= ws) w++; if (x >= ms) m++;
  }
  return { today: t, thisWeek: w, thisMonth: m };
}
function computeUserActivity(recentDocs: DocEntry[], modifiedDocs: DocEntry[]) {
  const map: Record<string, { created: number; modified: number; lastActivity: string }> = {};
  for (const d of recentDocs) {
    const u = d.creator || "Unknown"; if (!map[u]) map[u] = { created: 0, modified: 0, lastActivity: d.creationTime || "" };
    map[u].created++; if (d.creationTime && d.creationTime > map[u].lastActivity) map[u].lastActivity = d.creationTime;
  }
  for (const d of modifiedDocs) {
    const u = d.creator || "Unknown"; if (!map[u]) map[u] = { created: 0, modified: 0, lastActivity: d.lastModifiedTime || "" };
    map[u].modified++; if (d.lastModifiedTime && d.lastModifiedTime > map[u].lastActivity) map[u].lastActivity = d.lastModifiedTime;
  }
  return Object.entries(map).map(([name, s]) => ({ name, ...s, total: s.created + s.modified })).sort((a, b) => b.total - a.total);
}
const TOOLTIP_STYLE = {
  contentStyle: { background: "hsl(var(--popover))", border: "1px solid hsl(var(--border))", borderRadius: "8px", fontSize: "12px", boxShadow: "0 4px 16px rgba(0,0,0,0.12)", padding: "8px 12px" },
  itemStyle: { color: "hsl(var(--popover-foreground))" }, labelStyle: { color: "hsl(var(--muted-foreground))", fontWeight: 500 },
  cursor: { fill: "hsl(var(--muted))", opacity: 0.35 },
};

// ═══════════════════════════════════════════════════════════════════════════
// Shared sub-components
// ═══════════════════════════════════════════════════════════════════════════

function StatCard({ icon: Icon, label, labelAr, value, colorClass = "bg-primary/15", iconClass = "text-primary", sub }: {
  icon: any; label: string; labelAr: string; value: string | number; colorClass?: string; iconClass?: string; sub?: string;
}) {
  return (
    <div className="bg-card border border-border rounded-xl p-5 shadow-sm">
      <div className="flex items-start justify-between gap-3 mb-4">
        <div className={`w-11 h-11 rounded-lg ${colorClass} flex items-center justify-center flex-shrink-0`}><Icon className={`w-5 h-5 ${iconClass}`} /></div>
        {sub && <span className="text-xs text-muted-foreground bg-muted px-2 py-0.5 rounded-full">{sub}</span>}
      </div>
      <p className="text-3xl font-bold text-foreground mb-1 tabular-nums">{typeof value === "number" ? value.toLocaleString() : value}</p>
      <p className="text-sm text-muted-foreground leading-tight">{label}</p>
      <p className="text-xs text-muted-foreground/70 font-arabic mt-0.5" dir="rtl">{labelAr}</p>
    </div>
  );
}
function ChartCard({ title, sub, badge, children }: { title: string; sub?: string; badge?: React.ReactNode; children: React.ReactNode; }) {
  return (
    <div className="bg-card border border-border rounded-xl p-5 shadow-sm">
      <div className="flex items-start justify-between gap-2 mb-4">
        <div className="flex items-center gap-2 min-w-0">
          <div className="h-5 w-1 rounded-full bg-primary flex-shrink-0" />
          <h2 className="text-sm font-semibold text-foreground">{title}</h2>
          {sub && <span className="text-xs text-muted-foreground truncate">{sub}</span>}
        </div>
        {badge}
      </div>
      {children}
    </div>
  );
}
function EmptyState({ message }: { message: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-10 text-muted-foreground gap-2">
      <AlertCircle className="w-8 h-8 opacity-30" /><p className="text-sm">{message}</p>
    </div>
  );
}
function SmallBadge({ children, color = "bg-muted" }: { children: React.ReactNode; color?: string }) {
  return <span className={`text-xs ${color} px-2 py-0.5 rounded-full`}>{children}</span>;
}

// ═══════════════════════════════════════════════════════════════════════════
// Reusable data table with search / filter / pagination / sorting
// ═══════════════════════════════════════════════════════════════════════════

type Col<T> = { key: string; label: string; width?: string; sortable?: boolean; render?: (row: T) => React.ReactNode; };

function DataTable<T extends Record<string, any>>({
  columns, rows, searchPlaceholder = "Search...", filterOptions, filterLabel, dateField, pageSizes = [10, 20, 50], maxRows = 20, emptyMessage = "No data available.",
}: { columns: Col<T>[]; rows: T[]; searchPlaceholder?: string; filterOptions?: string[]; filterLabel?: string; dateField?: keyof T; pageSizes?: number[]; maxRows?: number; emptyMessage?: string; }) {
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<"asc" | "desc">("desc");
  const [pageSize, setPageSize] = useState(pageSizes[1] ?? 20);
  const [page, setPage] = useState(0);

  const filtered = useMemo(() => {
    let data = [...rows];
    const q = search.trim().toLowerCase();
    if (q) data = data.filter((r) => columns.some((c) => { const v = c.render ? "" : String(r[c.key] ?? "").toLowerCase(); return v.includes(q); }));
    if (filter) data = data.filter((r) => String(r.templateName ?? r[filterLabel ?? ""] ?? "") === filter);
    if (dateField && (dateFrom || dateTo)) data = data.filter((r) => isWithinDateRange(String(r[dateField] ?? ""), dateFrom, dateTo));
    if (sortKey) {
      data.sort((a, b) => {
        const av = a[sortKey] ?? "", bv = b[sortKey] ?? "";
        if (typeof av === "number" && typeof bv === "number") return sortDir === "asc" ? av - bv : bv - av;
        return sortDir === "asc" ? String(av).localeCompare(String(bv)) : String(bv).localeCompare(String(av));
      });
    }
    return data.slice(0, maxRows);
  }, [rows, search, filter, dateFrom, dateTo, sortKey, sortDir, columns, dateField, filterLabel, maxRows]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const paged = filtered.slice(page * pageSize, (page + 1) * pageSize);

  const toggleSort = (key: string) => { if (sortKey === key) { setSortDir((d) => (d === "asc" ? "desc" : "asc")); } else { setSortKey(key); setSortDir("desc"); } setPage(0); };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        <Input placeholder={searchPlaceholder} value={search} onChange={(e) => { setSearch(e.target.value); setPage(0); }} className="max-w-xs text-sm h-8" />
        {filterOptions && filterOptions.length > 0 && (
          <select value={filter} onChange={(e) => { setFilter(e.target.value); setPage(0); }} className="text-sm h-8 px-2 rounded-md border border-input bg-background">
            <option value="">All {filterLabel}s</option>
            {filterOptions.map((o) => <option key={o} value={o}>{o}</option>)}
          </select>
        )}
        {dateField && (<><Input type="date" value={dateFrom} onChange={(e) => { setDateFrom(e.target.value); setPage(0); }} className="text-sm h-8 w-36" /><Input type="date" value={dateTo} onChange={(e) => { setDateTo(e.target.value); setPage(0); }} className="text-sm h-8 w-36" /></>)}
      </div>
      <div className="overflow-hidden rounded-lg border border-border">
        <table className="w-full text-sm">
          <thead><tr className="bg-muted/50">
            {columns.map((c) => (
              <th key={c.key} className={`text-left px-3 py-2.5 font-semibold text-foreground ${c.width || ""} ${c.sortable ? "cursor-pointer select-none" : ""}`} onClick={() => c.sortable && toggleSort(c.key)}>
                {c.label}{c.sortable && sortKey === c.key && (sortDir === "asc" ? " ↑" : " ↓")}
              </th>
            ))}
          </tr></thead>
          <tbody>
            {paged.map((row, i) => (
              <tr key={i} className="border-t border-border hover:bg-muted/30 transition-colors">
                {columns.map((c) => (<td key={c.key} className="px-3 py-2 text-foreground">{c.render ? c.render(row) : String(row[c.key] ?? "N/A")}</td>))}
              </tr>
            ))}
            {paged.length === 0 && (<tr><td colSpan={columns.length} className="px-3 py-6 text-center text-sm text-muted-foreground">{emptyMessage}</td></tr>)}
          </tbody>
        </table>
      </div>
      {filtered.length > 0 && (
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <div className="flex items-center gap-2">
            {pageSizes.map((s) => (<button key={s} onClick={() => { setPageSize(s); setPage(0); }} className={`px-2 py-0.5 rounded ${pageSize === s ? "bg-primary/10 text-primary font-medium" : "hover:bg-muted"}`}>{s}</button>))}
            <span>rows/page</span>
          </div>
          <div className="flex items-center gap-2">
            <button onClick={() => setPage((p) => Math.max(0, p - 1))} disabled={page === 0} className="px-2 py-0.5 rounded hover:bg-muted disabled:opacity-30">Prev</button>
            <span>Page {page + 1} of {totalPages}</span>
            <button onClick={() => setPage((p) => Math.min(totalPages - 1, p + 1))} disabled={page >= totalPages - 1} className="px-2 py-0.5 rounded hover:bg-muted disabled:opacity-30">Next</button>
          </div>
        </div>
      )}
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════════════════
// Chart sizing helpers
// ═══════════════════════════════════════════════════════════════════════════

function computeBarChartHeight(c: number): number { if (c <= 4) return 260; if (c <= 8) return 320; if (c <= 12) return 380; if (c <= 20) return 440; return Math.min(600, 40 + c * 22); }
function computeMaxBarSize(c: number): number { if (c <= 4) return 64; if (c <= 8) return 48; if (c <= 14) return 34; if (c <= 24) return 26; return 20; }
function computeLabelMaxLen(c: number): number { if (c <= 4) return 24; if (c <= 8) return 16; if (c <= 14) return 12; return 9; }

type AggFolder = { name: string; documents: number; folders: number; isOthers?: boolean };
function aggregateTopN(data: DashboardStats["rootFolders"], n = TOP_N): AggFolder[] {
  if (data.length <= n) return data;
  const sorted = [...data].sort((a, b) => b.documents - a.documents);
  const top = sorted.slice(0, n), rest = sorted.slice(n);
  return [...top, { name: `Others (${rest.length})`, documents: rest.reduce((s, f) => s + f.documents, 0), folders: rest.reduce((s, f) => s + f.folders, 0), isOthers: true }];
}

// ═══════════════════════════════════════════════════════════════════════════
// Existing chart components
// ═══════════════════════════════════════════════════════════════════════════

function FolderTooltip({ active, payload, label, totalDocs, lookup }: { active?: boolean; payload?: any[]; label?: string; totalDocs: number; lookup: Record<string, { name: string; documents: number; isOthers: boolean }>; }) {
  if (!active || !payload?.length) return null;
  const item = lookup[label ?? ""]; if (!item) return null;
  const pct = totalDocs > 0 ? Math.round((item.documents / totalDocs) * 100) : 0;
  const color = item.isOthers ? OTHERS_COLOR : PALETTE[Object.keys(lookup).indexOf(label ?? "") % PALETTE.length];
  return (
    <div className="rounded-lg border bg-popover text-popover-foreground shadow-lg px-3 py-2 text-xs" style={{ borderColor: "hsl(var(--border))" }}>
      <p className="font-semibold mb-1" style={{ color }}>{item.name}</p>
      <div className="space-y-0.5 text-muted-foreground"><p><span className="font-medium text-foreground">{item.documents.toLocaleString()}</span> document{item.documents !== 1 ? "s" : ""}</p><p>{pct}% of total documents</p></div>
    </div>
  );
}

function RootFoldersChart({ data }: { data: DashboardStats["rootFolders"] | undefined }) {
  if (!data?.length) return <EmptyState message="No folder data available." />;
  const aggregated = aggregateTopN(data); const wasAgg = aggregated.some((f) => f.isOthers);
  const count = aggregated.length, totalDocs = aggregated.reduce((s, f) => s + f.documents, 0), maxDocs = Math.max(...aggregated.map((f) => f.documents), 0);
  const yTicks = computeYTicks(maxDocs), yMax = yTicks[yTicks.length - 1];
  const chartHeight = computeBarChartHeight(count), maxBarSize = computeMaxBarSize(count), maxLabelLen = computeLabelMaxLen(count);
  const labelAngle = count > 6 ? -40 : count > 3 ? -25 : 0, textAnchor = count > 6 ? "end" : "middle", bottomMargin = count > 6 ? 72 : count > 3 ? 48 : 28;
  const chartData = aggregated.map((f, i) => ({ name: f.name, label: truncateLabel(f.name, maxLabelLen), documents: f.documents, isOthers: !!f.isOthers, color: f.isOthers ? OTHERS_COLOR : PALETTE[i % PALETTE.length] }));
  const lookup: Record<string, { name: string; documents: number; isOthers: boolean }> = {};
  for (const d of chartData) lookup[d.label] = { name: d.name, documents: d.documents, isOthers: d.isOthers };
  return (
    <>
      {wasAgg && <p className="text-xs text-muted-foreground mb-2">Showing top {TOP_N} folders by document count &middot; remaining combined as "Others"</p>}
      <ResponsiveContainer width="100%" height={chartHeight}>
        <BarChart data={chartData} margin={{ top: 8, right: 12, bottom: bottomMargin, left: 0 }} barCategoryGap={count > 12 ? "12%" : count > 6 ? "20%" : "30%"}>
          <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.35} vertical={false} />
          <XAxis dataKey="label" tick={{ fontSize: count > 12 ? 9 : 11, fill: "hsl(var(--muted-foreground))" }} interval={0} angle={labelAngle} textAnchor={textAnchor} height={bottomMargin} tickLine={false} axisLine={{ stroke: "hsl(var(--border))" }} />
          <YAxis ticks={yTicks} domain={[0, yMax]} allowDecimals={false} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} width={yMax >= 1000 ? 52 : yMax >= 100 ? 40 : 32} tickFormatter={(v) => v.toLocaleString()} />
          <Tooltip cursor={{ fill: "hsl(var(--muted))", opacity: 0.3 }} content={<FolderTooltip totalDocs={totalDocs} lookup={lookup} /> as any} />
          <Bar dataKey="documents" radius={[6, 6, 0, 0]} maxBarSize={maxBarSize} isAnimationActive animationDuration={500} animationEasing="ease-out">
            {chartData.map((entry, i) => <Cell key={i} fill={entry.color} />)}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </>
  );
}

function TemplateTooltip({ active, payload }: { active?: boolean; payload?: any[] }) {
  if (!active || !payload?.length) return null;
  const p = payload[0], name = p.name as string, value = Number(p.value ?? 0), total = p.payload?.totalCount ?? value;
  const pct = total > 0 ? Math.round((value / total) * 100) : 0, i = p.payload?.__index ?? 0, color = PALETTE[i % PALETTE.length];
  return (
    <div className="rounded-lg border bg-popover text-popover-foreground shadow-lg px-3 py-2 text-xs" style={{ borderColor: "hsl(var(--border))" }}>
      <p className="font-semibold mb-1" style={{ color }}>{name}</p>
      <div className="space-y-0.5 text-muted-foreground"><p><span className="font-medium text-foreground">{value.toLocaleString()}</span> document{value !== 1 ? "s" : ""}</p><p>{pct}% of all templated documents</p></div>
    </div>
  );
}

function TemplatePieChart({ data }: { data: DashboardStats["templateStats"] | undefined }) {
  if (!data?.length) return <EmptyState message="No template information available." />;
  const count = data.length, total = data.reduce((s, t) => s + t.count, 0);
  const outerR = count > 12 ? 70 : count > 8 ? 80 : 92, innerR = count > 12 ? 38 : count > 8 ? 42 : 48, chartHeight = count > 12 ? 320 : 300;
  const enriched = data.map((t, i) => ({ ...t, totalCount: total, __index: i }));
  return (
    <ResponsiveContainer width="100%" height={chartHeight}>
      <PieChart>
        <Pie data={enriched} dataKey="count" nameKey="name" cx="50%" cy="45%" outerRadius={outerR} innerRadius={innerR} paddingAngle={count > 10 ? 1 : 2} label={({ percent }) => (percent > 0.04 ? `${(percent * 100).toFixed(0)}%` : "")} labelLine={false} isAnimationActive animationDuration={600} animationEasing="ease-out">
          {enriched.map((_, i) => <Cell key={i} fill={PALETTE[i % PALETTE.length]} stroke="none" />)}
        </Pie>
        <Tooltip cursor={{ fill: "transparent" }} content={<TemplateTooltip /> as any} />
        <Legend iconType="circle" iconSize={8} wrapperStyle={{ paddingTop: 10, fontSize: 11 }} formatter={(value) => <span style={{ color: "hsl(var(--muted-foreground))" }}>{truncateLabel(String(value), 24)}</span>} />
      </PieChart>
    </ResponsiveContainer>
  );
}

function SearchTooltip({ active, payload, label, total }: { active?: boolean; payload?: any[]; label?: string; total: number }) {
  if (!active || !payload?.length) return null;
  const value = Number(payload[0].value ?? 0), pct = total > 0 ? Math.round((value / total) * 100) : 0;
  return (
    <div className="rounded-lg border bg-popover text-popover-foreground shadow-lg px-3 py-2 text-xs" style={{ borderColor: "hsl(var(--border))" }}>
      <p className="font-semibold mb-1 text-blue-500">{label}</p>
      <div className="space-y-0.5 text-muted-foreground"><p><span className="font-medium text-foreground">{value.toLocaleString()}</span> search{value !== 1 ? "es" : ""}</p><p>{pct}% of weekly total</p></div>
    </div>
  );
}

function SearchActivityChart({ data }: { data: DashboardStats["searchesByDay"] | undefined }) {
  if (!data?.length) return <EmptyState message="No search activity recorded." />;
  const maxCount = Math.max(...data.map((d) => d.count), 0), yTicks = computeYTicks(maxCount), yMax = yTicks[yTicks.length - 1];
  const formatted = data.map((d) => ({ ...d, date: d.date.slice(5) })), total = data.reduce((s, d) => s + d.count, 0);
  return (
    <ResponsiveContainer width="100%" height={data.length > 10 ? 220 : 190}>
      <LineChart data={formatted} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.35} vertical={false} />
        <XAxis dataKey="date" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={{ stroke: "hsl(var(--border))" }} />
        <YAxis ticks={yTicks} domain={[0, yMax]} allowDecimals={false} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} width={yMax >= 100 ? 40 : 32} tickFormatter={(v) => v.toLocaleString()} />
        <Tooltip cursor={{ stroke: "hsl(var(--border))", strokeWidth: 1, strokeDasharray: "3 3" }} content={<SearchTooltip total={total} /> as any} />
        <Line type="monotone" dataKey="count" stroke="#3B82F6" strokeWidth={2.5} dot={{ r: 3.5, fill: "#3B82F6", strokeWidth: 0 }} activeDot={{ r: 5, fill: "#3B82F6", strokeWidth: 2, stroke: "#fff" }} isAnimationActive animationDuration={500} animationEasing="ease-out" />
      </LineChart>
    </ResponsiveContainer>
  );
}

function TemplateTable({ data }: { data: DashboardStats["templateStats"] | undefined }) {
  if (!data?.length) return <EmptyState message="No templates found in this repository." />;
  const total = data.reduce((s, t) => s + t.count, 0);
  return (
    <div className="overflow-hidden rounded-lg border border-border">
      <table className="w-full text-sm">
        <thead><tr className="bg-muted/50"><th className="text-left px-3 py-2.5 font-semibold text-foreground w-6" /><th className="text-left px-3 py-2.5 font-semibold text-foreground">Template Name</th><th className="text-right px-3 py-2.5 font-semibold text-foreground">Documents</th><th className="text-right px-3 py-2.5 font-semibold text-foreground w-24">Share</th></tr></thead>
        <tbody>
          {data.map((t, i) => { const pct = total > 0 ? Math.round((t.count / total) * 100) : 0; return (
            <tr key={t.name} className="border-t border-border hover:bg-muted/30 transition-colors">
              <td className="px-3 py-2.5"><span className="inline-block w-2.5 h-2.5 rounded-full" style={{ background: PALETTE[i % PALETTE.length] }} /></td>
              <td className="px-3 py-2.5 text-foreground font-medium">{t.name}</td><td className="px-3 py-2.5 text-right font-semibold text-foreground tabular-nums">{t.count.toLocaleString()}</td>
              <td className="px-3 py-2.5 text-right text-muted-foreground tabular-nums">{pct}%</td>
            </tr>
          ); })}
        </tbody>
      </table>
    </div>
  );
}

// ══════════════════════════════════════════════════════════════════════════
// NEW WIDGET: Document Type Distribution (horizontal bar chart)
// ══════════════════════════════════════════════════════════════════════════

function DocTypeTooltip({ active, payload }: { active?: boolean; payload?: any[] }) {
  if (!active || !payload?.length) return null;
  const p = payload[0]; const name = p.payload.name as string; const value = Number(p.value ?? 0); const color = p.payload.color as string;
  return (
    <div className="rounded-lg border bg-popover text-popover-foreground shadow-lg px-3 py-2 text-xs" style={{ borderColor: "hsl(var(--border))" }}>
      <p className="font-semibold mb-1" style={{ color }}>{name}</p>
      <p className="text-muted-foreground"><span className="font-medium text-foreground">{value.toLocaleString()}</span> document{value !== 1 ? "s" : ""}</p>
    </div>
  );
}

function DocTypeChart({ data }: { data: DashboardStats["templateStats"] | undefined }) {
  if (!data?.length) return <EmptyState message="No document types available." />;
  const sorted = [...data].sort((a, b) => a.count - b.count);
  const chartData = sorted.map((t, i) => ({ ...t, color: PALETTE[i % PALETTE.length] }));
  const maxCount = Math.max(...sorted.map((d) => d.count), 0);
  const yTicks = computeYTicks(maxCount); const xMax = yTicks[yTicks.length - 1];
  return (
    <ResponsiveContainer width="100%" height={Math.min(520, 40 + sorted.length * 28)}>
      <BarChart data={chartData} layout="vertical" margin={{ top: 4, right: 24, bottom: 4, left: 0 }} barCategoryGap="20%">
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.35} horizontal={false} />
        <XAxis type="number" ticks={yTicks} domain={[0, xMax]} allowDecimals={false} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} tickFormatter={(v) => v.toLocaleString()} />
        <YAxis type="category" dataKey="name" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} width={140} tickFormatter={(v) => truncateLabel(String(v), 20)} />
        <Tooltip cursor={{ fill: "hsl(var(--muted))", opacity: 0.3 }} content={<DocTypeTooltip /> as any} />
        <Bar dataKey="count" radius={[0, 5, 5, 0]} maxBarSize={22} isAnimationActive animationDuration={500} animationEasing="ease-out">
          {chartData.map((entry, i) => <Cell key={i} fill={entry.color} />)}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}

// ══════════════════════════════════════════════════════════════════════════
// NEW WIDGET: Recently Created Documents
// ══════════════════════════════════════════════════════════════════════════

function RecentCreatedDocs({ docs, templates }: { docs: DocEntry[]; templates: string[] }) {
  const cols: Col<DocEntry>[] = [
    { key: "name", label: "Document", width: "w-48", sortable: true, render: (r) => <span className="font-medium text-foreground">{r.name}</span> },
    { key: "templateName", label: "Type", width: "w-28", sortable: true, render: (r) => <span className="text-xs bg-muted px-2 py-0.5 rounded-full">{r.templateName || "Untitled"}</span> },
    { key: "creator", label: "Created By", width: "w-28", sortable: true },
    { key: "creationTime", label: "Created", width: "w-40", sortable: true, render: (r) => <span className="text-muted-foreground text-xs">{r.creationTime ? formatDate(r.creationTime) : "N/A"}</span> },
    { key: "fullPath", label: "Location", sortable: true, render: (r) => <span className="text-muted-foreground text-xs truncate max-w-xs">{r.fullPath}</span> },
  ];
  return <DataTable columns={cols} rows={docs} searchPlaceholder="Search documents..." filterOptions={templates} filterLabel="template" dateField="creationTime" pageSizes={[5, 10, 20]} maxRows={30} emptyMessage="No recently created documents." />;
}

// ══════════════════════════════════════════════════════════════════════════
// NEW WIDGET: Recently Modified Documents
// ══════════════════════════════════════════════════════════════════════════

function RecentModifiedDocs({ docs, templates }: { docs: DocEntry[]; templates: string[] }) {
  const cols: Col<DocEntry>[] = [
    { key: "name", label: "Document", width: "w-48", sortable: true, render: (r) => <span className="font-medium text-foreground">{r.name}</span> },
    { key: "templateName", label: "Type", width: "w-28", sortable: true, render: (r) => <span className="text-xs bg-muted px-2 py-0.5 rounded-full">{r.templateName || "Untitled"}</span> },
    { key: "creator", label: "Last Modified By", width: "w-28", sortable: true },
    { key: "lastModifiedTime", label: "Modified", width: "w-40", sortable: true, render: (r) => <span className="text-muted-foreground text-xs">{r.lastModifiedTime ? formatDate(r.lastModifiedTime) : "N/A"}</span> },
    { key: "fullPath", label: "Location", sortable: true, render: (r) => <span className="text-muted-foreground text-xs truncate max-w-xs">{r.fullPath}</span> },
  ];
  return <DataTable columns={cols} rows={docs} searchPlaceholder="Search documents..." filterOptions={templates} filterLabel="template" dateField="lastModifiedTime" pageSizes={[5, 10, 20]} maxRows={30} emptyMessage="No recently modified documents." />;
}

// ══════════════════════════════════════════════════════════════════════════
// NEW WIDGET: Documents by User Activity (computed client-side)
// ══════════════════════════════════════════════════════════════════════════

type UserRow = { name: string; created: number; modified: number; total: number; lastActivity: string };

function UserActivityWidget({ data }: { data: UserRow[] }) {
  const cols: Col<UserRow>[] = [
    { key: "name", label: "User", width: "w-40", sortable: true, render: (r) => <span className="font-medium text-foreground flex items-center gap-1.5"><User className="w-3.5 h-3.5 text-muted-foreground" />{r.name}</span> },
    { key: "created", label: "Created", width: "w-20", sortable: true, render: (r) => <span className="tabular-nums text-foreground">{r.created}</span> },
    { key: "modified", label: "Modified", width: "w-20", sortable: true, render: (r) => <span className="tabular-nums text-foreground">{r.modified}</span> },
    { key: "total", label: "Total", width: "w-20", sortable: true, render: (r) => <span className="tabular-nums font-semibold text-foreground">{r.total}</span> },
    { key: "lastActivity", label: "Last Activity", width: "w-40", sortable: true, render: (r) => <span className="text-muted-foreground text-xs">{formatTimeAgo(r.lastActivity)}</span> },
  ];
  return <DataTable columns={cols} rows={data} searchPlaceholder="Search users..." pageSizes={[5, 10, 20]} maxRows={25} emptyMessage="No user activity recorded." />;
}

// ══════════════════════════════════════════════════════════════════════════
// NEW: Export Dashboard Reports (PDF / Excel / CSV)
// ══════════════════════════════════════════════════════════════════════════

type ExportFormat = "pdf" | "excel" | "csv";

function useDashboardExport(stats: DashboardStats | undefined) {
  const { toast } = useToast();

  const buildExportData = useMemo(() => {
    if (!stats) return null;
    const now = new Date().toISOString();
    return {
      summary: [
        ["Metric", "Value"],
        ["Repository ID", stats.repositoryId || "N/A"],
        ["Status", stats.isLive ? "Connected" : "Disconnected"],
        ["Total Folders", stats.totalFolders.toString()],
        ["Total Documents", stats.totalDocuments.toString()],
        ["Total Templates", stats.totalTemplates.toString()],
        ["Documents with Template", stats.docsWithTemplate.toString()],
        ["Documents without Template", stats.docsWithoutTemplate.toString()],
        ["Total Searches", stats.totalSearches.toString()],
      ],
      templateStats: [["Template Name", "Document Count"], ...stats.templateStats.map((t) => [t.name, t.count.toString()])],
      rootFolders: [["Folder", "Documents", "Sub-folders"], ...stats.rootFolders.map((f) => [f.name, f.documents.toString(), f.folders.toString()])],
      searchesByDay: [["Date", "Search Count"], ...stats.searchesByDay.map((s) => [s.date, s.count.toString()])],
      topSearches: [["Query", "Count"], ...stats.topSearches.map((s) => [s.query, s.count.toString()])],
      recentDocs: [["ID", "Name", "Path", "Template", "Creator", "Created", "Modified"], ...stats.recentDocs.map((d) => [String(d.id), d.name, d.fullPath, d.templateName, d.creator, d.creationTime || "", d.lastModifiedTime || ""])],
      modifiedDocs: [["ID", "Name", "Path", "Template", "Creator", "Created", "Modified"], ...stats.modifiedDocs.map((d) => [String(d.id), d.name, d.fullPath, d.templateName, d.creator, d.creationTime || "", d.lastModifiedTime || ""])],
      userActivity: [["User", "Created", "Modified", "Total", "Last Activity"], ...computeUserActivity(stats.recentDocs, stats.modifiedDocs).map((u) => [u.name, String(u.created), String(u.modified), String(u.total), u.lastActivity])],
      generatedAt: now,
    };
  }, [stats]);

  const exportReport = async (format: ExportFormat) => {
    if (!buildExportData) { toast({ title: "Export Failed", description: "No data available to export.", variant: "destructive" }); return; }
    const { summary, templateStats, rootFolders, searchesByDay, topSearches, recentDocs, modifiedDocs, userActivity, generatedAt } = buildExportData;
    const dateLabel = new Date(generatedAt).toLocaleString("en-US", { year: "numeric", month: "short", day: "numeric" });

    try {
      if (format === "pdf") {
        const { jsPDF } = await import("jspdf");
        const autoTable = (await import("jspdf-autotable")).default;
        const doc = new jsPDF({ orientation: "portrait", unit: "mm", format: "a4" });
        doc.setFontSize(18); doc.text("GovSearch AI - Dashboard Report", 14, 20);
        doc.setFontSize(10); doc.text(`Generated: ${dateLabel}`, 14, 28);
        let y = 36;
        const addSection = (title: string, rows: string[][]) => {
          if (y > 260) { doc.addPage(); y = 20; }
          doc.setFontSize(12); doc.text(title, 14, y); y += 6;
          autoTable(doc, { startY: y, head: [rows[0]], body: rows.slice(1), theme: "striped", styles: { fontSize: 9 }, headStyles: { fillColor: [59, 130, 246] } });
          y = (doc as any).lastAutoTable?.finalY + 8 || y + 20;
        };
        addSection("Repository Summary", summary);
        addSection("Document Types", templateStats);
        addSection("Root Folders", rootFolders);
        addSection("Search Activity by Day", searchesByDay);
        addSection("Top Search Queries", topSearches);
        addSection("Recently Created Documents", recentDocs);
        addSection("Recently Modified Documents", modifiedDocs);
        addSection("User Activity", userActivity);
        doc.save(`govsearch-dashboard-${dateLabel}.pdf`);
        toast({ title: "PDF Exported", description: "Dashboard report downloaded." });
      } else if (format === "excel") {
        const XLSX = await import("xlsx");
        const wb = XLSX.utils.book_new();
        const addSheet = (name: string, rows: string[][]) => {
          const ws = XLSX.utils.aoa_to_sheet(rows);
          XLSX.utils.book_append_sheet(wb, ws, name.slice(0, 31));
        };
        addSheet("Summary", summary);
        addSheet("Doc Types", templateStats);
        addSheet("Folders", rootFolders);
        addSheet("Searches", searchesByDay);
        addSheet("Top Queries", topSearches);
        addSheet("Recent Created", recentDocs);
        addSheet("Recent Modified", modifiedDocs);
        addSheet("User Activity", userActivity);
        XLSX.writeFile(wb, `govsearch-dashboard-${dateLabel}.xlsx`);
        toast({ title: "Excel Exported", description: "Dashboard report downloaded." });
      } else {
        const csv = (rows: string[][]) => rows.map((r) => r.map((c) => `"${String(c).replace(/"/g, "\"\"")}"`).join(",")).join("\n");
        const blob = new Blob([
          csv(summary) + "\n\n" + csv(templateStats) + "\n\n" + csv(rootFolders) + "\n\n" + csv(searchesByDay) + "\n\n" + csv(topSearches) + "\n\n" + csv(recentDocs) + "\n\n" + csv(modifiedDocs) + "\n\n" + csv(userActivity)
        ], { type: "text/csv" });
        const a = document.createElement("a"); a.href = URL.createObjectURL(blob); a.download = `govsearch-dashboard-${dateLabel}.csv`; a.click(); URL.revokeObjectURL(a.href);
        toast({ title: "CSV Exported", description: "Dashboard report downloaded." });
      }
    } catch (e) {
      console.error(e);
      toast({ title: "Export Failed", description: String(e), variant: "destructive" });
    }
  };

  return { exportReport };
}
// ══════════════════════════════════════════════════════════════════════════
// Main Dashboard Page
// ══════════════════════════════════════════════════════════════════════════

export default function Dashboard() {
  const { data: stats, isLoading } = useQuery<DashboardStats>({ queryKey: ["/api/dashboard/stats"] });
  const { exportReport } = useDashboardExport(stats);

  const templates = useMemo(() => {
    if (!stats?.templateStats) return [];
    return [...new Set(stats.templateStats.map((t) => t.name))];
  }, [stats]);

  const userActivity = useMemo(() => {
    if (!stats) return [];
    return computeUserActivity(stats.recentDocs || [], stats.modifiedDocs || []);
  }, [stats]);

  const createdPeriod = useMemo(() => {
    if (!stats?.recentDocs?.length) return null;
    return countDocsByPeriod(stats.recentDocs, "creationTime");
  }, [stats]);

  const modifiedPeriod = useMemo(() => {
    if (!stats?.modifiedDocs?.length) return null;
    return countDocsByPeriod(stats.modifiedDocs, "lastModifiedTime");
  }, [stats]);

  const isConnected = stats?.isLive;
  const health = stats?.health;
  const statusText = isConnected ? "Connected" : "Disconnected";
  const StatusIcon = isConnected ? Wifi : WifiOff;
  const statusColor = isConnected ? "text-emerald-500" : "text-red-500";

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-[1400px] mx-auto px-4 sm:px-6 py-6 space-y-6">
        {/* Header */}
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="h-10 w-1.5 rounded-full bg-primary" />
            <div>
              <h1 className="text-2xl font-bold text-foreground">Analytics Dashboard</h1>
              <p className="text-sm text-muted-foreground">Analytics, health metrics and system overview</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            {stats && (
              <div className="flex items-center gap-2 text-sm">
                <StatusIcon className={`w-4 h-4 ${statusColor}`} />
                <span className={statusColor}>{statusText}</span>
              </div>
            )}
            <Button variant="outline" size="sm" onClick={() => window.location.reload()}><RefreshCw className="w-4 h-4 mr-1.5" /> Refresh</Button>
          </div>
        </div>

        {/* Export bar */}
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-muted-foreground mr-1">Export report:</span>
          <Button variant="outline" size="sm" onClick={() => exportReport("pdf")}><Printer className="w-4 h-4 mr-1.5" /> PDF</Button>
          <Button variant="outline" size="sm" onClick={() => exportReport("excel")}><FileSpreadsheet className="w-4 h-4 mr-1.5" /> Excel</Button>
          <Button variant="outline" size="sm" onClick={() => exportReport("csv")}><FileCode className="w-4 h-4 mr-1.5" /> CSV</Button>
        </div>

        {isLoading && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            {[1,2,3,4].map((i) => (<Skeleton key={i} className="h-[140px] rounded-xl" />))}
          </div>
        )}

        {!isLoading && stats && (
          <>
            {/* Stat cards */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
              <StatCard icon={Database} label="Total Documents" labelAr="إجمالي الوثائق" value={stats.totalDocuments} colorClass="bg-blue-500/15" iconClass="text-blue-500" sub={`${stats.totalFolders} folders`} />
              <StatCard icon={Layers} label="Templates" labelAr="القوالب" value={stats.totalTemplates} colorClass="bg-amber-500/15" iconClass="text-amber-500" sub={`${stats.docsWithTemplate} used`} />
              <StatCard icon={Search} label="Total Searches" labelAr="إجمالي البحث" value={stats.totalSearches} colorClass="bg-emerald-500/15" iconClass="text-emerald-500" sub="This week" />
              <StatCard icon={CheckCircle2} label="Docs with Template" labelAr="وثائق بقالب" value={stats.docsWithTemplate} colorClass="bg-violet-500/15" iconClass="text-violet-500" sub={`${stats.docsWithoutTemplate} without`} />
            </div>

            {/* Row 1: Doc Type Distribution + Template Distribution */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <ChartCard title="Document Type Distribution" sub="Documents by template / type" badge={<SmallBadge color="bg-blue-500/10 text-blue-500">{stats.templateStats.length} types</SmallBadge>}>
                <DocTypeChart data={stats.templateStats} />
              </ChartCard>
              <ChartCard title="Template Usage" sub="Share of documents per template" badge={<SmallBadge color="bg-amber-500/10 text-amber-500">{stats.totalTemplates} templates</SmallBadge>}>
                <TemplatePieChart data={stats.templateStats} />
              </ChartCard>
            </div>

            {/* Row 2: Root Folders + Template Table */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <ChartCard title="Root Folders" sub="Documents per top-level folder" badge={<SmallBadge color="bg-emerald-500/10 text-emerald-500">{stats.rootFolders.length} folders</SmallBadge>}>
                <RootFoldersChart data={stats.rootFolders} />
              </ChartCard>
              <ChartCard title="Template Breakdown" sub="Detailed template usage table">
                <TemplateTable data={stats.templateStats} />
              </ChartCard>
            </div>

            {/* Row 3: Search Activity + Top Searches */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <ChartCard title="Search Activity" sub="Daily search volume this week" badge={<SmallBadge color="bg-blue-500/10 text-blue-500">{stats.totalSearches} total</SmallBadge>}>
                <SearchActivityChart data={stats.searchesByDay} />
              </ChartCard>
              <ChartCard title="Top Search Queries" sub="Most frequent searches this week" badge={<SmallBadge color="bg-purple-500/10 text-purple-500">{stats.topSearches.length} queries</SmallBadge>}>
                <div className="space-y-2 max-h-[260px] overflow-y-auto pr-1">
                  {stats.topSearches.map((s, i) => (
                    <div key={s.query} className="flex items-center gap-3 p-2.5 rounded-lg border border-border hover:bg-muted/50 transition-colors">
                      <span className="flex-shrink-0 w-6 h-6 rounded-full bg-primary/10 text-primary flex items-center justify-center text-xs font-bold">{i + 1}</span>
                      <span className="flex-1 text-sm text-foreground truncate">{s.query}</span>
                      <span className="text-sm font-semibold text-foreground tabular-nums">{s.count}</span>
                    </div>
                  ))}
                  {!stats.topSearches.length && <EmptyState message="No search queries recorded." />}
                </div>
              </ChartCard>
            </div>

            {/* Row 4: Recently Created Documents */}
            <ChartCard title="Recently Created Documents" sub={createdPeriod ? `Today: ${createdPeriod.today} · This week: ${createdPeriod.thisWeek} · This month: ${createdPeriod.thisMonth}` : undefined} badge={<SmallBadge color="bg-teal-500/10 text-teal-500">{stats.recentDocs?.length ?? 0} docs</SmallBadge>}>
              <RecentCreatedDocs docs={stats.recentDocs || []} templates={templates} />
            </ChartCard>

            {/* Row 5: Recently Modified Documents */}
            <ChartCard title="Recently Modified Documents" sub={modifiedPeriod ? `Today: ${modifiedPeriod.today} · This week: ${modifiedPeriod.thisWeek} · This month: ${modifiedPeriod.thisMonth}` : undefined} badge={<SmallBadge color="bg-orange-500/10 text-orange-500">{stats.modifiedDocs?.length ?? 0} docs</SmallBadge>}>
              <RecentModifiedDocs docs={stats.modifiedDocs || []} templates={templates} />
            </ChartCard>

            {/* Row 6: User Activity */}
            <ChartCard title="Documents by User Activity" sub="Created and modified documents per user" badge={<SmallBadge color="bg-indigo-500/10 text-indigo-500">{userActivity.length} users</SmallBadge>}>
              <UserActivityWidget data={userActivity} />
            </ChartCard>

            {/* System Health */}
            {health && (
              <ChartCard title="System Health" sub="Laserfiche connection status" badge={
                health.status === "connected" ? <SmallBadge color="bg-emerald-500/10 text-emerald-500"><ShieldCheck className="w-3 h-3 inline mr-1" />Healthy</SmallBadge> :
                health.status === "reconnecting" ? <SmallBadge color="bg-amber-500/10 text-amber-500"><ShieldAlert className="w-3 h-3 inline mr-1" />Reconnecting</SmallBadge> :
                <SmallBadge color="bg-red-500/10 text-red-500"><AlertTriangle className="w-3 h-3 inline mr-1" />Disconnected</SmallBadge>
              }>
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                  <div className="p-3 rounded-lg border border-border bg-muted/30"><div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Activity className="w-3.5 h-3.5" /> Status</div><p className="font-semibold text-foreground">{health.status}</p></div>
                  <div className="p-3 rounded-lg border border-border bg-muted/30"><div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Database className="w-3.5 h-3.5" /> Repository</div><p className="font-semibold text-foreground">{health.repositoryId || "N/A"}</p></div>
                  <div className="p-3 rounded-lg border border-border bg-muted/30"><div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Globe className="w-3.5 h-3.5" /> Server URL</div><p className="font-semibold text-foreground text-xs truncate">{health.serverUrl || "N/A"}</p></div>
                  <div className="p-3 rounded-lg border border-border bg-muted/30"><div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><User className="w-3.5 h-3.5" /> Username</div><p className="font-semibold text-foreground">{health.username || "N/A"}</p></div>
                  <div className="p-3 rounded-lg border border-border bg-muted/30"><div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Clock className="w-3.5 h-3.5" /> Last Refresh</div><p className="font-semibold text-foreground">{formatTimeAgo(health.lastRefresh)}</p></div>
                  <div className="p-3 rounded-lg border border-border bg-muted/30"><div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Timer className="w-3.5 h-3.5" /> Scan Duration</div><p className="font-semibold text-foreground">{formatDuration(health.scanDurationMs)}</p></div>
                </div>
              </ChartCard>
            )}

            {/* Connection Status */}
            {!isConnected && (
              <div className="rounded-xl border border-amber-500/20 bg-amber-500/5 p-4 flex items-center gap-3">
                <AlertTriangle className="w-5 h-5 text-amber-500 flex-shrink-0" />
                <div><p className="text-sm font-medium text-foreground">Not connected to Laserfiche</p><p className="text-xs text-muted-foreground">Configure your Laserfiche server in the <a href="/laserfiche/settings" className="text-primary underline">Settings page</a> to see repository data.</p></div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

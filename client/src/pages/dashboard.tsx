import { useQuery } from "@tanstack/react-query";
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Cell,
  PieChart, Pie, Legend, LineChart, Line,
} from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import {
  RefreshCw, FileText, FolderOpen, Layers, CheckCircle2, AlertCircle,
  Database, TrendingUp, Search, Info,
} from "lucide-react";

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
  totalSearches: number;
  searchesByDay: Array<{ date: string; count: number }>;
  topSearches: Array<{ query: string; count: number }>;
};

/**
 * Professional 24-color palette. Every bar/pie slice gets a deterministic,
 * stable color (index % PALETTE.length). Colors are hand-picked for
 * distinguishability in both light and dark modes.
 */
const PALETTE = [
  "#3B82F6", "#14B8A6", "#F59E0B", "#8B5CF6", "#EF4444",
  "#22C55E", "#F97316", "#06B6D4", "#EC4899", "#84CC16",
  "#A855F7", "#F43F5E", "#10B981", "#FBBF24", "#60A5FA",
  "#6366F1", "#0EA5E9", "#D946EF", "#C2410C", "#0891B2",
  "#7C3AED", "#E11D48", "#2563EB", "#16A34A",
];
const OTHERS_COLOR = "#94A3B8";
const TOP_N = 15;

/**
 * Aggregate a folder list into the Top N by document count plus an "Others"
 * bar that sums all remaining folders. Returns the original list unchanged
 * if it has N or fewer entries.
 */
function aggregateTopN(
  data: DashboardStats["rootFolders"],
  n = TOP_N
): Array<{ name: string; documents: number; folders: number; isOthers?: boolean }> {
  if (data.length <= n) return data;
  const sorted = [...data].sort((a, b) => b.documents - a.documents);
  const top = sorted.slice(0, n);
  const rest = sorted.slice(n);
  const othersDocuments = rest.reduce((s, f) => s + f.documents, 0);
  const othersFolders = rest.reduce((s, f) => s + f.folders, 0);
  return [
    ...top,
    { name: `Others (${rest.length})`, documents: othersDocuments, folders: othersFolders, isOthers: true },
  ];
}

/**
 * Compute clean integer Y-axis ticks for a given maximum value.
 *
 * Max=7   → [0,1,2,3,4,5,6,7]
 * Max=23  → [0,5,10,15,20,25]
 * Max=43  → [0,10,20,30,40,50]
 * Max=260 → [0,50,100,150,200,250,300]
 */
function computeYTicks(maxValue: number): number[] {
  if (!maxValue || maxValue <= 0) return [0, 1];
  if (maxValue <= 10) {
    const ticks: number[] = [];
    for (let i = 0; i <= maxValue; i++) ticks.push(i);
    return ticks;
  }
  const rawStep = maxValue / 5;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)));
  const normalized = rawStep / magnitude;
  let step: number;
  if (normalized <= 1)        step = magnitude;
  else if (normalized <= 2)   step = 2 * magnitude;
  else if (normalized <= 7.5) step = 5 * magnitude;
  else                         step = 10 * magnitude;
  const ceiling = Math.ceil(maxValue / step) * step;
  const ticks: number[] = [];
  for (let t = 0; t <= ceiling; t += step) ticks.push(t);
  return ticks;
}

function truncateLabel(name: string, maxLen: number): string {
  return name.length > maxLen ? name.slice(0, maxLen - 1) + "…" : name;
}

const TOOLTIP_STYLE = {
  contentStyle: {
    background: "hsl(var(--popover))",
    border: "1px solid hsl(var(--border))",
    borderRadius: "8px",
    fontSize: "12px",
    boxShadow: "0 4px 16px rgba(0,0,0,0.12)",
    padding: "8px 12px",
  },
  itemStyle: { color: "hsl(var(--popover-foreground))" },
  labelStyle: { color: "hsl(var(--muted-foreground))", fontWeight: 500 },
  cursor: { fill: "hsl(var(--muted))", opacity: 0.35 },
};

// ─── Shared sub-components ──────────────────────────────────────────────────

function StatCard({
  icon: Icon, label, labelAr, value,
  colorClass = "bg-primary/15", iconClass = "text-primary", sub,
}: {
  icon: any; label: string; labelAr: string; value: string | number;
  colorClass?: string; iconClass?: string; sub?: string;
}) {
  return (
    <div
      className="bg-card border border-border rounded-xl p-5 shadow-sm"
      data-testid={`stat-card-${label.replace(/\s+/g, "-").toLowerCase()}`}
    >
      <div className="flex items-start justify-between gap-3 mb-4">
        <div className={`w-11 h-11 rounded-lg ${colorClass} flex items-center justify-center flex-shrink-0`}>
          <Icon className={`w-5 h-5 ${iconClass}`} />
        </div>
        {sub && (
          <span className="text-xs text-muted-foreground bg-muted px-2 py-0.5 rounded-full">
            {sub}
          </span>
        )}
      </div>
      <p className="text-3xl font-bold text-foreground mb-1 tabular-nums">
        {typeof value === "number" ? value.toLocaleString() : value}
      </p>
      <p className="text-sm text-muted-foreground leading-tight">{label}</p>
      <p className="text-xs text-muted-foreground/70 font-arabic mt-0.5" dir="rtl">{labelAr}</p>
    </div>
  );
}

function SectionHeader({ title, sub }: { title: string; sub?: string }) {
  return (
    <div className="flex items-center gap-2 mb-4">
      <div className="h-5 w-1 rounded-full bg-primary flex-shrink-0" />
      <h2 className="text-sm font-semibold text-foreground">{title}</h2>
      {sub && <span className="text-xs text-muted-foreground truncate">{sub}</span>}
    </div>
  );
}

function ChartCard({ title, sub, badge, children }: {
  title: string; sub?: string; badge?: React.ReactNode; children: React.ReactNode;
}) {
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
      <AlertCircle className="w-8 h-8 opacity-30" />
      <p className="text-sm">{message}</p>
    </div>
  );
}

// ─── Chart components ────────────────────────────────────────────────────────

type AggFolder = { name: string; documents: number; folders: number; isOthers?: boolean };

/** Responsive chart-height based on bar count so bars never overlap. */
function computeBarChartHeight(count: number): number {
  if (count <= 4)  return 260;
  if (count <= 8)  return 320;
  if (count <= 12) return 380;
  if (count <= 20) return 440;
  return Math.min(600, 40 + count * 22);
}

/** Compute max bar width so bars stay proportional and never collide. */
function computeMaxBarSize(count: number): number {
  if (count <= 4)  return 64;
  if (count <= 8)  return 48;
  if (count <= 14) return 34;
  if (count <= 24) return 26;
  return 20;
}

/** Smart label length: fewer bars → longer labels, many bars → shorter. */
function computeLabelMaxLen(count: number): number {
  if (count <= 4)  return 24;
  if (count <= 8)  return 16;
  if (count <= 14) return 12;
  return 9;
}

/** Rich tooltip for the folder bar chart. */
function FolderTooltip({
  active,
  payload,
  label,
  totalDocs,
  lookup,
}: {
  active?: boolean;
  payload?: any[];
  label?: string;
  totalDocs: number;
  lookup: Record<string, { name: string; documents: number; isOthers: boolean }>;
}) {
  if (!active || !payload?.length) return null;
  const item = lookup[label ?? ""];
  if (!item) return null;
  const pct = totalDocs > 0 ? Math.round((item.documents / totalDocs) * 100) : 0;
  const color = item.isOthers
    ? OTHERS_COLOR
    : PALETTE[Object.keys(lookup).indexOf(label ?? "") % PALETTE.length];
  return (
    <div
      className="rounded-lg border bg-popover text-popover-foreground shadow-lg px-3 py-2 text-xs"
      style={{ borderColor: "hsl(var(--border))" }}
    >
      <p className="font-semibold mb-1" style={{ color }}>
        {item.name}
      </p>
      <div className="space-y-0.5 text-muted-foreground">
        <p>
          <span className="font-medium text-foreground">{item.documents.toLocaleString()}</span>{" "}
          document{item.documents !== 1 ? "s" : ""}
        </p>
        <p>{pct}% of total documents</p>
      </div>
    </div>
  );
}

function RootFoldersChart({ data }: { data: DashboardStats["rootFolders"] | undefined }) {
  if (!data?.length) return <EmptyState message="No folder data available." />;

  const aggregated: AggFolder[] = aggregateTopN(data);
  const wasAggregated = aggregated.length < data.length + (aggregated.some((f) => f.isOthers) ? 0 : 1);
  const count = aggregated.length;
  const totalDocs = aggregated.reduce((s, f) => s + f.documents, 0);
  const maxDocs = Math.max(...aggregated.map((f) => f.documents), 0);
  const yTicks = computeYTicks(maxDocs);
  const yMax = yTicks[yTicks.length - 1];

  const chartHeight = computeBarChartHeight(count);
  const maxBarSize = computeMaxBarSize(count);
  const maxLabelLen = computeLabelMaxLen(count);

  // Label rotation: only when bars are dense enough that labels would collide.
  const labelAngle = count > 6 ? -40 : count > 3 ? -25 : 0;
  const textAnchor = count > 6 ? "end" : "middle";
  const bottomMargin = count > 6 ? 72 : count > 3 ? 48 : 28;

  const chartData = aggregated.map((f, i) => ({
    name: f.name,
    label: truncateLabel(f.name, maxLabelLen),
    documents: f.documents,
    isOthers: !!f.isOthers,
    color: f.isOthers ? OTHERS_COLOR : PALETTE[i % PALETTE.length],
  }));

  const lookup: Record<string, { name: string; documents: number; isOthers: boolean }> = {};
  for (const d of chartData) lookup[d.label] = { name: d.name, documents: d.documents, isOthers: d.isOthers };

  return (
    <>
      {wasAggregated && (
        <p className="text-xs text-muted-foreground mb-2">
          Showing top {TOP_N} folders by document count · remaining folders combined as "Others"
        </p>
      )}
      <ResponsiveContainer width="100%" height={chartHeight}>
        <BarChart
          data={chartData}
          margin={{ top: 8, right: 12, bottom: bottomMargin, left: 0 }}
          barCategoryGap={count > 12 ? "12%" : count > 6 ? "20%" : "30%"}
        >
          <CartesianGrid
            strokeDasharray="3 3"
            stroke="hsl(var(--border))"
            opacity={0.35}
            vertical={false}
          />
          <XAxis
            dataKey="label"
            tick={{ fontSize: count > 12 ? 9 : 11, fill: "hsl(var(--muted-foreground))" }}
            interval={0}
            angle={labelAngle}
            textAnchor={textAnchor}
            height={bottomMargin}
            tickLine={false}
            axisLine={{ stroke: "hsl(var(--border))" }}
          />
          <YAxis
            ticks={yTicks}
            domain={[0, yMax]}
            allowDecimals={false}
            tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
            tickLine={false}
            axisLine={false}
            width={yMax >= 1000 ? 52 : yMax >= 100 ? 40 : 32}
            tickFormatter={(v) => v.toLocaleString()}
          />
          <Tooltip
            cursor={{ fill: "hsl(var(--muted))", opacity: 0.3 }}
            content={
              <FolderTooltip
                totalDocs={totalDocs}
                lookup={lookup}
              /> as any
            }
          />
          <Bar
            dataKey="documents"
            radius={[6, 6, 0, 0]}
            maxBarSize={maxBarSize}
            isAnimationActive={true}
            animationDuration={500}
            animationEasing="ease-out"
          >
            {chartData.map((entry, i) => (
              <Cell key={i} fill={entry.color} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </>
  );
}

/** Rich tooltip for the template pie chart with percentage and count. */
function TemplateTooltip({
  active,
  payload,
}: {
  active?: boolean;
  payload?: any[];
}) {
  if (!active || !payload?.length) return null;
  const p = payload[0];
  const name = p.name as string;
  const value = Number(p.value ?? 0);
  const total = p.payload?.totalCount ?? value;
  const pct = total > 0 ? Math.round((value / total) * 100) : 0;
  const i = p.payload?.__index ?? 0;
  const color = PALETTE[i % PALETTE.length];
  return (
    <div
      className="rounded-lg border bg-popover text-popover-foreground shadow-lg px-3 py-2 text-xs"
      style={{ borderColor: "hsl(var(--border))" }}
    >
      <p className="font-semibold mb-1" style={{ color }}>
        {name}
      </p>
      <div className="space-y-0.5 text-muted-foreground">
        <p>
          <span className="font-medium text-foreground">{value.toLocaleString()}</span>{" "}
          document{value !== 1 ? "s" : ""}
        </p>
        <p>{pct}% of all templated documents</p>
      </div>
    </div>
  );
}

function TemplatePieChart({ data }: { data: DashboardStats["templateStats"] | undefined }) {
  if (!data?.length) return <EmptyState message="No template information available." />;

  const count = data.length;
  const total = data.reduce((s, t) => s + t.count, 0);

  // Responsive donut sizing based on slice count
  const outerR = count > 12 ? 70 : count > 8 ? 80 : 92;
  const innerR = count > 12 ? 38 : count > 8 ? 42 : 48;
  const chartHeight = count > 12 ? 320 : 300;

  // Enrich data with total and index for tooltip percentage calculation
  const enriched = data.map((t, i) => ({ ...t, totalCount: total, __index: i }));

  return (
    <ResponsiveContainer width="100%" height={chartHeight}>
      <PieChart>
        <Pie
          data={enriched}
          dataKey="count"
          nameKey="name"
          cx="50%"
          cy="45%"
          outerRadius={outerR}
          innerRadius={innerR}
          paddingAngle={count > 10 ? 1 : 2}
          label={({ percent }) =>
            percent > 0.04 ? `${(percent * 100).toFixed(0)}%` : ""
          }
          labelLine={false}
          isAnimationActive={true}
          animationDuration={600}
          animationEasing="ease-out"
        >
          {enriched.map((_, i) => (
            <Cell key={i} fill={PALETTE[i % PALETTE.length]} stroke="none" />
          ))}
        </Pie>
        <Tooltip
          cursor={{ fill: "transparent" }}
          content={<TemplateTooltip /> as any}
        />
        <Legend
          iconType="circle"
          iconSize={8}
          wrapperStyle={{ paddingTop: 10, fontSize: 11 }}
          formatter={(value) => (
            <span style={{ color: "hsl(var(--muted-foreground))" }}>
              {truncateLabel(String(value), 24)}
            </span>
          )}
        />
      </PieChart>
    </ResponsiveContainer>
  );
}

/** Rich tooltip for the search activity line chart. */
function SearchTooltip({
  active,
  payload,
  label,
  total,
}: {
  active?: boolean;
  payload?: any[];
  label?: string;
  total: number;
}) {
  if (!active || !payload?.length) return null;
  const value = Number(payload[0].value ?? 0);
  const pct = total > 0 ? Math.round((value / total) * 100) : 0;
  return (
    <div
      className="rounded-lg border bg-popover text-popover-foreground shadow-lg px-3 py-2 text-xs"
      style={{ borderColor: "hsl(var(--border))" }}
    >
      <p className="font-semibold mb-1 text-blue-500">
        {label}
      </p>
      <div className="space-y-0.5 text-muted-foreground">
        <p>
          <span className="font-medium text-foreground">{value.toLocaleString()}</span>{" "}
          search{value !== 1 ? "es" : ""}
        </p>
        <p>{pct}% of weekly total</p>
      </div>
    </div>
  );
}

function SearchActivityChart({ data }: { data: DashboardStats["searchesByDay"] | undefined }) {
  if (!data?.length) return <EmptyState message="No search activity recorded." />;

  const maxCount = Math.max(...data.map((d) => d.count), 0);
  const yTicks = computeYTicks(maxCount);
  const yMax = yTicks[yTicks.length - 1];
  const formatted = data.map((d) => ({ ...d, date: d.date.slice(5) }));
  const total = data.reduce((s, d) => s + d.count, 0);

  const chartHeight = data.length > 10 ? 220 : 190;

  return (
    <ResponsiveContainer width="100%" height={chartHeight}>
      <LineChart data={formatted} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
        <CartesianGrid
          strokeDasharray="3 3"
          stroke="hsl(var(--border))"
          opacity={0.35}
          vertical={false}
        />
        <XAxis
          dataKey="date"
          tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
          tickLine={false}
          axisLine={{ stroke: "hsl(var(--border))" }}
        />
        <YAxis
          ticks={yTicks}
          domain={[0, yMax]}
          allowDecimals={false}
          tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
          tickLine={false}
          axisLine={false}
          width={yMax >= 100 ? 40 : 32}
          tickFormatter={(v) => v.toLocaleString()}
        />
        <Tooltip
          cursor={{ stroke: "hsl(var(--border))", strokeWidth: 1, strokeDasharray: "3 3" }}
          content={<SearchTooltip total={total} /> as any}
        />
        <Line
          type="monotone"
          dataKey="count"
          stroke="#3B82F6"
          strokeWidth={2.5}
          dot={{ r: 3.5, fill: "#3B82F6", strokeWidth: 0 }}
          activeDot={{ r: 5, fill: "#3B82F6", strokeWidth: 2, stroke: "#fff" }}
          isAnimationActive={true}
          animationDuration={500}
          animationEasing="ease-out"
        />
      </LineChart>
    </ResponsiveContainer>
  );
}

function TemplateTable({ data }: { data: DashboardStats["templateStats"] | undefined }) {
  if (!data?.length) {
    return <EmptyState message="No templates found in this repository." />;
  }
  const total = data.reduce((s, t) => s + t.count, 0);
  return (
    <div className="overflow-hidden rounded-lg border border-border">
      <table className="w-full text-sm" data-testid="template-table">
        <thead>
          <tr className="bg-muted/50">
            <th className="text-left px-3 py-2.5 font-semibold text-foreground w-6" />
            <th className="text-left px-3 py-2.5 font-semibold text-foreground">Template Name</th>
            <th className="text-right px-3 py-2.5 font-semibold text-foreground">Documents</th>
            <th className="text-right px-3 py-2.5 font-semibold text-foreground w-24">Share</th>
          </tr>
        </thead>
        <tbody>
          {data.map((t, i) => {
            const pct = total > 0 ? Math.round((t.count / total) * 100) : 0;
            return (
              <tr
                key={t.name}
                className="border-t border-border hover:bg-muted/30 transition-colors"
              >
                <td className="px-3 py-2.5">
                  <span
                    className="inline-block w-2.5 h-2.5 rounded-full"
                    style={{ background: PALETTE[i % PALETTE.length] }}
                  />
                </td>
                <td className="px-3 py-2.5 text-foreground font-medium">{t.name}</td>
                <td className="px-3 py-2.5 text-right font-semibold text-foreground tabular-nums">
                  {t.count.toLocaleString()}
                </td>
                <td className="px-3 py-2.5 text-right text-muted-foreground tabular-nums">{pct}%</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

// ─── Page ────────────────────────────────────────────────────────────────────

export default function DashboardPage() {
  // Read optional ?repoId= from the URL. The WPF Desktop Client extension
  // injects this parameter when opening the popup so the dashboard
  // automatically uses the currently active repository.
  const urlRepoId = new URLSearchParams(window.location.search).get("repoId") || undefined;

  const {
    data: stats, isLoading, isError, error, refetch, isFetching,
  } = useQuery<DashboardStats>({
    queryKey: ["/api/dashboard/stats", urlRepoId ?? ""],
    queryFn: () =>
      fetch(`/api/dashboard/stats${urlRepoId ? `?repoId=${encodeURIComponent(urlRepoId)}` : ""}`)
        .then((r) => r.json()),
    staleTime: 2 * 60 * 1000,
  });

  if (isLoading) {
    return (
      <div className="h-full overflow-auto px-6 py-5 space-y-4">
        <Skeleton className="h-9 w-64 rounded-lg" />
        <div className="grid grid-cols-2 xl:grid-cols-5 gap-4">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-28 rounded-xl" />
          ))}
        </div>
        <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
          <Skeleton className="h-72 rounded-xl" />
          <Skeleton className="h-72 rounded-xl" />
        </div>
        <Skeleton className="h-64 rounded-xl" />
      </div>
    );
  }

  if (isError || !stats) {
    const message = error instanceof Error ? error.message : "Unable to load dashboard analytics";
    return (
      <div className="h-full overflow-auto px-6 py-5">
        <div className="max-w-3xl rounded-xl border border-destructive/40 bg-destructive/5 p-6">
          <h2 className="text-base font-semibold text-foreground mb-1">Dashboard unavailable</h2>
          <p className="text-sm text-muted-foreground mb-4">{message}</p>
          <Button onClick={() => refetch()} disabled={isFetching} data-testid="button-retry">
            <RefreshCw className="w-4 h-4 mr-2" />
            {isFetching ? "Retrying..." : "Retry"}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full overflow-auto bg-gradient-to-b from-background to-background/70">
      <div className="px-6 py-5">
        <div className="max-w-7xl space-y-5">

          {/* Header */}
          <div className="flex items-start justify-between gap-4 flex-wrap">
            <div>
              <h1 className="text-xl font-semibold text-foreground">Analytics Dashboard</h1>
              <p className="text-sm text-muted-foreground mt-0.5 font-arabic" dir="rtl">
                لوحة التحليلات
              </p>
            </div>
            <div className="flex items-center gap-2 flex-shrink-0 flex-wrap">
              {stats.isLive && stats.repositoryId && (
                <div
                  className="flex items-center gap-1.5 text-xs bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20 px-2.5 py-1 rounded-full"
                  data-testid="badge-repository"
                >
                  <Database className="w-3 h-3" />
                  <span className="font-medium">{stats.repositoryId}</span>
                </div>
              )}
              {urlRepoId && urlRepoId !== stats.repositoryId && (
                <div className="flex items-center gap-1.5 text-xs bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 px-2.5 py-1 rounded-full">
                  <Database className="w-3 h-3" />
                  <span>Active: {urlRepoId}</span>
                </div>
              )}
              {!stats.isLive && (
                <div
                  className="flex items-center gap-1.5 text-xs bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20 px-2.5 py-1 rounded-full"
                  data-testid="badge-offline"
                >
                  <AlertCircle className="w-3 h-3" />
                  <span>Not connected to Laserfiche</span>
                </div>
              )}
              <Button
                variant="outline"
                size="sm"
                onClick={() => refetch()}
                disabled={isFetching}
                data-testid="button-refresh"
              >
                <RefreshCw className={`w-4 h-4 mr-1.5 ${isFetching ? "animate-spin" : ""}`} />
                {isFetching ? "Loading..." : "Refresh"}
              </Button>
            </div>
          </div>

          {/* Not connected notice */}
          {!stats.isLive && (
            <div className="flex items-start gap-3 bg-amber-500/8 border border-amber-500/20 rounded-xl px-4 py-3">
              <Info className="w-4 h-4 text-amber-600 dark:text-amber-400 mt-0.5 flex-shrink-0" />
              <p className="text-sm text-amber-700 dark:text-amber-300">
                Dashboard is not connected to Laserfiche. Repository statistics are unavailable.
                Configure the connection in <strong>LF Settings</strong> to see live data.
              </p>
            </div>
          )}

          {/* KPI Cards */}
          <div className="grid grid-cols-2 sm:grid-cols-3 xl:grid-cols-5 gap-4">
            <StatCard
              icon={FolderOpen}
              label="Total Folders"
              labelAr="إجمالي المجلدات"
              value={stats.totalFolders ?? 0}
              colorClass="bg-blue-500/15"
              iconClass="text-blue-500"
            />
            <StatCard
              icon={FileText}
              label="Total Documents"
              labelAr="إجمالي الوثائق"
              value={stats.totalDocuments ?? 0}
              colorClass="bg-teal-500/15"
              iconClass="text-teal-500"
            />
            <StatCard
              icon={Layers}
              label="Total Templates"
              labelAr="إجمالي القوالب"
              value={stats.totalTemplates ?? 0}
              colorClass="bg-violet-500/15"
              iconClass="text-violet-500"
            />
            <StatCard
              icon={CheckCircle2}
              label="Docs with Template"
              labelAr="وثائق بها قالب"
              value={stats.docsWithTemplate ?? 0}
              colorClass="bg-emerald-500/15"
              iconClass="text-emerald-500"
            />
            <StatCard
              icon={AlertCircle}
              label="Docs without Template"
              labelAr="وثائق بدون قالب"
              value={stats.docsWithoutTemplate ?? 0}
              colorClass="bg-orange-500/15"
              iconClass="text-orange-500"
            />
          </div>

          {/* Charts Row: Root Folders + Template Distribution */}
          {stats.isLive && (
            <div className="grid grid-cols-1 xl:grid-cols-2 gap-5">
              <ChartCard
                title="Documents by Folder"
                sub={`Root-level folders · live from ${stats.repositoryId}`}
              >
                <RootFoldersChart data={stats.rootFolders ?? []} />
              </ChartCard>

              <ChartCard
                title="Template Distribution"
                sub="Auto-discovered · each template different color"
              >
                <TemplatePieChart data={stats.templateStats ?? []} />
              </ChartCard>
            </div>
          )}

          {/* Template Detail Table */}
          {stats.isLive && (
            <ChartCard
              title="Template Statistics"
              sub={
                (stats.templateStats?.length ?? 0) > 0
                  ? `${stats.templateStats.length} template${stats.templateStats.length !== 1 ? "s" : ""} with assigned documents`
                  : "No templates assigned to documents"
              }
            >
              <TemplateTable data={stats.templateStats ?? []} />
            </ChartCard>
          )}

          {/* GovSearch Search Activity */}
          <div className="grid grid-cols-1 xl:grid-cols-3 gap-5">
            <div className="xl:col-span-2">
              <ChartCard
                title="GovSearch Search Activity"
                sub="Based on searches performed inside GovSearch · last 7 days"
                badge={
                  <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 px-2 py-0.5 rounded-full">
                    <Info className="w-3 h-3" />
                    GovSearch audit log
                  </span>
                }
              >
                <div className="flex items-center gap-4 mb-3">
                  <div className="flex items-center gap-1.5">
                    <TrendingUp className="w-4 h-4 text-blue-500" />
                    <span className="text-2xl font-bold text-foreground tabular-nums">
                      {(stats.totalSearches ?? 0).toLocaleString()}
                    </span>
                  </div>
                  <span className="text-sm text-muted-foreground">searches via GovSearch</span>
                </div>
                <SearchActivityChart data={stats.searchesByDay ?? []} />
              </ChartCard>
            </div>

            <ChartCard
              title="Top Queries"
              sub="Based on searches performed inside GovSearch"
              badge={
                <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 px-2 py-0.5 rounded-full">
                  <Info className="w-3 h-3" />
                  GovSearch audit log
                </span>
              }
            >
              {!(stats.topSearches?.length) ? (
                <EmptyState message="No search history yet." />
              ) : (
                <div className="space-y-1" data-testid="top-searches-list">
                  {stats.topSearches.map((s, i) => (
                    <div
                      key={s.query}
                      className="flex items-center gap-2.5 py-1.5 px-1 rounded-lg hover:bg-muted/40 transition-colors"
                      data-testid={`top-search-${i}`}
                    >
                      <Search className="w-3.5 h-3.5 text-muted-foreground flex-shrink-0" />
                      <span
                        className="flex-1 text-sm text-foreground truncate"
                        title={s.query}
                        dir="auto"
                      >
                        {s.query}
                      </span>
                      <span className="text-xs font-semibold text-muted-foreground bg-muted px-1.5 py-0.5 rounded tabular-nums flex-shrink-0">
                        {s.count}×
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </ChartCard>
          </div>

          {/* Widget Audit Table */}
          <WidgetAuditTable isLive={stats.isLive} />

        </div>
      </div>
    </div>
  );
}

// ─── Widget Audit Table ───────────────────────────────────────────────────────

type AuditRow = {
  widget: string;
  dataSource: string;
  origin: "laserfiche" | "govsearch" | "both";
  liveOnly: boolean;
  notes: string;
};

const AUDIT_ROWS: AuditRow[] = [
  {
    widget: "Total Folders",
    dataSource: "LF REST API — recursive folder scan",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Counts subfolders at all depths via scanFolder()",
  },
  {
    widget: "Total Documents",
    dataSource: "LF REST API — recursive folder scan",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Counts electronic documents at all depths",
  },
  {
    widget: "Total Templates",
    dataSource: "LF REST API — /TemplateDefinitions",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Counts template definitions from the repository schema",
  },
  {
    widget: "Docs with Template",
    dataSource: "LF REST API — templateName field on folder children",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Counted during the folder scan pass; v1 TemplateName / v2 templateName",
  },
  {
    widget: "Docs without Template",
    dataSource: "Derived: Total Documents − Docs with Template",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Computed, not a separate API call",
  },
  {
    widget: "Documents by Folder",
    dataSource: "LF REST API — recursive folder scan",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Top 15 root-level folders by document count; Others bar aggregates rest",
  },
  {
    widget: "Template Distribution (pie)",
    dataSource: "LF REST API — templateName field on folder children",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Same scan pass as template counting; one slice per template",
  },
  {
    widget: "Template Statistics (table)",
    dataSource: "LF REST API — templateName field on folder children",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Sorted by document count; shows % share per template",
  },
  {
    widget: "GovSearch Search Activity",
    dataSource: "GovSearch in-process audit log (storage.getAuditLogs)",
    origin: "govsearch",
    liveOnly: false,
    notes: "Based on searches performed inside GovSearch · last 7 days · NOT from LF server",
  },
  {
    widget: "Top Queries",
    dataSource: "GovSearch in-process audit log (storage.getAuditLogs)",
    origin: "govsearch",
    liveOnly: false,
    notes: "Based on searches performed inside GovSearch · top 5 by frequency · NOT from LF server",
  },
];

const ORIGIN_BADGE: Record<AuditRow["origin"], { label: string; cls: string }> = {
  laserfiche: {
    label: "Laserfiche",
    cls: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20",
  },
  govsearch: {
    label: "GovSearch",
    cls: "bg-blue-500/10 text-blue-600 dark:text-blue-400 border-blue-500/20",
  },
  both: {
    label: "Both",
    cls: "bg-violet-500/10 text-violet-600 dark:text-violet-400 border-violet-500/20",
  },
};

function WidgetAuditTable({ isLive }: { isLive: boolean }) {
  return (
    <div className="bg-card border border-border rounded-xl p-5 shadow-sm">
      <div className="flex items-start justify-between gap-2 mb-4">
        <div className="flex items-center gap-2 min-w-0">
          <div className="h-5 w-1 rounded-full bg-primary flex-shrink-0" />
          <h2 className="text-sm font-semibold text-foreground">Widget Data Source Audit</h2>
          <span className="text-xs text-muted-foreground truncate">
            What powers each widget on this dashboard
          </span>
        </div>
        {!isLive && (
          <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20 px-2 py-0.5 rounded-full">
            <AlertCircle className="w-3 h-3" />
            LF widgets hidden (not connected)
          </span>
        )}
      </div>

      <div className="overflow-hidden rounded-lg border border-border">
        <table className="w-full text-xs" data-testid="widget-audit-table">
          <thead>
            <tr className="bg-muted/50">
              <th className="text-left px-3 py-2.5 font-semibold text-foreground">Widget</th>
              <th className="text-left px-3 py-2.5 font-semibold text-foreground">Data Source</th>
              <th className="text-left px-3 py-2.5 font-semibold text-foreground w-28">Origin</th>
              <th className="text-left px-3 py-2.5 font-semibold text-foreground hidden lg:table-cell">Notes</th>
            </tr>
          </thead>
          <tbody>
            {AUDIT_ROWS.map((row) => (
              <tr
                key={row.widget}
                className="border-t border-border hover:bg-muted/30 transition-colors"
                data-testid={`audit-row-${row.widget.replace(/\s+/g, "-").toLowerCase()}`}
              >
                <td className="px-3 py-2.5 font-medium text-foreground whitespace-nowrap">
                  {row.widget}
                  {row.liveOnly && (
                    <span className="ml-1.5 text-xs text-amber-600 dark:text-amber-400 opacity-70">
                      (live only)
                    </span>
                  )}
                </td>
                <td className="px-3 py-2.5 text-muted-foreground">{row.dataSource}</td>
                <td className="px-3 py-2.5">
                  <span
                    className={`inline-flex items-center px-2 py-0.5 rounded-full border text-xs font-medium ${ORIGIN_BADGE[row.origin].cls}`}
                  >
                    {ORIGIN_BADGE[row.origin].label}
                  </span>
                </td>
                <td className="px-3 py-2.5 text-muted-foreground hidden lg:table-cell">
                  {row.notes}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

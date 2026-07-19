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
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  RefreshCw, FileText, FolderOpen, Layers, CheckCircle2, AlertCircle,
  Database, TrendingUp, Search,
  Clock, User, Timer, Globe, ShieldCheck, ShieldAlert, AlertTriangle,
  Wifi, WifiOff, Activity, FileSpreadsheet, FileCode, Printer,
  ChevronDown, Download,
} from "lucide-react";

type DocEntry = {
  id: number;
  name: string;
  fullPath: string;
  templateName: string;
  creator: string;
  creationTime?: string;
  lastModifiedTime?: string;
};

type HealthInfo = {
  status: string;
  repositoryId: string | null;
  serverUrl: string;
  username: string;
  lastRefresh: string;
  scanDurationMs: number;
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
  recentDocs?: DocEntry[];
  modifiedDocs?: DocEntry[];
  allDocs?: DocEntry[];
  health?: HealthInfo;
};

const PALETTE = [
  "#3B82F6", "#14B8A6", "#F59E0B", "#8B5CF6", "#EF4444",
  "#22C55E", "#F97316", "#06B6D4", "#EC4899", "#84CC16",
  "#A855F7", "#F43F5E", "#10B981", "#FBBF24", "#60A5FA",
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
    boxShadow: "0 4px 12px rgba(0,0,0,0.12)",
  },
  itemStyle: { color: "hsl(var(--popover-foreground))" },
  labelStyle: { color: "hsl(var(--muted-foreground))", fontWeight: 500 },
  cursor: { fill: "hsl(var(--muted))", opacity: 0.6 },
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

function RootFoldersChart({ data }: { data: DashboardStats["rootFolders"] | undefined }) {
  if (!data?.length) return <EmptyState message="No folder data available." />;

  const aggregated: AggFolder[] = aggregateTopN(data);
  const wasAggregated = aggregated.length < data.length + (aggregated.some((f) => f.isOthers) ? 0 : 1);
  const count = aggregated.length;
  const maxDocs = Math.max(...aggregated.map((f) => f.documents), 0);
  const yTicks = computeYTicks(maxDocs);
  const yMax = yTicks[yTicks.length - 1];

  const chartHeight = count <= 6 ? 220 : count <= 12 ? 260 : 300;
  const labelAngle = count > 5 ? -35 : 0;
  const textAnchor = count > 5 ? "end" : "middle";
  const bottomMargin = count > 5 ? 64 : 28;
  const maxBarSize = count <= 4 ? 56 : count <= 8 ? 44 : count <= 14 ? 32 : 20;
  const maxLabelLen = count <= 4 ? 20 : count <= 8 ? 14 : 10;

  const chartData = aggregated.map((f) => ({
    name: f.name,
    label: truncateLabel(f.name, maxLabelLen),
    documents: f.documents,
    isOthers: !!f.isOthers,
  }));

  return (
    <>
      {wasAggregated && (
        <p className="text-xs text-muted-foreground mb-2">
          Showing top {TOP_N} folders · remaining folders combined as "Others"
        </p>
      )}
      <ResponsiveContainer width="100%" height={chartHeight}>
        <BarChart
          data={chartData}
          margin={{ top: 8, right: 12, bottom: bottomMargin, left: 0 }}
          barCategoryGap={count > 12 ? "15%" : "25%"}
        >
          <CartesianGrid
            strokeDasharray="4 4"
            stroke="hsl(var(--border))"
            opacity={0.5}
            vertical={false}
          />
          <XAxis
            dataKey="label"
            tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
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
            width={yMax >= 1000 ? 48 : 36}
            tickFormatter={(v) => v.toLocaleString()}
          />
          <Tooltip
            {...TOOLTIP_STYLE}
            formatter={(v: any) => [Number(v).toLocaleString(), "Documents"]}
            labelFormatter={(label) => {
              const match = chartData.find((d) => d.label === label);
              return match?.name ?? label;
            }}
          />
          <Bar
            dataKey="documents"
            radius={[5, 5, 0, 0]}
            maxBarSize={maxBarSize}
            isAnimationActive={true}
            animationDuration={600}
            animationEasing="ease-out"
          >
            {chartData.map((entry, i) => (
              <Cell
                key={i}
                fill={entry.isOthers ? OTHERS_COLOR : PALETTE[i % PALETTE.length]}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </>
  );
}

function TemplatePieChart({ data }: { data: DashboardStats["templateStats"] | undefined }) {
  if (!data?.length) return <EmptyState message="No template information available." />;

  const count = data.length;
  const outerR = count > 8 ? 80 : 90;
  const innerR = count > 8 ? 40 : 46;

  return (
    <ResponsiveContainer width="100%" height={280}>
      <PieChart>
        <Pie
          data={data}
          dataKey="count"
          nameKey="name"
          cx="50%"
          cy="45%"
          outerRadius={outerR}
          innerRadius={innerR}
          paddingAngle={count > 10 ? 1 : 2}
          label={({ percent }) =>
            percent > 0.05 ? `${(percent * 100).toFixed(0)}%` : ""
          }
          labelLine={false}
          isAnimationActive={true}
          animationDuration={700}
          animationEasing="ease-out"
        >
          {data.map((_, i) => (
            <Cell key={i} fill={PALETTE[i % PALETTE.length]} />
          ))}
        </Pie>
        <Tooltip
          contentStyle={TOOLTIP_STYLE.contentStyle}
          itemStyle={TOOLTIP_STYLE.itemStyle}
          formatter={(v: any, name: string) => [Number(v).toLocaleString(), name]}
        />
        <Legend
          iconType="circle"
          iconSize={8}
          wrapperStyle={{ paddingTop: 8, fontSize: 11 }}
          formatter={(value) => (
            <span style={{ color: "hsl(var(--muted-foreground))" }}>
              {truncateLabel(String(value), 22)}
            </span>
          )}
        />
      </PieChart>
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
              <ExportDropdown stats={stats} />
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

          {/* ═════ New Widgets ═════ */}

          {/* Row 4: Document Type Distribution + System Health */}
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-5">
            {stats.isLive && (
              <ChartCard
                title="Document Type Distribution"
                sub="Documents by template / type"
                badge={
                  <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 px-2 py-0.5 rounded-full">
                    {(stats.templateStats?.length ?? 0)} types
                  </span>
                }
              >
                <DocTypeChart data={stats.templateStats ?? []} />
              </ChartCard>
            )}
            {stats.health && (
              <ChartCard
                title="System Health"
                sub="Laserfiche connection status"
                badge={
                  stats.health.status === "connected" ? (
                    <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20 px-2 py-0.5 rounded-full">
                      <ShieldCheck className="w-3 h-3" />Healthy
                    </span>
                  ) : stats.health.status === "reconnecting" ? (
                    <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20 px-2 py-0.5 rounded-full">
                      <ShieldAlert className="w-3 h-3" />Reconnecting
                    </span>
                  ) : (
                    <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-red-500/10 text-red-600 dark:text-red-400 border border-red-500/20 px-2 py-0.5 rounded-full">
                      <AlertTriangle className="w-3 h-3" />Disconnected
                    </span>
                  )
                }
              >
                <SystemHealthGrid health={stats.health} />
              </ChartCard>
            )}
          </div>

          {/* Row 5: Recently Created + Recently Modified */}
          {stats.isLive && (
            <>
              <ChartCard
                title="Recently Created Documents"
                sub={`Latest documents created in repository · ${(stats.recentDocs?.length ?? 0)} tracked`}
                badge={
                  <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-teal-500/10 text-teal-600 dark:text-teal-400 border border-teal-500/20 px-2 py-0.5 rounded-full">
                    <Clock className="w-3 h-3" />{(stats.recentDocs?.length ?? 0)} docs
                  </span>
                }
              >
                <RecentCreatedWidget docs={stats.recentDocs ?? []} />
              </ChartCard>

              <ChartCard
                title="Recently Modified Documents"
                sub={`Latest documents modified in repository · ${(stats.modifiedDocs?.length ?? 0)} tracked`}
                badge={
                  <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-orange-500/10 text-orange-600 dark:text-orange-400 border border-orange-500/20 px-2 py-0.5 rounded-full">
                    <Clock className="w-3 h-3" />{(stats.modifiedDocs?.length ?? 0)} docs
                  </span>
                }
              >
                <RecentModifiedWidget docs={stats.modifiedDocs ?? []} />
              </ChartCard>
            </>
          )}

          {/* Row 6: Documents by User Activity */}
          {(() => {
            const ua = computeUserActivity(stats.allDocs ?? []);
            return (
              <ChartCard
                title="Documents by User Activity"
                sub="Based on Laserfiche Creator field"
                badge={
                  <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-indigo-500/10 text-indigo-600 dark:text-indigo-400 border border-indigo-500/20 px-2 py-0.5 rounded-full">
                    <User className="w-3 h-3" />
                    {`${ua.rows.length} (LF)`}
                  </span>
                }
              >
                <UserActivityWidget data={ua.rows} note={ua.note} />
              </ChartCard>
            );
          })()}

        </div>
      </div>
    </div>
  );
}

// ═════ New Widget Components ═════

/* ── Prompt 6: Document Type Distribution ── */
function DocTypeChart({ data }: { data: Array<{ name: string; count: number }> }) {
  if (!data.length) return <EmptyState message="No document types available." />;
  const chartData = data.map((t) => ({ name: t.name, count: t.count })).sort((a, b) => b.count - a.count);
  return (
    <div className="space-y-3">
      {chartData.map((t, i) => (
        <div key={t.name} className="flex items-center gap-3">
          <div className="w-3 h-3 rounded-full flex-shrink-0" style={{ backgroundColor: PALETTE[i % PALETTE.length] }} />
          <span className="text-sm text-foreground flex-1 truncate">{t.name}</span>
          <span className="text-sm font-semibold text-foreground tabular-nums">{t.count}</span>
        </div>
      ))}
    </div>
  );
}

/* ── Prompt 7: Recently Created Documents ── */
function folderFromPath(fullPath?: string): string {
  if (!fullPath) return "-";
  const parts = fullPath.split("\\");
  if (parts.length > 1) return parts.slice(0, -1).join("\\");
  const parts2 = fullPath.split("/");
  if (parts2.length > 1) return parts2.slice(0, -1).join("/");
  return "-";
}

function RecentCreatedWidget({ docs }: { docs: DocEntry[] }) {
  const [query, setQuery] = useState("");
  const filtered = docs.filter((d) =>
    d.name.toLowerCase().includes(query.toLowerCase()) ||
    (d.fullPath || "").toLowerCase().includes(query.toLowerCase())
  );
  if (!docs.length) return <EmptyState message="No recently created documents found." />;
  return (
    <div className="space-y-3">
      <Input
        placeholder="Search documents..."
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        className="text-sm"
      />
      <div className="overflow-hidden rounded-lg border border-border">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-muted/50">
              <th className="text-left px-3 py-2.5 font-semibold">Document Name</th>
              <th className="text-left px-3 py-2.5 font-semibold">Template</th>
              <th className="text-left px-3 py-2.5 font-semibold">Folder</th>
              <th className="text-left px-3 py-2.5 font-semibold">Created Date</th>
              <th className="text-left px-3 py-2.5 font-semibold">Created By</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((d) => (
              <tr key={d.id} className="border-t border-border hover:bg-muted/30 transition-colors">
                <td className="px-3 py-2.5 font-medium text-foreground truncate max-w-[180px]" title={d.name}>{d.name}</td>
                <td className="px-3 py-2.5 text-muted-foreground">{d.templateName || "-"}</td>
                <td className="px-3 py-2.5 text-muted-foreground truncate max-w-[120px]" title={folderFromPath(d.fullPath)}>{folderFromPath(d.fullPath)}</td>
                <td className="px-3 py-2.5 text-muted-foreground whitespace-nowrap">{formatDate(d.creationTime)}</td>
                <td className="px-3 py-2.5 text-muted-foreground">{d.creator || "-"}</td>
              </tr>
            ))}
            {!filtered.length && (
              <tr><td colSpan={5} className="px-3 py-4 text-center text-muted-foreground text-sm">No matches.</td></tr>
            )}
          </tbody>
        </table>
      </div>
      <p className="text-xs text-muted-foreground">{filtered.length} of {docs.length} documents</p>
    </div>
  );
}

/* ── Prompt 8: Recently Modified Documents ── */
function RecentModifiedWidget({ docs }: { docs: DocEntry[] }) {
  const [query, setQuery] = useState("");
  const filtered = docs.filter((d) =>
    d.name.toLowerCase().includes(query.toLowerCase()) ||
    (d.fullPath || "").toLowerCase().includes(query.toLowerCase())
  );
  if (!docs.length) return <EmptyState message="No recently modified documents found." />;
  return (
    <div className="space-y-3">
      <Input
        placeholder="Search documents..."
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        className="text-sm"
      />
      <div className="overflow-hidden rounded-lg border border-border">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-muted/50">
              <th className="text-left px-3 py-2.5 font-semibold">Document</th>
              <th className="text-left px-3 py-2.5 font-semibold">Template</th>
              <th className="text-left px-3 py-2.5 font-semibold">Modified Date</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((d) => (
              <tr key={d.id} className="border-t border-border hover:bg-muted/30 transition-colors">
                <td className="px-3 py-2.5 font-medium text-foreground truncate max-w-[180px]" title={d.name}>{d.name}</td>
                <td className="px-3 py-2.5 text-muted-foreground">{d.templateName || "-"}</td>
                <td className="px-3 py-2.5 text-muted-foreground whitespace-nowrap">{formatDate(d.lastModifiedTime)}</td>
              </tr>
            ))}
            {!filtered.length && (
              <tr><td colSpan={3} className="px-3 py-4 text-center text-muted-foreground text-sm">No matches.</td></tr>
            )}
          </tbody>
        </table>
      </div>
      <p className="text-xs text-muted-foreground">{filtered.length} of {docs.length} documents</p>
    </div>
  );
}

/* ── Prompt 9: Documents by User Activity ── */
type UserRow = { name: string; created: number; lastActivity: string };

function computeUserActivity(
  recent: DocEntry[]
): { rows: UserRow[]; note: string } {
  const map = new Map<string, { created: number; lastActivity: string }>();
  for (const d of recent) {
    const u = d.creator || "Unknown";
    const cur = map.get(u) || { created: 0, lastActivity: "" };
    cur.created += 1;
    const t = d.creationTime || "";
    if (t > cur.lastActivity) cur.lastActivity = t;
    map.set(u, cur);
  }

  if (map.size > 0) {
    const rows = Array.from(map.entries())
      .map(([name, v]) => ({ name, created: v.created, lastActivity: formatDate(v.lastActivity) }))
      .sort((a, b) => b.created - a.created);
    return {
      rows,
      note: "Based on Laserfiche Creator field.",
    };
  }

  return { rows: [], note: "No Laserfiche documents found. Connect to Laserfiche and ensure documents exist in the repository to populate this widget." };
}

type UserActivityProps = {
  data: UserRow[];
  note: string;
};

function UserActivityWidget({ data, note }: UserActivityProps) {
  if (!data.length) {
    return (
      <div className="space-y-3">
        <EmptyState message={note} />
      </div>
    );
  }
  return (
    <div className="space-y-3">
      <div className="overflow-hidden rounded-lg border border-border">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-muted/50">
              <th className="text-left px-3 py-2.5 font-semibold">User</th>
              <th className="text-left px-3 py-2.5 font-semibold">Created</th>
              <th className="text-left px-3 py-2.5 font-semibold">Last Activity</th>
            </tr>
          </thead>
          <tbody>
            {data.map((u) => (
              <tr key={u.name} className="border-t border-border hover:bg-muted/30 transition-colors">
                <td className="px-3 py-2.5 font-medium text-foreground">{u.name}</td>
                <td className="px-3 py-2.5 text-muted-foreground">{u.created}</td>
                <td className="px-3 py-2.5 text-muted-foreground whitespace-nowrap">{u.lastActivity}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/* ── System Health Grid ── */
function SystemHealthGrid({ health }: { health: HealthInfo }) {
  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
      <div className="p-3 rounded-lg border border-border bg-muted/30">
        <div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Activity className="w-3.5 h-3.5" /> Status</div>
        <p className="font-semibold text-foreground">{health.status}</p>
      </div>
      <div className="p-3 rounded-lg border border-border bg-muted/30">
        <div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Database className="w-3.5 h-3.5" /> Repository</div>
        <p className="font-semibold text-foreground">{health.repositoryId || "N/A"}</p>
      </div>
      <div className="p-3 rounded-lg border border-border bg-muted/30">
        <div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Globe className="w-3.5 h-3.5" /> Server URL</div>
        <p className="font-semibold text-foreground text-xs truncate">{health.serverUrl || "N/A"}</p>
      </div>
      <div className="p-3 rounded-lg border border-border bg-muted/30">
        <div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><User className="w-3.5 h-3.5" /> Username</div>
        <p className="font-semibold text-foreground">{health.username || "N/A"}</p>
      </div>
      <div className="p-3 rounded-lg border border-border bg-muted/30">
        <div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Clock className="w-3.5 h-3.5" /> Last Refresh</div>
        <p className="font-semibold text-foreground">{formatTimeAgo(health.lastRefresh)}</p>
      </div>
      <div className="p-3 rounded-lg border border-border bg-muted/30">
        <div className="flex items-center gap-2 text-sm text-muted-foreground mb-1"><Timer className="w-3.5 h-3.5" /> Scan Duration</div>
        <p className="font-semibold text-foreground">{formatDuration(health.scanDurationMs)}</p>
      </div>
    </div>
  );
}

/* ── Prompt 10: Export Dashboard Reports ── */
type ExportFormat = "pdf" | "excel" | "csv";

function ExportDropdown({ stats }: { stats: DashboardStats | undefined }) {
  const { toast } = useToast();
  const exportReport = async (format: ExportFormat) => {
    if (!stats) { toast({ title: "Export Failed", description: "No data available.", variant: "destructive" }); return; }
    try {
      const dateLabel = new Date().toLocaleString("en-US", { year: "numeric", month: "short", day: "numeric" });
      if (format === "csv") {
        // UTF-8 BOM ensures Excel opens Arabic correctly
        const BOM = "\uFEFF";
        const csv = (rows: string[][]) => rows.map((r) => r.map((c) => `"${String(c).replace(/"/g, "\"\"")}"`).join(",")).join("\n");
        const summary = [
          ["Metric", "Value"],
          ["Repository", stats.repositoryId || "N/A"],
          ["Status", stats.isLive ? "Connected" : "Disconnected"],
          ["Total Folders", String(stats.totalFolders)],
          ["Total Documents", String(stats.totalDocuments)],
          ["Total Templates", String(stats.totalTemplates)],
        ];
        const templates = [["Template", "Count"], ...stats.templateStats.map((t) => [t.name, String(t.count)])];
        const folders = [["Folder", "Documents", "Sub-folders"], ...stats.rootFolders.map((f) => [f.name, String(f.documents), String(f.folders)])];
        const blob = new Blob([BOM + csv(summary) + "\n\n" + csv(templates) + "\n\n" + csv(folders)], { type: "text/csv;charset=utf-8" });
        const a = document.createElement("a"); a.href = URL.createObjectURL(blob); a.download = `govsearch-dashboard-${dateLabel}.csv`; a.click(); URL.revokeObjectURL(a.href);
        toast({ title: "CSV Exported", description: "Dashboard report downloaded." });
      } else if (format === "excel") {
        const XLSX = await import("xlsx");
        const wb = XLSX.utils.book_new();
        const add = (name: string, rows: (string | number)[][]) => {
          const ws = XLSX.utils.aoa_to_sheet(rows);
          // Auto-size columns based on content
          const colWidths = rows[0].map((_, ci) => {
            let max = 8;
            for (const row of rows) {
              const cell = row[ci];
              const len = cell ? String(cell).length : 0;
              if (len > max) max = len;
            }
            return { wch: Math.min(max + 2, 60) };
          });
          ws["!cols"] = colWidths;
          XLSX.utils.book_append_sheet(wb, ws, name.slice(0, 31));
        };
        add("Summary", [["Metric", "Value"], ["Repository", stats.repositoryId || "N/A"], ["Status", stats.isLive ? "Connected" : "Disconnected"], ["Total Folders", String(stats.totalFolders)], ["Total Documents", String(stats.totalDocuments)], ["Total Templates", String(stats.totalTemplates)]]);
        add("Templates", [["Template", "Count"], ...stats.templateStats.map((t) => [t.name, t.count])]);
        add("Folders", [["Folder", "Documents", "Sub-folders"], ...stats.rootFolders.map((f) => [f.name, f.documents, f.folders])]);
        if (stats.recentDocs && stats.recentDocs.length > 0) {
          add("Recent Docs", [["Document", "Template", "Folder", "Created", "By"], ...stats.recentDocs.map((d) => [d.name, d.templateName || "-", folderFromPath(d.fullPath), formatDate(d.creationTime), d.creator || "-"])]);
        }
        if (stats.modifiedDocs && stats.modifiedDocs.length > 0) {
          add("Modified Docs", [["Document", "Template", "Modified"], ...stats.modifiedDocs.map((d) => [d.name, d.templateName || "-", formatDate(d.lastModifiedTime)])]);
        }
        XLSX.writeFile(wb, `govsearch-dashboard-${dateLabel}.xlsx`);
        toast({ title: "Excel Exported", description: "Dashboard report downloaded." });
      } else {
        // PDF with Arabic support via html2canvas + jsPDF image embedding
        const html2canvas = (await import("html2canvas")).default;
        const { jsPDF } = await import("jspdf");
        const dashboardEl = document.querySelector(".max-w-7xl") as HTMLElement | null;
        if (!dashboardEl) { toast({ title: "PDF Failed", description: "Dashboard element not found.", variant: "destructive" }); return; }
        toast({ title: "Capturing dashboard...", description: "This may take a few seconds." });
        const canvas = await html2canvas(dashboardEl, { scale: 2, useCORS: true, backgroundColor: "#ffffff" });
        const imgData = canvas.toDataURL("image/png");
        const pdf = new jsPDF({ orientation: "portrait", unit: "mm", format: "a4" });
        const pdfWidth = pdf.internal.pageSize.getWidth();
        const pdfHeight = pdf.internal.pageSize.getHeight();
        const imgWidth = canvas.width;
        const imgHeight = canvas.height;
        const ratio = Math.min(pdfWidth / imgWidth, pdfHeight / imgHeight);
        const scaledHeight = imgHeight * ratio;
        // Multi-page for tall dashboards: one screenshot, sliced across pages
        let pageIndex = 0;
        let heightLeft = scaledHeight;
        while (heightLeft > 0) {
          const position = 10 - pageIndex * (pdfHeight - 20);
          if (pageIndex > 0) pdf.addPage();
          pdf.addImage(imgData, "PNG", 0, position, imgWidth * ratio, imgHeight * ratio);
          heightLeft -= (pdfHeight - 20);
          pageIndex++;
        }
        pdf.save(`govsearch-dashboard-${dateLabel}.pdf`);
        toast({ title: "PDF Exported", description: "Dashboard report downloaded." });
      }
    } catch (e) {
      console.error(e);
      toast({ title: "Export Failed", description: String(e), variant: "destructive" });
    }
  };
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" data-testid="button-export">
          <Download className="w-4 h-4 mr-1.5" /> Export <ChevronDown className="w-3 h-3 ml-1" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onClick={() => exportReport("pdf")} data-testid="button-export-pdf">
          <Printer className="w-4 h-4 mr-2" /> PDF
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => exportReport("excel")} data-testid="button-export-excel">
          <FileSpreadsheet className="w-4 h-4 mr-2" /> Excel
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => exportReport("csv")} data-testid="button-export-csv">
          <FileCode className="w-4 h-4 mr-2" /> CSV
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

/* ── Helper utilities for new widgets ── */
function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  try { return new Date(dateStr).toLocaleString("en-US", { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" }); }
  catch { return dateStr; }
}

function formatTimeAgo(dateStr?: string): string {
  if (!dateStr) return "N/A";
  try {
    const diff = Date.now() - new Date(dateStr).getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1) return "Just now";
    if (mins < 60) return `${mins}m ago`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24) return `${hrs}h ago`;
    const days = Math.floor(hrs / 24);
    if (days < 30) return `${days}d ago`;
    return `${Math.floor(days / 30)}mo ago`;
  } catch { return dateStr; }
}

function formatDuration(ms?: number): string {
  if (!ms) return "N/A";
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

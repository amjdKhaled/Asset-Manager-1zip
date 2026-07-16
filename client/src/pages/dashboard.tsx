import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { jsPDF } from "jspdf";
import autoTable from "jspdf-autotable";
import html2canvas from "html2canvas";
import * as XLSX from "xlsx";
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Cell,
  PieChart, Pie, Legend, LineChart, Line,
} from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import {
  RefreshCw, FileText, FolderOpen, Layers, CheckCircle2, AlertCircle,
  Database, TrendingUp, Search, Info, Download, FileSpreadsheet,
  Users, Clock, Upload, Activity, Folder,
} from "lucide-react";

type RecentDoc = {
  name: string; folder: string;
  creationTime: string; lastModifiedTime: string; creator: string;
};
type FolderStat = { name: string; path: string; documents: number };

type DashboardStats = {
  repositoryId: string | null;
  isLive: boolean;
  lastRefreshedAt?: string;
  totalFolders: number;
  totalDocuments: number;
  totalTemplates: number;
  docsWithTemplate: number;
  docsWithoutTemplate: number;
  templateStats: Array<{ name: string; count: number }>;
  rootFolders: Array<{ name: string; documents: number; folders: number }>;
  allFolderStats?: FolderStat[];
  emptyFolderCount?: number;
  emptyFolders?: Array<{ name: string; path: string }>;
  docTypeDistribution?: Array<{ label: string; count: number }>;
  recentlyCreated?: RecentDoc[];
  recentlyModified?: RecentDoc[];
  documentsByCreator?: Array<{ creator: string; count: number }> | null;
  growthStats?: { thisMonth: number; lastMonth: number; growthPercent: number | null } | null;
  totalSearches: number;
  searchesByDay: Array<{ date: string; count: number }>;
  topSearches: Array<{ query: string; count: number }>;
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

function SearchActivityChart({ data }: { data: DashboardStats["searchesByDay"] | undefined }) {
  if (!data?.length) return <EmptyState message="No search activity recorded." />;

  const maxCount = Math.max(...data.map((d) => d.count), 0);
  const yTicks = computeYTicks(maxCount);
  const yMax = yTicks[yTicks.length - 1];
  const formatted = data.map((d) => ({ ...d, date: d.date.slice(5) }));

  return (
    <ResponsiveContainer width="100%" height={180}>
      <LineChart data={formatted} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
        <CartesianGrid
          strokeDasharray="4 4"
          stroke="hsl(var(--border))"
          opacity={0.5}
          vertical={false}
        />
        <XAxis
          dataKey="date"
          tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
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
          width={32}
        />
        <Tooltip
          contentStyle={TOOLTIP_STYLE.contentStyle}
          itemStyle={TOOLTIP_STYLE.itemStyle}
          labelStyle={TOOLTIP_STYLE.labelStyle}
          formatter={(v: any) => [v, "Searches"]}
        />
        <Line
          type="monotone"
          dataKey="count"
          stroke="#3B82F6"
          strokeWidth={2.5}
          dot={{ r: 3.5, fill: "#3B82F6", strokeWidth: 0 }}
          activeDot={{ r: 5, fill: "#3B82F6", strokeWidth: 2, stroke: "#fff" }}
          isAnimationActive={true}
          animationDuration={600}
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

// ─── Export functions ────────────────────────────────────────────────────────

async function exportToPdf(stats: DashboardStats) {
  const doc = new jsPDF("p", "mm", "a4");
  const W = doc.internal.pageSize.getWidth();
  const H = doc.internal.pageSize.getHeight();
  const M = 14;
  const CW = W - 2 * M;

  const captureEl = async (id: string): Promise<string | null> => {
    const el = document.getElementById(id);
    if (!el) return null;
    try {
      const canvas = await html2canvas(el as HTMLElement, {
        scale: 2, useCORS: true, logging: false, backgroundColor: "#ffffff",
      });
      return canvas.toDataURL("image/png");
    } catch { return null; }
  };

  const addFooter = (pageNum: number, total: number) => {
    doc.setFillColor(21, 60, 112);
    doc.rect(0, H - 9, W, 9, "F");
    doc.setTextColor(255, 255, 255);
    doc.setFontSize(7); doc.setFont("helvetica", "normal");
    doc.text("GovSearch AI — Confidential Analytics Report", M, H - 3.5);
    doc.text(`Page ${pageNum} of ${total}`, W - M, H - 3.5, { align: "right" });
  };

  // ── Page 1 ──────────────────────────────────────────────────────────────
  doc.setFillColor(21, 60, 112);
  doc.rect(0, 0, W, 40, "F");
  doc.setTextColor(255, 255, 255);
  doc.setFontSize(17); doc.setFont("helvetica", "bold");
  doc.text("GovSearch AI — Repository Analytics Report", M, 14);
  doc.setFontSize(9); doc.setFont("helvetica", "normal");
  doc.text(`Repository: ${stats.repositoryId ?? "Not connected"}`, M, 23);
  doc.text(`Generated: ${new Date().toLocaleString()}`, M, 30);
  const live = stats.isLive;
  doc.setFillColor(...(live ? [34, 197, 94] : [239, 68, 68]) as [number,number,number]);
  doc.roundedRect(W - M - 22, 10, 22, 10, 2, 2, "F");
  doc.setTextColor(255, 255, 255); doc.setFontSize(8); doc.setFont("helvetica", "bold");
  doc.text(live ? "LIVE" : "OFFLINE", W - M - 11, 16.5, { align: "center" });

  let y = 48;

  // KPI cards
  doc.setTextColor(25, 25, 25); doc.setFontSize(12); doc.setFont("helvetica", "bold");
  doc.text("Key Performance Indicators", M, y); y += 7;
  const kpis = [
    { label: "Total Folders",        value: (stats.totalFolders ?? 0).toLocaleString(), r: 59,  g: 130, b: 246 },
    { label: "Total Documents",       value: (stats.totalDocuments ?? 0).toLocaleString(), r: 20,  g: 184, b: 166 },
    { label: "Total Templates",       value: (stats.totalTemplates ?? 0).toLocaleString(), r: 139, g: 92,  b: 246 },
    { label: "With Template",         value: (stats.docsWithTemplate ?? 0).toLocaleString(), r: 34,  g: 197, b: 94 },
    { label: "Without Template",      value: (stats.docsWithoutTemplate ?? 0).toLocaleString(), r: 249, g: 115, b: 22 },
  ];
  const cw = (CW - 4) / 5;
  kpis.forEach((k, i) => {
    const x = M + i * (cw + 1);
    doc.setFillColor(k.r, k.g, k.b);
    doc.roundedRect(x, y, cw, 23, 2, 2, "F");
    doc.setTextColor(255, 255, 255); doc.setFontSize(15); doc.setFont("helvetica", "bold");
    doc.text(k.value, x + cw / 2, y + 10, { align: "center" });
    doc.setFontSize(7); doc.setFont("helvetica", "normal");
    doc.text(k.label, x + cw / 2, y + 17, { align: "center", maxWidth: cw - 2 });
  });
  y += 30;

  // Growth stats
  if (stats.growthStats?.growthPercent != null) {
    const g = stats.growthStats.growthPercent;
    const pos = g >= 0;
    doc.setFillColor(pos ? 240 : 254, pos ? 253 : 226, pos ? 244 : 226);
    doc.roundedRect(M, y, CW, 10, 2, 2, "F");
    doc.setTextColor(pos ? 21 : 153, pos ? 128 : 27, pos ? 61 : 27);
    doc.setFontSize(9); doc.setFont("helvetica", "bold");
    doc.text(
      `${pos ? "▲" : "▼"} ${Math.abs(g)}% document growth vs last month  (this month: ${stats.growthStats.thisMonth} · last month: ${stats.growthStats.lastMonth})`,
      M + 4, y + 6.5
    );
    y += 14;
  }

  // Template coverage bar
  if (stats.totalDocuments > 0) {
    const pct = Math.round((stats.docsWithTemplate / stats.totalDocuments) * 100);
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    doc.text("Template Coverage", M, y); y += 5;
    doc.setFillColor(229, 231, 235); doc.roundedRect(M, y, CW, 7, 2, 2, "F");
    doc.setFillColor(34, 197, 94); doc.roundedRect(M, y, (pct / 100) * CW, 7, 2, 2, "F");
    doc.setTextColor(80, 80, 80); doc.setFontSize(8); doc.setFont("helvetica", "normal");
    doc.text(`${pct}%  ·  ${stats.docsWithTemplate.toLocaleString()} with template, ${stats.docsWithoutTemplate.toLocaleString()} without`, M + 2, y + 14);
    y += 18;
  }

  // Chart: Root folders
  const rfImg = await captureEl("chart-root-folders");
  if (rfImg && y < 200) {
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    doc.text("Documents by Folder", M, y); y += 4;
    const ih = 55;
    doc.addImage(rfImg, "PNG", M, y, CW, ih); y += ih + 6;
  }

  if (y > 225) { addFooter(1, 4); doc.addPage(); y = 14; }

  // Chart: Template + Doc Type side by side
  const tpImg = await captureEl("chart-template-pie");
  const dtImg = await captureEl("chart-doc-types");
  if (tpImg || dtImg) {
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    if (tpImg) doc.text("Template Distribution", M, y);
    if (dtImg) doc.text("Document Types", M + CW / 2 + 2, y);
    y += 4;
    const ih = 58;
    if (tpImg) doc.addImage(tpImg, "PNG", M, y, CW / 2 - 2, ih);
    if (dtImg) doc.addImage(dtImg, "PNG", M + CW / 2 + 2, y, CW / 2 - 2, ih);
    y += ih + 6;
  }

  // ── Page 2: Folder + Template tables ────────────────────────────────────
  addFooter(1, 4); doc.addPage(); y = 14;
  if (stats.rootFolders?.length) {
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    doc.text("Folder Statistics", M, y); y += 4;
    autoTable(doc, {
      startY: y, margin: { left: M, right: M },
      head: [["Folder", "Total Documents", "Subfolders"]],
      body: stats.rootFolders.map((f) => [f.name, f.documents.toLocaleString(), f.folders.toLocaleString()]),
      styles: { fontSize: 9, cellPadding: 3 },
      headStyles: { fillColor: [21, 60, 112], textColor: 255, fontStyle: "bold" },
      alternateRowStyles: { fillColor: [248, 250, 252] },
    });
    y = (doc as any).lastAutoTable.finalY + 8;
  }
  if (stats.templateStats?.length) {
    if (y > 220) { addFooter(2, 4); doc.addPage(); y = 14; }
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    doc.text("Template Statistics", M, y); y += 4;
    const total = stats.docsWithTemplate;
    autoTable(doc, {
      startY: y, margin: { left: M, right: M },
      head: [["Template Name", "Documents", "Share"]],
      body: stats.templateStats.map((t) => [
        t.name, t.count.toLocaleString(),
        total > 0 ? `${Math.round((t.count / total) * 100)}%` : "0%",
      ]),
      styles: { fontSize: 9, cellPadding: 3 },
      headStyles: { fillColor: [139, 92, 246], textColor: 255, fontStyle: "bold" },
      alternateRowStyles: { fillColor: [250, 245, 255] },
    });
    y = (doc as any).lastAutoTable.finalY + 8;
  }

  // ── Page 3: Doc types + Recent docs ─────────────────────────────────────
  addFooter(2, 4); doc.addPage(); y = 14;
  if (stats.docTypeDistribution?.length) {
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    doc.text("Document Type Distribution", M, y); y += 4;
    const dtTotal = stats.docTypeDistribution.reduce((s, d) => s + d.count, 0);
    autoTable(doc, {
      startY: y, margin: { left: M, right: M },
      head: [["Document Type", "Count", "Percentage"]],
      body: stats.docTypeDistribution.map((d) => [
        d.label, d.count.toLocaleString(),
        dtTotal > 0 ? `${Math.round((d.count / dtTotal) * 100)}%` : "0%",
      ]),
      styles: { fontSize: 9, cellPadding: 3 },
      headStyles: { fillColor: [8, 145, 178], textColor: 255, fontStyle: "bold" },
      alternateRowStyles: { fillColor: [236, 254, 255] },
    });
    y = (doc as any).lastAutoTable.finalY + 8;
  }
  if (stats.recentlyCreated?.length) {
    if (y > 185) { addFooter(3, 4); doc.addPage(); y = 14; }
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    doc.text("Recently Created Documents", M, y); y += 4;
    autoTable(doc, {
      startY: y, margin: { left: M, right: M },
      head: [["Document Name", "Folder", "Created At", "Creator"]],
      body: stats.recentlyCreated.map((d) => [
        d.name, d.folder || "/",
        d.creationTime ? new Date(d.creationTime).toLocaleDateString() : "",
        d.creator || "",
      ]),
      styles: { fontSize: 8, cellPadding: 2.5 },
      headStyles: { fillColor: [22, 163, 74], textColor: 255, fontStyle: "bold" },
      alternateRowStyles: { fillColor: [240, 253, 244] },
    });
    y = (doc as any).lastAutoTable.finalY + 8;
  }
  if (stats.recentlyModified?.length) {
    if (y > 185) { addFooter(3, 4); doc.addPage(); y = 14; }
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    doc.text("Recently Modified Documents", M, y); y += 4;
    autoTable(doc, {
      startY: y, margin: { left: M, right: M },
      head: [["Document Name", "Folder", "Last Modified", "Creator"]],
      body: stats.recentlyModified.map((d) => [
        d.name, d.folder || "/",
        d.lastModifiedTime ? new Date(d.lastModifiedTime).toLocaleDateString() : "",
        d.creator || "",
      ]),
      styles: { fontSize: 8, cellPadding: 2.5 },
      headStyles: { fillColor: [234, 88, 12], textColor: 255, fontStyle: "bold" },
      alternateRowStyles: { fillColor: [255, 247, 237] },
    });
    y = (doc as any).lastAutoTable.finalY + 8;
  }

  // ── Page 4: Documents by creator ────────────────────────────────────────
  addFooter(3, 4); doc.addPage(); y = 14;
  if (stats.documentsByCreator?.length) {
    doc.setTextColor(25, 25, 25); doc.setFontSize(11); doc.setFont("helvetica", "bold");
    doc.text("Documents by Creator", M, y); y += 4;
    autoTable(doc, {
      startY: y, margin: { left: M, right: M },
      head: [["#", "Creator", "Documents"]],
      body: stats.documentsByCreator.map((d, i) => [i + 1, d.creator, d.count.toLocaleString()]),
      styles: { fontSize: 9, cellPadding: 3 },
      headStyles: { fillColor: [88, 28, 135], textColor: 255, fontStyle: "bold" },
      alternateRowStyles: { fillColor: [250, 245, 255] },
    });
  } else {
    doc.setTextColor(120, 120, 120); doc.setFontSize(10); doc.setFont("helvetica", "italic");
    doc.text("No creator data available for this repository.", M, y + 20);
  }
  addFooter(4, 4);

  const fname = `GovSearch_Analytics_${stats.repositoryId ?? "repo"}_${new Date().toISOString().slice(0, 10)}.pdf`;
  doc.save(fname);
}

async function exportToExcel(stats: DashboardStats) {
  const wb = XLSX.utils.book_new();

  // ── Sheet 1: KPI Summary ─────────────────────────────────────────────────
  const kpiRows: any[][] = [
    ["GovSearch AI — Repository Analytics"],
    [],
    ["Repository", stats.repositoryId ?? "Not connected"],
    ["Generated", new Date().toLocaleString()],
    ["Connection Status", stats.isLive ? "Live" : "Offline"],
    [],
    ["Metric", "Value"],
    ["Total Folders", stats.totalFolders ?? 0],
    ["Total Documents", stats.totalDocuments ?? 0],
    ["Total Templates", stats.totalTemplates ?? 0],
    ["Documents with Template", stats.docsWithTemplate ?? 0],
    ["Documents without Template", stats.docsWithoutTemplate ?? 0],
    ["Total GovSearch Queries", stats.totalSearches ?? 0],
  ];
  if (stats.growthStats) {
    kpiRows.push(
      [], ["Growth Analytics"],
      ["Documents Created This Month", stats.growthStats.thisMonth],
      ["Documents Created Last Month", stats.growthStats.lastMonth],
      ["Month-over-Month Growth", stats.growthStats.growthPercent != null
        ? `${stats.growthStats.growthPercent >= 0 ? "+" : ""}${stats.growthStats.growthPercent}%`
        : "Insufficient data"],
    );
  }
  const wsKpi = XLSX.utils.aoa_to_sheet(kpiRows);
  wsKpi["!cols"] = [{ wch: 36 }, { wch: 26 }];
  XLSX.utils.book_append_sheet(wb, wsKpi, "KPI Summary");

  // ── Sheet 2: Folder Statistics ───────────────────────────────────────────
  if (stats.rootFolders?.length) {
    const rows: any[][] = [
      ["Folder Name", "Total Documents", "Total Subfolders"],
      ...stats.rootFolders.map((f) => [f.name, f.documents, f.folders]),
    ];
    const ws = XLSX.utils.aoa_to_sheet(rows);
    ws["!cols"] = [{ wch: 40 }, { wch: 20 }, { wch: 20 }];
    XLSX.utils.book_append_sheet(wb, ws, "Folder Statistics");
  }

  // ── Sheet 3: All Folder Stats (largest folders) ──────────────────────────
  if (stats.allFolderStats?.length) {
    const sorted = [...stats.allFolderStats].sort((a, b) => b.documents - a.documents);
    const rows: any[][] = [
      ["Folder Name", "Path", "Documents"],
      ...sorted.map((f) => [f.name, f.path, f.documents]),
    ];
    const ws = XLSX.utils.aoa_to_sheet(rows);
    ws["!cols"] = [{ wch: 35 }, { wch: 55 }, { wch: 15 }];
    XLSX.utils.book_append_sheet(wb, ws, "All Folders");
  }

  // ── Sheet 4: Template Statistics ─────────────────────────────────────────
  if (stats.templateStats?.length) {
    const total = stats.docsWithTemplate;
    const rows: any[][] = [
      ["Template Name", "Document Count", "Share (%)"],
      ...stats.templateStats.map((t) => [
        t.name, t.count,
        total > 0 ? `${Math.round((t.count / total) * 100)}%` : "0%",
      ]),
    ];
    const ws = XLSX.utils.aoa_to_sheet(rows);
    ws["!cols"] = [{ wch: 40 }, { wch: 18 }, { wch: 12 }];
    XLSX.utils.book_append_sheet(wb, ws, "Template Statistics");
  }

  // ── Sheet 5: Document Types ───────────────────────────────────────────────
  if (stats.docTypeDistribution?.length) {
    const dtTotal = stats.docTypeDistribution.reduce((s, d) => s + d.count, 0);
    const rows: any[][] = [
      ["Document Type", "Count", "Percentage"],
      ...stats.docTypeDistribution.map((d) => [
        d.label, d.count,
        dtTotal > 0 ? `${Math.round((d.count / dtTotal) * 100)}%` : "0%",
      ]),
    ];
    const ws = XLSX.utils.aoa_to_sheet(rows);
    ws["!cols"] = [{ wch: 22 }, { wch: 15 }, { wch: 15 }];
    XLSX.utils.book_append_sheet(wb, ws, "Document Types");
  }

  // ── Sheet 6: Recent Documents ─────────────────────────────────────────────
  const recentRows: any[][] = [["Name", "Folder", "Created At", "Last Modified", "Creator"]];
  const seen = new Set<string>();
  for (const d of [...(stats.recentlyCreated ?? []), ...(stats.recentlyModified ?? [])]) {
    const key = `${d.name}||${d.folder}`;
    if (!seen.has(key)) {
      seen.add(key);
      recentRows.push([
        d.name, d.folder || "/",
        d.creationTime ? new Date(d.creationTime).toLocaleString() : "",
        d.lastModifiedTime ? new Date(d.lastModifiedTime).toLocaleString() : "",
        d.creator || "",
      ]);
    }
  }
  if (recentRows.length > 1) {
    const ws = XLSX.utils.aoa_to_sheet(recentRows);
    ws["!cols"] = [{ wch: 42 }, { wch: 32 }, { wch: 22 }, { wch: 22 }, { wch: 22 }];
    XLSX.utils.book_append_sheet(wb, ws, "Recent Documents");
  }

  // ── Sheet 7: Documents by Creator ─────────────────────────────────────────
  if (stats.documentsByCreator?.length) {
    const rows: any[][] = [
      ["Rank", "Creator", "Documents Created"],
      ...stats.documentsByCreator.map((d, i) => [i + 1, d.creator, d.count]),
    ];
    const ws = XLSX.utils.aoa_to_sheet(rows);
    ws["!cols"] = [{ wch: 8 }, { wch: 38 }, { wch: 22 }];
    XLSX.utils.book_append_sheet(wb, ws, "Documents by Creator");
  }

  // ── Sheet 8: GovSearch Search Queries ─────────────────────────────────────
  if (stats.topSearches?.length) {
    const rows: any[][] = [
      ["Query", "Count"],
      ...stats.topSearches.map((s) => [s.query, s.count]),
    ];
    const ws = XLSX.utils.aoa_to_sheet(rows);
    ws["!cols"] = [{ wch: 55 }, { wch: 12 }];
    XLSX.utils.book_append_sheet(wb, ws, "Search Queries");
  }

  const fname = `GovSearch_Analytics_${stats.repositoryId ?? "repo"}_${new Date().toISOString().slice(0, 10)}.xlsx`;
  XLSX.writeFile(wb, fname);
}

// ─── New widget components ───────────────────────────────────────────────────

function RepositoryHealthCard({ stats }: { stats: DashboardStats }) {
  const refreshTime = stats.lastRefreshedAt
    ? new Date(stats.lastRefreshedAt).toLocaleTimeString()
    : null;
  return (
    <div className="bg-card border border-border rounded-xl p-5 shadow-sm" data-testid="repo-health-card">
      <div className="flex items-center gap-2 mb-4">
        <div className="h-5 w-1 rounded-full bg-primary flex-shrink-0" />
        <Activity className="w-4 h-4 text-muted-foreground" />
        <h2 className="text-sm font-semibold text-foreground">Repository Health</h2>
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Repository</span>
          <span className="text-sm font-semibold text-foreground truncate" title={stats.repositoryId ?? "—"}>
            {stats.repositoryId ?? "—"}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Connection</span>
          <span className={`inline-flex items-center gap-1.5 text-sm font-semibold ${
            stats.isLive ? "text-emerald-600 dark:text-emerald-400" : "text-red-500"
          }`}>
            <span className={`w-2 h-2 rounded-full flex-shrink-0 ${stats.isLive ? "bg-emerald-500 animate-pulse" : "bg-red-500"}`} />
            {stats.isLive ? "Live" : "Offline"}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Last Refresh</span>
          <span className="text-sm font-semibold text-foreground">{refreshTime ?? "—"}</span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Data Source</span>
          <span className="text-sm font-semibold text-foreground">
            {stats.isLive ? "Laserfiche REST API" : "Not available"}
          </span>
        </div>
      </div>
    </div>
  );
}

function LargestFoldersChart({ data }: { data: FolderStat[] | undefined }) {
  if (!data?.length) return <EmptyState message="No folder data available." />;
  const sorted = [...data].sort((a, b) => b.documents - a.documents);
  const top10 = sorted.slice(0, 10);
  const rest = sorted.slice(10);
  const chartData = [
    ...top10.map((f) => ({ name: f.name, label: truncateLabel(f.name, 11), documents: f.documents, isOthers: false })),
    ...(rest.length > 0 ? [{ name: `Others (${rest.length})`, label: "Others", documents: rest.reduce((s, f) => s + f.documents, 0), isOthers: true }] : []),
  ];
  const maxDocs = Math.max(...chartData.map((f) => f.documents), 0);
  const yTicks = computeYTicks(maxDocs);
  const yMax = yTicks[yTicks.length - 1];
  const count = chartData.length;
  const labelAngle = count > 5 ? -35 : 0;
  const textAnchor = count > 5 ? ("end" as const) : ("middle" as const);
  const bottomMargin = count > 5 ? 64 : 28;
  return (
    <ResponsiveContainer width="100%" height={count <= 6 ? 220 : 260}>
      <BarChart data={chartData} margin={{ top: 8, right: 12, bottom: bottomMargin, left: 0 }} barCategoryGap="25%">
        <CartesianGrid strokeDasharray="4 4" stroke="hsl(var(--border))" opacity={0.5} vertical={false} />
        <XAxis dataKey="label" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} interval={0} angle={labelAngle} textAnchor={textAnchor} height={bottomMargin} tickLine={false} axisLine={{ stroke: "hsl(var(--border))" }} />
        <YAxis ticks={yTicks} domain={[0, yMax]} allowDecimals={false} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} width={yMax >= 1000 ? 48 : 36} tickFormatter={(v) => v.toLocaleString()} />
        <Tooltip {...TOOLTIP_STYLE} formatter={(v: any) => [Number(v).toLocaleString(), "Documents"]} labelFormatter={(label) => chartData.find((d) => d.label === label)?.name ?? label} />
        <Bar dataKey="documents" radius={[5, 5, 0, 0]} maxBarSize={44} isAnimationActive animationDuration={600} animationEasing="ease-out">
          {chartData.map((entry, i) => <Cell key={i} fill={entry.isOthers ? OTHERS_COLOR : PALETTE[i % PALETTE.length]} />)}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}

function EmptyFoldersCard({ count, folders }: { count: number; folders: Array<{ name: string; path: string }> }) {
  const [expanded, setExpanded] = useState(false);
  return (
    <div className="bg-card border border-border rounded-xl p-5 shadow-sm h-full" data-testid="empty-folders-card">
      <div className="flex items-center gap-2 mb-3">
        <div className="h-5 w-1 rounded-full bg-primary flex-shrink-0" />
        <Folder className="w-4 h-4 text-muted-foreground" />
        <h2 className="text-sm font-semibold text-foreground">Empty Folders</h2>
      </div>
      <div className="flex items-center gap-4 mb-3">
        <div className={`w-12 h-12 rounded-lg flex items-center justify-center ${count > 0 ? "bg-amber-500/15" : "bg-emerald-500/15"}`}>
          <Folder className={`w-5 h-5 ${count > 0 ? "text-amber-500" : "text-emerald-500"}`} />
        </div>
        <div>
          <p className="text-3xl font-bold text-foreground tabular-nums">{count.toLocaleString()}</p>
          <p className="text-xs text-muted-foreground">{count === 0 ? "All folders contain documents" : `empty folder${count !== 1 ? "s" : ""} detected`}</p>
        </div>
      </div>
      {count > 0 && folders.length > 0 && (
        <>
          <button onClick={() => setExpanded((v) => !v)} className="text-xs text-primary hover:underline mb-2 flex items-center gap-1" data-testid="button-toggle-empty-folders">
            {expanded ? "Hide" : "Show"} folder list ({folders.length})
          </button>
          {expanded && (
            <div className="overflow-hidden rounded-lg border border-border max-h-44 overflow-y-auto">
              {folders.map((f, i) => (
                <div key={i} className="flex items-center gap-2 px-3 py-2 border-t border-border first:border-t-0 hover:bg-muted/30">
                  <Folder className="w-3 h-3 text-muted-foreground flex-shrink-0" />
                  <span className="text-xs text-foreground truncate flex-1" title={f.path}>{f.name}</span>
                  {f.path !== f.name && <span className="text-xs text-muted-foreground truncate ml-auto max-w-[100px]" title={f.path}>{f.path}</span>}
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function TemplateCoverageWidget({ docsWithTemplate, totalDocuments }: { docsWithTemplate: number; totalDocuments: number }) {
  const pct = totalDocuments > 0 ? Math.round((docsWithTemplate / totalDocuments) * 100) : 0;
  const [colorBar, textColor] = pct >= 80
    ? ["bg-emerald-500", "text-emerald-600 dark:text-emerald-400"]
    : pct >= 50
    ? ["bg-amber-500", "text-amber-600 dark:text-amber-400"]
    : ["bg-red-500", "text-red-600 dark:text-red-400"];
  return (
    <div className="bg-card border border-border rounded-xl p-5 shadow-sm h-full" data-testid="template-coverage-widget">
      <div className="flex items-center gap-2 mb-4">
        <div className="h-5 w-1 rounded-full bg-primary flex-shrink-0" />
        <Layers className="w-4 h-4 text-muted-foreground" />
        <h2 className="text-sm font-semibold text-foreground">Template Coverage</h2>
      </div>
      <div className="flex items-end gap-3 mb-4">
        <span className={`text-5xl font-extrabold tabular-nums ${textColor}`}>{pct}%</span>
        <div className="mb-1">
          <p className="text-sm font-medium text-foreground">of documents have templates</p>
          <p className="text-xs text-muted-foreground">{docsWithTemplate.toLocaleString()} with · {(totalDocuments - docsWithTemplate).toLocaleString()} without</p>
        </div>
      </div>
      <div className="relative h-3 w-full bg-muted rounded-full overflow-hidden">
        <div className={`absolute left-0 top-0 h-full rounded-full transition-all duration-700 ease-out ${colorBar}`} style={{ width: `${pct}%` }} />
      </div>
      <div className="flex justify-between mt-1.5">
        <span className="text-xs text-muted-foreground">0%</span>
        <span className="text-xs text-muted-foreground">100%</span>
      </div>
    </div>
  );
}

const DOC_TYPE_COLORS = ["#3B82F6","#EF4444","#22C55E","#F59E0B","#8B5CF6","#06B6D4","#94A3B8"];

function DocTypeChart({ data }: { data: Array<{ label: string; count: number }> | undefined }) {
  if (!data?.length) return <EmptyState message="No document type information available." />;
  const total = data.reduce((s, d) => s + d.count, 0);
  return (
    <div className="flex flex-col gap-3">
      <ResponsiveContainer width="100%" height={190}>
        <PieChart>
          <Pie data={data} dataKey="count" nameKey="label" cx="50%" cy="46%" outerRadius={80} innerRadius={38} paddingAngle={2}
            label={({ percent }) => percent > 0.05 ? `${(percent * 100).toFixed(0)}%` : ""} labelLine={false}
            isAnimationActive animationDuration={700} animationEasing="ease-out">
            {data.map((_, i) => <Cell key={i} fill={DOC_TYPE_COLORS[i % DOC_TYPE_COLORS.length]} />)}
          </Pie>
          <Tooltip contentStyle={TOOLTIP_STYLE.contentStyle} itemStyle={TOOLTIP_STYLE.itemStyle} formatter={(v: any, name: string) => [Number(v).toLocaleString(), name]} />
        </PieChart>
      </ResponsiveContainer>
      <div className="space-y-1">
        {data.map((d, i) => (
          <div key={d.label} className="flex items-center gap-2 text-xs">
            <span className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{ background: DOC_TYPE_COLORS[i % DOC_TYPE_COLORS.length] }} />
            <span className="flex-1 text-foreground font-medium">{d.label}</span>
            <span className="tabular-nums text-muted-foreground">{d.count.toLocaleString()}</span>
            <span className="tabular-nums text-muted-foreground w-10 text-right">
              {total > 0 ? `${Math.round((d.count / total) * 100)}%` : "0%"}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

function RecentDocsTable({ data, dateKey, dateLabel }: {
  data: RecentDoc[] | undefined;
  dateKey: "creationTime" | "lastModifiedTime";
  dateLabel: string;
}) {
  if (!data?.length) return <EmptyState message="No recent documents available." />;
  return (
    <div className="overflow-hidden rounded-lg border border-border">
      <table className="w-full text-xs" data-testid={`recent-${dateKey}-table`}>
        <thead>
          <tr className="bg-muted/50">
            <th className="text-left px-3 py-2.5 font-semibold text-foreground">Document</th>
            <th className="text-left px-3 py-2.5 font-semibold text-foreground hidden md:table-cell">Folder</th>
            <th className="text-left px-3 py-2.5 font-semibold text-foreground w-24">{dateLabel}</th>
          </tr>
        </thead>
        <tbody>
          {data.map((doc, i) => (
            <tr key={i} className="border-t border-border hover:bg-muted/30 transition-colors" data-testid={`recent-doc-row-${i}`}>
              <td className="px-3 py-2 font-medium text-foreground truncate max-w-[180px]" title={doc.name}>{doc.name}</td>
              <td className="px-3 py-2 text-muted-foreground hidden md:table-cell truncate max-w-[140px]" title={doc.folder}>{doc.folder || "/"}</td>
              <td className="px-3 py-2 text-muted-foreground whitespace-nowrap">
                {doc[dateKey] ? new Date(doc[dateKey]).toLocaleDateString() : "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DocumentsByUserTable({ data }: { data: Array<{ creator: string; count: number }> | null | undefined }) {
  if (!data?.length) return null;
  return (
    <div className="overflow-hidden rounded-lg border border-border">
      <table className="w-full text-sm" data-testid="docs-by-user-table">
        <thead>
          <tr className="bg-muted/50">
            <th className="text-left px-3 py-2.5 font-semibold text-foreground w-10">#</th>
            <th className="text-left px-3 py-2.5 font-semibold text-foreground">Creator</th>
            <th className="text-right px-3 py-2.5 font-semibold text-foreground w-32">Documents</th>
          </tr>
        </thead>
        <tbody>
          {data.map((row, i) => (
            <tr key={row.creator} className="border-t border-border hover:bg-muted/30 transition-colors" data-testid={`user-row-${i}`}>
              <td className="px-3 py-2.5 text-muted-foreground tabular-nums">{i + 1}</td>
              <td className="px-3 py-2.5 font-medium text-foreground">{row.creator}</td>
              <td className="px-3 py-2.5 text-right font-semibold text-foreground tabular-nums">{row.count.toLocaleString()}</td>
            </tr>
          ))}
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
              <Button
                variant="outline"
                size="sm"
                onClick={() => exportToExcel(stats)}
                data-testid="button-export-excel"
              >
                <FileSpreadsheet className="w-4 h-4 mr-1.5 text-emerald-600" />
                Excel
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => exportToPdf(stats)}
                data-testid="button-export-pdf"
              >
                <Download className="w-4 h-4 mr-1.5 text-primary" />
                PDF Report
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

          {/* Repository Health */}
          {stats.isLive && <RepositoryHealthCard stats={stats} />}

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
              sub={
                stats.growthStats?.growthPercent != null
                  ? `${stats.growthStats.growthPercent >= 0 ? "+" : ""}${stats.growthStats.growthPercent}% this month`
                  : undefined
              }
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
                <div id="chart-root-folders">
                  <RootFoldersChart data={stats.rootFolders ?? []} />
                </div>
              </ChartCard>

              <ChartCard
                title="Template Distribution"
                sub="Auto-discovered · each template different color"
              >
                <div id="chart-template-pie">
                  <TemplatePieChart data={stats.templateStats ?? []} />
                </div>
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

          {/* ── NEW ANALYTICS SECTIONS ── */}

          {/* Largest Folders + Empty Folders */}
          {stats.isLive && (
            <div className="grid grid-cols-1 xl:grid-cols-3 gap-5">
              <div className="xl:col-span-2">
                <ChartCard
                  title="Largest Folders"
                  sub="Top 10 folders across all levels by document count"
                >
                  <LargestFoldersChart data={stats.allFolderStats} />
                </ChartCard>
              </div>
              <EmptyFoldersCard
                count={stats.emptyFolderCount ?? 0}
                folders={stats.emptyFolders ?? []}
              />
            </div>
          )}

          {/* Template Coverage + Document Type Distribution */}
          {stats.isLive && (
            <div className="grid grid-cols-1 xl:grid-cols-2 gap-5">
              <TemplateCoverageWidget
                docsWithTemplate={stats.docsWithTemplate ?? 0}
                totalDocuments={stats.totalDocuments ?? 0}
              />
              <ChartCard
                title="Document Type Distribution"
                sub="Breakdown by file extension across all documents"
              >
                <div id="chart-doc-types">
                  <DocTypeChart data={stats.docTypeDistribution} />
                </div>
              </ChartCard>
            </div>
          )}

          {/* Recently Created + Recently Modified */}
          {stats.isLive && (
            <div className="grid grid-cols-1 xl:grid-cols-2 gap-5">
              <ChartCard
                title="Recently Created"
                sub="Latest 10 documents added to the repository"
                badge={
                  <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20 px-2 py-0.5 rounded-full">
                    <Upload className="w-3 h-3" />
                    By creation date
                  </span>
                }
              >
                <RecentDocsTable
                  data={stats.recentlyCreated}
                  dateKey="creationTime"
                  dateLabel="Created"
                />
              </ChartCard>

              <ChartCard
                title="Recently Modified"
                sub="Latest 10 documents changed in the repository"
                badge={
                  <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-orange-500/10 text-orange-600 dark:text-orange-400 border border-orange-500/20 px-2 py-0.5 rounded-full">
                    <Clock className="w-3 h-3" />
                    By modified date
                  </span>
                }
              >
                <RecentDocsTable
                  data={stats.recentlyModified}
                  dateKey="lastModifiedTime"
                  dateLabel="Modified"
                />
              </ChartCard>
            </div>
          )}

          {/* Documents by Creator */}
          {stats.isLive && stats.documentsByCreator && (
            <ChartCard
              title="Documents by Creator"
              sub="Top contributors by document count in the repository"
              badge={
                <span className="flex-shrink-0 flex items-center gap-1 text-xs bg-violet-500/10 text-violet-600 dark:text-violet-400 border border-violet-500/20 px-2 py-0.5 rounded-full">
                  <Users className="w-3 h-3" />
                  From creator field
                </span>
              }
            >
              <DocumentsByUserTable data={stats.documentsByCreator} />
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
    widget: "Largest Folders",
    dataSource: "LF REST API — recursive folder scan (allFolderStats)",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Top 10 folders by doc count; collected across all depths in single scan pass",
  },
  {
    widget: "Empty Folders",
    dataSource: "LF REST API — recursive folder scan (emptyFolderNames)",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Folder considered empty when its entire subtree has 0 documents",
  },
  {
    widget: "Template Coverage",
    dataSource: "Derived: docsWithTemplate ÷ totalDocuments",
    origin: "laserfiche",
    liveOnly: true,
    notes: "% of docs with a template assigned; computed from scan-pass template counts",
  },
  {
    widget: "Document Type Distribution",
    dataSource: "LF REST API — extension field on folder children",
    origin: "laserfiche",
    liveOnly: true,
    notes: "v1 Extension / v2 extension; mapped to friendly labels (PDF, Word, Excel…)",
  },
  {
    widget: "Recently Created",
    dataSource: "LF REST API — creationTime field on folder children",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Bounded merge across all folders; top 10 most recently created docs",
  },
  {
    widget: "Recently Modified",
    dataSource: "LF REST API — lastModifiedTime field on folder children",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Bounded merge across all folders; top 10 most recently modified docs",
  },
  {
    widget: "Documents by Creator",
    dataSource: "LF REST API — creator field on folder children",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Shown only when at least one document has a creator field; v1 Creator / v2 creator",
  },
  {
    widget: "Growth Stats (Total Documents badge)",
    dataSource: "LF REST API — creationTime field (monthCounts map)",
    origin: "laserfiche",
    liveOnly: true,
    notes: "Month-over-month % change; current calendar month vs previous month",
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

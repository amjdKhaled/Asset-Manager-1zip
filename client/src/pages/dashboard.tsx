import { useQuery } from "@tanstack/react-query";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, PieChart, Pie, Cell, Legend } from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import {
  Database, Search, Building2, Zap, TrendingUp, FileText,
  Shield, Brain, Layers, BarChart2, Clock
} from "lucide-react";

type DashboardStats = {
  totalFiles?: number;
  totalDocuments: number;
  totalFields?: number;
  fieldTypesBreakdown?: Record<string, number>;
  parentFolderDocCounts?: Record<string, number>;
  totalSearches: number;
  totalDepartments: number;
  avgResponseMs: number;
  docsByType: Record<string, number>;
  docsByDepartment: Record<string, number>;
  searchesByDay: Array<{ date: string; count: number }>;
  topSearches: Array<{ query: string; count: number }>;
};

const PIE_COLORS = ["hsl(210,85%,32%)", "hsl(195,75%,35%)", "hsl(25,80%,40%)", "hsl(280,65%,38%)", "hsl(150,70%,32%)", "hsl(30,85%,38%)"];

function StatCard({ icon: Icon, label, labelAr, value, sub, color = "primary" }: {
  icon: any; label: string; labelAr: string; value: string | number; sub?: string; color?: string;
}) {
  return (
    <div className="bg-card border border-card-border rounded-md p-5 hover-elevate" data-testid={`stat-${label.toLowerCase().replace(/\s/g, "-")}`}>
      <div className="flex items-start justify-between gap-3 mb-4">
        <div className="w-10 h-10 rounded-md bg-primary/10 flex items-center justify-center flex-shrink-0">
          <Icon className="w-5 h-5 text-primary" />
        </div>
        {sub && <span className="text-xs text-muted-foreground">{sub}</span>}
      </div>
      <p className="text-2xl font-bold text-foreground mb-0.5">{value}</p>
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className="text-xs text-muted-foreground/70 font-arabic mt-0.5" dir="rtl">{labelAr}</p>
    </div>
  );
}

function SectionHeader({ icon: Icon, title, titleAr }: { icon: any; title: string; titleAr: string }) {
  return (
    <div className="flex items-center gap-2 mb-4">
      <Icon className="w-4 h-4 text-primary" />
      <h2 className="text-sm font-semibold text-foreground">{title}</h2>
      <span className="text-xs text-muted-foreground font-arabic" dir="rtl">{titleAr}</span>
    </div>
  );
}

const formatDate = (d: string) => {
  const dt = new Date(d);
  return dt.toLocaleDateString("en-GB", { month: "short", day: "numeric" });
};

function CustomTooltip({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;
  return (
    <div className="bg-popover border border-popover-border rounded-md px-3 py-2 text-xs shadow-lg">
      <p className="font-medium text-popover-foreground">{label}</p>
      <p className="text-primary">{payload[0].value} searches</p>
    </div>
  );
}

export default function DashboardPage() {
  const { data: stats, isLoading, isError, error, refetch, isFetching } = useQuery<DashboardStats>({
    queryKey: ["/api/dashboard/stats"],
  });

  if (isLoading) {
    return (
      <div className="h-full overflow-auto px-6 py-5">
        <div className="max-w-6xl">
          <Skeleton className="h-7 w-48 mb-1" />
          <Skeleton className="h-4 w-64 mb-6" />
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
            {[1, 2, 3, 4].map(i => <Skeleton key={i} className="h-28 rounded-md" />)}
          </div>
          <div className="grid grid-cols-2 gap-5">
            <Skeleton className="h-64 rounded-md" />
            <Skeleton className="h-64 rounded-md" />
          </div>
        </div>
      </div>
    );
  }

  if (isError) {
    const message = error instanceof Error ? error.message : "Failed to load dashboard stats";
    return (
      <div className="h-full overflow-auto px-6 py-5">
        <div className="max-w-3xl rounded-md border border-destructive/40 bg-destructive/5 p-6">
          <h2 className="text-base font-semibold text-foreground mb-1">Dashboard failed to load</h2>
          <p className="text-sm text-muted-foreground mb-4">{message}</p>
          <Button onClick={() => refetch()} disabled={isFetching} data-testid="dashboard-retry">
            {isFetching ? "Retrying..." : "Retry"}
          </Button>
        </div>
      </div>
    );
  }

  if (!stats) {
    return (
      <div className="h-full overflow-auto px-6 py-5">
        <div className="max-w-3xl rounded-md border border-card-border bg-card p-6">
          <h2 className="text-base font-semibold text-foreground mb-1">No dashboard data available</h2>
          <p className="text-sm text-muted-foreground mb-4">The server returned an empty dashboard response.</p>
          <Button variant="outline" onClick={() => refetch()} disabled={isFetching} data-testid="dashboard-refetch-empty">
            {isFetching ? "Refreshing..." : "Refresh"}
          </Button>
        </div>
      </div>
    );
  }

  const pieData = Object.entries(stats.docsByType || {}).map(([name, value]) => ({ name, value }));
  const deptData = Object.entries(stats.docsByDepartment || {})
    .sort((a, b) => b[1] - a[1])
    .slice(0, 6)
    .map(([name, value]) => ({ name: name.split(" ").slice(-2).join(" "), value }));

  return (
    <div className="h-full overflow-auto">
      <div className="px-6 py-5">
        <div className="max-w-6xl">
          <div className="mb-6">
            <h1 className="text-xl font-semibold text-foreground">Analytics Dashboard</h1>
            <p className="text-sm text-muted-foreground mt-0.5 font-arabic" dir="rtl">لوحة تحليلات منصة البحث الذكي</p>
          </div>

          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
            <StatCard
              icon={Database}
              label="Total Files"
              labelAr="إجمالي الملفات"
              value={(stats.totalFiles ?? stats.totalDocuments).toLocaleString()}
              sub="Laserfiche ECM"
            />
            <StatCard
              icon={FileText}
              label="Total Documents"
              labelAr="إجمالي الوثائق"
              value={stats.totalDocuments.toLocaleString()}
              sub="Laserfiche ECM"
            />
            <StatCard
              icon={Search}
              label="Total Searches"
              labelAr="إجمالي عمليات البحث"
              value={stats.totalSearches.toLocaleString()}
              sub="All time"
            />
            <StatCard
              icon={Building2}
              label="Departments"
              labelAr="الجهات الحكومية"
              value={stats.totalDepartments}
              sub="Indexed"
            />
            <StatCard
              icon={Zap}
              label="Total Fields"
              labelAr="إجمالي الحقول"
              value={(stats.totalFields ?? 0).toLocaleString()}
              sub="Metadata fields"
            />
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-5 mb-5">
            <div className="lg:col-span-2 bg-card border border-card-border rounded-md p-5">
              <SectionHeader icon={BarChart2} title="Search Activity (7 Days)" titleAr="نشاط البحث (7 أيام)" />
              <ResponsiveContainer width="100%" height={200}>
                <BarChart data={stats.searchesByDay.map(d => ({ ...d, date: formatDate(d.date) }))} barSize={24}>
                  <XAxis dataKey="date" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} axisLine={false} tickLine={false} />
                  <YAxis tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} axisLine={false} tickLine={false} width={24} />
                  <Tooltip content={<CustomTooltip />} cursor={{ fill: "hsl(var(--muted))" }} />
                  <Bar dataKey="count" fill="hsl(var(--primary))" radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>

            <div className="bg-card border border-card-border rounded-md p-5">
              <SectionHeader icon={FileText} title="By Document Type" titleAr="حسب نوع الوثيقة" />
              <ResponsiveContainer width="100%" height={200}>
                <PieChart>
                  <Pie data={pieData} dataKey="value" nameKey="name" cx="50%" cy="50%" outerRadius={75} innerRadius={35}>
                    {pieData.map((_, index) => (
                      <Cell key={index} fill={PIE_COLORS[index % PIE_COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip formatter={(v: number, name: string) => [v, name]} contentStyle={{ fontSize: 11, background: "hsl(var(--popover))", border: "1px solid hsl(var(--popover-border))", borderRadius: 6 }} />
                  <Legend wrapperStyle={{ fontSize: 11 }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
            <div className="bg-card border border-card-border rounded-md p-5">
              <SectionHeader icon={Building2} title="Documents by Department" titleAr="المستندات حسب الجهة" />
              <ResponsiveContainer width="100%" height={220}>
                <BarChart data={deptData} layout="vertical" barSize={16}>
                  <XAxis type="number" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} axisLine={false} tickLine={false} />
                  <YAxis type="category" dataKey="name" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} axisLine={false} tickLine={false} width={110} />
                  <Tooltip content={<CustomTooltip />} cursor={{ fill: "hsl(var(--muted))" }} />
                  <Bar dataKey="value" fill="hsl(var(--chart-2))" radius={[0, 4, 4, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>

            <div className="bg-card border border-card-border rounded-md p-5">
              <SectionHeader icon={Layers} title="Field Data Types" titleAr="أنواع بيانات الحقول" />
              <div className="space-y-2.5 mb-5">
                {Object.entries(stats.fieldTypesBreakdown || {}).length === 0 ? (
                  <p className="text-sm text-muted-foreground">No field data available</p>
                ) : (
                  Object.entries(stats.fieldTypesBreakdown || {})
                    .sort((a, b) => b[1] - a[1])
                    .map(([type, count]) => (
                      <div key={type} className="flex items-center justify-between border-b border-border pb-1.5 last:border-b-0">
                        <span className="text-xs text-muted-foreground uppercase">{type}</span>
                        <span className="text-xs font-medium text-foreground">{count.toLocaleString()}</span>
                      </div>
                    ))
                )}
              </div>
              <SectionHeader icon={Clock} title="Top Searched Queries" titleAr="أكثر الاستعلامات بحثاً" />
              {stats.topSearches.length === 0 ? (
                <p className="text-sm text-muted-foreground text-center py-8">No searches yet</p>
              ) : (
                <div className="space-y-2.5">
                  {stats.topSearches.map((s, i) => {
                    const isAr = /[\u0600-\u06FF]/.test(s.query);
                    const maxCount = stats.topSearches[0]?.count || 1;
                    return (
                      <div key={i} className="flex items-center gap-3" data-testid={`top-search-${i}`}>
                        <span className="text-xs font-mono text-muted-foreground w-4 text-right">{i + 1}</span>
                        <div className="flex-1 min-w-0">
                          <p className={`text-xs text-foreground truncate ${isAr ? "font-arabic text-right" : ""}`} dir={isAr ? "rtl" : "ltr"}>
                            {s.query}
                          </p>
                          <div className="mt-1 h-1.5 bg-muted rounded-full overflow-hidden">
                            <div
                              className="h-full bg-primary rounded-full"
                              style={{ width: `${(s.count / maxCount) * 100}%` }}
                            />
                          </div>
                        </div>
                        <span className="text-xs text-muted-foreground flex-shrink-0 w-6 text-right">{s.count}x</span>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          </div>
          <div className="mt-5 bg-card border border-card-border rounded-md p-5">
            <SectionHeader icon={TrendingUp} title="Parent Folders as Departments (Documents Count)" titleAr="المجلدات الرئيسية كجهات (عدد الوثائق)" />
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
              {Object.entries(stats.parentFolderDocCounts || {})
                .sort((a, b) => b[1] - a[1])
                .map(([name, count]) => (
                  <div key={name} className="rounded-md border border-border p-3">
                    <p className="text-sm font-medium text-foreground truncate">{name}</p>
                    <p className="text-xs text-muted-foreground mt-1">{count.toLocaleString()} documents</p>
                  </div>
                ))}
            </div>
          </div>

          <div className="mt-5 bg-card border border-card-border rounded-md p-5">
            <SectionHeader icon={Shield} title="Security & Compliance" titleAr="الأمن والامتثال" />
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              {[
                { label: "RBAC", labelAr: "التحكم بالوصول", status: "Enforced", icon: Shield },
                { label: "Audit Logging", labelAr: "سجل التدقيق", status: "Active", icon: Clock },
                { label: "Data Encryption", labelAr: "تشفير البيانات", status: "AES-256", icon: Layers },
                { label: "Air-Gapped AI", labelAr: "ذكاء اصطناعي معزول", status: "On-Premise", icon: Brain },
              ].map(item => (
                <div key={item.label} className="flex items-start gap-2.5 p-3 bg-muted/40 rounded-md" data-testid={`security-${item.label.toLowerCase().replace(/\s/g, "-")}`}>
                  <item.icon className="w-4 h-4 text-primary flex-shrink-0 mt-0.5" />
                  <div>
                    <p className="text-xs font-medium text-foreground">{item.label}</p>
                    <p className="text-xs text-muted-foreground font-arabic mt-0.5" dir="rtl">{item.labelAr}</p>
                    <p className="text-xs text-emerald-600 dark:text-emerald-400 font-medium mt-1">{item.status}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

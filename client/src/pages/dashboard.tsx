import { useQuery } from "@tanstack/react-query";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, PieChart, Pie, Cell, Legend, CartesianGrid, LineChart, Line } from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { Database, Building2, Layers, FolderTree, FileType2, RefreshCw } from "lucide-react";

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
  workflowRunsByDay?: Array<{ date: string; count: number }>;
  workflowByName?: Record<string, number>;
};

const PIE_COLORS = ["#3B82F6", "#14B8A6", "#F59E0B", "#8B5CF6", "#EF4444", "#22C55E", "#F97316"];
const formatDate = (d: string) => new Date(d).toLocaleDateString("en-GB", { month: "short", day: "numeric" });

function StatCard({ icon: Icon, label, labelAr, value, sub }: {
  icon: any; label: string; labelAr: string; value: string | number; sub?: string;
}) {
  return (
    <div className="bg-card border border-card-border rounded-xl p-5 shadow-sm">
      <div className="flex items-start justify-between gap-3 mb-4">
        <div className="w-11 h-11 rounded-lg bg-primary/15 flex items-center justify-center flex-shrink-0">
          <Icon className="w-5 h-5 text-primary" />
        </div>
        {sub && <span className="text-xs text-muted-foreground">{sub}</span>}
      </div>
      <p className="text-3xl font-bold text-foreground mb-1">{value}</p>
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

export default function DashboardPage() {
  const { data: stats, isLoading, isError, error, refetch, isFetching } = useQuery<DashboardStats>({
    queryKey: ["/api/dashboard/stats"],
  });

  if (isLoading) {
    return <div className="h-full overflow-auto px-6 py-5"><Skeleton className="h-[420px] w-full rounded-xl" /></div>;
  }

  if (isError || !stats) {
    const message = error instanceof Error ? error.message : "Unable to load Laserfiche analytics";
    return (
      <div className="h-full overflow-auto px-6 py-5">
        <div className="max-w-3xl rounded-xl border border-destructive/40 bg-destructive/5 p-6">
          <h2 className="text-base font-semibold text-foreground mb-1">Dashboard unavailable</h2>
          <p className="text-sm text-muted-foreground mb-4">{message}</p>
          <Button onClick={() => refetch()} disabled={isFetching}>
            <RefreshCw className="w-4 h-4 mr-2" /> {isFetching ? "Retrying..." : "Retry"}
          </Button>
        </div>
      </div>
    );
  }

  const pieData = Object.entries(stats.docsByType || {}).map(([name, value]) => ({ name, value }));
  const deptData = Object.entries(stats.docsByDepartment || {}).sort((a, b) => b[1] - a[1]).slice(0, 8).map(([name, value]) => ({ name, value }));
  const topDept = deptData[0];
  const totalFolders = Number((stats.docsByType || {})["Folder"] || 0);
  const avgFieldsPerDoc = stats.totalDocuments > 0 ? ((stats.totalFields || 0) / stats.totalDocuments).toFixed(1) : "0.0";
  const parentFolderData = Object.entries(stats.parentFolderDocCounts || {}).sort((a, b) => b[1] - a[1]).slice(0, 8).map(([name, value]) => ({ name, value }));
  const workflowRunsByDay = (stats.workflowRunsByDay || []).map((d) => ({ ...d, date: formatDate(d.date) }));
  const workflowByNameData = Object.entries(stats.workflowByName || {}).sort((a,b)=>b[1]-a[1]).slice(0,8).map(([name,value])=>({name,value}));

  return (
    <div className="h-full overflow-auto bg-gradient-to-b from-background to-background/70">
      <div className="px-6 py-5">
        <div className="max-w-7xl space-y-5">
          <div className="mb-2">
            <h1 className="text-xl font-semibold text-foreground">Laserfiche Analytics Dashboard</h1>
            <p className="text-sm text-muted-foreground mt-0.5 font-arabic" dir="rtl">لوحة تحليلات Laserfiche</p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-5 gap-4">
            <StatCard icon={Database} label="Total Files" labelAr="إجمالي الملفات" value={(stats.totalFiles ?? stats.totalDocuments).toLocaleString()} sub="From Laserfiche" />
            <StatCard icon={FolderTree} label="Total Folders" labelAr="إجمالي المجلدات" value={totalFolders.toLocaleString()} sub="Repository tree" />
            <StatCard icon={Building2} label="Departments" labelAr="عدد الجهات" value={stats.totalDepartments.toLocaleString()} sub="Detected folders" />
            <StatCard icon={Layers} label="Avg Fields / Doc" labelAr="متوسط الحقول لكل وثيقة" value={avgFieldsPerDoc} sub="Metadata quality" />
            <StatCard icon={FileType2} label="Top Department" labelAr="أكثر جهة وثائق" value={topDept?.name || "-"} sub={topDept ? `${topDept.value.toLocaleString()} docs` : "No data"} />
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-3 gap-5">
            <div className="xl:col-span-2 bg-card border border-card-border rounded-xl p-5">
              <SectionHeader icon={Building2} title="Documents by Department" titleAr="الوثائق حسب الجهة" />
              <ResponsiveContainer width="100%" height={280}>
                <BarChart data={deptData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.35} />
                  <XAxis dataKey="name" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} interval={0} angle={-20} height={55} textAnchor="end" />
                  <YAxis tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
                  <Tooltip cursor={{ fill: "hsl(var(--muted))" }} />
                  <Bar dataKey="value" fill="#3B82F6" radius={[6, 6, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>

            <div className="bg-card border border-card-border rounded-xl p-5">
              <SectionHeader icon={FileType2} title="Document Types" titleAr="أنواع الوثائق" />
              <ResponsiveContainer width="100%" height={280}>
                <PieChart>
                  <Pie data={pieData} dataKey="value" nameKey="name" outerRadius={95} innerRadius={48}>
                    {pieData.map((_, idx) => <Cell key={idx} fill={PIE_COLORS[idx % PIE_COLORS.length]} />)}
                  </Pie>
                  <Tooltip contentStyle={{ background: "hsl(var(--popover))", border: "1px solid hsl(var(--border))" }} />
                  <Legend wrapperStyle={{ fontSize: 12 }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-5">
            <div className="bg-card border border-card-border rounded-xl p-5">
              <SectionHeader icon={Layers} title="Top Parent Folders by Documents" titleAr="أكثر المجلدات الرئيسية حسب عدد الوثائق" />
              <ResponsiveContainer width="100%" height={260}>
                <BarChart data={parentFolderData} layout="vertical" margin={{ left: 8, right: 8 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.35} />
                  <XAxis type="number" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
                  <YAxis type="category" dataKey="name" width={120} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
                  <Tooltip />
                  <Bar dataKey="value" fill="#F59E0B" radius={[0, 6, 6, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>

            <div className="bg-card border border-card-border rounded-xl p-5">
              <SectionHeader icon={Database} title="Workflow Runs (Last 7 Days)" titleAr="تشغيلات سير العمل (آخر 7 أيام)" />
              <ResponsiveContainer width="100%" height={260}>
                <LineChart data={workflowRunsByDay}>
                  <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.35} />
                  <XAxis dataKey="date" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
                  <YAxis tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
                  <Tooltip />
                  <Line type="monotone" dataKey="count" stroke="#22C55E" strokeWidth={3} dot={{ r: 3 }} />
                </LineChart>
              </ResponsiveContainer>
            </div>
          </div>

          <div className="bg-card border border-card-border rounded-xl p-5">
            <SectionHeader icon={FileType2} title="Top Workflows by Documents" titleAr="أكثر مسارات العمل استخداماً" />
            <ResponsiveContainer width="100%" height={260}>
              <BarChart data={workflowByNameData} layout="vertical" margin={{ left: 8, right: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.35} />
                <XAxis type="number" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
                <YAxis type="category" dataKey="name" width={180} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
                <Tooltip />
                <Bar dataKey="value" fill="#8B5CF6" radius={[0, 6, 6, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>
    </div>
  );
}

import { useQuery } from "@tanstack/react-query";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { RefreshCw, FileText, FolderOpen, Layers, Calculator } from "lucide-react";

type Department = {
  name: string;
  count: number;
};

type Section = {
  name: string;
  nameEn: string;
  total: number;
  departments: Department[];
};

type DashboardStats = {
  section1Total: number;
  section2Total: number;
  grandTotal: number;
  totalDepartments: number;
  totalSearches: number;
  avgResponseMs: number;
  sections: Section[];
  searchesByDay: Array<{ date: string; count: number }>;
  topSearches: Array<{ query: string; count: number }>;
  workflowRunsByDay?: Array<{ date: string; count: number }>;
  workflowByName?: Record<string, number>;
};

const BAR_COLORS = ["#3B82F6", "#14B8A6", "#F59E0B", "#8B5CF6", "#EF4444", "#22C55E", "#F97316"];

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

function SectionCard({ section, index }: { section: Section; index: number }) {
  const chartData = section.departments.map((d) => ({ name: d.name, count: d.count }));
  const color = BAR_COLORS[index % BAR_COLORS.length];
  return (
    <div className="bg-card border border-card-border rounded-xl p-5 shadow-sm">
      <div className="flex items-center gap-2 mb-1">
        <FolderOpen className="w-4 h-4 text-primary" />
        <h2 className="text-sm font-semibold text-foreground">{section.nameEn}</h2>
        <span className="text-xs text-muted-foreground font-arabic" dir="rtl">{section.name}</span>
      </div>
      <p className="text-sm text-muted-foreground mb-4">
        {section.total.toLocaleString()} documents total
        <span className="font-arabic mx-1" dir="rtl">({section.total.toLocaleString()} وثيقة إجمالياً)</span>
      </p>

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-5">
        {/* Table */}
        <div className="overflow-hidden rounded-lg border border-border">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-muted/50">
                <th className="text-left px-3 py-2 font-semibold text-foreground">Department</th>
                <th className="text-right px-3 py-2 font-semibold text-foreground">
                  <span className="font-arabic" dir="rtl">الوثائق</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {section.departments.map((dept) => (
                <tr key={dept.name} className="border-t border-border hover:bg-muted/30">
                  <td className="px-3 py-2 text-foreground">{dept.name}</td>
                  <td className="px-3 py-2 text-right font-semibold text-foreground">{dept.count.toLocaleString()}</td>
                </tr>
              ))}
              {section.departments.length === 0 && (
                <tr>
                  <td colSpan={2} className="px-3 py-4 text-center text-muted-foreground">
                    No departments found
                    <span className="font-arabic mr-1" dir="rtl">(لم يتم العثور على أقسام)</span>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        {/* Chart */}
        <div>
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={chartData}>
              <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.35} />
              <XAxis
                dataKey="name"
                tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
                interval={0}
                angle={-20}
                height={55}
                textAnchor="end"
              />
              <YAxis tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
              <Tooltip
                cursor={{ fill: "hsl(var(--muted))" }}
                contentStyle={{
                  background: "hsl(var(--popover))",
                  border: "1px solid hsl(var(--border))",
                }}
                itemStyle={{ color: "hsl(var(--popover-foreground))" }}
              />
              <Bar dataKey="count" fill={color} radius={[6, 6, 0, 0]} activeBar={{ fill: color + "CC" }} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>
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
        <Skeleton className="h-[120px] w-full rounded-xl mb-4" />
        <Skeleton className="h-[340px] w-full rounded-xl mb-4" />
        <Skeleton className="h-[340px] w-full rounded-xl" />
      </div>
    );
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

  return (
    <div className="h-full overflow-auto bg-gradient-to-b from-background to-background/70">
      <div className="px-6 py-5">
        <div className="max-w-7xl space-y-5">
          <div className="mb-2">
            <h1 className="text-xl font-semibold text-foreground">Laserfiche Analytics Dashboard</h1>
            <p className="text-sm text-muted-foreground mt-0.5 font-arabic" dir="rtl">لوحة تحليلات Laserfiche</p>
          </div>

          {/* KPI Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
            <StatCard
              icon={FileText}
              label="Total Documents in Document Entry"
              labelAr="إجمالي الوثائق في إدخال الوثيقة"
              value={(stats.section1Total ?? 0).toLocaleString()}
              sub="Section 1"
            />
            <StatCard
              icon={FileText}
              label="Total Documents in Archives Center"
              labelAr="إجمالي الوثائق في مركز الوثائق"
              value={(stats.section2Total ?? 0).toLocaleString()}
              sub="Section 2"
            />
            <StatCard
              icon={Calculator}
              label="Grand Total Documents"
              labelAr="الإجمالي الكلي للوثائق"
              value={(stats.grandTotal ?? 0).toLocaleString()}
              sub="All sections"
            />
            <StatCard
              icon={Layers}
              label="Total Departments"
              labelAr="إجمالي الأقسام"
              value={(stats.totalDepartments ?? 0).toLocaleString()}
              sub="Active departments"
            />
          </div>

          {/* Section 1: إدخال الوثيقة */}
          {stats.sections?.[0] && <SectionCard section={stats.sections[0]} index={0} />}

          {/* Section 2: مركز الوثائق والمحفوظات */}
          {stats.sections?.[1] && <SectionCard section={stats.sections[1]} index={1} />}
        </div>
      </div>
    </div>
  );
}

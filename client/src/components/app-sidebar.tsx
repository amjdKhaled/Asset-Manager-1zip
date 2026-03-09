import { Link, useLocation } from "wouter";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar";
import { Search, LayoutDashboard, FileText, Shield, Database, ChevronRight } from "lucide-react";
import { Badge } from "@/components/ui/badge";

const navItems = [
  { title: "Semantic Search", titleAr: "البحث الدلالي", url: "/", icon: Search },
  { title: "Dashboard", titleAr: "لوحة التحكم", url: "/dashboard", icon: LayoutDashboard },
  { title: "Document Archive", titleAr: "أرشيف المستندات", url: "/archive", icon: FileText },
  { title: "Audit Log", titleAr: "سجل التدقيق", url: "/audit", icon: Shield },
];

export function AppSidebar() {
  const [location] = useLocation();

  return (
    <Sidebar>
      <SidebarHeader className="border-b border-sidebar-border px-4 py-4">
        <div className="flex items-center gap-3">
          <div className="w-9 h-9 rounded-md bg-primary flex items-center justify-center flex-shrink-0">
            <Database className="w-5 h-5 text-primary-foreground" />
          </div>
          <div className="min-w-0">
            <p className="text-sm font-semibold text-sidebar-foreground leading-tight truncate">GovSearch AI</p>
            <p className="text-xs text-muted-foreground leading-tight" dir="rtl">منصة البحث الذكي</p>
          </div>
        </div>
      </SidebarHeader>

      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel className="text-xs uppercase tracking-wider text-muted-foreground px-2 py-2">
            Navigation
          </SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {navItems.map((item) => {
                const isActive = location === item.url || (item.url !== "/" && location.startsWith(item.url));
                return (
                  <SidebarMenuItem key={item.url}>
                    <SidebarMenuButton asChild data-active={isActive}>
                      <Link href={item.url} className={`flex items-center gap-3 px-3 py-2 rounded-md transition-colors ${isActive ? "bg-sidebar-accent text-sidebar-accent-foreground font-medium" : "text-sidebar-foreground"}`}>
                        <item.icon className="w-4 h-4 flex-shrink-0" />
                        <div className="flex-1 min-w-0">
                          <div className="text-sm leading-tight">{item.title}</div>
                          <div className="text-xs text-muted-foreground leading-tight" dir="rtl">{item.titleAr}</div>
                        </div>
                        {isActive && <ChevronRight className="w-3 h-3 text-muted-foreground flex-shrink-0" />}
                      </Link>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                );
              })}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>

        <SidebarGroup>
          <SidebarGroupLabel className="text-xs uppercase tracking-wider text-muted-foreground px-2 py-2">
            System
          </SidebarGroupLabel>
          <SidebarGroupContent>
            <div className="mx-2 rounded-md bg-sidebar-accent/50 p-3 space-y-2">
              <div className="flex items-center justify-between gap-2">
                <span className="text-xs text-muted-foreground">Laserfiche ECM</span>
                <Badge variant="secondary" className="text-xs py-0">Connected</Badge>
              </div>
              <div className="flex items-center justify-between gap-2">
                <span className="text-xs text-muted-foreground">Vector Index</span>
                <Badge variant="secondary" className="text-xs py-0">Active</Badge>
              </div>
              <div className="flex items-center justify-between gap-2">
                <span className="text-xs text-muted-foreground">Arabic NLP</span>
                <Badge variant="secondary" className="text-xs py-0">Running</Badge>
              </div>
            </div>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>

      <SidebarFooter className="border-t border-sidebar-border px-4 py-3">
        <div className="flex items-center gap-2">
          <div className="w-7 h-7 rounded-full bg-primary/20 flex items-center justify-center flex-shrink-0">
            <span className="text-xs font-semibold text-primary">DU</span>
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-xs font-medium text-sidebar-foreground truncate">Demo User</p>
            <p className="text-xs text-muted-foreground truncate">Ministry of Finance</p>
          </div>
          <Badge variant="outline" className="text-xs flex-shrink-0">Admin</Badge>
        </div>
      </SidebarFooter>
    </Sidebar>
  );
}

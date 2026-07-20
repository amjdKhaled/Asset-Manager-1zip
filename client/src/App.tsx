import { Switch, Route, useLocation } from "wouter";
import { queryClient } from "./lib/queryClient";
import { QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "@/components/ui/toaster";
import { TooltipProvider } from "@/components/ui/tooltip";
import { SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { Button } from "@/components/ui/button";
import NotFound from "@/pages/not-found";
import SearchPage from "@/pages/search";
import LFDocumentPage from "@/pages/lf-document";
import DashboardPage from "@/pages/dashboard";
import ArchivePage from "@/pages/archive";
import LaserficheSettingsPage from "@/pages/laserfiche-settings";
import ChatPage from "@/pages/chat";
import PdfExtractionPage from "@/pages/pdf-extraction";
import { Moon, Sun } from "lucide-react";
import { useState, useEffect } from "react";

/**
 * Listen for commands dispatched by the Laserfiche WPF Desktop Client
 * extension via the WebView2 bridge (IWebAppCommunicationService.SendCommandAsync).
 * The extension fires a "laserfiche-command" CustomEvent on window with
 * detail = { command: string, payload: object }.
 *
 * Handled commands:
 *   SetActiveRepository — extension tells the web app which LF repository
 *     it is connected to. We call POST /api/session/active-repository so
 *     all subsequent dashboard stats calls automatically use the right repo.
 */
function useLaserficheCommandBridge() {
  useEffect(() => {
    const handler = (event: Event) => {
      const { command, payload } = (event as CustomEvent).detail ?? {};
      if (!command) return;

      if (command === "SetActiveRepository") {
        const repositoryId = (payload?.repositoryId as string | undefined)?.trim() ?? "";
        fetch("/api/session/active-repository", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ repositoryId }),
        })
          .then(() =>
            queryClient.invalidateQueries({ queryKey: ["/api/dashboard/stats"] })
          )
          .catch(() => {});
      }
    };

    window.addEventListener("laserfiche-command", handler);
    return () => window.removeEventListener("laserfiche-command", handler);
  }, []);
}

function ThemeToggle() {
  const [dark, setDark] = useState(false);

  useEffect(() => {
    const stored = localStorage.getItem("theme");
    if (stored === "dark") {
      setDark(true);
      document.documentElement.classList.add("dark");
    }
  }, []);

  const toggle = () => {
    const next = !dark;
    setDark(next);
    document.documentElement.classList.toggle("dark", next);
    localStorage.setItem("theme", next ? "dark" : "light");
  };

  return (
    <Button
      size="icon"
      variant="ghost"
      onClick={toggle}
      data-testid="theme-toggle"
      aria-label="Toggle theme"
    >
      {dark ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
    </Button>
  );
}

function PageHeader() {
  const [location] = useLocation();

  const pageInfo: Record<string, { title: string; titleAr: string }> = {
    "/": { title: "Semantic Search", titleAr: "البحث الدلالي" },
    "/dashboard": { title: "Analytics Dashboard", titleAr: "لوحة التحليلات" },
    "/dashbord": { title: "Analytics Dashboard", titleAr: "لوحة التحليلات" },
    "/archive": { title: "Document Archive", titleAr: "أرشيف المستندات" },
    "/laserfiche/settings": { title: "Laserfiche Settings", titleAr: "إعدادات Laserfiche" },
    "/chat": { title: "AI Assistant", titleAr: "المساعد الذكي" },
    "/pdf-extraction": { title: "Arabic PDF OCR", titleAr: "استخراج PDF العربي" },
  };

  const isDocPage = location.startsWith("/document/");
  const isLFDocPage = location.startsWith("/lf-document/");
  const info = isLFDocPage
    ? { title: "Laserfiche Document", titleAr: "وثيقة Laserfiche" }
    : isDocPage
    ? { title: "Document Detail", titleAr: "تفاصيل الوثيقة" }
    : (pageInfo[location] || pageInfo["/"]);

  return (
    <header className="flex items-center justify-between px-4 py-2 border-b border-border bg-background flex-shrink-0 h-12">
      <div className="flex items-center gap-2">
        <SidebarTrigger data-testid="button-sidebar-toggle" />
        <div className="w-px h-4 bg-border mx-1" />
        <span className="text-sm text-muted-foreground hidden sm:block">{info.title}</span>
        <span className="text-sm text-muted-foreground font-arabic hidden md:block" dir="rtl">· {info.titleAr}</span>
      </div>
      <div className="flex items-center gap-1">
        <ThemeToggle />
      </div>
    </header>
  );
}

function Router() {
  return (
    <Switch>
      <Route path="/" component={SearchPage} />
      <Route path="/dashboard" component={DashboardPage} />
      <Route path="/dashbord" component={DashboardPage} />
      <Route path="/archive" component={ArchivePage} />
      <Route path="/document/:id" component={LFDocumentPage} />
      <Route path="/lf-document/:entryId" component={LFDocumentPage} />
      <Route path="/laserfiche/settings" component={LaserficheSettingsPage} />
      <Route path="/chat" component={ChatPage} />
      <Route path="/pdf-extraction" component={PdfExtractionPage} />
      <Route component={NotFound} />
    </Switch>
  );
}

function App() {
  useLaserficheCommandBridge();

  const style = {
    "--sidebar-width": "18rem",
    "--sidebar-width-icon": "3rem",
  };

  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>
        <SidebarProvider style={style as React.CSSProperties}>
          <div className="flex h-screen w-full overflow-hidden">
            <AppSidebar />
            <div className="flex flex-col flex-1 min-w-0">
              <PageHeader />
              <main className="flex-1 overflow-hidden">
                <Router />
              </main>
            </div>
          </div>
        </SidebarProvider>
        <Toaster />
      </TooltipProvider>
    </QueryClientProvider>
  );
}

export default App;

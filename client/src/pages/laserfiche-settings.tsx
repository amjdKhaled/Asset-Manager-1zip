import { useEffect, useState } from "react";
import { useQuery, useMutation } from "@tanstack/react-query";
import { apiRequest, queryClient } from "@/lib/queryClient";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { useToast } from "@/hooks/use-toast";
import {
  Settings, Server, Database, User, Lock, Eye, EyeOff,
  CheckCircle, XCircle, AlertCircle, Save, Plug, Trash2, ShieldCheck, Search,
} from "lucide-react";

type ConfigResponse = {
  configured: boolean;
  serverUrl: string;
  repositoryId: string;
  username: string;
  passwordSet: boolean;
};

type TestResponse = {
  ok: boolean;
  status?: number;
  message: string;
  serverUrl?: string;
  repositoryId?: string;
  username?: string;
  errors?: Record<string, string[] | undefined>;
};

type DiscoverResponse = {
  ok: boolean;
  apiVersion?: "v1" | "v2";
  serverUrl: string;
  repos: { repoName: string; repoId?: string; webClientUrl?: string }[];
  message: string;
  status?: number;
};

const PASSWORD_PLACEHOLDER = "********";

type FormState = {
  serverUrl: string;
  repositoryId: string;
  username: string;
  password: string;
};

const EMPTY_FORM: FormState = {
  serverUrl: "",
  repositoryId: "",
  username: "",
  password: "",
};

export default function LaserficheSettingsPage() {
  const { toast } = useToast();
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [showPassword, setShowPassword] = useState(false);
  const [passwordTouched, setPasswordTouched] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [lastResult, setLastResult] = useState<TestResponse | null>(null);
  const [discovered, setDiscovered] = useState<DiscoverResponse | null>(null);

  const { data: config, isLoading } = useQuery<ConfigResponse>({
    queryKey: ["/api/laserfiche/config"],
  });

  useEffect(() => {
    if (!config) return;
    setForm({
      serverUrl: config.serverUrl || "",
      repositoryId: config.repositoryId || "",
      username: config.username || "",
      password: config.passwordSet ? PASSWORD_PLACEHOLDER : "",
    });
    setPasswordTouched(false);
  }, [config]);

  const validate = (state: FormState): Record<string, string> => {
    const errs: Record<string, string> = {};
    if (!state.serverUrl.trim()) errs.serverUrl = "Server URL is required";
    else if (!/^https?:\/\//i.test(state.serverUrl.trim())) errs.serverUrl = "Must start with http:// or https://";
    if (!state.repositoryId.trim()) errs.repositoryId = "Repository ID is required";
    if (!state.username.trim()) errs.username = "Username is required";
    if (!state.password.trim()) errs.password = "Password is required";
    return errs;
  };

  const buildPayload = () => ({
    serverUrl: form.serverUrl.trim(),
    repositoryId: form.repositoryId.trim(),
    username: form.username.trim(),
    password: form.password,
  });

  const saveMutation = useMutation({
    mutationFn: async () => {
      const res = await apiRequest("POST", "/api/laserfiche/config", buildPayload());
      return res.json() as Promise<TestResponse>;
    },
    onSuccess: (data) => {
      setLastResult(data);
      queryClient.invalidateQueries({ queryKey: ["/api/laserfiche/config"] });
      queryClient.invalidateQueries({ queryKey: ["/api/laserfiche/status"] });
      if (data.ok) {
        toast({ title: "Saved & connected", description: data.message });
        setPasswordTouched(false);
        setForm((f) => ({ ...f, password: PASSWORD_PLACEHOLDER }));
      } else {
        toast({ title: "Saved, but connection failed", description: data.message, variant: "destructive" });
      }
    },
    onError: (err: any) => {
      const message = err?.message || "Failed to save configuration";
      setLastResult({ ok: false, message });
      toast({ title: "Save failed", description: message, variant: "destructive" });
    },
  });

  const testMutation = useMutation({
    mutationFn: async () => {
      const res = await apiRequest("POST", "/api/laserfiche/test", buildPayload());
      return res.json() as Promise<TestResponse>;
    },
    onSuccess: (data) => {
      setLastResult(data);
      if (data.ok) {
        toast({ title: "Connection successful", description: data.message });
      } else {
        toast({ title: "Connection failed", description: data.message, variant: "destructive" });
      }
    },
    onError: (err: any) => {
      const message = err?.message || "Failed to test connection";
      setLastResult({ ok: false, message });
      toast({ title: "Test failed", description: message, variant: "destructive" });
    },
  });

  const discoverMutation = useMutation({
    mutationFn: async () => {
      const res = await apiRequest("POST", "/api/laserfiche/discover", {
        serverUrl: form.serverUrl.trim(),
      });
      return res.json() as Promise<DiscoverResponse>;
    },
    onSuccess: (data) => {
      setDiscovered(data);
      if (data.ok && data.repos.length > 0) {
        toast({ title: "Repositories found", description: data.message });
        if (data.repos.length === 1) {
          setForm((f) => ({ ...f, repositoryId: data.repos[0].repoName }));
          setErrors((e) => ({ ...e, repositoryId: "" }));
        }
      } else {
        toast({ title: "No repositories found", description: data.message, variant: "destructive" });
      }
    },
    onError: (err: any) => {
      const message = err?.message || "Discovery failed";
      setDiscovered({ ok: false, serverUrl: form.serverUrl, repos: [], message });
      toast({ title: "Discovery failed", description: message, variant: "destructive" });
    },
  });

  const clearMutation = useMutation({
    mutationFn: async () => {
      const res = await apiRequest("DELETE", "/api/laserfiche/config");
      return res.json();
    },
    onSuccess: () => {
      setForm(EMPTY_FORM);
      setPasswordTouched(false);
      setLastResult(null);
      setErrors({});
      queryClient.invalidateQueries({ queryKey: ["/api/laserfiche/config"] });
      queryClient.invalidateQueries({ queryKey: ["/api/laserfiche/status"] });
      toast({ title: "Cleared", description: "Saved Laserfiche credentials removed." });
    },
    onError: (err: any) => {
      toast({ title: "Failed to clear", description: err?.message || String(err), variant: "destructive" });
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const errs = validate(form);
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;
    saveMutation.mutate();
  };

  const handleTest = () => {
    const errs = validate(form);
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;
    testMutation.mutate();
  };

  const updateField = (key: keyof FormState, value: string) => {
    setForm((f) => ({ ...f, [key]: value }));
    if (errors[key]) setErrors((e) => ({ ...e, [key]: "" }));
    if (key === "password") setPasswordTouched(true);
  };

  const isBusy = saveMutation.isPending || testMutation.isPending || clearMutation.isPending || discoverMutation.isPending;
  const allFilled =
    form.serverUrl.trim() && form.repositoryId.trim() && form.username.trim() && form.password.trim();

  return (
    <div className="h-full overflow-auto bg-background">
      <div className="max-w-3xl mx-auto px-6 py-8">
        <div className="flex items-start justify-between gap-4 mb-6">
          <div>
            <div className="flex items-center gap-2 mb-1">
              <Settings className="w-5 h-5 text-primary" />
              <h1 className="text-xl font-semibold text-foreground">Laserfiche Connection Settings</h1>
            </div>
            <p className="text-sm text-muted-foreground">
              Configure how this site connects to your Laserfiche API Server.
            </p>
            <p className="text-xs text-muted-foreground font-arabic mt-0.5" dir="rtl">
              إعدادات الاتصال بخادم Laserfiche API
            </p>
          </div>
          {isLoading ? (
            <Skeleton className="h-7 w-28 rounded-full" />
          ) : config?.configured ? (
            <Badge
              variant="outline"
              className="text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/30 border-emerald-200 gap-1.5"
              data-testid="badge-status-saved"
            >
              <CheckCircle className="w-3 h-3" />
              Configured
            </Badge>
          ) : (
            <Badge
              variant="outline"
              className="text-amber-600 border-amber-200 gap-1.5"
              data-testid="badge-status-empty"
            >
              <AlertCircle className="w-3 h-3" />
              Not Configured
            </Badge>
          )}
        </div>

        <div className="bg-amber-50/60 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-800/50 rounded-md px-4 py-3 mb-5 flex items-start gap-3">
          <ShieldCheck className="w-4 h-4 text-amber-600 dark:text-amber-400 mt-0.5 flex-shrink-0" />
          <div className="text-xs text-amber-800 dark:text-amber-200 leading-relaxed">
            Credentials are stored on the server only. The password is never sent back to this page after it's saved.
            All Laserfiche API calls happen server-side. Use HTTPS endpoints in production.
          </div>
        </div>

        <form
          onSubmit={handleSubmit}
          className="bg-card border border-card-border rounded-md p-6 space-y-5"
          data-testid="form-laserfiche-settings"
        >
          <div className="space-y-2">
            <Label htmlFor="serverUrl" className="flex items-center gap-1.5 text-sm">
              <Server className="w-3.5 h-3.5 text-muted-foreground" />
              LF_SERVER_URL
              <span className="text-xs text-muted-foreground font-normal">— Base URL of Laserfiche API Server</span>
            </Label>
            <Input
              id="serverUrl"
              value={form.serverUrl}
              onChange={(e) => updateField("serverUrl", e.target.value)}
              placeholder="https://your-server/LFRepositoryAPI"
              autoComplete="off"
              spellCheck={false}
              disabled={isBusy}
              data-testid="input-server-url"
              aria-invalid={!!errors.serverUrl}
            />
            {errors.serverUrl && (
              <p className="text-xs text-red-600" data-testid="error-server-url">{errors.serverUrl}</p>
            )}
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between gap-2">
              <Label htmlFor="repositoryId" className="flex items-center gap-1.5 text-sm">
                <Database className="w-3.5 h-3.5 text-muted-foreground" />
                LF_REPO_ID
                <span className="text-xs text-muted-foreground font-normal">— Repository name or ID</span>
              </Label>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={!form.serverUrl.trim() || isBusy}
                onClick={() => discoverMutation.mutate()}
                className="h-7 text-xs"
                data-testid="button-discover"
              >
                <Search className="w-3 h-3 mr-1" />
                {discoverMutation.isPending ? "Discovering..." : "Discover"}
              </Button>
            </div>
            <Input
              id="repositoryId"
              value={form.repositoryId}
              onChange={(e) => updateField("repositoryId", e.target.value)}
              placeholder="YourRepoName"
              autoComplete="off"
              spellCheck={false}
              disabled={isBusy}
              data-testid="input-repo-id"
              aria-invalid={!!errors.repositoryId}
            />
            {errors.repositoryId && (
              <p className="text-xs text-red-600" data-testid="error-repo-id">{errors.repositoryId}</p>
            )}
            {discovered && (
              <div
                className={`mt-1 rounded-md border px-3 py-2 text-xs ${
                  discovered.ok && discovered.repos.length > 0
                    ? "bg-emerald-50 dark:bg-emerald-950/20 border-emerald-200 dark:border-emerald-800/50"
                    : "bg-amber-50 dark:bg-amber-950/20 border-amber-200 dark:border-amber-800/50"
                }`}
                data-testid="discover-result"
              >
                <p className={`font-medium ${
                  discovered.ok && discovered.repos.length > 0
                    ? "text-emerald-800 dark:text-emerald-200"
                    : "text-amber-800 dark:text-amber-200"
                }`}>
                  {discovered.message}
                  {discovered.apiVersion && (
                    <Badge variant="outline" className="ml-2 text-[10px] py-0">{discovered.apiVersion.toUpperCase()}</Badge>
                  )}
                </p>
                {discovered.repos.length > 0 && (
                  <div className="flex flex-wrap gap-1.5 mt-2">
                    {discovered.repos.map((r) => (
                      <button
                        key={r.repoName}
                        type="button"
                        onClick={() => {
                          setForm((f) => ({ ...f, repositoryId: r.repoName }));
                          setErrors((e) => ({ ...e, repositoryId: "" }));
                        }}
                        className={`px-2 py-1 rounded border text-xs font-mono transition-colors ${
                          form.repositoryId === r.repoName
                            ? "bg-primary text-primary-foreground border-primary"
                            : "bg-card text-foreground border-border hover-elevate"
                        }`}
                        data-testid={`button-pick-repo-${r.repoName}`}
                      >
                        {r.repoName}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="username" className="flex items-center gap-1.5 text-sm">
              <User className="w-3.5 h-3.5 text-muted-foreground" />
              LF_USERNAME
              <span className="text-xs text-muted-foreground font-normal">— Repo or domain user</span>
            </Label>
            <Input
              id="username"
              value={form.username}
              onChange={(e) => updateField("username", e.target.value)}
              placeholder="admin"
              autoComplete="off"
              spellCheck={false}
              disabled={isBusy}
              data-testid="input-username"
              aria-invalid={!!errors.username}
            />
            {errors.username && (
              <p className="text-xs text-red-600" data-testid="error-username">{errors.username}</p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="password" className="flex items-center gap-1.5 text-sm">
              <Lock className="w-3.5 h-3.5 text-muted-foreground" />
              LF_PASSWORD
              <span className="text-xs text-muted-foreground font-normal">— Stored securely, never displayed</span>
            </Label>
            <div className="relative">
              <Input
                id="password"
                type={showPassword ? "text" : "password"}
                value={form.password}
                onChange={(e) => updateField("password", e.target.value)}
                onFocus={() => {
                  if (!passwordTouched && form.password === PASSWORD_PLACEHOLDER) {
                    setForm((f) => ({ ...f, password: "" }));
                    setPasswordTouched(true);
                  }
                }}
                placeholder={config?.passwordSet ? "Leave unchanged or enter new password" : "Your Laserfiche password"}
                autoComplete="new-password"
                disabled={isBusy}
                className="pr-10"
                data-testid="input-password"
                aria-invalid={!!errors.password}
              />
              <button
                type="button"
                onClick={() => setShowPassword((v) => !v)}
                className="absolute right-2 top-1/2 -translate-y-1/2 p-1 rounded text-muted-foreground hover:text-foreground"
                tabIndex={-1}
                aria-label={showPassword ? "Hide password" : "Show password"}
                data-testid="button-toggle-password"
              >
                {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
            {errors.password && (
              <p className="text-xs text-red-600" data-testid="error-password">{errors.password}</p>
            )}
          </div>

          {lastResult && (
            <div
              className={`rounded-md border px-4 py-3 flex items-start gap-3 ${
                lastResult.ok
                  ? "bg-emerald-50 dark:bg-emerald-950/20 border-emerald-200 dark:border-emerald-800/50"
                  : "bg-red-50 dark:bg-red-950/20 border-red-200 dark:border-red-800/50"
              }`}
              data-testid="status-message"
            >
              {lastResult.ok ? (
                <CheckCircle className="w-4 h-4 text-emerald-600 mt-0.5 flex-shrink-0" />
              ) : (
                <XCircle className="w-4 h-4 text-red-600 mt-0.5 flex-shrink-0" />
              )}
              <div className="min-w-0">
                <p
                  className={`text-sm font-medium ${
                    lastResult.ok ? "text-emerald-800 dark:text-emerald-200" : "text-red-800 dark:text-red-200"
                  }`}
                >
                  {lastResult.ok ? "Connected successfully" : "Connection error"}
                  {lastResult.status ? <span className="ml-2 text-xs opacity-70">HTTP {lastResult.status}</span> : null}
                </p>
                <p
                  className={`text-xs mt-0.5 break-words ${
                    lastResult.ok ? "text-emerald-700 dark:text-emerald-300" : "text-red-700 dark:text-red-300"
                  }`}
                >
                  {lastResult.message}
                </p>
              </div>
            </div>
          )}

          <div className="flex flex-wrap items-center gap-2 pt-2">
            <Button
              type="submit"
              disabled={!allFilled || isBusy}
              data-testid="button-save"
            >
              <Save className="w-3.5 h-3.5 mr-1.5" />
              {saveMutation.isPending ? "Saving..." : "Save & Connect"}
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={!allFilled || isBusy}
              onClick={handleTest}
              data-testid="button-test"
            >
              <Plug className="w-3.5 h-3.5 mr-1.5" />
              {testMutation.isPending ? "Testing..." : "Test Connection"}
            </Button>
            {config?.configured && (
              <Button
                type="button"
                variant="ghost"
                disabled={isBusy}
                onClick={() => clearMutation.mutate()}
                className="ml-auto text-red-600 hover:text-red-700 hover:bg-red-50 dark:hover:bg-red-950/20"
                data-testid="button-clear"
              >
                <Trash2 className="w-3.5 h-3.5 mr-1.5" />
                {clearMutation.isPending ? "Clearing..." : "Clear Saved Settings"}
              </Button>
            )}
          </div>
        </form>

        <p className="text-xs text-muted-foreground mt-4 leading-relaxed">
          Tip: For production, set <code className="font-mono">LF_SERVER_URL</code>, <code className="font-mono">LF_REPO_ID</code>,{" "}
          <code className="font-mono">LF_USERNAME</code>, and <code className="font-mono">LF_PASSWORD</code> as Replit Secrets.
          Environment variables always take precedence over values saved on this page.
        </p>
      </div>
    </div>
  );
}

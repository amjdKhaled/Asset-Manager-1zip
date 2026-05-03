import { useState, useRef, useEffect, useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { useToast } from "@/hooks/use-toast";
import { Link } from "wouter";
import {
  Bot, Send, User, FileText, Loader2, AlertCircle, CheckCircle,
  RefreshCw, Trash2, ChevronDown, ChevronUp, Sparkles, Server,
  BookOpen, MessageSquare, ExternalLink, Copy, Check, StopCircle,
  Zap, Search, Hash
} from "lucide-react";
import { cn } from "@/lib/utils";

type OllamaStatus = {
  running: boolean;
  host: string;
  model: string;
  modelsAvailable: string[];
  modelReady: boolean;
  error?: string;
};

type SourceDoc = {
  id: string;
  title: string;
  titleAr?: string | null;
  department: string;
  year?: number | null;
};

type LFEntryRef = {
  entryId: number;
  name: string;
};

type ChatMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  sources?: SourceDoc[];
  lfEntry?: LFEntryRef;
  isStreaming?: boolean;
  lang?: "ar" | "en";
};

const SUGGESTIONS_LF_AR = [
  "لخص الوثيقة رقم 19",
  "ابحث عن المعاملات المتأخرة",
  "ما هي الوثائق في قسم QA؟",
  "حلل الوثيقة رقم 17",
];

const SUGGESTIONS_LF_EN = [
  "Summarize document 19",
  "Find all contracts in the archive",
  "What documents are in the QA folder?",
  "Analyze document 17",
];

const SUGGESTIONS_AR = [
  "لخص أهم وثائق الميزانية",
  "ما هي المعاملات التي تحتوي على عقود صيانة؟",
  "اعطني معلومات عن التقارير المالية لعام 2023",
];

const SUGGESTIONS_EN = [
  "Summarize the budget-related documents",
  "What contracts are in the archive?",
  "Find documents from the Ministry of Finance",
];

function TypingDots() {
  return (
    <span className="inline-flex items-center gap-0.5 ml-1">
      {[0, 1, 2].map(i => (
        <span
          key={i}
          className="w-1 h-1 rounded-full bg-current opacity-60 animate-bounce"
          style={{ animationDelay: `${i * 150}ms` }}
        />
      ))}
    </span>
  );
}

function renderMarkdown(text: string): React.ReactNode[] {
  const lines = text.split("\n");
  const result: React.ReactNode[] = [];
  let key = 0;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];

    if (/^###\s/.test(line)) {
      result.push(<p key={key++} className="font-semibold text-foreground mt-3 mb-1 text-sm">{line.replace(/^###\s/, "")}</p>);
    } else if (/^##\s/.test(line)) {
      result.push(<p key={key++} className="font-bold text-foreground mt-4 mb-1">{line.replace(/^##\s/, "")}</p>);
    } else if (/^#\s/.test(line)) {
      result.push(<p key={key++} className="font-bold text-foreground text-base mt-4 mb-1">{line.replace(/^#\s/, "")}</p>);
    } else if (/^\*\*(.+?)\*\*:?/.test(line)) {
      const clean = line.replace(/^\d+\.\s*/, "").replace(/\*\*(.+?)\*\*/g, "$1");
      const [label, ...rest] = clean.split(":");
      result.push(
        <p key={key++} className="mt-2">
          <span className="font-semibold text-foreground">{label.trim()}</span>
          {rest.length ? <span className="text-foreground/90">: {rest.join(":").trim()}</span> : null}
        </p>
      );
    } else if (/^[-•*]\s/.test(line)) {
      result.push(
        <div key={key++} className="flex items-start gap-2 mt-1">
          <span className="text-primary mt-1 flex-shrink-0">•</span>
          <span>{inlineFormat(line.replace(/^[-•*]\s/, ""))}</span>
        </div>
      );
    } else if (/^\d+\.\s/.test(line)) {
      const num = line.match(/^(\d+)\./)?.[1];
      const content = line.replace(/^\d+\.\s/, "");
      result.push(
        <div key={key++} className="flex items-start gap-2 mt-1.5">
          <span className="text-primary font-semibold text-xs mt-0.5 flex-shrink-0 w-4">{num}.</span>
          <span>{inlineFormat(content)}</span>
        </div>
      );
    } else if (line.trim() === "") {
      if (i > 0 && lines[i - 1].trim() !== "") result.push(<div key={key++} className="h-2" />);
    } else {
      result.push(<p key={key++} className="leading-relaxed">{inlineFormat(line)}</p>);
    }
  }
  return result;
}

function inlineFormat(text: string): React.ReactNode {
  const parts = text.split(/(\*\*[^*]+\*\*)/g);
  return parts.map((part, i) =>
    /^\*\*[^*]+\*\*$/.test(part)
      ? <strong key={i} className="font-semibold text-foreground">{part.slice(2, -2)}</strong>
      : part
  );
}

function SourcesList({ sources, lang }: { sources: SourceDoc[]; lang: "ar" | "en" }) {
  const [expanded, setExpanded] = useState(false);
  if (!sources.length) return null;

  const label = lang === "ar" ? "المصادر المستخدمة" : "Sources used";
  return (
    <div className="mt-3 border-t border-border/50 pt-2">
      <button
        onClick={() => setExpanded(v => !v)}
        className="flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
        data-testid="toggle-sources"
      >
        <BookOpen className="w-3 h-3" />
        <span>{label} ({sources.length})</span>
        {expanded ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
      </button>
      {expanded && (
        <div className="mt-2 space-y-1.5">
          {sources.map(s => (
            <Link
              key={s.id}
              href={`/document/${s.id}`}
              className="flex items-start gap-2 p-2 rounded-md bg-muted/50 hover:bg-muted transition-colors group"
              data-testid={`source-${s.id}`}
            >
              <FileText className="w-3.5 h-3.5 text-primary flex-shrink-0 mt-0.5" />
              <div className="flex-1 min-w-0">
                <p className={cn("text-xs font-medium text-foreground truncate", lang === "ar" && "font-arabic")} dir={lang === "ar" ? "rtl" : "ltr"}>
                  {lang === "ar" && s.titleAr ? s.titleAr : s.title}
                </p>
                <p className="text-xs text-muted-foreground">{s.department}{s.year ? ` · ${s.year}` : ""}</p>
              </div>
              <ExternalLink className="w-3 h-3 text-muted-foreground opacity-0 group-hover:opacity-100 flex-shrink-0 mt-0.5" />
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}

function LFEntryCard({ entry, lang }: { entry: LFEntryRef; lang: "ar" | "en" }) {
  const label = lang === "ar" ? "عرض الوثيقة" : "Open Document";
  return (
    <div className="mt-3 border-t border-border/50 pt-2">
      <Link href={`/lf-document/${entry.entryId}`}>
        <div className="flex items-center gap-2 p-2 rounded-md bg-primary/5 border border-primary/20 hover:bg-primary/10 transition-colors group cursor-pointer" data-testid={`lf-entry-${entry.entryId}`}>
          <FileText className="w-3.5 h-3.5 text-primary flex-shrink-0" />
          <div className="flex-1 min-w-0">
            <p className="text-xs font-medium text-primary truncate">{entry.name}</p>
            <p className="text-xs text-muted-foreground">Entry #{entry.entryId}</p>
          </div>
          <ExternalLink className="w-3 h-3 text-primary opacity-60 group-hover:opacity-100 flex-shrink-0" />
          <span className="text-xs text-primary font-medium">{label}</span>
        </div>
      </Link>
    </div>
  );
}

function MessageBubble({ msg }: { msg: ChatMessage }) {
  const [copied, setCopied] = useState(false);
  const isUser = msg.role === "user";
  const isArabic = msg.lang === "ar" || /[\u0600-\u06FF]/.test(msg.content);

  const copy = () => {
    navigator.clipboard.writeText(msg.content);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div
      className={cn("flex gap-3 group", isUser ? "flex-row-reverse" : "flex-row")}
      data-testid={`message-${msg.id}`}
    >
      <div className={cn(
        "w-8 h-8 rounded-full flex items-center justify-center flex-shrink-0 mt-0.5",
        isUser
          ? "bg-primary text-primary-foreground"
          : "bg-muted border border-border"
      )}>
        {isUser ? <User className="w-4 h-4" /> : <Bot className="w-4 h-4 text-primary" />}
      </div>

      <div className={cn("flex-1 max-w-[82%]", isUser ? "items-end" : "items-start", "flex flex-col")}>
        <div className={cn(
          "rounded-2xl px-4 py-3 text-sm leading-relaxed relative",
          isUser
            ? "bg-primary text-primary-foreground rounded-tr-sm"
            : "bg-card border border-card-border rounded-tl-sm",
          isArabic && !isUser && "font-arabic text-base"
        )} dir={isArabic ? "rtl" : "ltr"}>

          {isUser ? (
            <span>{msg.content}</span>
          ) : msg.isStreaming && !msg.content ? (
            <span className="text-muted-foreground text-xs flex items-center gap-2">
              <Loader2 className="w-3 h-3 animate-spin" />
              {isArabic ? "الذكاء الاصطناعي يفكر" : "AI is thinking"}
              <TypingDots />
            </span>
          ) : (
            <div className="space-y-0.5">
              {renderMarkdown(msg.content)}
              {msg.isStreaming && <TypingDots />}
            </div>
          )}

          {!isUser && !msg.isStreaming && msg.content && (
            <button
              onClick={copy}
              className="absolute -top-2 -right-2 opacity-0 group-hover:opacity-100 transition-opacity w-6 h-6 bg-background border border-border rounded-full flex items-center justify-center shadow-sm"
            >
              {copied ? <Check className="w-3 h-3 text-emerald-500" /> : <Copy className="w-3 h-3 text-muted-foreground" />}
            </button>
          )}
        </div>

        {!isUser && !msg.isStreaming && (
          <>
            {msg.lfEntry && <div className="mt-1 w-full px-1"><LFEntryCard entry={msg.lfEntry} lang={msg.lang || "en"} /></div>}
            {msg.sources && msg.sources.length > 0 && (
              <div className="mt-1 w-full px-1"><SourcesList sources={msg.sources} lang={msg.lang || "en"} /></div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

function QuickAction({ icon: Icon, label, onClick }: { icon: any; label: string; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className="flex items-center gap-2 px-3 py-2 rounded-xl border border-border bg-card hover:border-primary/40 hover:bg-primary/5 text-sm text-muted-foreground hover:text-foreground transition-all"
      data-testid={`quick-action-${label.slice(0, 10)}`}
    >
      <Icon className="w-3.5 h-3.5 text-primary flex-shrink-0" />
      <span className="truncate">{label}</span>
    </button>
  );
}

function OllamaSetupGuide({ status }: { status: OllamaStatus }) {
  return (
    <div className="flex-1 flex items-center justify-center p-6">
      <div className="max-w-md w-full">
        <div className="text-center mb-6">
          <div className="w-14 h-14 rounded-full bg-muted border border-border flex items-center justify-center mx-auto mb-3">
            <Server className="w-7 h-7 text-muted-foreground" />
          </div>
          <h2 className="text-lg font-semibold text-foreground">Ollama Not Running</h2>
          <p className="text-sm text-muted-foreground mt-1 font-arabic" dir="rtl">Ollama غير متاح حالياً</p>
        </div>

        <div className="bg-card border border-card-border rounded-lg p-5 space-y-4">
          <div>
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2">Step 1 — Install Ollama</p>
            <div className="bg-muted rounded-md px-3 py-2 font-mono text-xs text-foreground">
              https://ollama.com/download
            </div>
          </div>
          <div>
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2">Step 2 — Pull the AI Model (Arabic + English)</p>
            <div className="bg-muted rounded-md px-3 py-2 font-mono text-xs text-foreground">
              ollama pull qwen2.5:7b
            </div>
            <p className="text-xs text-muted-foreground mt-1">~4.7 GB download — runs fully offline after that</p>
          </div>
          <div>
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-2">Step 3 — Start the Server</p>
            <div className="bg-muted rounded-md px-3 py-2 font-mono text-xs text-foreground">
              ollama serve
            </div>
          </div>

          {status.error && (
            <div className="bg-red-50 dark:bg-red-950/20 border border-red-200 dark:border-red-800 rounded-md p-3">
              <p className="text-xs text-red-700 dark:text-red-400 font-mono">{status.error}</p>
            </div>
          )}

          {status.running && !status.modelReady && (
            <div className="bg-amber-50 dark:bg-amber-950/20 border border-amber-200 rounded-md p-3">
              <p className="text-xs text-amber-700 dark:text-amber-400">
                Ollama is running but model <strong>{status.model}</strong> is not loaded yet.
                Run: <code className="font-mono bg-amber-100 dark:bg-amber-900/30 px-1 rounded">ollama pull {status.model}</code>
              </p>
              {status.modelsAvailable.length > 0 && (
                <p className="text-xs text-amber-600 mt-1">Available models: {status.modelsAvailable.join(", ")}</p>
              )}
            </div>
          )}
        </div>

        <p className="text-center text-xs text-muted-foreground mt-4 font-arabic" dir="rtl">
          بعد التثبيت، يعمل النموذج محلياً بالكامل — لا إنترنت، لا بيانات تُرسل خارجياً
        </p>
      </div>
    </div>
  );
}

export default function ChatPage() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const abortRef = useRef<AbortController | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const { toast } = useToast();

  const { data: ollamaStatus, isLoading: statusLoading, refetch: refetchStatus } = useQuery<OllamaStatus>({
    queryKey: ["/api/chat/status"],
    refetchInterval: isStreaming ? false : 10000,
  });

  const isArabicInput = /[\u0600-\u06FF]/.test(input);
  const lang = isArabicInput ? "ar" : "en";

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  const sendMessage = useCallback(async (text: string) => {
    if (!text.trim() || isStreaming) return;

    const detectedLang: "ar" | "en" = /[\u0600-\u06FF]/.test(text) ? "ar" : "en";

    const userMsg: ChatMessage = {
      id: crypto.randomUUID(),
      role: "user",
      content: text.trim(),
      lang: detectedLang,
    };

    const assistantId = crypto.randomUUID();
    const assistantMsg: ChatMessage = {
      id: assistantId,
      role: "assistant",
      content: "",
      isStreaming: true,
      lang: detectedLang,
    };

    setMessages(prev => [...prev, userMsg, assistantMsg]);
    setInput("");
    setIsStreaming(true);

    const ctrl = new AbortController();
    abortRef.current = ctrl;

    const history = messages
      .filter(m => !m.isStreaming)
      .map(m => ({ role: m.role, content: m.content }));

    try {
      const res = await fetch("/api/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ messages: history, query: text.trim() }),
        signal: ctrl.signal,
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({ error: "Request failed" }));
        if (err.error === "Ollama is not running") {
          refetchStatus();
          setMessages(prev => prev.filter(m => m.id !== assistantId));
          toast({ title: "Ollama not running", description: err.hint, variant: "destructive" });
          return;
        }
        throw new Error(err.error || "Request failed");
      }

      const reader = res.body!.getReader();
      const decoder = new TextDecoder();
      let sources: SourceDoc[] = [];
      let lfEntry: LFEntryRef | undefined;
      let fullText = "";

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        const chunk = decoder.decode(value, { stream: true });
        for (const line of chunk.split("\n")) {
          if (!line.startsWith("data: ")) continue;
          try {
            const ev = JSON.parse(line.slice(6));
            if (ev.type === "lf-entry") {
              lfEntry = { entryId: ev.entryId, name: ev.name };
              setMessages(prev => prev.map(m =>
                m.id === assistantId ? { ...m, lfEntry } : m
              ));
            } else if (ev.type === "sources") {
              sources = ev.sources || [];
              setMessages(prev => prev.map(m =>
                m.id === assistantId ? { ...m, sources } : m
              ));
            } else if (ev.type === "token") {
              fullText += ev.token;
              setMessages(prev => prev.map(m =>
                m.id === assistantId ? { ...m, content: fullText } : m
              ));
            } else if (ev.type === "done") {
              setMessages(prev => prev.map(m =>
                m.id === assistantId ? { ...m, isStreaming: false } : m
              ));
            } else if (ev.type === "error") {
              throw new Error(ev.error);
            }
          } catch {}
        }
      }
    } catch (err: any) {
      if (err.name === "AbortError") {
        setMessages(prev => prev.map(m =>
          m.id === assistantId
            ? { ...m, content: m.content || (detectedLang === "ar" ? "(تم الإيقاف)" : "(stopped)"), isStreaming: false }
            : m
        ));
      } else {
        setMessages(prev => prev.map(m =>
          m.id === assistantId
            ? { ...m, content: `Error: ${err.message}`, isStreaming: false }
            : m
        ));
      }
    } finally {
      setIsStreaming(false);
      abortRef.current = null;
      inputRef.current?.focus();
    }
  }, [messages, isStreaming, refetchStatus, toast]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      sendMessage(input);
    }
  };

  const stopStreaming = () => abortRef.current?.abort();
  const clearChat = () => { setMessages([]); inputRef.current?.focus(); };

  const ready = ollamaStatus?.running && ollamaStatus?.modelReady;

  const quickActions = lang === "ar"
    ? [
        { icon: Zap, label: "لخص وثيقة رقم 19", text: "لخص الوثيقة رقم 19" },
        { icon: Search, label: "ابحث في الأرشيف", text: "ابحث عن المعاملات المتأخرة في الأرشيف" },
        { icon: Hash, label: "وثائق QA", text: "ما هي الوثائق الموجودة في مجلد QA؟" },
      ]
    : [
        { icon: Zap, label: "Summarize doc 19", text: "Summarize document 19" },
        { icon: Search, label: "Search archive", text: "Find all contracts in the archive" },
        { icon: Hash, label: "QA documents", text: "What documents are in the QA folder?" },
      ];

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-shrink-0 bg-background border-b border-border px-6 py-4">
        <div className="flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-full bg-primary/10 border border-primary/20 flex items-center justify-center">
              <Bot className="w-5 h-5 text-primary" />
            </div>
            <div>
              <h1 className="text-base font-semibold text-foreground leading-tight">AI Archive Assistant</h1>
              <p className="text-xs text-muted-foreground font-arabic" dir="rtl">المساعد الذكي للأرشيف · Laserfiche + Ollama</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {statusLoading ? (
              <Skeleton className="h-6 w-24 rounded-full" />
            ) : ollamaStatus?.running ? (
              <Badge variant="outline" className={cn(
                "gap-1.5 text-xs",
                ollamaStatus.modelReady
                  ? "text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/30 border-emerald-200"
                  : "text-amber-600 border-amber-200"
              )}>
                <CheckCircle className="w-3 h-3" />
                {ollamaStatus.modelReady ? ollamaStatus.model : "Model not loaded"}
              </Badge>
            ) : (
              <Badge variant="outline" className="text-red-500 border-red-200 gap-1.5 text-xs">
                <AlertCircle className="w-3 h-3" />
                Offline
              </Badge>
            )}
            <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => refetchStatus()} data-testid="refresh-ollama">
              <RefreshCw className="w-3.5 h-3.5" />
            </Button>
            {messages.length > 0 && (
              <Button variant="ghost" size="icon" className="h-8 w-8" onClick={clearChat} data-testid="clear-chat">
                <Trash2 className="w-3.5 h-3.5" />
              </Button>
            )}
          </div>
        </div>
      </div>

      {!ready && !statusLoading ? (
        <OllamaSetupGuide status={ollamaStatus!} />
      ) : (
        <>
          <div className="flex-1 overflow-y-auto px-6 py-4">
            {messages.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full min-h-[300px] gap-5">
                <div className="text-center">
                  <Sparkles className="w-10 h-10 text-primary/40 mx-auto mb-3" />
                  <h2 className="text-lg font-semibold text-foreground">
                    {lang === "ar" ? "كيف يمكنني مساعدتك؟" : "How can I help you?"}
                  </h2>
                  <p className="text-sm text-muted-foreground mt-1 font-arabic" dir="rtl">
                    لخص الوثائق، ابحث في الأرشيف، أو اسألني أي سؤال
                  </p>
                  <p className="text-sm text-muted-foreground">
                    Summarize documents, search the archive, or ask anything
                  </p>
                </div>

                <div className="w-full max-w-xl space-y-3">
                  <p className="text-xs text-muted-foreground text-center uppercase tracking-wide">
                    {lang === "ar" ? "اقتراحات سريعة" : "Quick Actions"}
                  </p>
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
                    {quickActions.map(a => (
                      <QuickAction key={a.label} icon={a.icon} label={a.label} onClick={() => sendMessage(a.text)} />
                    ))}
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-1">
                    {(lang === "ar" ? SUGGESTIONS_LF_AR : SUGGESTIONS_LF_EN).slice(0, 2).map(s => (
                      <button
                        key={s}
                        onClick={() => sendMessage(s)}
                        className={cn(
                          "text-sm bg-card border border-card-border rounded-xl px-4 py-3 text-muted-foreground hover:text-foreground hover:border-primary/40 hover:bg-primary/5 transition-all",
                          lang === "ar" ? "text-right font-arabic" : "text-left"
                        )}
                        dir={lang === "ar" ? "rtl" : "ltr"}
                        data-testid={`suggestion-${s.slice(0, 10)}`}
                      >
                        {s}
                      </button>
                    ))}
                    {(lang === "ar" ? SUGGESTIONS_AR : SUGGESTIONS_EN).slice(0, 2).map(s => (
                      <button
                        key={s}
                        onClick={() => sendMessage(s)}
                        className={cn(
                          "text-sm bg-card border border-card-border rounded-xl px-4 py-3 text-muted-foreground hover:text-foreground hover:border-primary/40 hover:bg-primary/5 transition-all",
                          lang === "ar" ? "text-right font-arabic" : "text-left"
                        )}
                        dir={lang === "ar" ? "rtl" : "ltr"}
                        data-testid={`suggestion-${s.slice(0, 10)}`}
                      >
                        {s}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
            ) : (
              <div className="space-y-5 max-w-3xl mx-auto pb-4">
                {messages.map(msg => (
                  <MessageBubble key={msg.id} msg={msg} />
                ))}
                <div ref={bottomRef} />
              </div>
            )}
          </div>

          <div className="flex-shrink-0 bg-background border-t border-border px-6 py-4">
            <div className="max-w-3xl mx-auto">
              <div className={cn(
                "flex items-end gap-2 bg-card border-2 border-border focus-within:border-primary rounded-2xl px-4 py-3 transition-colors",
              )}>
                <MessageSquare className="w-4 h-4 text-muted-foreground flex-shrink-0 mb-0.5" />
                <textarea
                  ref={inputRef}
                  value={input}
                  onChange={e => setInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  dir={isArabicInput ? "rtl" : "ltr"}
                  placeholder={
                    lang === "ar"
                      ? 'مثال: "لخص الوثيقة رقم 19" أو "ابحث عن العقود" (Enter للإرسال)'
                      : 'e.g. "Summarize document 19" or "Find all contracts" (Enter to send)'
                  }
                  className={cn(
                    "flex-1 bg-transparent text-foreground placeholder:text-muted-foreground resize-none outline-none text-sm leading-relaxed min-h-[24px] max-h-40",
                    isArabicInput && "font-arabic text-base"
                  )}
                  rows={1}
                  disabled={isStreaming && !abortRef.current}
                  data-testid="chat-input"
                />
                {isStreaming ? (
                  <Button
                    size="icon"
                    variant="ghost"
                    className="h-8 w-8 flex-shrink-0 text-red-500 hover:text-red-600 hover:bg-red-50"
                    onClick={stopStreaming}
                    data-testid="stop-button"
                  >
                    <StopCircle className="w-4 h-4" />
                  </Button>
                ) : (
                  <Button
                    size="icon"
                    className="h-8 w-8 flex-shrink-0"
                    onClick={() => sendMessage(input)}
                    disabled={!input.trim()}
                    data-testid="send-button"
                  >
                    <Send className="w-3.5 h-3.5" />
                  </Button>
                )}
              </div>
              <p className="text-center text-xs text-muted-foreground mt-2">
                {lang === "ar"
                  ? "يعمل محلياً بالكامل — لا بيانات تُرسل خارجياً · Ollama + Laserfiche"
                  : "Fully local — no data sent externally · Powered by Ollama + Laserfiche"}
              </p>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

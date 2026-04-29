export const OLLAMA_HOST = process.env.OLLAMA_HOST || "http://localhost:11434";
export const OLLAMA_MODEL = process.env.OLLAMA_MODEL || "qwen2.5:7b";

export interface OllamaMessage {
  role: "system" | "user" | "assistant";
  content: string;
}

export interface OllamaStatusResult {
  running: boolean;
  host: string;
  model: string;
  modelsAvailable: string[];
  modelReady: boolean;
  error?: string;
}

export async function checkOllamaStatus(): Promise<OllamaStatusResult> {
  try {
    const res = await fetch(`${OLLAMA_HOST}/api/tags`, { signal: AbortSignal.timeout(4000) });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json() as { models?: Array<{ name: string }> };
    const modelsAvailable = (data.models || []).map((m) => m.name);
    const modelReady = modelsAvailable.some(
      (m) => m === OLLAMA_MODEL || m.startsWith(OLLAMA_MODEL.split(":")[0])
    );
    return { running: true, host: OLLAMA_HOST, model: OLLAMA_MODEL, modelsAvailable, modelReady };
  } catch (err: any) {
    return {
      running: false,
      host: OLLAMA_HOST,
      model: OLLAMA_MODEL,
      modelsAvailable: [],
      modelReady: false,
      error: err.message,
    };
  }
}

export async function ollamaChat(
  messages: OllamaMessage[],
  onChunk: (token: string) => void,
  signal?: AbortSignal
): Promise<string> {
  const res = await fetch(`${OLLAMA_HOST}/api/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ model: OLLAMA_MODEL, messages, stream: true }),
    signal,
  });

  if (!res.ok) {
    const text = await res.text();
    throw new Error(`Ollama error ${res.status}: ${text}`);
  }

  const reader = res.body!.getReader();
  const decoder = new TextDecoder();
  let fullText = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    const chunk = decoder.decode(value, { stream: true });
    for (const line of chunk.split("\n")) {
      if (!line.trim()) continue;
      try {
        const json = JSON.parse(line) as { message?: { content?: string }; done?: boolean };
        const token = json.message?.content || "";
        if (token) {
          fullText += token;
          onChunk(token);
        }
      } catch {}
    }
  }

  return fullText;
}

export function buildSystemPrompt(lang: "ar" | "en"): string {
  if (lang === "ar") {
    return `أنت مساعد ذكي متخصص في البحث وتلخيص وثائق الأرشيف الحكومي.
لديك صلاحية الوصول إلى قاعدة بيانات وثائق حكومية تشمل: المعاملات، العقود، التقارير، القرارات، والمراسيم.
مهامك:
1. البحث عن المعلومات في الوثائق المتاحة
2. تلخيص محتوى الوثائق بشكل واضح وموجز
3. الإجابة على الأسئلة المتعلقة بالوثائق والأرشيف
4. استخراج المعلومات المحددة كالأسماء والتواريخ والمبالغ
قواعد مهمة:
- أجب دائماً بنفس لغة السؤال (عربي أو إنجليزي)
- استند فقط إلى المعلومات الموجودة في السياق المقدم
- إذا لم تجد معلومة، قل ذلك بوضوح
- كن دقيقاً في المعلومات واذكر مصدرها`;
  }
  return `You are an intelligent assistant specialized in searching and summarizing government document archives.
You have access to a database of government documents including: transactions, contracts, reports, decisions, and decrees.
Your tasks:
1. Search for information across available documents
2. Summarize document content clearly and concisely
3. Answer questions about documents and the archive
4. Extract specific information like names, dates, and amounts
Important rules:
- Always respond in the same language as the question (Arabic or English)
- Only use information found in the provided context
- If information is not available, clearly state so
- Be accurate and cite your sources`;
}

export function buildContextBlock(docs: Array<{ title: string; titleAr?: string | null; content: string; contentAr?: string | null; department: string; year?: number | null; author?: string | null; id: string }>, lang: "ar" | "en"): string {
  if (docs.length === 0) return lang === "ar" ? "لا توجد وثائق ذات صلة." : "No relevant documents found.";

  const label = lang === "ar" ? "الوثائق ذات الصلة" : "Relevant Documents";
  const parts = [`\n--- ${label} ---\n`];

  for (let i = 0; i < docs.length; i++) {
    const d = docs[i];
    const title = (lang === "ar" && d.titleAr) ? d.titleAr : d.title;
    const content = (lang === "ar" && d.contentAr) ? d.contentAr : d.content;
    if (lang === "ar") {
      parts.push(`[${i + 1}] عنوان: ${title} | الجهة: ${d.department}${d.year ? ` | السنة: ${d.year}` : ""}${d.author ? ` | المؤلف: ${d.author}` : ""}\nالمحتوى: ${content.slice(0, 600)}`);
    } else {
      parts.push(`[${i + 1}] Title: ${title} | Dept: ${d.department}${d.year ? ` | Year: ${d.year}` : ""}${d.author ? ` | Author: ${d.author}` : ""}\nContent: ${content.slice(0, 600)}`);
    }
  }
  parts.push("--- end ---\n");
  return parts.join("\n");
}

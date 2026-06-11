import { storage } from "./storage";
import {
  getLaserficheConfig,
  getLaserficheToken,
  naturalLanguageToLFSearchCommand,
  laserficheRepositorySearch,
  laserficheGetEntry,
  laserficheGetEntryFieldsRaw,
  type LaserficheConfig,
} from "./laserfiche";
import { type Document, type SearchResult } from "@shared/schema";

export type MatchReason =
  | "semantic"
  | "keyword"
  | "metadata"
  | "laserfiche"
  | "laserfiche-metadata"
  | "title-match"
  | "year-match";

export type UnifiedResult = {
  id: string;
  type: "local" | "laserfiche";
  title: string;
  titleAr?: string | null;
  department: string;
  departmentAr?: string | null;
  docType: string;
  docTypeAr?: string | null;
  classification: string;
  securityLevel: string;
  author?: string | null;
  year?: number | null;
  createdAt?: Date | null;
  snippet: string;
  snippetAr?: string | null;
  score: number;
  matchReasons: Array<{ reason: MatchReason; detail?: string }>;
  sourceUrl?: string;
  previewUrl?: string;
  openUrl?: string;
  downloadUrl?: string;
  laserficheId?: string | null;
  metadata?: Record<string, string[]>;
  pageCount?: number | null;
  fileSizeKb?: number | null;
};

export type SmartSearchResponse = {
  query: string;
  queryLanguage: "ar" | "en" | "mixed";
  total: number;
  results: UnifiedResult[];
  enginesUsed: string[];
  intent: string;
  processingTimeMs: number;
  lfConnected: boolean;
  laserficheCommand?: string;
  laserficheTerms?: string[];
};

export type SearchIntent =
  | "keyword"
  | "semantic"
  | "metadata"
  | "laserfiche"
  | "mixed"
  | "exact-id"
  | "year-filter";

function detectLanguage(query: string): "ar" | "en" | "mixed" {
  const hasArabic = /[\u0600-\u06FF]/.test(query);
  const hasEnglish = /[a-zA-Z]/.test(query);
  if (hasArabic && hasEnglish) return "mixed";
  return hasArabic ? "ar" : "en";
}

function classifyIntent(query: string): SearchIntent {
  const q = query.trim().toLowerCase();

  // Exact ID / Contract number pattern
  if (/\b[A-Z]{2,}-\d{4,}-[A-Z]{2,}-\d{3,}\b/.test(query)) return "exact-id";

  // Metadata field patterns
  const metadataPatterns = [
    /department\s*=\s*/i,
    /dept\s*=\s*/i,
    /classification\s*=\s*/i,
    /type\s*=\s*/i,
    /status\s*=\s*/i,
    /year\s*=\s*/i,
    /from\s*20\d{2}\s*to\s*20\d{2}/i,
    /من\s*20\d{2}\s*إلى\s*20\d{2}/,
    /الجهة\s*[:=]/,
    /النوع\s*[:=]/,
    /التصنيف\s*[:=]/,
    /الحالة\s*[:=]/,
    /السنة\s*[:=]/,
    /\b(all\s+(approved|active|completed|closed|published|under review|distributed))\b/i,
    /\bجميع\s+(الموافق\s+عليها|النشطة|المكتملة|المغلقة|المنشورة|تحت\s+المراجعة|الموزعة)\b/,
  ];
  if (metadataPatterns.some(p => p.test(q))) return "metadata";

  // Natural language / conversational patterns (must come before year-filter)
  const nlPatterns = [
    /^(show|find|give|get|list|display|search|look|ابحث|أعطني|اعرض|أعرض|اعطني|اجلب|أجلب|أريد|اريد)/i,
    /\b(all\s+(contracts|reports|memos|policies|tenders|plans|documents|files))\b/i,
    /(?:^|\s)(جميع|كل\s+الوثائق|كل\s+العقود|كل\s+التقارير|كل\s+المذكرات|كل\s+السياسات|كل\s+المناقصات|كل\s+الخطط)(?:\s|$)/,
  ];
  if (nlPatterns.some(p => p.test(q))) return "laserfiche";

  // Year-specific filter
  if (/\bfrom\s+(20\d{2})\b/i.test(q) || /\bto\s+(20\d{2})\b/i.test(q) || /\b(year|سنة)\s+(20\d{2})\b/i.test(q)) {
    return "year-filter";
  }

  // Short keyword query (few words, contains likely keyword terms)
  const tokens = q.split(/\s+/).filter(t => t.length > 2);
  if (tokens.length <= 3 && tokens.some(t => /\d/.test(t))) return "keyword";

  // Semantic query patterns (longer, conceptual, descriptive)
  const semanticPatterns = [
    /\b(policy|guideline|procedure|regulation|framework|strategy|initiative|program|plan|report|assessment|analysis|evaluation|survey|study|research|review|audit)\b/i,
    /(?:^|\s)(سياسة|إرشاد|إجراء|لائحة|إطار|استراتيجية|مبادرة|برنامج|خطة|تقرير|تقييم|تحليل|دراسة|بحث|مراجعة|تدقيق)(?:\s|$)/,
    /\b(regarding|about|related\s+to|concerning|pertaining\s+to|dealing\s+with|addressing)\b/i,
    /(?:^|\s)(حول|بخصوص|بشأن|يتعلق\s+ب)(?:\s|$)/,
  ];
  if (semanticPatterns.some(p => p.test(q))) return "semantic";

  // Default: mixed for longer queries, keyword for very short
  return tokens.length > 4 ? "mixed" : "keyword";
}

function selectEngines(intent: SearchIntent, lfConnected: boolean): string[] {
  const engines: string[] = [];

  switch (intent) {
    case "exact-id":
      engines.push("keyword", "laserfiche");
      break;
    case "metadata":
      engines.push("metadata", "hybrid", "laserfiche");
      break;
    case "year-filter":
      engines.push("keyword", "hybrid", "laserfiche");
      break;
    case "laserfiche":
      engines.push("laserfiche", "hybrid");
      break;
    case "keyword":
      engines.push("keyword", "hybrid");
      break;
    case "semantic":
      engines.push("semantic", "hybrid");
      break;
    case "mixed":
      engines.push("hybrid", "semantic", "keyword", "laserfiche");
      break;
  }

  if (!lfConnected) {
    return engines.filter(e => e !== "laserfiche");
  }

  return [...new Set(engines)];
}

function localSearchResultToUnified(r: SearchResult): UnifiedResult {
  const reasons: Array<{ reason: MatchReason; detail?: string }> = [];
  if (r.scoreBreakdown.semantic > 0.1) reasons.push({ reason: "semantic", detail: `score ${Math.round(r.scoreBreakdown.semantic * 100)}%` });
  if (r.scoreBreakdown.keyword > 0.1) reasons.push({ reason: "keyword", detail: `score ${Math.round(r.scoreBreakdown.keyword * 100)}%` });
  if (r.scoreBreakdown.metadata > 0.1) reasons.push({ reason: "metadata", detail: `score ${Math.round(r.scoreBreakdown.metadata * 100)}%` });
  if (r.matchedTerms.length > 0) reasons.push({ reason: "title-match", detail: r.matchedTerms.slice(0, 3).join(", ") });

  return {
    id: r.document.id,
    type: "local",
    title: r.document.title,
    titleAr: r.document.titleAr,
    department: r.document.department,
    departmentAr: r.document.departmentAr,
    docType: r.document.docType,
    docTypeAr: r.document.docTypeAr,
    classification: r.document.classification,
    securityLevel: r.document.securityLevel,
    author: r.document.author,
    year: r.document.year,
    createdAt: r.document.createdAt,
    snippet: r.snippet,
    snippetAr: r.snippetAr,
    score: r.score,
    matchReasons: reasons,
    laserficheId: r.document.laserficheId,
    pageCount: r.document.pageCount,
    fileSizeKb: r.document.fileSizeKb,
  };
}

function lfEntryToUnified(entry: any, command: string, score: number): UnifiedResult {
  const reasons: Array<{ reason: MatchReason; detail?: string }> = [
    { reason: "laserfiche", detail: `LF: ${command}` },
  ];
  if (entry.metadata && Object.keys(entry.metadata).length > 0) {
    reasons.push({ reason: "laserfiche-metadata", detail: `${Object.keys(entry.metadata).length} fields` });
  }

  return {
    id: `lf-${entry.id}`,
    type: "laserfiche",
    title: entry.name || `Entry ${entry.id}`,
    titleAr: null,
    department: entry.metadata?.Department?.[0] || entry.metadata?.الجهة?.[0] || "Laserfiche",
    departmentAr: null,
    docType: entry.extension?.toUpperCase() || "Document",
    docTypeAr: null,
    classification: "Official",
    securityLevel: "Internal",
    author: entry.creator || null,
    year: entry.creationTime ? new Date(entry.creationTime).getFullYear() : null,
    createdAt: entry.creationTime ? new Date(entry.creationTime) : null,
    snippet: entry.fullPath || `Laserfiche entry ${entry.id}`,
    snippetAr: null,
    score,
    matchReasons: reasons,
    previewUrl: entry.previewUrl,
    openUrl: entry.openUrl,
    downloadUrl: entry.downloadUrl,
    laserficheId: String(entry.id),
    metadata: entry.metadata,
    pageCount: entry.pageCount || null,
    fileSizeKb: entry.electronicDocumentSize ? Math.round(entry.electronicDocumentSize / 1024) : null,
  };
}

function deduplicateAndRank(results: UnifiedResult[]): UnifiedResult[] {
  const seen = new Set<string>();
  const unique: UnifiedResult[] = [];
  for (const r of results) {
    const key = r.laserficheId || r.id;
    if (seen.has(key)) {
      // Merge match reasons with existing
      const existing = unique.find(u => (u.laserficheId || u.id) === key);
      if (existing) {
        existing.score = Math.max(existing.score, r.score);
        const existingReasons = new Set(existing.matchReasons.map(m => m.reason));
        for (const m of r.matchReasons) {
          if (!existingReasons.has(m.reason)) {
            existing.matchReasons.push(m);
          }
        }
      }
      continue;
    }
    seen.add(key);
    unique.push(r);
  }
  return unique.sort((a, b) => b.score - a.score);
}

function normalizeScore(raw: number, engine: string, maxInBatch: number): number {
  // Normalize to 0-1 range relative to the best result in this engine
  if (maxInBatch <= 0) return 0;
  const relative = raw / maxInBatch;

  // Apply engine-specific quality curves
  switch (engine) {
    case "laserfiche": return 0.6 + relative * 0.35;
    case "hybrid": return 0.5 + relative * 0.45;
    case "semantic": return 0.45 + relative * 0.4;
    case "keyword": return 0.4 + relative * 0.45;
    case "metadata": return 0.5 + relative * 0.4;
    default: return relative;
  }
}

export async function executeSmartSearch(
  query: string,
  filters?: any,
  page = 1,
  limit = 10
): Promise<SmartSearchResponse> {
  const startTime = Date.now();
  const lang = detectLanguage(query);
  const intent = classifyIntent(query);
  const lfConfig = getLaserficheConfig();
  const lfConnected = !!lfConfig;
  const engines = selectEngines(intent, lfConnected);

  let localResults: UnifiedResult[] = [];
  let lfResults: UnifiedResult[] = [];
  let laserficheCommand: string | undefined;
  let laserficheTerms: string[] | undefined;

  // Run local engines in parallel
  const localEngines = engines.filter(e => e !== "laserfiche");
  const localPromises = localEngines.map(async (engine) => {
    try {
      const searchType = engine === "metadata" ? "hybrid" : engine;
      const response = await storage.searchDocuments({
        query,
        searchType: searchType as any,
        filters,
        page: 1,
        limit: 50,
      });
      const maxScore = response.results.length > 0 ? Math.max(...response.results.map(r => r.score)) : 1;
      return response.results.map(r => {
        const unified = localSearchResultToUnified(r);
        unified.score = normalizeScore(r.score, engine, maxScore);
        return unified;
      });
    } catch (err) {
      console.error(`Smart search local engine ${engine} failed:`, err);
      return [];
    }
  });

  // Run Laserfiche in parallel if connected
  const lfPromise = (async () => {
    if (!engines.includes("laserfiche") || !lfConfig) return [];
    try {
      const nlResult = naturalLanguageToLFSearchCommand(query);
      laserficheCommand = nlResult.command;
      laserficheTerms = nlResult.extractedTerms;

      const token = await getLaserficheToken(lfConfig);
      const allEntries = await laserficheRepositorySearch(lfConfig, token, nlResult.command, 50);
      const maxScore = allEntries.length > 0 ? allEntries.length : 1;

      const entries = await Promise.all(
        allEntries.map(async (entry: any) => {
          const [details, rawFields] = await Promise.all([
            laserficheGetEntry(lfConfig, token, Number(entry.id)).catch(() => entry),
            laserficheGetEntryFieldsRaw(lfConfig, token, Number(entry.id)).catch(() => []),
          ]);
          const metadata: Record<string, string[]> = {};
          for (const field of rawFields as any[]) {
            const name = String(field?.fieldName || "").trim();
            if (!name) continue;
            metadata[name] = Array.isArray(field?.values) ? field.values.map((v: any) => String(v?.value ?? "")).filter(Boolean) : [];
          }
          return {
            ...details,
            id: Number(details.id || entry.id),
            metadata,
            previewUrl: `/api/laserfiche/entries/${Number(entry.id)}/content?disposition=inline`,
            openUrl: `/api/laserfiche/entries/${Number(entry.id)}/open`,
            downloadUrl: `/api/laserfiche/entries/${Number(entry.id)}/content?disposition=attachment`,
          };
        })
      );

      return entries.map((entry, idx) => {
        const rawScore = 1 - (idx / maxScore);
        return lfEntryToUnified(entry, nlResult.command, normalizeScore(rawScore, "laserfiche", 1));
      });
    } catch (err) {
      console.error("Smart search Laserfiche engine failed:", err);
      return [];
    }
  })();

  const [localBatches, lfBatch] = await Promise.all([
    Promise.all(localPromises),
    lfPromise,
  ]);

  localResults = localBatches.flat();
  lfResults = lfBatch;

  const allResults = deduplicateAndRank([...localResults, ...lfResults]);
  const total = allResults.length;
  const start = (Math.max(page, 1) - 1) * limit;
  const paged = allResults.slice(start, start + limit);

  const processingTimeMs = Date.now() - startTime;

  return {
    query,
    queryLanguage: lang,
    total,
    results: paged,
    enginesUsed: engines,
    intent,
    processingTimeMs,
    lfConnected,
    laserficheCommand,
    laserficheTerms,
  };
}

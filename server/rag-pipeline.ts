/**
 * Enterprise-Grade RAG Pipeline for GovSearch AI
 *
 * Implements a hybrid retrieval pipeline combining:
 *   1. Laserfiche SimpleSearch (native full-text)
 *   2. Laserfiche Repository Search with metadata field targeting
 *   3. BM25 local scoring with field weights
 *   4. Arabic query expansion with synonym mapping
 *   5. Merge, re-rank, and confidence scoring
 *   6. Structured context with citations, match reasons, and context hits
 */

import {
  getLaserficheToken,
  laserficheGetEntry,
  laserficheGetEntryFieldsRaw,
  laserficheGetEntryTags,
  laserficheSimpleSearch,
  laserficheRepositorySearch,
  laserficheFieldValueSearch,
  type LaserficheConfig,
} from "./laserfiche";

/* ── Arabic Text Normalization ─────────────────────────────────────────── */

const ARABIC_STOPWORDS = new Set([
  "في", "من", "على", "إلى", "عن", "مع", "أو", "و", "أن", "كان", "كانت",
  "هذا", "هذه", "هؤلاء", "ذلك", "تلك", "التي", "الذي", "الذين", "اللذان",
  "لعام", "لسنة", "لعامي", "خلال", "حتى", "بعد", "قبل", "بين", "منذ",
  "يتم", "يجب", "جميع", "كل", "بما", "حيث", "وفق", "وفقا", "وفقاً",
  "لدى", "لهذا", "لذلك", "وكذلك", "أيضا", "أيضاً", "نحو", "نسبة",
  "الخاصة", "الخاص", "العامة", "العام", "المانية", "الماني",
]);

const ENGLISH_STOPWORDS = new Set([
  "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "from",
  "is", "are", "was", "were", "be", "been", "have", "has", "had", "do", "does", "did", "will", "would",
  "should", "may", "might", "all", "any", "this", "that", "these", "those", "its", "it", "as", "up",
  "out", "if", "so", "no", "not", "search", "find", "document", "documents", "file", "files",
]);

/**
 * Extract bare-value tokens from queries that don't specify field names.
 * E.g., "documents about Qatar" → ["Qatar"]; "all about Doha" → ["Doha"]
 * "The host country is Qatar" → ["Qatar"]
 */
function extractBareValueTokens(query: string): string[] {
  const norm = normalizeArabic(query);
  // Generic Arabic/English prefixes that introduce bare values
  const prefixes = [
    "ب", "حول", "عن", "فيها", "في", "تحتوي", "تحتوي على", "يساوي",
    "باسم", "عليها", "ل", "لها", "خصوص", "بخصوص", "بشأن",
    "about", "containing", "contains", "with", "where", "for",
    "is", "equals", "in", "has", "have",
  ];
  const tokens = tokenize(norm);
  const bare: string[] = [];

  // If query has a pattern "prefix ... value", extract value after prefix
  for (const prefix of prefixes) {
    if (norm.includes(prefix)) {
      const after = norm.slice(norm.indexOf(prefix) + prefix.length).trim();
      const afterTokens = after.split(/\s+/).filter((t) => t.length >= 2);
      for (const at of afterTokens.slice(0, 3)) {
        if (!ARABIC_STOPWORDS.has(at) && !ENGLISH_STOPWORDS.has(at)) {
          bare.push(at);
        }
      }
    }
  }

  // Also, the last meaningful token in short queries is often the bare value
  if (tokens.length <= 4 && tokens.length > 0) {
    const last = tokens[tokens.length - 1];
    if (!ARABIC_STOPWORDS.has(last) && !ENGLISH_STOPWORDS.has(last)) {
      bare.push(last);
    }
  }

  // Country / city / number patterns
  const countryPattern = /(قطر|الدوحة|السعودية|الإمارات|الكويت|عمان|مصر|الأردن|البحرين|qatar|doha|saudi|uae|kuwait|oman|egypt|jordan|bahrain)/i;
  const countryMatch = norm.match(countryPattern);
  if (countryMatch) bare.push(countryMatch[1]);

  // Number pattern (transaction IDs, etc.)
  const numberMatch = norm.match(/(\d{3,})/);
  if (numberMatch) bare.push(numberMatch[1]);

  return [...new Set(bare)];
}

function normalizeArabic(text: string): string {
  return (text || "")
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[\u064B-\u065F\u0670]/g, "") // remove diacritics
    .replace(/[إأآ]/g, "ا") // alef variants
    .replace(/ة/g, "ه") // ta marbuta
    .replace(/ى/g, "ي") // alif maqsura
    .replace(/[ءؤئ]/g, "") // hamza variants
    .replace(/\s+/g, " ")
    .trim();
}

/* ── Synonym & Query Expansion Maps ──────────────────────────────────── */

const ARABIC_SYNONYMS: Record<string, string[]> = {
  "معاملة": ["طلب", "خطاب", "وثيقة", "نموذج", "مذكرة", "إشعار"],
  "طلب": ["معاملة", "وثيقة", "خطاب", "نموذج", "تمديد"],
  "خطاب": ["رسالة", "مراسلة", "مذكرة", "تعميم", "عقد"],
  "وثيقة": ["مستند", "ملف", "عقد", "مذكرة", "تقرير", "تعميم"],
  "نموذج": ["شكل", "قالب", "مستند", "وثيقة", "الى"],
  "قرار": ["رأي", "تصريح", "تعميم", "مؤشر", "مذكرة", "اعتماد"],
  "تعميم": ["مذكرة", "وثيقة", "خطاب", "رسالة"],
  "ترشيح": ["طلب ترشيح", "نموذج ترشيح", "إعتماد ترشيح", "ترشيح موظف", "موظف موصى ترشيح"],
  "عقد": ["اتفاقية", "معاملة", "شريكة", "وثيقة"],
  "مشروع": ["برنامج", "خطة", "تنفيذ", "عمل"],
  "موظف": ["موارد بشرية", "عامل", "موظفين"],
  "راتب": ["رواتب", "مكافأة", "مرتب", "أجر", "رمز راتب"],
  "رواتب": ["راتب", "مكافأة", "مراتب", "أجور", "رمز رواتب"],
  "تدقيق": ["تفتيش", "مراجعة", "فحص", "امتثال"],
  "ميزانية": ["المالية", "بيانات", "مصروفات", "فاءات"],
  "تقرير": ["وثيقة", "مستند", "ملف", "تحليل", "رسالة"],
  "سياسة": ["إرشاد", "لائحة", "تعميم", "قرار", "عمل"],
  "تدريب": ["دورة", "أساسية", "مؤهل", "دراسة"],
  "بنية": ["تحتية", "المعلومات", "بنية تحتية"],
  // Country-related synonyms
  "البلد": ["الدولة", "المستضيف", "الموقع", "المحل"],
  "الدولة": ["البلد", "المستضيف", "الموقع", "country"],
  "المستضيف": ["البلد المستضيف", "الدولة المستضيفة", "host", "hosting"],
  "قطر": ["qatar", "الدوحة", "doha"],
  "qatar": ["قطر", "الدوحة", "doha"],
};

function tokenize(text: string): string[] {
  return text
    .toLowerCase()
    .replace(/[^\u0600-\u06FFa-zA-Z0-9\s]/g, " ")
    .split(/\s+/)
    .filter((t) => t.length > 2)
    .filter((t) => !ENGLISH_STOPWORDS.has(t) && !ARABIC_STOPWORDS.has(t));
}

function expandQueryTokens(query: string): { tokens: string[]; expanded: string[] } {
  const normalized = normalizeArabic(query);
  const rawTokens = tokenize(normalized);
  const expanded: string[] = [...rawTokens];

  for (const t of rawTokens) {
    const prefixes = ["ال", "وال", "بال", "كال", "فال", "لل", "وب", "فب", "كب", "وك", "فك"];
    const suffixes = ["ها", "هم", "هن", "هما", "كم", "كن", "كما", "ية", "ات", "ون", "ين", "ان"];
    for (const p of prefixes) {
      if (t.startsWith(p) && t.length > p.length + 2) expanded.push(t.slice(p.length));
    }
    for (const s of suffixes) {
      if (t.endsWith(s) && t.length > s.length + 2) expanded.push(t.slice(0, t.length - s.length));
    }

    for (const [root, syns] of Object.entries(ARABIC_SYNONYMS)) {
      if (root === t || expanded.includes(root)) {
        expanded.push(...syns);
      }
      for (const syn of syns) {
        if (syn === t || normalizeArabic(syn) === t) {
          expanded.push(root, ...syns);
        }
      }
    }
  }

  return { tokens: rawTokens, expanded: [...new Set(expanded)] };
}

/* ── BM25 Implementation ───────────────────────────────────────────────── */

function avgFieldLength(docs: Array<Record<string, string>>, field: string): number {
  const lengths = docs.map((d) => (d[field] || "").length);
  return lengths.length > 0 ? lengths.reduce((a, b) => a + b, 0) / lengths.length : 1;
}

function computeBM25(
  docTexts: Record<string, string>,
  queryTokens: string[],
  corpusStats: { totalDocs: number; avgLengths: Record<string, number> }
): number {
  const K1 = 1.5;
  const B = 0.75;

  const FIELD_WEIGHTS: Record<string, number> = {
    name: 4.0,
    templateName: 3.0,
    creator: 2.5,
    folderName: 2.0,
    metadata: 2.5,
    path: 1.5,
    body: 0.5,
  };

  let totalScore = 0;

  const docCountWithToken: Record<string, number> = {};
  for (const token of queryTokens) {
    let count = 0;
    for (const text of Object.values(docTexts)) {
      if (text.includes(token)) count++;
    }
    docCountWithToken[token] = count;
  }

  for (const [field, weight] of Object.entries(FIELD_WEIGHTS)) {
    const text = (docTexts[field] || "").toLowerCase();
    const len = text.length;
    const avgLen = corpusStats.avgLengths[field] || 1;
    const normLen = len / avgLen;

    let fieldScore = 0;
    for (const token of queryTokens) {
      const tf = (text.match(new RegExp(token.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "g")) || []).length;
      if (tf === 0) continue;

      const n = docCountWithToken[token] || 0;
      const idf = Math.log(1 + (corpusStats.totalDocs - n + 0.5) / (n + 0.5));
      const tfNorm = (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * normLen));
      fieldScore += idf * tfNorm;
    }

    totalScore += fieldScore * weight;
  }

  return totalScore;
}

/* ── Document Types ──────────────────────────────────────────────────────────────────────────────────── */

type SearchableDoc = {
  id: number;
  name: string;
  path: string;
  folderName: string;
  templateName?: string;
  creator?: string;
  creationTime?: string;
  lastModifiedTime?: string;
  metadata?: Record<string, string[]>;
  isElectronicDocument?: boolean;
};

export type MatchReason =
  | "exact-metadata-match"
  | "native-search-match"
  | "native-search-context-hit"
  | "title-match"
  | "template-match"
  | "field-match"
  | "bm25-metadata"
  | "bm25-name"
  | "bm25-body"
  | "simple-search-match";

type ScoredDoc = SearchableDoc & {
  score: number;
  matchReasons: MatchReason[];
  matchedTokens: string[];
  contextHits?: string[];
  source: "simple-search" | "repository-search" | "field-search" | "bm25" | "merged";
};

export type RetrievalResult = {
  docs: ScoredDoc[];
  confidence: number;
  confidenceLabel: "high" | "medium" | "low";
  topScore: number;
  scoreGap: number;
  queryCoverage: number;
  queryTokens: string[];
};

function flattenMetadata(metadata?: Record<string, string[]>): string {
  if (!metadata) return "";
  return Object.entries(metadata)
    .map(([k, v]) => `${k}: ${v.join(", ")}`)
    .join(" ");
}

function buildDocTexts(doc: SearchableDoc): Record<string, string> {
  return {
    name: normalizeArabic(doc.name),
    templateName: normalizeArabic(doc.templateName || ""),
    creator: normalizeArabic(doc.creator || ""),
    folderName: normalizeArabic(doc.folderName || ""),
    metadata: normalizeArabic(flattenMetadata(doc.metadata)),
    path: normalizeArabic(doc.path),
    body: normalizeArabic(flattenMetadata(doc.metadata) + " " + (doc.name || "")),
  };
}

/* ── BM25 Retrieval (local fallback) ───────────────────────────────────────────────────── */

export function retrieveDocs(
  query: string,
  docs: SearchableDoc[],
  options?: { topK?: number; minScore?: number }
): RetrievalResult {
  const { tokens, expanded } = expandQueryTokens(query);
  const topK = options?.topK ?? 20;

  if (expanded.length === 0 || docs.length === 0) {
    return {
      docs: [],
      confidence: 0,
      confidenceLabel: "low",
      topScore: 0,
      scoreGap: 0,
      queryCoverage: 0,
      queryTokens: tokens,
    };
  }

  const allTexts = docs.map((d) => buildDocTexts(d));
  const avgLengths: Record<string, number> = {
    name: avgFieldLength(allTexts, "name"),
    templateName: avgFieldLength(allTexts, "templateName"),
    creator: avgFieldLength(allTexts, "creator"),
    folderName: avgFieldLength(allTexts, "folderName"),
    metadata: avgFieldLength(allTexts, "metadata"),
    path: avgFieldLength(allTexts, "path"),
    body: avgFieldLength(allTexts, "body"),
  };
  const corpusStats = { totalDocs: docs.length, avgLengths };

  const scored: ScoredDoc[] = docs.map((doc) => {
    const docTexts = buildDocTexts(doc);
    const score = computeBM25(docTexts, expanded, corpusStats);

    const matchedTokens: string[] = [];
    for (const token of expanded) {
      const allText = Object.values(docTexts).join(" ");
      if (allText.includes(token)) matchedTokens.push(token);
    }

    const matchReasons: MatchReason[] = [];
    if (docTexts.name.includes(expanded[0] || "")) matchReasons.push("title-match");
    if (docTexts.templateName && expanded.some((t) => docTexts.templateName.includes(t))) {
      matchReasons.push("template-match");
    }
    if (docTexts.metadata && expanded.some((t) => docTexts.metadata.includes(t))) {
      matchReasons.push("bm25-metadata");
    }
    if (score > 0 && matchReasons.length === 0) matchReasons.push("bm25-body");

    return { ...doc, score, matchReasons, matchedTokens, source: "bm25" };
  });

  scored.sort((a, b) => b.score - a.score);
  const topDocs = scored.slice(0, topK);

  const topScore = topDocs[0]?.score ?? 0;
  const secondScore = topDocs[1]?.score ?? 0;
  const scoreGap = topScore > 0 ? (topScore - secondScore) / topScore : 0;
  const queryCoverage = tokens.length > 0
    ? topDocs[0]?.matchedTokens.filter((t) => tokens.includes(t)).length / tokens.length
    : 0;

  const scoreMagnitude = Math.min(topScore / 10, 1);
  const confidence = scoreMagnitude * 0.4 + scoreGap * 0.4 + queryCoverage * 0.2;

  let confidenceLabel: "high" | "medium" | "low" = "low";
  if (confidence >= 0.7) confidenceLabel = "high";
  else if (confidence >= 0.4) confidenceLabel = "medium";

  return {
    docs: topDocs,
    confidence,
    confidenceLabel,
    topScore,
    scoreGap,
    queryCoverage,
    queryTokens: tokens,
  };
}

/* ── Hybrid Retrieval Engine ─────────────────────────────────────────────────────────────────────────────────────────── */

type LFEntry = {
  id: number;
  name: string;
  entryType: string;
  fullPath: string;
  creator: string;
  creationTime?: string;
  lastModifiedTime?: string;
  templateName?: string;
  fields?: Record<string, string | number | boolean | null>;
  tags?: string[];
  contextHits?: string[];
};

function lfEntryToSearchableDoc(entry: LFEntry): SearchableDoc {
  const meta: Record<string, string[]> = {};
  if (entry.fields) {
    for (const [k, v] of Object.entries(entry.fields)) {
      if (v != null) meta[k] = [String(v)];
    }
  }
  if (entry.tags) meta["Tags"] = entry.tags;
  return {
    id: entry.id,
    name: entry.name || `Entry ${entry.id}`,
    path: entry.fullPath || "",
    folderName: entry.fullPath?.split("/").slice(0, -1).join("/") || "",
    templateName: entry.templateName || "",
    creator: entry.creator || "",
    creationTime: entry.creationTime || "",
    lastModifiedTime: entry.lastModifiedTime || "",
    metadata: meta,
    isElectronicDocument: true,
  };
}

function mergeSearchResults(
  simpleResults: LFEntry[],
  fieldResults: LFEntry[],
  bm25Results: ScoredDoc[],
  topK: number
): ScoredDoc[] {
  const merged = new Map<number, ScoredDoc>();

  // Priority 1: SimpleSearch (native full-text) → base 6.0
  for (const entry of simpleResults) {
    const doc = lfEntryToSearchableDoc(entry);
    merged.set(entry.id, {
      ...doc,
      score: 6.0,
      matchReasons: ["simple-search-match"],
      matchedTokens: [],
      source: "simple-search",
      contextHits: entry.contextHits,
    });
  }

  // Priority 2: Field-targeted search (exact metadata match) → base 10.0
  for (const entry of fieldResults) {
    const existing = merged.get(entry.id);
    if (existing) {
      existing.score = Math.max(existing.score, 10.0);
      if (!existing.matchReasons.includes("exact-metadata-match")) {
        existing.matchReasons.push("exact-metadata-match", "field-match");
      }
      existing.source = "merged";
      if (entry.contextHits) existing.contextHits = entry.contextHits;
    } else {
      const doc = lfEntryToSearchableDoc(entry);
      merged.set(entry.id, {
        ...doc,
        score: 10.0,
        matchReasons: ["exact-metadata-match", "field-match"],
        matchedTokens: [],
        source: "field-search",
        contextHits: entry.contextHits,
      });
    }
  }

  // Priority 3: BM25 fallback → scaled down
  for (const doc of bm25Results) {
    const existing = merged.get(doc.id);
    if (existing) {
      existing.score += doc.score * 0.3;
      for (const r of doc.matchReasons) {
        if (!existing.matchReasons.includes(r)) existing.matchReasons.push(r);
      }
      existing.matchedTokens.push(...doc.matchedTokens);
      existing.source = "merged";
    } else {
      merged.set(doc.id, { ...doc, score: Math.max(doc.score * 0.5, 1.0) });
    }
  }

  const all = [...merged.values()];
  all.sort((a, b) => b.score - a.score);
  return all.slice(0, topK);
}

export interface HybridRetrievalResult extends RetrievalResult {
  strategiesUsed: string[];
}

export async function hybridRetrieve(
  query: string,
  allDocs: SearchableDoc[],
  config: LaserficheConfig,
  options?: { topK?: number; lang?: "ar" | "en" | "mixed" }
): Promise<HybridRetrievalResult> {
  const topK = options?.topK ?? 15;
  const strategiesUsed: string[] = [];
  const token = await getLaserficheToken(config);

  // Strategy 1: BM25 on all cached docs (always runs)
  const bm25Result = retrieveDocs(query, allDocs, { topK: topK * 2 });
  strategiesUsed.push("bm25-local");

  // Field Discovery & Field-Value Extraction
  let fieldPairs: import("./field-discovery").FieldValuePair[] = [];
  let discoveredFields: import("./field-discovery").DiscoveredField[] = [];
  try {
    const { discoverFields, extractFieldValuePairs } = await import("./field-discovery");
    discoveredFields = await discoverFields(config);
    fieldPairs = extractFieldValuePairs(query, discoveredFields);
  } catch {
    // Field discovery is optional
  }

  // Strategy 2: Laserfiche SimpleSearch (native full-text)
  let simpleResults: LFEntry[] = [];
  try {
    const rawSimple = await laserficheSimpleSearch(config, token, query, 50);
    simpleResults = rawSimple.map((e: any) => ({ ...e, contextHits: e.contextHits }));
    if (simpleResults.length > 0) strategiesUsed.push("laserfiche-simple-search");
  } catch {
    // SimpleSearch may fail
  }

  // Strategy 3: Laserfiche Repository Search with metadata clauses
  let fieldResults: LFEntry[] = [];
  if (fieldPairs.length > 0) {
    try {
      const { expandValue } = await import("./field-discovery");
      const commands: string[] = [];
      for (const pair of fieldPairs) {
        const expandedValues = expandValue(pair.value);
        for (const val of expandedValues) {
          commands.push(`{LF:LOOKIN="FIELD:${pair.field.fieldName}"}="${val.replace(/"/g, '\\"')}"`);
        }
      }
      for (const cmd of commands.slice(0, 3)) {
        try {
          const res = await laserficheRepositorySearch(config, token, cmd, 25);
          fieldResults.push(...res.map((e: any) => ({ ...e, contextHits: e.contextHits })));
        } catch {}
      }
      if (fieldResults.length > 0) strategiesUsed.push("laserfiche-field-search");
    } catch {}
  }

  // Strategy 4: Repository Search with keyword fallback
  let repoKeywordResults: LFEntry[] = [];
  if (simpleResults.length === 0 && fieldResults.length === 0) {
    try {
      const { expanded } = expandQueryTokens(query);
      const keywordCmd = `{LF:Basic~="${expanded.slice(0, 5).join(" ")}"}`;
      repoKeywordResults = await laserficheRepositorySearch(config, token, keywordCmd, 25);
      if (repoKeywordResults.length > 0) strategiesUsed.push("laserfiche-keyword-search");
    } catch {}
  }

  // Strategy 5: Bare-value cross-field search (no field name in query)
  let bareValueResults: LFEntry[] = [];
  if (fieldPairs.length === 0 && discoveredFields.length > 0) {
    try {
      const { expandValue } = await import("./field-discovery");
      // Extract likely bare values: tokens after generic words
      const bareTokens = extractBareValueTokens(query);
      for (const bt of bareTokens) {
        const expandedVals = expandValue(bt);
        for (const val of expandedVals) {
          const crossField = await laserficheFieldValueSearch(config, token, val, discoveredFields.map((f) => f.name), 8);
          bareValueResults.push(...crossField);
        }
      }
      if (bareValueResults.length > 0) strategiesUsed.push("laserfiche-bare-value-search");
    } catch (e) {
      console.log("[hybridRetrieve] Bare-value search error:", e);
    }
  }

  // Merge all results
  const allFieldResults = [...fieldResults, ...repoKeywordResults, ...bareValueResults];
  const merged = mergeSearchResults(simpleResults, allFieldResults, bm25Result.docs, topK);

  // Enrich merged results with full metadata
  const enrichedDocs = await Promise.all(
    merged.map(async (doc) => {
      try {
        const [entry, rawFields, tags] = await Promise.all([
          laserficheGetEntry(config, token, doc.id).catch(() => null),
          laserficheGetEntryFieldsRaw(config, token, doc.id).catch(() => []),
          laserficheGetEntryTags(config, token, doc.id).catch(() => []),
        ]);

        const metadata: Record<string, string[]> = {};
        for (const field of rawFields as any[]) {
          const fieldName = String(field?.fieldName || "").trim();
          if (!fieldName) continue;
          metadata[fieldName] = Array.isArray(field?.values)
            ? field.values.map((v: any) => String(v?.value ?? "")).filter(Boolean)
            : [];
        }
        if (tags.length > 0) metadata["Tags"] = tags;

        const enriched: SearchableDoc = {
          ...doc,
          name: (entry as any)?.name || doc.name,
          path: (entry as any)?.fullPath || doc.path,
          creator: (entry as any)?.creator || doc.creator,
          creationTime: (entry as any)?.creationTime || doc.creationTime,
          lastModifiedTime: (entry as any)?.lastModifiedTime || doc.lastModifiedTime,
          templateName: (entry as any)?.templateName || doc.templateName,
          metadata,
        };

        const docTexts = buildDocTexts(enriched);
        const avgLengths = Object.fromEntries(
          Object.keys(docTexts).map((k) => [k, avgFieldLength(allDocs.map((d) => buildDocTexts(d)), k)])
        );
        const { expanded } = expandQueryTokens(query);
        const bm25Bonus = computeBM25(docTexts, expanded, { totalDocs: allDocs.length, avgLengths });
        const finalScore = doc.score + Math.min(bm25Bonus * 0.5, 3.0);

        const matchedTokens: string[] = [];
        for (const token of expanded) {
          const allText = Object.values(docTexts).join(" ");
          if (allText.includes(token)) matchedTokens.push(token);
        }

        return {
          ...enriched,
          score: finalScore,
          matchReasons: doc.matchReasons,
          matchedTokens,
          source: doc.source,
          contextHits: doc.contextHits,
        };
      } catch {
        return doc;
      }
    })
  );

  enrichedDocs.sort((a, b) => b.score - a.score);

  const topScore = enrichedDocs[0]?.score ?? 0;
  const secondScore = enrichedDocs[1]?.score ?? 0;
  const scoreGap = topScore > 0 ? (topScore - secondScore) / topScore : 0;
  const queryCoverage = bm25Result.queryTokens.length > 0
    ? (enrichedDocs[0]?.matchedTokens.filter((t) => bm25Result.queryTokens.includes(t)).length ?? 0) / bm25Result.queryTokens.length
    : 0;

  const hasNativeSearch = strategiesUsed.some((s) => s.startsWith("laserfiche"));
  const hasFieldSearch = strategiesUsed.includes("laserfiche-field-search");
  const sourceBoost = hasFieldSearch ? 0.2 : hasNativeSearch ? 0.1 : 0;
  const scoreMagnitude = Math.min(topScore / 15, 1);
  const confidence = Math.min(scoreMagnitude * 0.4 + scoreGap * 0.4 + queryCoverage * 0.2 + sourceBoost, 1.0);

  let confidenceLabel: "high" | "medium" | "low" = "low";
  if (confidence >= 0.7) confidenceLabel = "high";
  else if (confidence >= 0.4) confidenceLabel = "medium";

  return {
    docs: enrichedDocs,
    confidence,
    confidenceLabel,
    topScore,
    scoreGap,
    queryCoverage,
    queryTokens: bm25Result.queryTokens,
    strategiesUsed,
  };
}

/* ── Backward-compatible alias ────────────────────────────────────────────────────────────────────────────────────────────────────────────── */

export async function retrieveLaserficheDocs(
  query: string,
  allDocs: SearchableDoc[],
  config: LaserficheConfig,
  options?: { topK?: number; fetchMetadata?: boolean }
): Promise<RetrievalResult> {
  const result = await hybridRetrieve(query, allDocs, config, { topK: options?.topK ?? 15 });
  return {
    docs: result.docs,
    confidence: result.confidence,
    confidenceLabel: result.confidenceLabel,
    topScore: result.topScore,
    scoreGap: result.scoreGap,
    queryCoverage: result.queryCoverage,
    queryTokens: result.queryTokens,
  };
}

/* ── Structured Context Building (with match reasons & context hits) ── */

export function buildStructuredContext(
  result: RetrievalResult,
  lang: "ar" | "en"
): string {
  const { docs, confidence, confidenceLabel } = result;

  if (docs.length === 0) {
    return lang === "ar"
      ? "\u200f" + "لم يتم العثور على أي وثائق مطابقة."
      : "No matching documents were found.";
  }

  const lines: string[] = [];
  if (lang === "ar") {
    lines.push("\u200f" + `الوثائق المسترجعة (${docs.length} نتيجة – مستوى الثقة: ${confidenceLabel === "high" ? "عال" : confidenceLabel === "medium" ? "متوسط" : "منخفض"})`);
  } else {
    lines.push(`Retrieved Documents (${docs.length} results – confidence: ${confidenceLabel})`);
  }
  lines.push("");

  for (let i = 0; i < docs.length; i++) {
    const d = docs[i];
    if (lang === "ar") {
      lines.push(`[${i + 1}] الوثيقة: ${d.name}`);
      lines.push(`    رقم المدخل: ${d.id}`);
      if (d.templateName) lines.push(`    القالب: ${d.templateName}`);
      if (d.creator) lines.push(`    المنشئ: ${d.creator}`);
      if (d.folderName) lines.push(`    المجلد: ${d.folderName}`);
      if (d.path) lines.push(`    المسار: ${d.path}`);
      if (d.creationTime) lines.push(`    تاريخ الإنشاء: ${new Date(d.creationTime).toLocaleDateString("ar-SA")}`);
      if (d.metadata && Object.keys(d.metadata).length > 0) {
        const metaLines = Object.entries(d.metadata)
          .slice(0, 6)
          .map(([k, v]) => `      • ${k}: ${v.slice(0, 3).join(", ")}`)
          .join("\n");
        lines.push(`    البيانات الوصفية:\n${metaLines}`);
      }
      if (d.matchReasons && d.matchReasons.length > 0) {
        lines.push(`    أسباب التطابق: ${d.matchReasons.join(", ")}`);
      }
      if (d.contextHits && d.contextHits.length > 0) {
        lines.push(`    مقتطفات السياق: ${d.contextHits.slice(0, 2).join(" | ")}`);
      }
      lines.push(`    درجة التطابق: ${Math.round(d.score * 10) / 10}`);
    } else {
      lines.push(`[${i + 1}] Document: ${d.name}`);
      lines.push(`    Entry ID: ${d.id}`);
      if (d.templateName) lines.push(`    Template: ${d.templateName}`);
      if (d.creator) lines.push(`    Created By: ${d.creator}`);
      if (d.folderName) lines.push(`    Folder: ${d.folderName}`);
      if (d.path) lines.push(`    Path: ${d.path}`);
      if (d.creationTime) lines.push(`    Created: ${new Date(d.creationTime).toLocaleDateString("en-GB")}`);
      if (d.metadata && Object.keys(d.metadata).length > 0) {
        const metaLines = Object.entries(d.metadata)
          .slice(0, 6)
          .map(([k, v]) => `      • ${k}: ${v.slice(0, 3).join(", ")}`)
          .join("\n");
        lines.push(`    Metadata:\n${metaLines}`);
      }
      if (d.matchReasons && d.matchReasons.length > 0) {
        lines.push(`    Match reasons: ${d.matchReasons.join(", ")}`);
      }
      if (d.contextHits && d.contextHits.length > 0) {
        lines.push(`    Context hits: ${d.contextHits.slice(0, 2).join(" | ")}`);
      }
      lines.push(`    Relevance: ${Math.round(d.score * 10) / 10}`);
    }
    lines.push("");
  }

  return lines.join("\n");
}

/* ── Confidence Gate for Chat ─────────────────────────────────────────── */

export function shouldAnswer(query: string, result: RetrievalResult): { answer: boolean; reason: string } {
  // Always answer if the user asks a direct entry-ID question
  if (/\bentry\s*#?\s*\d+/i.test(query) || /\bالمدخل\s*\d+/.test(query)) {
    return { answer: true, reason: "Direct entry ID query" };
  }

  if (result.confidenceLabel === "high") {
    return { answer: true, reason: `High confidence (${Math.round(result.confidence * 100)}%)` };
  }

  if (result.confidenceLabel === "medium" && result.docs.length >= 3) {
    return { answer: true, reason: `Medium confidence with multiple results` };
  }

  if (result.docs.length === 0) {
    return { answer: false, reason: "No matching documents found" };
  }

  return {
    answer: false,
    reason: `Low confidence (${Math.round(result.confidence * 100)}%) – insufficient evidence`,
  };
}

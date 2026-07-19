/**
 * Enterprise-Grade RAG Pipeline for GovSearch AI
 *
 * This module implements a complete search-and-retrieval pipeline:
 *   1. Query expansion (Arabic normalization + synonyms + related terms)
 *   2. BM25 keyword scoring with field weights (metadata-first)
 *   3. Two-stage retrieval (quick scan → detailed metadata for top candidates)
 *   4. Re-ranking (weighted signal combination)
 *   5. Confidence scoring (based on score gap + query coverage)
 *   6. Structured context building (citations with doc name, ID, template, folder)
 */

import {
  getLaserficheConfig,
  getLaserficheToken,
  laserficheGetEntry,
  laserficheGetEntryFieldsRaw,
  laserficheGetEntryTags,
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

function normalizeArabic(text: string): string {
  return (text || "")
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[\u064B-\u065F\u0670]/g, "") // remove diacritics
    .replace(/[إأآ]/g, "ا") // alef variants
    .replace(/ة/g, "ه") // ta marbuta
    .replace(/ى/g, "ي") // alif maqsura
    .replace(/[\u0621\u0624\u0626]/g, "") // hamza variants
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
    // Strip Arabic affixes
    const prefixes = ["ال", "وال", "بال", "كال", "فال", "لل", "وب", "فب", "كب", "وك", "فك"];
    const suffixes = ["ها", "هم", "هن", "هما", "كم", "كن", "كما", "ية", "ات", "ون", "ين", "ان"];
    for (const p of prefixes) {
      if (t.startsWith(p) && t.length > p.length + 2) expanded.push(t.slice(p.length));
    }
    for (const s of suffixes) {
      if (t.endsWith(s) && t.length > s.length + 2) expanded.push(t.slice(0, t.length - s.length));
    }

    // Add synonyms
    for (const [root, syns] of Object.entries(ARABIC_SYNONYMS)) {
      if (root === t || expanded.includes(root)) {
        expanded.push(...syns);
      }
      // Check if query token matches any synonym
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

  // Field weights: metadata first, then body
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

  // Pre-compute token frequencies across corpus (IDF)
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

/* ── Document Type ─────────────────────────────────────────────────────── */

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

type ScoredDoc = SearchableDoc & {
  score: number;
  matchReasons: string[];
  matchedTokens: string[];
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

/* ── Metadata Flattening ─────────────────────────────────────────────── */

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

/* ── Main Retrieval Function ─────────────────────────────────────────── */

export function retrieveDocs(
  query: string,
  docs: SearchableDoc[],
  options?: {
    topK?: number;
    minScore?: number;
  }
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

  // Compute corpus stats for BM25
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

  // Score each document
  const scored: ScoredDoc[] = docs.map((doc) => {
    const docTexts = buildDocTexts(doc);
    const score = computeBM25(docTexts, expanded, corpusStats);

    // Track which tokens actually matched
    const matchedTokens: string[] = [];
    for (const token of expanded) {
      const allText = Object.values(docTexts).join(" ");
      if (allText.includes(token)) matchedTokens.push(token);
    }

    // Match reasons
    const matchReasons: string[] = [];
    if (score > 0) matchReasons.push(`BM25 ${Math.round(score * 10) / 10}`);
    if (docTexts.name.includes(expanded[0] || "")) matchReasons.push("name-match");
    if (docTexts.templateName && expanded.some((t) => docTexts.templateName.includes(t))) {
      matchReasons.push("template-match");
    }
    if (docTexts.metadata && expanded.some((t) => docTexts.metadata.includes(t))) {
      matchReasons.push("metadata-match");
    }

    return { ...doc, score, matchReasons, matchedTokens };
  });

  // Sort and take top K
  scored.sort((a, b) => b.score - a.score);
  const topDocs = scored.slice(0, topK);

  // Confidence scoring
  const topScore = topDocs[0]?.score ?? 0;
  const secondScore = topDocs[1]?.score ?? 0;
  const scoreGap = topScore > 0 ? (topScore - secondScore) / topScore : 0;
  const queryCoverage = tokens.length > 0
    ? topDocs[0]?.matchedTokens.filter((t) => tokens.includes(t)).length / tokens.length
    : 0;

  // Normalize confidence: combine top score magnitude, gap between 1st/2nd, and query coverage
  // Weighted: 40% score magnitude (scaled against expected max ~10), 40% gap, 20% coverage
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

/* ── Two-Stage Laserfiche Retrieval ──────────────────────────────────── */

export async function retrieveLaserficheDocs(
  query: string,
  allDocs: SearchableDoc[],
  config: LaserficheConfig,
  options?: { topK?: number; fetchMetadata?: boolean }
): Promise<RetrievalResult> {
  const topK = options?.topK ?? 15;
  const fetchMetadata = options?.fetchMetadata ?? true;

  // Stage 1: BM25 score all docs (quick, no API calls)
  const stage1 = retrieveDocs(query, allDocs, { topK });

  if (!fetchMetadata || stage1.docs.length === 0) {
    return stage1;
  }

  // Stage 2: Fetch full metadata for top candidates only
  const token = await getLaserficheToken(config);
  const enrichedDocs = await Promise.all(
    stage1.docs.map(async (doc) => {
      try {
        const [entry, rawFields, tags] = await Promise.all([
          laserficheGetEntry(config, token, doc.id).catch(() => null),
          laserficheGetEntryFieldsRaw(config, token, doc.id).catch(() => []),
          laserficheGetEntryTags(config, token, doc.id).catch(() => []),
        ]);

        const metadata: Record<string, string[]> = {};
        for (const field of rawFields) {
          const fieldName = String(field?.fieldName || "").trim();
          if (!fieldName) continue;
          metadata[fieldName] = Array.isArray(field?.values)
            ? field.values.map((v: any) => String(v?.value ?? "")).filter(Boolean)
            : [];
        }
        if (tags.length > 0) metadata["Tags"] = tags;

        const enriched: SearchableDoc = {
          ...doc,
          name: entry?.name || doc.name,
          path: entry?.fullPath || doc.path,
          creator: entry?.creator || doc.creator,
          creationTime: entry?.creationTime || doc.creationTime,
          lastModifiedTime: entry?.lastModifiedTime || doc.lastModifiedTime,
          templateName: entry?.templateName || doc.templateName,
          metadata,
        };

        // Re-score with enriched metadata
        const docTexts = buildDocTexts(enriched);
        const avgLengths = Object.fromEntries(
          Object.keys(docTexts).map((k) => [k, avgFieldLength(allDocs.map((d) => buildDocTexts(d)), k)])
        );
        const { expanded } = expandQueryTokens(query);
        const newScore = computeBM25(docTexts, expanded, { totalDocs: allDocs.length, avgLengths });

        const matchedTokens: string[] = [];
        for (const token of expanded) {
          const allText = Object.values(docTexts).join(" ");
          if (allText.includes(token)) matchedTokens.push(token);
        }

        return { ...enriched, score: newScore, matchReasons: doc.matchReasons, matchedTokens };
      } catch {
        return doc;
      }
    })
  );

  enrichedDocs.sort((a, b) => b.score - a.score);

  const topScore = enrichedDocs[0]?.score ?? 0;
  const secondScore = enrichedDocs[1]?.score ?? 0;
  const scoreGap = topScore > 0 ? (topScore - secondScore) / topScore : 0;
  const queryCoverage = stage1.queryTokens.length > 0
    ? (enrichedDocs[0]?.matchedTokens.filter((t) => stage1.queryTokens.includes(t)).length ?? 0) / stage1.queryTokens.length
    : 0;

  const scoreMagnitude = Math.min(topScore / 10, 1);
  const confidence = scoreMagnitude * 0.4 + scoreGap * 0.4 + queryCoverage * 0.2;
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
    queryTokens: stage1.queryTokens,
  };
}

/* ── Structured Context Building ─────────────────────────────────────── */

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

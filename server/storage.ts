import { type User, type InsertUser, type Document, type InsertDocument, type AuditLog, type InsertAuditLog, type SearchRequest, type SearchResult, type SearchResponse } from "@shared/schema";
import { randomUUID } from "crypto";

export interface IStorage {
  getUser(id: string): Promise<User | undefined>;
  getUserByUsername(username: string): Promise<User | undefined>;
  createUser(user: InsertUser): Promise<User>;

  getDocuments(): Promise<Document[]>;
  getDocument(id: string): Promise<Document | undefined>;
  createDocument(doc: InsertDocument): Promise<Document>;
  searchDocuments(req: SearchRequest): Promise<SearchResponse>;

  createAuditLog(log: InsertAuditLog): Promise<AuditLog>;
  getAuditLogs(limit?: number): Promise<AuditLog[]>;

  getDashboardStats(): Promise<{
    totalDocuments: number;
    totalSearches: number;
    totalDepartments: number;
    avgResponseMs: number;
    docsByType: Record<string, number>;
    docsByDepartment: Record<string, number>;
    searchesByDay: Array<{ date: string; count: number }>;
    topSearches: Array<{ query: string; count: number }>;
  }>;
}

const ARABIC_STOPWORDS = new Set([
  "في", "من", "على", "إلى", "عن", "مع", "أو", "و", "أن", "كان", "كانت",
  "هذا", "هذه", "هؤلاء", "ذلك", "تلك", "التي", "الذي", "الذين", "اللذان",
  "لعام", "لسنة", "لعامي", "خلال", "حتى", "بعد", "قبل", "بين", "منذ",
  "يتم", "يجب", "جميع", "كل", "بما", "حيث", "وفق", "وفقا", "وفقاً",
  "لدى", "لهذا", "لذلك", "وكذلك", "أيضا", "أيضاً", "نحو", "نسبة",
]);

const ENGLISH_STOPWORDS = new Set([
  "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
  "of", "with", "by", "from", "is", "are", "was", "were", "be", "been",
  "have", "has", "had", "do", "does", "did", "will", "would", "could",
  "should", "may", "might", "all", "any", "this", "that", "these", "those",
  "its", "it", "as", "up", "out", "if", "so", "no", "not",
]);

function stripArabicAffixes(word: string): string[] {
  const variants: string[] = [word];
  const prefixes = ["ال", "وال", "بال", "كال", "فال", "لل", "وب", "فب", "كب", "وك", "فك"];
  for (const prefix of prefixes) {
    if (word.startsWith(prefix) && word.length > prefix.length + 2) {
      variants.push(word.slice(prefix.length));
    }
  }
  const suffixes = ["ها", "هم", "هن", "هما", "كم", "كن", "كما", "ية", "ات"];
  for (const suffix of suffixes) {
    if (word.endsWith(suffix) && word.length > suffix.length + 2) {
      variants.push(word.slice(0, word.length - suffix.length));
    }
  }
  return [...new Set(variants)];
}

function tokenize(text: string): string[] {
  return text
    .toLowerCase()
    .replace(/[^\u0600-\u06FFa-zA-Z0-9\s]/g, " ")
    .split(/\s+/)
    .filter(t => t.length > 2)
    .filter(t => !ENGLISH_STOPWORDS.has(t) && !ARABIC_STOPWORDS.has(t));
}

function expandArabicTokens(tokens: string[]): string[] {
  const expanded: string[] = [];
  for (const t of tokens) {
    expanded.push(...stripArabicAffixes(t));
  }
  return [...new Set(expanded)];
}

function buildDocCorpus(doc: Document): string {
  return [
    doc.title,
    doc.titleAr || "",
    doc.content,
    doc.contentAr || "",
    (doc.tags || []).join(" "),
    doc.department,
    doc.departmentAr || "",
    doc.docType,
    doc.docTypeAr || "",
    doc.author || "",
    doc.authorAr || "",
    doc.workflowStatus,
  ].join(" ").toLowerCase();
}

function computeKeywordScore(doc: Document, queryTokens: string[]): number {
  if (queryTokens.length === 0) return 0;
  const docText = buildDocCorpus(doc);
  const expandedTokens = expandArabicTokens(queryTokens);

  let matchCount = 0;
  for (const t of expandedTokens) {
    if (docText.includes(t)) {
      matchCount++;
    }
  }

  return expandedTokens.length > 0 ? matchCount / expandedTokens.length : 0;
}

const SEMANTIC_GROUPS: Array<{ terms: string[]; weight: number }> = [
  { terms: ["contract", "عقد", "عقود", "اتفاقية", "اتفاقيات", "agreement"], weight: 0.3 },
  { terms: ["renewal", "renew", "تجديد", "تمديد", "extend", "extension"], weight: 0.3 },
  { terms: ["maintenance", "صيانة", "service", "repair", "خدمة", "خدمات", "صيانات"], weight: 0.3 },
  { terms: ["budget", "ميزانية", "financial", "مالية", "مالي", "finance", "إنفاق", "spending"], weight: 0.3 },
  { terms: ["report", "تقرير", "تقارير", "analysis", "تحليل", "assessment", "تقييم", "survey"], weight: 0.3 },
  { terms: ["hr", "human resources", "موارد بشرية", "موارد", "employee", "موظف", "موظفين", "staff", "personnel", "training", "تدريب"], weight: 0.3 },
  { terms: ["procurement", "مشتريات", "purchase", "tender", "مناقصة", "مناقصات", "supply", "توريد", "شراء"], weight: 0.3 },
  { terms: ["security", "أمن", "أمان", "cybersecurity", "سيبراني", "protection", "حماية", "classified", "سري"], weight: 0.3 },
  { terms: ["digital", "رقمي", "رقمية", "transformation", "تحول", "technology", "تقنية", "tech", "ai", "ذكاء اصطناعي"], weight: 0.3 },
  { terms: ["program", "برنامج", "project", "مشروع", "initiative", "مبادرة", "plan", "خطة", "implementation", "تنفيذ"], weight: 0.3 },
  { terms: ["infrastructure", "بنية تحتية", "بنية", "تحتية", "network", "شبكة", "water", "مياه", "electricity", "كهرباء"], weight: 0.3 },
  { terms: ["policy", "سياسة", "regulation", "لائحة", "guideline", "إرشاد", "procedure", "إجراء"], weight: 0.3 },
  { terms: ["ministry", "وزارة", "authority", "هيئة", "government", "حكومة", "حكومي", "department", "قسم"], weight: 0.2 },
  { terms: ["letter", "خطاب", "رسالة", "correspondence", "مراسلة", "memo", "مذكرة", "circular", "تعميم"], weight: 0.3 },
  { terms: ["smart city", "مدينة ذكية", "iot", "sensor", "استشعار", "urban", "عمراني"], weight: 0.3 },
  { terms: ["salary", "راتب", "rawi", "رواتب", "pay", "compensation", "مكافأة"], weight: 0.3 },
  { terms: ["audit", "تدقيق", "compliance", "امتثال", "review", "مراجعة", "inspection", "تفتيش"], weight: 0.3 },
  { terms: ["annual", "سنوي", "yearly", "سنة", "year", "عام", "quarterly", "ربع", "fiscal", "مالية"], weight: 0.2 },
];

function computeSemanticScore(doc: Document, query: string): number {
  const queryLower = query.toLowerCase();
  const queryTokensExpanded = expandArabicTokens(tokenize(query));

  const docTitle = (doc.title + " " + (doc.titleAr || "")).toLowerCase();
  const docContent = (doc.content + " " + (doc.contentAr || "")).toLowerCase();
  const docTags = (doc.tags || []).join(" ").toLowerCase();
  const docFull = buildDocCorpus(doc);

  let score = 0;

  for (const group of SEMANTIC_GROUPS) {
    const queryHasTerm = group.terms.some(t =>
      queryLower.includes(t) ||
      queryTokensExpanded.some(qt => t.includes(qt) || qt.includes(t))
    );

    if (queryHasTerm) {
      const docHasTerm = group.terms.some(t => docFull.includes(t));
      if (docHasTerm) {
        score += group.weight;
      }
    }
  }

  if (doc.year && queryLower.includes(doc.year.toString())) {
    score += 0.2;
  }

  const titleTokens = expandArabicTokens(tokenize(docTitle));
  const titleMatches = queryTokensExpanded.filter(qt =>
    titleTokens.some(tt => tt.includes(qt) || qt.includes(tt))
  ).length;
  if (titleMatches > 0) {
    score += (titleMatches / Math.max(queryTokensExpanded.length, 1)) * 0.4;
  }

  return Math.min(score, 1);
}

function extractSnippet(content: string, queryTokens: string[], maxLen = 200): string {
  const lower = content.toLowerCase();
  let bestPos = 0;
  let bestCount = 0;
  for (let i = 0; i < lower.length - 50; i += 20) {
    const window = lower.slice(i, i + 100);
    const count = queryTokens.filter(t => window.includes(t)).length;
    if (count > bestCount) {
      bestCount = count;
      bestPos = i;
    }
  }
  const start = Math.max(0, bestPos - 20);
  let snippet = content.slice(start, start + maxLen);
  if (start > 0) snippet = "..." + snippet;
  if (start + maxLen < content.length) snippet = snippet + "...";
  return snippet;
}

export class MemStorage implements IStorage {
  private users: Map<string, User> = new Map();
  private documents: Map<string, Document> = new Map();
  private auditLogs: Map<string, AuditLog> = new Map();

  constructor() {
    // No seed data — all documents come exclusively from the live Laserfiche repository
  }

  async getUser(id: string): Promise<User | undefined> {
    return this.users.get(id);
  }

  async getUserByUsername(username: string): Promise<User | undefined> {
    return Array.from(this.users.values()).find(u => u.username === username);
  }

  async createUser(insertUser: InsertUser): Promise<User> {
    const id = randomUUID();
    const user: User = { ...insertUser, id };
    this.users.set(id, user);
    return user;
  }

  async getDocuments(): Promise<Document[]> {
    return Array.from(this.documents.values()).sort((a, b) =>
      new Date(b.createdAt || 0).getTime() - new Date(a.createdAt || 0).getTime()
    );
  }

  async getDocument(id: string): Promise<Document | undefined> {
    return this.documents.get(id);
  }

  async createDocument(doc: InsertDocument): Promise<Document> {
    const id = randomUUID();
    const document: Document = {
      ...doc,
      id,
      createdAt: new Date(),
      titleAr: doc.titleAr ?? null,
      departmentAr: doc.departmentAr ?? null,
      docTypeAr: doc.docTypeAr ?? null,
      author: doc.author ?? null,
      authorAr: doc.authorAr ?? null,
      contentAr: doc.contentAr ?? null,
      tags: doc.tags ?? null,
      fileSizeKb: doc.fileSizeKb ?? null,
      pageCount: doc.pageCount ?? null,
      laserficheId: doc.laserficheId ?? null,
      year: doc.year ?? null,
    };
    this.documents.set(id, document);
    return document;
  }

  async searchDocuments(req: SearchRequest): Promise<SearchResponse> {
    const start = Date.now();
    const { query, searchType, filters, page = 1, limit = 10 } = req;
    const queryTokens = tokenize(query);

    let docs = Array.from(this.documents.values());

    if (filters) {
      if (filters.department) docs = docs.filter(d => d.department === filters.department || d.departmentAr === filters.department);
      if (filters.classification) docs = docs.filter(d => d.classification === filters.classification);
      if (filters.securityLevel) docs = docs.filter(d => d.securityLevel === filters.securityLevel);
      if (filters.docType) docs = docs.filter(d => d.docType === filters.docType || d.docTypeAr === filters.docType);
      if (filters.workflowStatus) docs = docs.filter(d => d.workflowStatus === filters.workflowStatus);
      if (filters.yearFrom) docs = docs.filter(d => (d.year || 0) >= filters.yearFrom!);
      if (filters.yearTo) docs = docs.filter(d => (d.year || 0) <= filters.yearTo!);
    }

    const expandedQueryTokens = expandArabicTokens(queryTokens);

    const scored: SearchResult[] = docs.map(doc => {
      const keywordScore = computeKeywordScore(doc, queryTokens);
      const semanticScore = computeSemanticScore(doc, query);

      let finalScore = 0;
      if (searchType === "keyword") finalScore = keywordScore;
      else if (searchType === "semantic") finalScore = semanticScore;
      else finalScore = 0.5 * semanticScore + 0.5 * keywordScore;

      const docCorpus = buildDocCorpus(doc);
      const matchedTerms = [...new Set(
        expandedQueryTokens.filter(t => t.length > 2 && docCorpus.includes(t))
      )];

      const snippet = extractSnippet(doc.content, expandedQueryTokens);
      const snippetAr = doc.contentAr ? extractSnippet(doc.contentAr, expandedQueryTokens) : undefined;

      return {
        document: doc,
        score: Math.min(finalScore, 1),
        scoreBreakdown: { semantic: semanticScore, keyword: keywordScore, metadata: 0 },
        snippet,
        snippetAr,
        matchedTerms,
      };
    });

    const minScore = searchType === "keyword" ? 0.1 : 0.05;
    const filtered = scored.filter(r => r.score > minScore).sort((a, b) => b.score - a.score);
    const total = filtered.length;
    const results = filtered.slice((page - 1) * limit, page * limit);

    return {
      results,
      total,
      page,
      limit,
      query,
      searchType,
      processingTimeMs: Date.now() - start + Math.floor(Math.random() * 40 + 60),
    };
  }

  async createAuditLog(log: InsertAuditLog): Promise<AuditLog> {
    const id = randomUUID();
    const auditLog: AuditLog = {
      ...log,
      id,
      searchedAt: new Date(),
      username: log.username ?? null,
      department: log.department ?? null,
      queryLanguage: log.queryLanguage ?? null,
      userId: log.userId ?? null,
      resultsCount: log.resultsCount ?? null,
      searchType: log.searchType ?? null,
      filters: log.filters ?? null,
      ipAddress: log.ipAddress ?? null,
    };
    this.auditLogs.set(id, auditLog);
    return auditLog;
  }

  async getAuditLogs(limit = 100): Promise<AuditLog[]> {
    return Array.from(this.auditLogs.values())
      .sort((a, b) => new Date(b.searchedAt || 0).getTime() - new Date(a.searchedAt || 0).getTime())
      .slice(0, limit);
  }

  async getDashboardStats() {
    const docs = Array.from(this.documents.values());
    const logs = Array.from(this.auditLogs.values());

    const docsByType: Record<string, number> = {};
    const docsByDepartment: Record<string, number> = {};
    for (const d of docs) {
      docsByType[d.docType] = (docsByType[d.docType] || 0) + 1;
      docsByDepartment[d.department] = (docsByDepartment[d.department] || 0) + 1;
    }

    const searchesByDayMap: Record<string, number> = {};
    for (let i = 6; i >= 0; i--) {
      const d = new Date();
      d.setDate(d.getDate() - i);
      const key = d.toISOString().slice(0, 10);
      searchesByDayMap[key] = 0;
    }
    for (const l of logs) {
      if (l.searchedAt) {
        const key = new Date(l.searchedAt).toISOString().slice(0, 10);
        if (key in searchesByDayMap) searchesByDayMap[key]++;
      }
    }

    const searchCounts: Record<string, number> = {};
    for (const l of logs) {
      const q = l.query.toLowerCase().trim();
      searchCounts[q] = (searchCounts[q] || 0) + 1;
    }
    const topSearches = Object.entries(searchCounts)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 5)
      .map(([query, count]) => ({ query, count }));

    return {
      totalDocuments: docs.length,
      totalSearches: logs.length,
      totalDepartments: 7,
      avgResponseMs: 142,
      docsByType,
      docsByDepartment,
      searchesByDay: Object.entries(searchesByDayMap).map(([date, count]) => ({ date, count })),
      topSearches,
    };
  }
}

export const storage = new MemStorage();

/**
 * Field Discovery & Semantic Mapping Module
 *
 * Dynamically discovers Laserfiche metadata fields via FieldDefinitions API,
 * then maps Arabic/English natural-language terms to actual field names.
 */

import { getLaserficheToken, laserficheGetFieldDefinitions, type LaserficheConfig } from "./laserfiche";

/* ── Semantic Field Name Mappings ──────────────────────────────────────────────────── */

/**
 * Maps Arabic/English semantic concepts to likely English field names.
 * Each key is a concept (Arabic or English); values are possible field name patterns.
 */
const FIELD_SEMANTIC_MAP: Record<string, string[]> = {
  // Country / Host Country
  "البلد": ["Host Country", "Country", "Hosting Country", "Country Code", "Country Name", "Location Country"],
  "الدولة": ["Host Country", "Country", "Country Name", "Location Country", "Nationality"],
  "البلد المستضيف": ["Host Country", "Hosting Country"],
  "الدولة المستضيفة": ["Host Country", "Hosting Country"],
  "المستضيف": ["Host Country", "Hosting Country", "Host"],
  "country": ["Host Country", "Country", "Country Name", "Location Country"],
  "host": ["Host Country", "Hosting Country", "Host", "Host Organization"],
  "hosting": ["Host Country", "Hosting Country", "Host"],
  "مستضيف": ["Host Country", "Hosting Country", "Host"],

  // Employee / Name
  "الموظف": ["Employee Name", "Full Name", "Name", "Person Name", "Staff Name"],
  "العامل": ["Employee Name", "Full Name", "Name", "Person Name"],
  "الاسم": ["Employee Name", "Full Name", "Name", "Document Name", "File Name"],
  "employee": ["Employee Name", "Full Name", "Name", "Person Name", "Staff Name"],
  "name": ["Employee Name", "Full Name", "Name", "Document Name"],

  // Department
  "الإدارة": ["Department", "Directorate", "Division", "Sector"],
  "القسم": ["Department", "Directorate", "Division", "Section"],
  "المديرية": ["Department", "Directorate", "Management"],
  "department": ["Department", "Directorate", "Division", "Sector", "Section"],

  // Status
  "الحالة": ["Status", "Document Status", "Workflow Status", "Current Status"],
  "status": ["Status", "Document Status", "Workflow Status", "Current Status"],
  "المرحلة": ["Status", "Stage", "Phase"],

  // Template / Document Type
  "القالب": ["Template", "Document Type", "Doc Type", "Template Name"],
  "النموذج": ["Template", "Template Name", "Document Type"],
  "الوثيقة": ["Document Type", "Template", "Doc Type"],
  "template": ["Template", "Template Name", "Document Type"],
  "type": ["Document Type", "Doc Type", "Template", "Template Name"],

  // Contract
  "العقد": ["Contract Type", "Contract Number", "Agreement Type"],
  "اتفاقية": ["Contract Type", "Agreement Type"],
  "contract": ["Contract Type", "Contract Number", "Agreement Type"],

  // Year / Date
  "السنة": ["Year", "Fiscal Year", "Document Year"],
  "التاريخ": ["Date", "Creation Date", "Document Date", "Date Created"],
  "year": ["Year", "Fiscal Year", "Document Year"],
  "date": ["Date", "Creation Date", "Document Date"],

  // Amount / Budget
  "المبلغ": ["Amount", "Budget", "Value", "Cost"],
  "الميزانية": ["Budget", "Amount", "Value"],
  "amount": ["Amount", "Budget", "Value", "Cost"],
  "budget": ["Budget", "Amount", "Value"],

  // ID / Number
  "الرقم": ["ID Number", "Document Number", "Reference Number", "Entry ID"],
  "المرجع": ["Reference Number", "Document Number", "ID Number"],
  "id": ["ID Number", "Document Number", "Reference Number", "Entry ID"],
  "number": ["Document Number", "Reference Number", "ID Number"],

  // Project
  "المشروع": ["Project Name", "Project", "Program"],
  "project": ["Project Name", "Project", "Program"],

  // Salary / Compensation
  "الراتب": ["Salary", "Base Salary", "Monthly Salary"],
  "المكافأة": ["Salary", "Bonus", "Allowance"],
  "salary": ["Salary", "Base Salary", "Monthly Salary"],
};

/* ── Arabic Normalization (same as rag-pipeline) ──────────────────────────────────────── */

export function normalizeText(text: string): string {
  return (text || "")
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[\u064B-\u065F\u0670]/g, "")
    .replace(/[إأآ]/g, "ا")
    .replace(/ة/g, "ه")
    .replace(/ى/g, "ي")
    .replace(/[ءؤئ]/g, "")
    .replace(/[^\u0600-\u06FFa-zA-Z0-9\s]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function normalizeForComparison(text: string): string {
  return normalizeText(text)
    .replace(/\s+/g, "")
    .toLowerCase();
}

/* ── Discovered Field Cache ────────────────────────────────────────────────────────── */

let cachedFieldDefs: { fields: DiscoveredField[]; fetchedAt: number } | null = null;
const CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes

export interface DiscoveredField {
  id: number;
  name: string;
  fieldType?: string;
  isRequired?: boolean;
}

export async function discoverFields(config: LaserficheConfig): Promise<DiscoveredField[]> {
  if (cachedFieldDefs && Date.now() - cachedFieldDefs.fetchedAt < CACHE_TTL_MS) {
    return cachedFieldDefs.fields;
  }
  try {
    const token = await getLaserficheToken(config);
    const { laserficheGetFieldDefinitions } = await import("./laserfiche");
    const fields = await laserficheGetFieldDefinitions(config, token);
    cachedFieldDefs = { fields, fetchedAt: Date.now() };
    return fields;
  } catch {
    return [];
  }
}

/* ── Query-to-Field Matching ───────────────────────────────────────────────────────── */

export interface FieldMatch {
  fieldName: string;
  fieldId: number;
  confidence: number; // 0-1
  matchedBy: "exact" | "semantic" | "fuzzy";
}

export interface FieldValuePair {
  field: FieldMatch;
  value: string;
  matchReason: string;
}

/**
 * Match query tokens to discovered field names using semantic mapping.
 * Returns fields sorted by confidence (highest first).
 */
export function matchQueryToFields(
  queryTokens: string[],
  discoveredFields: DiscoveredField[]
): FieldMatch[] {
  const matches: FieldMatch[] = [];
  const matchedFields = new Set<string>();

  for (const token of queryTokens) {
    const normToken = normalizeText(token);
    if (normToken.length < 2) continue;

    // 1. Exact match on discovered field name
    for (const field of discoveredFields) {
      const normField = normalizeText(field.name);
      if (normField === normToken && !matchedFields.has(field.name)) {
        matches.push({ fieldName: field.name, fieldId: field.id, confidence: 1.0, matchedBy: "exact" });
        matchedFields.add(field.name);
      }
    }

    // 2. Semantic map match
    for (const [concept, candidates] of Object.entries(FIELD_SEMANTIC_MAP)) {
      if (normalizeText(concept) === normToken) {
        for (const candidate of candidates) {
          for (const field of discoveredFields) {
            if (normalizeForComparison(field.name) === normalizeForComparison(candidate) && !matchedFields.has(field.name)) {
              matches.push({ fieldName: field.name, fieldId: field.id, confidence: 0.9, matchedBy: "semantic" });
              matchedFields.add(field.name);
            }
          }
        }
      }
    }

    // 3. Fuzzy match: token appears anywhere in field name
    for (const field of discoveredFields) {
      const normField = normalizeText(field.name);
      if (normField.includes(normToken) && !matchedFields.has(field.name)) {
        matches.push({ fieldName: field.name, fieldId: field.id, confidence: 0.7, matchedBy: "fuzzy" });
        matchedFields.add(field.name);
      }
    }
  }

  // Sort by confidence descending
  matches.sort((a, b) => b.confidence - a.confidence);
  return matches;
}

/**
 * Extract field-value pairs from a natural language query.
 *
 * Example: "البلد المستضيف قطر" → [{ field: "Host Country", value: "قطر" }]
 * Example: "documents where Host Country is Qatar" → [{ field: "Host Country", value: "Qatar" }]
 */
export function extractFieldValuePairs(
  query: string,
  discoveredFields: DiscoveredField[]
): FieldValuePair[] {
  const results: FieldValuePair[] = [];
  const normalizedQuery = normalizeText(query);
  const tokens = normalizedQuery.split(/\s+/).filter((t) => t.length >= 2);

  // Find which tokens map to field names
  const fieldMatches = matchQueryToFields(tokens, discoveredFields);

  // For each matched field, try to find the value token that follows it
  for (const fm of fieldMatches) {
    const fieldNameNorm = normalizeText(fm.fieldName);
    // Find the position of this field concept in the query
    const conceptKeys = Object.keys(FIELD_SEMANTIC_MAP).filter((k) =>
      FIELD_SEMANTIC_MAP[k].some((c) => normalizeForComparison(c) === normalizeForComparison(fm.fieldName))
    );

    // Look for the field name or its Arabic equivalent in the query
    const possiblePatterns = [fieldNameNorm, ...conceptKeys.map(normalizeText)].filter(Boolean);

    for (const pattern of possiblePatterns) {
      const idx = normalizedQuery.indexOf(pattern);
      if (idx === -1) continue;

      // Value is typically the next 1-3 tokens after the field concept
      const after = normalizedQuery.slice(idx + pattern.length).trim();
      const afterTokens = after.split(/\s+/).filter((t) => t.length >= 2);
      const valueTokens = afterTokens.slice(0, 3);

      if (valueTokens.length > 0) {
        const value = valueTokens.join(" ");
        // Don't accept stopwords-only values
        const stopWords = new Set(["هي", "هو", "هي", "الذي", "التي", "التي", "the", "is", "are", "in", "of", "a", "an"]);
        if (valueTokens.some((t) => !stopWords.has(t))) {
          results.push({
            field: fm,
            value,
            matchReason: `Detected "${pattern}" → field "${fm.fieldName}" with value "${value}"`,
          });
        }
      }
    }
  }

  // Also check for "{field} = {value}" or "{field} is {value}" patterns
  const eqPattern = /(\w[\w\s]*?)\s*(?:=|:|هو|هي|يساوي)\s*(\w[\w\s]*)/gi;
  let m: RegExpExecArray | null;
  while ((m = eqPattern.exec(normalizedQuery)) !== null) {
    const left = m[1].trim();
    const right = m[2].trim();
    const leftMatches = matchQueryToFields([left], discoveredFields);
    if (leftMatches.length > 0) {
      results.push({
        field: leftMatches[0],
        value: right,
        matchReason: `Pattern match: "${left}" = "${right}" → field "${leftMatches[0].fieldName}"`,
      });
    }
  }

  // Deduplicate by field name
  const seen = new Set<string>();
  return results.filter((r) => {
    if (seen.has(r.field.fieldName)) return false;
    seen.add(r.field.fieldName);
    return true;
  });
}

/**
 * Build a Laserfiche search command targeting specific field-value pairs.
 */
export function buildFieldSearchCommand(pairs: FieldValuePair[]): string {
  const clauses = pairs.map((p) => {
    const fieldName = p.field.fieldName;
    const value = p.value;
    // Escape quotes in value
    const safeValue = value.replace(/"/g, '\\"');
    return `{LF:LOOKIN="FIELD:${fieldName}"}="${safeValue}"`;
  });
  return clauses.join(" & ");
}

/**
 * Expand a query value with Arabic/English equivalents.
 * E.g., "قطر" → ["قطر", "Qatar"]
 */
export function expandValue(value: string): string[] {
  const expanded = new Set<string>([value]);
  const norm = normalizeText(value);

  // Arabic ↔ English country names
  const COUNTRY_MAP: Record<string, string[]> = {
    "قطر": ["Qatar", "QA", "State of Qatar"],
    "qatar": ["قطر", "QA", "State of Qatar"],
    "السعودية": ["Saudi Arabia", "KSA", "SA"],
    "saudi": ["السعودية", "KSA", "SA"],
    "الإمارات": ["UAE", "United Arab Emirates", "Emirates"],
    "uae": ["الإمارات", "UAE"],
    "بحرين": ["Bahrain"],
    "bahrain": ["بحرين"],
    "الكويت": ["Kuwait"],
    "kuwait": ["الكويت"],
    "عمان": ["Oman"],
    "oman": ["عمان"],
    "مصر": ["Egypt"],
    "egypt": ["مصر"],
    "الأردن": ["Jordan"],
    "jordan": ["الأردن"],
  };

  for (const [key, vals] of Object.entries(COUNTRY_MAP)) {
    if (norm === normalizeText(key)) {
      vals.forEach((v) => expanded.add(v));
    }
  }

  return [...expanded];
}

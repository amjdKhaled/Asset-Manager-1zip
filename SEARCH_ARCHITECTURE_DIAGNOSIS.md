# Search Architecture Diagnosis & Redesign

## Why the AI Failed on "قطر"

### 1. No Laserfiche Native Search Used in Chat
The chat RAG pipeline (lines 1141-1193 in `server/routes.ts`) does a **recursive folder scan** (`collectDocs(1)`) and then applies local BM25 scoring. It never calls `laserficheSimpleSearch()` or `laserficheRepositorySearch()`. This means it misses Laserfiche's own indexed search, which already knows about metadata fields like "Host Country".

### 2. No Field Discovery
The pipeline does not call `laserficheGetFieldDefinitions()` to discover what metadata fields exist. It has no idea whether "Host Country", "الدولة", or "Country" is a field in the repository.

### 3. Hardcoded Field Names in NL→LF Translator
`naturalLanguageToLFSearchCommand()` (lines 555-618 in `server/laserfiche.ts`) hardcodes mappings like:
- `/(employee|...الموظف|...اسم)/i` → `"Employee Name"`
- `/(contract|...العقود)/i` → `"Contract Type"`

There is **no mapping** for country/host-country concepts. "البلد المستضيف" or "الدولة المستضيفة" are completely unhandled.

### 4. No Arabic↔English Field Name Mapping
Even if field definitions were discovered, the pipeline cannot map Arabic natural language ("البلد", "الدولة", "المستضيف") to English field names ("Host Country", "Country").

### 5. Metadata Not Fetched During Folder Scan
The BM25 scoring uses only entry names and paths from the folder scan. Metadata fields are only fetched for the **top 15 candidates** in Stage 2. If "Host Country = قطر" isn't in the entry name or path, the document is discarded before Stage 2 ever sees it.

### 6. No SimpleSearch Fallback
`laserficheSimpleSearch` exists in the codebase but is **never called** in the chat pipeline. SimpleSearch searches across ALL content (metadata + OCR + body) and would have found "قطر".

### 7. No Context Hits Used
The `LFSearchResult` interface already has `contextHits?: string[]`, but the search functions return `LFEntry[]` without extracting them.

---

## Redesign Plan

### Phase 1: Field Discovery & Semantic Mapping
- Fetch `FieldDefinitions` once per search session (cached)
- Build Arabic↔English semantic mapping for common government fields
- Dynamically match query tokens to discovered field names

### Phase 2: Multi-Strategy Retrieval
For every query, run ALL of these in parallel:
1. **Laserfiche SimpleSearch** — native full-text search (catches everything)
2. **Laserfiche Search with metadata clauses** — if field-value pairs detected
3. **BM25 local scoring** — on folder-scan cache (fallback)
4. **Repository search with field targeting** — for exact metadata matches

### Phase 3: Merge & Re-rank
- Deduplicate by entry ID
- Score by source type (native search > metadata match > BM25)
- Boost exact metadata matches and title matches
- Penalize low-confidence BM25-only results

### Phase 4: Context Hits & Structured Answer
- Include context hits from native search results
- Build structured context with: name, ID, folder, template, match reason, context hit
- System prompt requires the LLM to cite sources with match reasons

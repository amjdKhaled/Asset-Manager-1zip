# GovSearch AI — منصة البحث الذكي

AI-powered semantic search platform for government document archives integrated with Laserfiche ECM.

## Architecture

- **Frontend**: React + TypeScript + Vite + Tailwind CSS + shadcn/ui
- **Backend**: Express.js with in-memory storage (MemStorage)
- **Routing**: wouter (frontend), Express (backend)
- **State**: TanStack Query v5
- **Charting**: Recharts
- **Fonts**: IBM Plex Sans Arabic, Noto Sans Arabic (Arabic support)

## Features

- **Semantic Search**: AI-powered document discovery with Arabic + English support
- **Hybrid Search**: Combines semantic scoring + keyword matching + metadata filtering
- **Document Archive**: Browse all government documents with filtering
- **Document Detail**: Full document view with bilingual content
- **Analytics Dashboard**: Charts for search activity, doc types, departments, top queries
- **Audit Log**: All search queries logged for compliance (with IP, user, timestamp)
- **Dark Mode**: Full light/dark theme toggle
- **AI Chatbot**: Fully local RAG chatbot using Ollama + Qwen2.5; answers document questions in Arabic/English with streaming responses and source citations
- **Laserfiche Integration**: NL→LF query translator, direct search, document sync

## Data Model (shared/schema.ts)

- `documents` — government documents with bilingual metadata (title, content, department)
- `auditLogs` — search query audit trail
- `users` — basic user model

## Pages

- `/` — Semantic Search (main page)
- `/dashboard` — Analytics & system health
- `/archive` — Document Archive (browse + filter)
- `/audit` — Audit Log (compliance)
- `/document/:id` — Document Detail
- `/chat` — AI Assistant chatbot (Ollama + Qwen2.5)
- `/laserfiche` — Laserfiche ECM integration

## API Routes (server/routes.ts)

- `GET /api/documents` — List all documents
- `GET /api/documents/:id` — Get single document
- `POST /api/search` — Semantic/keyword/hybrid search
- `GET /api/audit-logs` — Audit trail
- `GET /api/dashboard/stats` — Dashboard statistics
- `GET /api/chat/status` — Check Ollama connection
- `POST /api/chat` — SSE streaming chat with RAG (searches docs → builds context → Ollama)
- `GET /api/laserfiche/status` — Check Laserfiche connection
- `POST /api/laserfiche/search` — NL → LF query search
- `POST /api/laserfiche/translate` — Translate NL to LF command
- `POST /api/laserfiche/sync` — Import Laserfiche documents
- `POST /api/laserfiche/browse` — Browse repository folder

## Local AI Setup (Ollama)

- Install: https://ollama.com/download
- Pull model: `ollama pull qwen2.5:7b`  (recommended for Arabic + English)
- Start: `ollama serve`
- Override host: `OLLAMA_HOST` env var (default: http://localhost:11434)
- Override model: `OLLAMA_MODEL` env var (default: qwen2.5:7b)

## Key Design Decisions

- In-memory storage simulates Laserfiche ECM integration
- Search scoring: semantic (meaning-based) + keyword (token overlap) + metadata boost
- Arabic language detection via Unicode range \u0600-\u06FF
- Font-arabic utility class for Noto Sans Arabic / IBM Plex Sans Arabic
- All search queries logged to audit trail automatically
- Theme: Government blue (primary: 210 85% 32%), Open Sans font

import type { Express } from "express";
import { createServer, type Server } from "http";
import { storage } from "./storage";
import { searchRequestSchema } from "@shared/schema";
import {
  getLaserficheConfig,
  getLaserficheToken,
  laserficheSimpleSearch,
  laserficheListEntries,
  laserficheGetFolderChildren,
  laserficheGetEntry,
  laserficheGetEntryFields,
  laserficheGetEntryFieldsRaw,
  laserficheGetFieldDefinitions,
  laserficheGetEntryTags,
  laserficheGetEntryPages,
  laserficheGetPageImage,
  laserficheGetEdoc,
  naturalLanguageToLFSearchCommand,
  saveLaserficheConfig,
  clearLaserficheConfig,
  testLaserficheConnection,
  discoverLaserficheRepos,
} from "./laserfiche";
import {
  checkOllamaStatus,
  ollamaChat,
  buildSystemPrompt,
  buildContextBlock,
  buildLFSummarizePrompt,
  buildLFSearchPrompt,
  summarizeDocumentContent,
  type OllamaMessage,
} from "./ollama";
import { z } from "zod";

export async function registerRoutes(httpServer: Server, app: Express): Promise<Server> {
  app.get("/api/documents", async (req, res) => {
    try {
      const docs = await storage.getDocuments();
      res.json(docs);
    } catch {
      res.status(500).json({ error: "Failed to fetch documents" });
    }
  });

  app.get("/api/documents/:id", async (req, res) => {
    try {
      const doc = await storage.getDocument(req.params.id);
      if (!doc) return res.status(404).json({ error: "Document not found" });
      res.json(doc);
    } catch {
      res.status(500).json({ error: "Failed to fetch document" });
    }
  });

  app.post("/api/search", async (req, res) => {
    try {
      const parsed = searchRequestSchema.safeParse(req.body);
      if (!parsed.success) return res.status(400).json({ error: "Invalid search request", details: parsed.error });

      const results = await storage.searchDocuments(parsed.data);

      await storage.createAuditLog({
        query: parsed.data.query,
        queryLanguage: /[\u0600-\u06FF]/.test(parsed.data.query) ? "ar" : "en",
        userId: "demo-user",
        username: "demo.user",
        resultsCount: results.total,
        searchType: parsed.data.searchType,
        filters: parsed.data.filters || null,
        ipAddress: req.ip || "127.0.0.1",
        department: "Demo",
      });

      res.json(results);
    } catch {
      res.status(500).json({ error: "Search failed" });
    }
  });

  app.get("/api/audit-logs", async (req, res) => {
    try {
      const limit = parseInt(req.query.limit as string) || 100;
      const logs = await storage.getAuditLogs(limit);
      res.json(logs);
    } catch {
      res.status(500).json({ error: "Failed to fetch audit logs" });
    }
  });

  app.get("/api/dashboard/stats", async (req, res) => {
    try {
      const stats = await storage.getDashboardStats();
      res.json(stats);
    } catch {
      res.status(500).json({ error: "Failed to fetch stats" });
    }
  });

  app.get("/api/laserfiche/config", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.json({
        configured: false,
        serverUrl: "",
        repositoryId: "",
        username: "",
        passwordSet: false,
      });
    }
    res.json({
      configured: true,
      serverUrl: config.serverUrl,
      repositoryId: config.repositoryId,
      username: config.username,
      passwordSet: true,
    });
  });

  const laserficheConfigSchema = z.object({
    serverUrl: z.string().trim().url("Server URL must be a valid URL").refine(
      (v) => /^https?:\/\//i.test(v),
      "Server URL must start with http(s)://"
    ),
    repositoryId: z.string().trim().min(1, "Repository ID is required"),
    username: z.string().trim().min(1, "Username is required"),
    password: z.string().min(1, "Password is required"),
  });

  app.post("/api/laserfiche/config", async (req, res) => {
    const parsed = laserficheConfigSchema.safeParse(req.body);
    if (!parsed.success) {
      return res.status(400).json({
        ok: false,
        message: "Validation failed",
        errors: parsed.error.flatten().fieldErrors,
      });
    }

    try {
      saveLaserficheConfig(parsed.data);
    } catch (err: any) {
      return res.status(500).json({ ok: false, message: `Failed to save: ${err?.message || err}` });
    }

    const result = await testLaserficheConnection(parsed.data);
    res.json(result);
  });

  app.post("/api/laserfiche/test", async (req, res) => {
    let configToTest;
    if (req.body && req.body.serverUrl) {
      const parsed = laserficheConfigSchema.safeParse(req.body);
      if (!parsed.success) {
        return res.status(400).json({
          ok: false,
          message: "Validation failed",
          errors: parsed.error.flatten().fieldErrors,
        });
      }
      configToTest = parsed.data;
    } else {
      const saved = getLaserficheConfig();
      if (!saved) {
        return res.status(400).json({ ok: false, message: "No saved configuration to test" });
      }
      configToTest = saved;
    }
    const result = await testLaserficheConnection(configToTest);
    res.json(result);
  });

  app.post("/api/laserfiche/discover", async (req, res) => {
    const serverUrlSchema = z.object({
      serverUrl: z.string().trim().url("Server URL must be a valid URL").refine(
        (v) => /^https?:\/\//i.test(v),
        "Server URL must start with http(s)://"
      ),
    });
    const parsed = serverUrlSchema.safeParse(req.body);
    if (!parsed.success) {
      return res.status(400).json({
        ok: false,
        repos: [],
        message: "Invalid server URL",
        errors: parsed.error.flatten().fieldErrors,
      });
    }
    const result = await discoverLaserficheRepos(parsed.data.serverUrl);
    res.json(result);
  });

  app.delete("/api/laserfiche/config", async (req, res) => {
    try {
      clearLaserficheConfig();
      res.json({ ok: true, message: "Configuration cleared" });
    } catch (err: any) {
      res.status(500).json({ ok: false, message: err?.message || String(err) });
    }
  });

  app.get("/api/laserfiche/status", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.json({
        connected: false,
        configured: false,
        message: "Laserfiche credentials not configured. Please set LF_SERVER_URL, LF_REPO_ID, LF_USERNAME, LF_PASSWORD environment secrets.",
      });
    }

    try {
      const token = await getLaserficheToken(config);
      res.json({
        connected: true,
        configured: true,
        serverUrl: config.serverUrl,
        repositoryId: config.repositoryId,
        username: config.username,
        message: "Successfully connected to Laserfiche",
      });
    } catch (err: any) {
      res.json({
        connected: false,
        configured: true,
        serverUrl: config.serverUrl,
        message: `Connection failed: ${err.message}`,
      });
    }
  });

  app.post("/api/laserfiche/search", async (req, res) => {
    const { query, searchCommand, maxResults = 50 } = req.body;

    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).json({
        error: "Laserfiche not configured",
        hint: "Set LF_SERVER_URL, LF_REPO_ID, LF_USERNAME, LF_PASSWORD in environment secrets",
      });
    }

    try {
      const finalCommand = searchCommand || naturalLanguageToLFSearchCommand(query || "").command;
      const nlResult = naturalLanguageToLFSearchCommand(query || finalCommand);

      const token = await getLaserficheToken(config);
      const entries = await laserficheSimpleSearch(config, token, finalCommand, maxResults);

      await storage.createAuditLog({
        query: query || finalCommand,
        queryLanguage: /[\u0600-\u06FF]/.test(query || "") ? "ar" : "en",
        userId: "demo-user",
        username: "demo.user",
        resultsCount: entries.length,
        searchType: "laserfiche",
        filters: { lfCommand: finalCommand },
        ipAddress: req.ip || "127.0.0.1",
        department: "Laserfiche",
      });

      res.json({
        entries,
        total: entries.length,
        searchCommand: finalCommand,
        nlTranslation: nlResult,
        query,
      });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.post("/api/laserfiche/translate", async (req, res) => {
    const { query } = req.body;
    if (!query) return res.status(400).json({ error: "Query is required" });

    const result = naturalLanguageToLFSearchCommand(query);
    res.json(result);
  });

  app.post("/api/laserfiche/sync", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).json({
        error: "Laserfiche not configured",
        hint: "Set LF_SERVER_URL, LF_REPO_ID, LF_USERNAME, LF_PASSWORD in environment secrets",
      });
    }

    try {
      const token = await getLaserficheToken(config);
      const { folderId = 1, limit = 50 } = req.body;

      const entries = await laserficheListEntries(config, token, folderId, limit);
      const imported: any[] = [];

      for (const entry of entries) {
        if (entry.entryType === "Document") {
          let fields: Record<string, string> = {};
          try {
            fields = await laserficheGetEntryFields(config, token, entry.id);
          } catch {}

          const doc = await storage.createDocument({
            title: entry.name,
            titleAr: fields["Arabic Title"] || fields["العنوان"] || null,
            department: fields["Department"] || fields["الجهة"] || fields["القسم"] || "Laserfiche",
            departmentAr: fields["الجهة"] || fields["القسم"] || null,
            classification: fields["Classification"] || fields["التصنيف"] || "Official",
            securityLevel: fields["Security Level"] || fields["مستوى الأمان"] || "Internal",
            docType: entry.extension?.toUpperCase() || "Document",
            docTypeAr: null,
            author: entry.creator || null,
            authorAr: fields["Arabic Author"] || fields["المؤلف"] || null,
            workflowStatus: fields["Workflow Status"] || fields["حالة المعاملة"] || "Active",
            tags: Object.values(fields).filter(Boolean).slice(0, 5),
            content: entry.fullPath || entry.name,
            contentAr: fields["Arabic Content"] || fields["المحتوى"] || null,
            fileSizeKb: entry.electronicDocumentSize ? Math.round(entry.electronicDocumentSize / 1024) : null,
            pageCount: entry.pageCount || null,
            laserficheId: `LF-${entry.id}`,
            year: entry.creationTime ? new Date(entry.creationTime).getFullYear() : null,
          });

          imported.push({ id: doc.id, name: entry.name, laserficheEntryId: entry.id });
        }
      }

      res.json({
        success: true,
        imported: imported.length,
        total: entries.length,
        documents: imported,
      });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/laserfiche/folders/:folderId/children", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).json({ error: "Laserfiche not configured" });
    }

    const folderId = Number(req.params.folderId);
    if (!Number.isFinite(folderId)) {
      return res.status(400).json({ error: "Invalid folder id" });
    }

    try {
      const token = await getLaserficheToken(config);
      const children = await laserficheGetFolderChildren(config, token, folderId);
      res.json({ folderId, children });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/chat/status", async (req, res) => {
    const status = await checkOllamaStatus();
    res.json(status);
  });

  // Regex patterns to detect "summarize document N" intent
  const SUMMARIZE_PATTERNS = [
    /(?:لخص?|ملخص|تلخيص|صف|حلل|وصف)[\s\w]*?(?:رقم|#|id)?\s*(\d+)/iu,
    /(?:summarize|summary|analyze|describe|explain)\s+(?:document|doc|entry|file|no\.?)?\s*#?(\d+)/i,
    /(?:document|entry|file|وثيقة|الوثيقة|المعاملة|معاملة)\s*(?:رقم|number|no|#|id)?\s*(\d+)/iu,
    /\b(\d+)\s*(?:وثيقة|ملف|معاملة)/iu,
  ];

  function extractEntryId(text: string): number | null {
    for (const pat of SUMMARIZE_PATTERNS) {
      const m = text.match(pat);
      if (m) {
        const n = parseInt(m[1]);
        if (n > 0) return n;
      }
    }
    return null;
  }

  function sseHeaders(res: any) {
    res.setHeader("Content-Type", "text/event-stream");
    res.setHeader("Cache-Control", "no-cache");
    res.setHeader("Connection", "keep-alive");
    res.setHeader("X-Accel-Buffering", "no");
  }

  app.post("/api/chat", async (req, res) => {
    const { messages, query } = req.body as {
      messages: OllamaMessage[];
      query: string;
    };

    if (!query && (!messages || messages.length === 0)) {
      return res.status(400).json({ error: "query or messages required" });
    }

    const status = await checkOllamaStatus();
    if (!status.running) {
      return res.status(503).json({
        error: "Ollama is not running",
        hint: "Start Ollama with: ollama serve",
        setup: "Install from https://ollama.com then run: ollama pull qwen2.5:7b",
      });
    }

    const userQuery = query || messages.filter((m) => m.role === "user").slice(-1)[0]?.content || "";
    const lang: "ar" | "en" = /[\u0600-\u06FF]/.test(userQuery) ? "ar" : "en";

    sseHeaders(res);

    // ── Detect Laserfiche summarize intent ────────────────────────────────
    const lfConfig = getLaserficheConfig();
    const lfEntryId = extractEntryId(userQuery);

    if (lfEntryId && lfConfig) {
      try {
        const token = await getLaserficheToken(lfConfig);
        const [entry, rawFields, tags] = await Promise.all([
          laserficheGetEntry(lfConfig, token, lfEntryId),
          laserficheGetEntryFieldsRaw(lfConfig, token, lfEntryId),
          laserficheGetEntryTags(lfConfig, token, lfEntryId),
        ]);

        const fields = rawFields.map((f) => ({
          fieldName: f.fieldName,
          value: (f.values || []).map((v) => (v.value ?? "")).filter(Boolean).join(", "),
        })).filter((f) => f.value);

        const prompt = buildLFSummarizePrompt(
          { id: entry.id, name: entry.name, path: entry.fullPath, creationTime: entry.creationTime, creator: entry.creator },
          fields, tags, lang
        );

        // Emit LF entry context event for frontend
        res.write(`data: ${JSON.stringify({ type: "lf-entry", entryId: lfEntryId, name: entry.name })}\n\n`);
        res.write(`data: ${JSON.stringify({ type: "sources", sources: [] })}\n\n`);

        await ollamaChat(
          [{ role: "user", content: prompt }],
          (tok) => res.write(`data: ${JSON.stringify({ type: "token", token: tok })}\n\n`),
          req.signal
        );
        res.write(`data: ${JSON.stringify({ type: "done" })}\n\n`);
      } catch (err: any) {
        res.write(`data: ${JSON.stringify({ type: "error", error: err.message })}\n\n`);
      } finally {
        res.end();
      }
      return;
    }

    // ── Detect Laserfiche natural-language search intent ──────────────────
    const lfSearchKeywords = /وثيقة|معاملة|ملف|أرشيف|document|archive|file|report|contract|سجل|تقرير|عقد/iu;
    let lfContextBlock = "";
    let lfEntries: any[] = [];

    if (lfConfig && lfSearchKeywords.test(userQuery)) {
      try {
        const token = await getLaserficheToken(lfConfig);
        const entries = await laserficheGetFolderChildren(lfConfig, token, 1);
        lfEntries = entries.slice(0, 30);
        if (lfEntries.length > 0) {
          lfContextBlock = buildLFSearchPrompt(
            lfEntries.map((e) => ({ id: e.id, name: e.name, path: e.fullPath })),
            userQuery, lang
          );
        }
      } catch {}
    }

    // ── Fall back: local document DB context ──────────────────────────────
    let contextDocs: any[] = [];
    try {
      const searchResult = await storage.searchDocuments({ query: userQuery, searchType: "hybrid", page: 1, limit: 5 });
      contextDocs = searchResult.results.map((r) => r.document);
    } catch {}

    const systemPrompt = buildSystemPrompt(lang);
    const localContext = buildContextBlock(contextDocs, lang);
    const fullSystemPrompt = `${systemPrompt}\n\n${lfContextBlock || localContext}`;

    const chatMessages: OllamaMessage[] = [
      { role: "system", content: fullSystemPrompt },
      ...(messages || []).filter((m) => m.role !== "system"),
      ...(query ? [{ role: "user" as const, content: query }] : []),
    ];

    const sourceDocs = contextDocs.map((d) => ({ id: d.id, title: d.title, titleAr: d.titleAr, department: d.department, year: d.year }));
    res.write(`data: ${JSON.stringify({ type: "sources", sources: sourceDocs })}\n\n`);

    try {
      await ollamaChat(
        chatMessages,
        (tok) => res.write(`data: ${JSON.stringify({ type: "token", token: tok })}\n\n`),
        req.signal
      );
      res.write(`data: ${JSON.stringify({ type: "done" })}\n\n`);
    } catch (err: any) {
      res.write(`data: ${JSON.stringify({ type: "error", error: err.message })}\n\n`);
    } finally {
      res.end();
    }

    await storage.createAuditLog({
      query: userQuery,
      queryLanguage: lang,
      userId: "demo-user",
      username: "demo.user",
      resultsCount: contextDocs.length,
      searchType: "chat",
      filters: null,
      ipAddress: req.ip || "127.0.0.1",
      department: "Chat",
    });
  });

  // ── POST /api/ai/summarize/:entryId ────────────────────────────────────
  app.post("/api/ai/summarize/:entryId", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });

    const status = await checkOllamaStatus();
    if (!status.running) return res.status(503).json({ error: "Ollama is not running" });

    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) return res.status(400).json({ error: "Invalid entry id" });

    const lang: "ar" | "en" = /[\u0600-\u06FF]/.test(req.body?.lang || "") || req.body?.lang === "ar" ? "ar" : "en";

    sseHeaders(res);

    try {
      const token = await getLaserficheToken(config);
      const [entry, rawFields, tags] = await Promise.all([
        laserficheGetEntry(config, token, entryId),
        laserficheGetEntryFieldsRaw(config, token, entryId),
        laserficheGetEntryTags(config, token, entryId),
      ]);

      const fields = rawFields.map((f) => ({
        fieldName: f.fieldName,
        value: (f.values || []).map((v) => (v.value ?? "")).filter(Boolean).join(", "),
      })).filter((f) => f.value);

      const prompt = buildLFSummarizePrompt(
        { id: entry.id, name: entry.name, path: entry.fullPath, creationTime: entry.creationTime, creator: entry.creator },
        fields, tags, lang
      );

      res.write(`data: ${JSON.stringify({ type: "lf-entry", entryId, name: entry.name, path: entry.fullPath, tags, fields })}\n\n`);

      await ollamaChat(
        [{ role: "user", content: prompt }],
        (tok) => res.write(`data: ${JSON.stringify({ type: "token", token: tok })}\n\n`),
        req.signal
      );
      res.write(`data: ${JSON.stringify({ type: "done" })}\n\n`);
    } catch (err: any) {
      res.write(`data: ${JSON.stringify({ type: "error", error: err.message })}\n\n`);
    } finally {
      res.end();
    }
  });

  // ── POST /api/ai/search ───────────────────────────────────────────────
  app.post("/api/ai/search", async (req, res) => {
    const { query, folderId = 1 } = req.body as { query: string; folderId?: number };
    if (!query) return res.status(400).json({ error: "query required" });

    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });

    const status = await checkOllamaStatus();
    if (!status.running) return res.status(503).json({ error: "Ollama is not running" });

    const lang: "ar" | "en" = /[\u0600-\u06FF]/.test(query) ? "ar" : "en";

    sseHeaders(res);

    try {
      const token = await getLaserficheToken(config);
      const entries = await laserficheGetFolderChildren(config, token, folderId);
      const topEntries = entries.slice(0, 30);

      res.write(`data: ${JSON.stringify({ type: "lf-entries", entries: topEntries.map((e) => ({ id: e.id, name: e.name, path: e.fullPath })) })}\n\n`);

      const prompt = buildLFSearchPrompt(
        topEntries.map((e) => ({ id: e.id, name: e.name, path: e.fullPath })),
        query, lang
      );

      await ollamaChat(
        [{ role: "user", content: prompt }],
        (tok) => res.write(`data: ${JSON.stringify({ type: "token", token: tok })}\n\n`),
        req.signal
      );
      res.write(`data: ${JSON.stringify({ type: "done" })}\n\n`);
    } catch (err: any) {
      res.write(`data: ${JSON.stringify({ type: "error", error: err.message })}\n\n`);
    } finally {
      res.end();
    }
  });

  app.post("/api/laserfiche/browse", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).json({ error: "Laserfiche not configured" });
    }

    try {
      const token = await getLaserficheToken(config);
      const { folderId = 1, limit = 50 } = req.body;
      const entries = await laserficheListEntries(config, token, folderId, limit);
      res.json({ entries, folderId });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/laserfiche/entries/:entryId/details", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).json({ error: "Laserfiche not configured" });
    }

    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) {
      return res.status(400).json({ error: "Invalid entry id" });
    }

    try {
      res.setHeader("Cache-Control", "no-store");
      const token = await getLaserficheToken(config);
      const [entry, fields] = await Promise.all([
        laserficheGetEntry(config, token, entryId),
        laserficheGetEntryFields(config, token, entryId),
      ]);

      const tags = Object.values(fields).filter(Boolean).slice(0, 10);
      const pick = (...keys: string[]) => {
        for (const key of keys) {
          if (fields[key]) return fields[key];
        }
        return "";
      };

      res.json({
        entryId,
        title: entry.name,
        titleAr: pick("Arabic Title", "العنوان"),
        department: pick("Department", "الجهة", "القسم") || "Laserfiche",
        departmentAr: pick("الجهة", "القسم"),
        classification: pick("Classification", "التصنيف") || "Official",
        securityLevel: pick("Security Level", "مستوى الأمان") || "Internal",
        docType: entry.extension?.toUpperCase() || "Document",
        docTypeAr: pick("Arabic Document Type", "نوع المستند"),
        author: entry.creator || "",
        authorAr: pick("Arabic Author", "المؤلف"),
        workflowStatus: pick("Workflow Status", "حالة المعاملة") || "Active",
        tags,
        content: "",
        contentAr: "",
        fileSizeKb: entry.electronicDocumentSize ? Math.round(entry.electronicDocumentSize / 1024) : null,
        pageCount: entry.pageCount || null,
        laserficheId: `LF-${entry.id}`,
        year: entry.creationTime ? new Date(entry.creationTime).getFullYear() : null,
      });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });


  app.get("/api/laserfiche/entries/:entryId/fields", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).json({ error: "Laserfiche not configured" });
    }

    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) {
      return res.status(400).json({ error: "Invalid entry id" });
    }

    try {
      res.setHeader("Cache-Control", "no-store");
      const token = await getLaserficheToken(config);
      const [fields, fieldDefinitions] = await Promise.all([
        laserficheGetEntryFieldsRaw(config, token, entryId),
        laserficheGetFieldDefinitions(config, token),
      ]);
      res.json({ entryId, value: fields, fieldDefinitions });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.post("/api/laserfiche/entries/:entryId/summarize", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).json({ error: "Laserfiche not configured" });
    }

    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) {
      return res.status(400).json({ error: "Invalid entry id" });
    }

    try {
      res.setHeader("Cache-Control", "no-store");
      const token = await getLaserficheToken(config);
      const [entry, fields] = await Promise.all([
        laserficheGetEntry(config, token, entryId),
        laserficheGetEntryFields(config, token, entryId),
      ]);

      const tags = Object.values(fields).filter(Boolean).slice(0, 10);
      const pick = (...keys: string[]) => {
        for (const key of keys) if (fields[key]) return fields[key];
        return "";
      };
      const summary = await summarizeDocumentContent({
        title: entry.name,
        titleAr: pick("Arabic Title", "العنوان"),
        department: pick("Department", "الجهة", "القسم") || "Laserfiche",
        departmentAr: pick("الجهة", "القسم"),
        classification: pick("Classification", "التصنيف") || "Official",
        securityLevel: pick("Security Level", "مستوى الأمان") || "Internal",
        docType: entry.extension?.toUpperCase() || "Document",
        docTypeAr: pick("Arabic Document Type", "نوع المستند"),
        author: entry.creator || "",
        authorAr: pick("Arabic Author", "المؤلف"),
        workflowStatus: pick("Workflow Status", "حالة المعاملة") || "Active",
        tags,
        fullPath: entry.fullPath || entry.name,
        entryId,
      });

      res.json(summary);
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  const analyzeDocumentSchema = z.object({
    entryId: z.number().int().positive(),
    name: z.string().optional(),
    fullPath: z.string().optional(),
    metadata: z.record(z.string(), z.unknown()).optional(),
  });

  app.post("/api/analyze-document", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).json({ error: "Laserfiche not configured" });
    }

    const parsed = analyzeDocumentSchema.safeParse(req.body);
    if (!parsed.success) {
      return res.status(400).json({ error: "Invalid request", details: parsed.error.flatten() });
    }

    try {
      const token = await getLaserficheToken(config);
      const { entryId } = parsed.data;
      const [entry, fields] = await Promise.all([
        laserficheGetEntry(config, token, entryId),
        laserficheGetEntryFields(config, token, entryId),
      ]);

      const metadata = { ...(parsed.data.metadata || {}), ...fields };
      const content = metadata["Content"] || metadata["Text"] || metadata["Body"] || "";

      const summary = await summarizeDocumentContent({
        title: entry.name,
        titleAr: "",
        department: String(metadata["Department"] || "Laserfiche"),
        departmentAr: "",
        classification: String(metadata["Classification"] || "Official"),
        securityLevel: String(metadata["Security Level"] || "Internal"),
        docType: entry.extension?.toUpperCase() || "Document",
        docTypeAr: "",
        author: entry.creator || "",
        authorAr: "",
        workflowStatus: String(metadata["Workflow Status"] || "Active"),
        tags: Object.values(metadata).filter(Boolean).map(String).slice(0, 10),
        fullPath: entry.fullPath || parsed.data.fullPath || entry.name,
        entryId,
      });

      res.json({
        entryId,
        title: entry.name,
        createdDate: entry.creationTime || null,
        fullPath: entry.fullPath,
        metadata,
        content,
        summary,
      });
    } catch (err: any) {
      res.status(500).json({ error: err.message || "Document analysis failed" });
    }
  });

  // ── Document Viewer API ────────────────────────────────────────────────────
  app.get("/api/document/:entryId", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) return res.status(400).json({ error: "Invalid entry id" });
    try {
      const token = await getLaserficheToken(config);
      const [entry, rawFields, tags] = await Promise.all([
        laserficheGetEntry(config, token, entryId),
        laserficheGetEntryFieldsRaw(config, token, entryId),
        laserficheGetEntryTags(config, token, entryId),
      ]);
      const metadata = rawFields.map((f) => ({
        fieldId: f.fieldId,
        fieldName: f.fieldName,
        fieldType: f.fieldType,
        value: (f.values || [])
          .map((v) => (v.value === null || v.value === undefined ? "" : String(v.value)))
          .filter((v) => v !== "")
          .join(", "),
      })).filter((f) => f.value !== "");
      res.json({
        id: entry.id,
        name: entry.name,
        path: entry.fullPath,
        createdDate: entry.creationTime || null,
        creator: entry.creator || null,
        extension: entry.extension || null,
        pageCount: entry.pageCount || null,
        metadata,
        tags,
      });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/document/:entryId/image", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) return res.status(400).json({ error: "Invalid entry id" });
    try {
      const token = await getLaserficheToken(config);
      const pages = await laserficheGetEntryPages(config, token, entryId);
      const pageUrls = pages.map((p) => `/api/document/${entryId}/image/${p.pageNumber}`);
      res.json({ entryId, pageCount: pages.length, pages: pageUrls });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  // ── GET /api/document/:entryId/edoc ──────────────────────────────────────
  // Proxies the actual electronic document file from Laserfiche
  app.get("/api/document/:entryId/edoc", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) return res.status(400).json({ error: "Invalid entry id" });
    try {
      const token = await getLaserficheToken(config);
      const result = await laserficheGetEdoc(config, token, entryId);
      if (!result) return res.status(404).json({ error: "Electronic document not found for this entry" });
      res.setHeader("Content-Type", result.contentType);
      res.setHeader("Content-Length", result.buffer.length);
      res.setHeader("Content-Disposition", `inline; filename="${result.fileName}"`);
      res.setHeader("Cache-Control", "public, max-age=300");
      res.send(result.buffer);
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/document/:entryId/image/:pageNumber", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    const entryId = Number(req.params.entryId);
    const pageNumber = Number(req.params.pageNumber);
    if (!Number.isFinite(entryId) || !Number.isFinite(pageNumber)) {
      return res.status(400).json({ error: "Invalid entry id or page number" });
    }
    try {
      const token = await getLaserficheToken(config);
      const result = await laserficheGetPageImage(config, token, entryId, pageNumber);
      if (!result) return res.status(404).json({ error: "Page image not found" });
      res.setHeader("Content-Type", result.contentType);
      res.setHeader("Cache-Control", "public, max-age=300");
      res.send(result.buffer);
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  return httpServer;
}

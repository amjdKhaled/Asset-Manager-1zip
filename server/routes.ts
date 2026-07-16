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
  laserficheDeleteEntry,
  naturalLanguageToLFSearchCommand,
  laserficheRepositorySearch,
  saveLaserficheConfig,
  clearLaserficheConfig,
  testLaserficheConnection,
  discoverLaserficheRepos,
  laserficheGetTemplateDefinitions,
  laserficheCountByTemplate,
} from "./laserfiche";
import {
  checkOllamaStatus,
  ollamaChat,
  buildSystemPrompt,
  buildContextBlock,
  buildLFSummarizePrompt,
  buildLFSearchPrompt,
  summarizeDocumentContent,
  buildDocumentMetadataChatPrompt,
  type OllamaMessage,
} from "./ollama";
import { z } from "zod";
import { executeSmartSearch } from "./smart-search";

const requestSignal = (req: unknown): AbortSignal | undefined =>
  (req as { signal?: AbortSignal })?.signal;

export async function registerRoutes(httpServer: Server, app: Express): Promise<Server> {
  app.get("/api/documents", async (req, res) => {
    try {
      const lfConfig = getLaserficheConfig();
      if (lfConfig) {
        const token = await getLaserficheToken(lfConfig);
        const rootFolderId = Number(req.query.rootFolderId || 1);
        const visited = new Set<number>();
        const collectDocuments = async (folderId: number): Promise<any[]> => {
          if (visited.has(folderId)) return [];
          visited.add(folderId);

          const children = await laserficheGetFolderChildren(lfConfig, token, folderId);
          const docsHere = children.filter((entry: any) => entry?.entryType?.toLowerCase().includes("document"));
          const subfolders = children.filter((entry: any) => entry?.entryType?.toLowerCase().includes("folder"));
          const nested = await Promise.all(subfolders.map((folder: any) => collectDocuments(Number(folder.id))));
          return [...docsHere, ...nested.flat()];
        };

        const allDocuments = await collectDocuments(rootFolderId);

        const documents = await Promise.all(
          allDocuments.map(async (entry: any) => {
              const details = await laserficheGetEntry(lfConfig, token, Number(entry.id));
              const fields = await laserficheGetEntryFields(lfConfig, token, Number(entry.id)).catch(() => ({ value: [] as any[] }));
              const map: Record<string, string> = {};
              for (const f of fields.value || []) {
                const values = Array.isArray(f.values) ? f.values.map((v: any) => v?.value ?? "").filter(Boolean).join(", ") : "";
                if (f.fieldName) map[f.fieldName] = values;
              }

              return {
                id: String(entry.id),
                title: details.name || entry.name || `Entry ${entry.id}`,
                titleAr: map["العنوان"] || null,
                department: map["Department"] || map["الجهة"] || "Unknown",
                departmentAr: map["الجهة"] || null,
                classification: map["Classification"] || "Internal",
                securityLevel: map["Security Level"] || "Internal",
                docType: map["Document Type"] || details.extension || "Document",
                docTypeAr: map["نوع المستند"] || null,
                createdAt: details.creationTime ? new Date(details.creationTime) : new Date(),
                author: details.creator || null,
                authorAr: null,
                workflowStatus: map["Workflow Status"] || map["الحالة"] || "Active",
                tags: [],
                content: details.fullPath || "",
                contentAr: map["المحتوى"] || null,
                fileSizeKb: details.electronicDocumentSize ? Math.round(details.electronicDocumentSize / 1024) : null,
                pageCount: details.pageCount || null,
                laserficheId: String(entry.id),
                year: details.creationTime ? new Date(details.creationTime).getFullYear() : null,
              };
            })
        );
        return res.json(documents);
      }

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

  app.post("/api/smart-search", async (req, res) => {
    try {
      const body = req.body;
      if (!body?.query || typeof body.query !== "string" || body.query.trim().length === 0) {
        return res.status(400).json({ error: "Query is required" });
      }
      const result = await executeSmartSearch(body.query, body.filters, body.page || 1, body.limit || 10);
      await storage.createAuditLog({
        query: body.query,
        queryLanguage: /[\u0600-\u06FF]/.test(body.query) ? "ar" : "en",
        userId: "demo-user",
        username: "demo.user",
        resultsCount: result.total,
        searchType: "smart",
        filters: body.filters || null,
        ipAddress: req.ip || "127.0.0.1",
        department: "Demo",
      });
      res.json(result);
    } catch {
      res.status(500).json({ error: "Smart search failed" });
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
      const lfConfig = getLaserficheConfig();

      // ── Audit log stats (always available, regardless of LF connection) ──
      const buildAuditStats = async () => {
        const logs = await storage.getAuditLogs(500);
        const today = new Date();
        const searchesByDayMap: Record<string, number> = {};
        for (let i = 6; i >= 0; i--) {
          const d = new Date(today);
          d.setDate(today.getDate() - i);
          searchesByDayMap[d.toISOString().slice(0, 10)] = 0;
        }
        for (const log of logs) {
          const day = new Date(log.searchedAt as any).toISOString().slice(0, 10);
          if (day in searchesByDayMap) searchesByDayMap[day] += 1;
        }
        const topMap: Record<string, number> = {};
        for (const l of logs) topMap[l.query] = (topMap[l.query] || 0) + 1;
        return {
          totalSearches: logs.length,
          searchesByDay: Object.entries(searchesByDayMap).map(([date, count]) => ({ date, count })),
          topSearches: Object.entries(topMap)
            .map(([query, count]) => ({ query, count }))
            .sort((a, b) => b.count - a.count)
            .slice(0, 5),
        };
      };

      if (lfConfig) {
        try {
          const token = await getLaserficheToken(lfConfig);

          // ── Helper: count docs + folders recursively from a folder ──
          const scanFolder = async (
            folderId: number,
            visited: Set<number>
          ): Promise<{ documents: number; folders: number }> => {
            if (visited.has(folderId)) return { documents: 0, folders: 0 };
            visited.add(folderId);
            let children: any[];
            try {
              children = await laserficheGetFolderChildren(lfConfig, token, folderId);
            } catch {
              return { documents: 0, folders: 0 };
            }
            const docs = children.filter(
              (e: any) => e.isElectronicDocument || e.entryType?.toLowerCase().includes("document")
            ).length;
            const subfolders = children.filter((e: any) => e.entryType?.toLowerCase().includes("folder"));
            const nested = await Promise.all(
              subfolders.map((f: any) => scanFolder(Number(f.id), visited))
            );
            return {
              documents: docs + nested.reduce((s, n) => s + n.documents, 0),
              folders: subfolders.length + nested.reduce((s, n) => s + n.folders, 0),
            };
          };

          // ── Step 1 (parallel): get root children + template definitions ──
          const [rootChildren, templateDefs] = await Promise.all([
            laserficheGetFolderChildren(lfConfig, token, 1).catch(() => [] as any[]),
            laserficheGetTemplateDefinitions(lfConfig, token).catch(() => []),
          ]);

          const rootFolderEntries = rootChildren.filter(
            (c: any) => c.entryType?.toLowerCase().includes("folder")
          );
          const rootDocsDirect = rootChildren.filter(
            (c: any) => c.isElectronicDocument || c.entryType?.toLowerCase().includes("document")
          ).length;

          // ── Step 2 (parallel): count docs + folders inside each root folder ──
          const rootFolderCounts = await Promise.all(
            rootFolderEntries.map((f: any) =>
              scanFolder(Number(f.id), new Set<number>()).then((r) => ({
                name: String(f.name || `Folder ${f.id}`),
                documents: r.documents,
                folders: r.folders,
              }))
            )
          );

          const totalDocuments = rootDocsDirect + rootFolderCounts.reduce((s, f) => s + f.documents, 0);
          const totalFolders = rootFolderEntries.length + rootFolderCounts.reduce((s, f) => s + f.folders, 0);

          // ── Step 3 (parallel): count documents per template ──
          const templateCounts = await Promise.all(
            templateDefs.map((tmpl) =>
              laserficheCountByTemplate(lfConfig, token, tmpl.name)
                .then((count) => ({ name: tmpl.name, count }))
                .catch(() => ({ name: tmpl.name, count: 0 }))
            )
          );
          const templateStats = templateCounts
            .filter((t) => t.count > 0)
            .sort((a, b) => b.count - a.count);

          const docsWithTemplate = templateStats.reduce((s, t) => s + t.count, 0);
          const docsWithoutTemplate = Math.max(0, totalDocuments - docsWithTemplate);

          // ── Step 4: audit log stats ──
          const auditStats = await buildAuditStats();

          return res.json({
            repositoryId: lfConfig.repositoryId,
            isLive: true,
            totalFolders,
            totalDocuments,
            totalTemplates: templateDefs.length,
            docsWithTemplate,
            docsWithoutTemplate,
            templateStats,
            rootFolders: rootFolderCounts,
            ...auditStats,
          });
        } catch (err: any) {
          console.warn("Laserfiche dashboard stats failed, using fallback:", err?.message || err);
        }
      }

      // ── Fallback: no LF connection or LF unavailable ──
      const auditStats = await buildAuditStats();
      const memStats = await storage.getDashboardStats();
      return res.json({
        repositoryId: null,
        isLive: false,
        totalFolders: 0,
        totalDocuments: memStats.totalDocuments,
        totalTemplates: 0,
        docsWithTemplate: 0,
        docsWithoutTemplate: memStats.totalDocuments,
        templateStats: [],
        rootFolders: Object.entries(memStats.docsByDepartment).map(([name, documents]) => ({
          name,
          documents,
          folders: 0,
        })),
        ...auditStats,
      });
    } catch {
      res.status(500).json({ error: "Failed to fetch dashboard stats" });
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
    const { query, searchCommand, maxResults = 25, page = 1 } = req.body;
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    try {
      const finalCommand = searchCommand || naturalLanguageToLFSearchCommand(String(query || "")).command;
      const nlResult = naturalLanguageToLFSearchCommand(String(query || finalCommand));
      const token = await getLaserficheToken(config);
      const allEntries = await laserficheRepositorySearch(config, token, finalCommand, Math.min(Number(maxResults) * Math.max(Number(page), 1), 200));
      const start = (Math.max(Number(page), 1) - 1) * Number(maxResults);
      const pageEntries = allEntries.slice(start, start + Number(maxResults));
      const entries = await Promise.all(pageEntries.map(async (entry: any) => {
        const [details, rawFields] = await Promise.all([
          laserficheGetEntry(config, token, Number(entry.id)).catch(() => entry),
          laserficheGetEntryFieldsRaw(config, token, Number(entry.id)).catch(() => []),
        ]);
        const metadata: Record<string, string[]> = {};
        for (const field of rawFields as any[]) {
          const name = String(field?.fieldName || "").trim();
          if (!name) continue;
          metadata[name] = Array.isArray(field?.values) ? field.values.map((v: any) => String(v?.value ?? "")).filter(Boolean) : [];
        }
        return { ...details, id: Number(details.id || entry.id), metadata, previewUrl: `/api/laserfiche/entries/${Number(entry.id)}/content?disposition=inline`, openUrl: `/lf-document/${Number(entry.id)}`, sourceUrl: `/api/laserfiche/entries/${Number(entry.id)}/open`, downloadUrl: `/api/laserfiche/entries/${Number(entry.id)}/content?disposition=attachment` };
      }));
      res.json({ entries, total: allEntries.length, page: Number(page), pageSize: Number(maxResults), searchCommand: finalCommand, nlTranslation: nlResult, query });
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

  app.get("/api/laserfiche/folders", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    const rootFolderId = Number(req.query.rootFolderId || 1);
    try {
      const token = await getLaserficheToken(config);
      const children = await laserficheGetFolderChildren(config, token, rootFolderId);
      const folders = children
        .filter((c: any) => c.entryType?.toLowerCase().includes("folder"))
        .map((f: any) => ({ id: f.id, name: f.name }));
      res.json(folders);
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/lf/folders", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    const rootFolderId = Number(req.query.rootFolderId || 1);
    try {
      const token = await getLaserficheToken(config);
      const children = await laserficheGetFolderChildren(config, token, rootFolderId);
      const folders = children
        .filter((c: any) => c.entryType?.toLowerCase().includes("folder"))
        .map((f: any) => ({ id: f.id, name: f.name }));
      res.json(folders);
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/lf/root-candidates", async (_req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    try {
      const token = await getLaserficheToken(config);
      const candidates = [1, 2, 3, 5, 10, 17, 20, 50, 100];
      const results: Array<{ id: number; name: string }> = [];
      for (const id of candidates) {
        try {
          const entry = await laserficheGetEntry(config, token, id);
          if (entry?.entryType?.toLowerCase().includes("folder")) {
            results.push({ id, name: entry.name || `Folder ${id}` });
          }
        } catch {}
      }
      // also include direct children of repository root when available
      try {
        const rootChildren = await laserficheGetFolderChildren(config, token, 1);
        for (const c of rootChildren.filter((x: any) => x.entryType?.toLowerCase().includes("folder")).slice(0, 30)) {
          if (!results.find((r) => r.id === Number(c.id))) {
            results.push({ id: Number(c.id), name: c.name || `Folder ${c.id}` });
          }
        }
      } catch {}
      res.json({ candidates: results });
    } catch (err: any) {
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/lf/documents", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });

    const requestedRootFolderId = req.query.rootFolderId !== undefined ? Number(req.query.rootFolderId) : null;
    if (requestedRootFolderId !== null && !Number.isFinite(requestedRootFolderId)) {
      return res.status(400).json({ error: "Invalid root folder id" });
    }

    try {
      const token = await getLaserficheToken(config);
      const apiCalls: string[] = [];
      const failedCalls: Array<{ endpoint: string; error: string }> = [];
      const rootCandidates = [1, 2, 3, 5, 10, 17, 20, 50, 100];

      const listChildren = async (folderId: number) => {
        const endpoint = `/v1|v2/Repositories/${config.repositoryId}/Entries/${folderId}/Folder/Children`;
        apiCalls.push(endpoint);
        try {
          const children = await laserficheGetFolderChildren(config, token, folderId);
          console.info("[ArchiveDocuments] folder children loaded", {
            folderId,
            childCount: children.length,
            documents: children.filter((entry: any) => entry?.isElectronicDocument || entry?.entryType?.toLowerCase().includes("document")).length,
            folders: children.filter((entry: any) => entry?.entryType?.toLowerCase().includes("folder")).length,
          });
          return children;
        } catch (err: any) {
          failedCalls.push({ endpoint, error: err.message || String(err) });
          console.error("[ArchiveDocuments] failed to load folder children", { folderId, endpoint, error: err.message || String(err) });
          return [];
        }
      };

      const discoverRoots = async (): Promise<Array<{ id: number; name: string }>> => {
        if (requestedRootFolderId !== null && requestedRootFolderId !== 1) {
          return [{ id: requestedRootFolderId, name: `Folder ${requestedRootFolderId}` }];
        }

        const roots = new Map<number, string>();
        for (const id of rootCandidates) {
          try {
            const endpoint = `/v1|v2/Repositories/${config.repositoryId}/Entries/${id}`;
            apiCalls.push(endpoint);
            const entry = await laserficheGetEntry(config, token, id);
            if (entry?.entryType?.toLowerCase().includes("folder")) {
              roots.set(id, entry.name || `Folder ${id}`);
            }
          } catch (err: any) {
            failedCalls.push({ endpoint: `/v1|v2/Repositories/${config.repositoryId}/Entries/${id}`, error: err.message || String(err) });
          }
        }

        const rootChildren = await listChildren(1);
        for (const child of rootChildren) {
          if (child?.entryType?.toLowerCase().includes("folder")) {
            roots.set(Number(child.id), child.name || `Folder ${child.id}`);
          }
        }

        return Array.from(roots, ([id, name]) => ({ id, name }));
      };

      const roots = await discoverRoots();
      const visitedFolders = new Set<number>();
      const documentsById = new Map<number, any>();
      let folderCount = 0;

      const walkFolder = async (folderId: number, folderName: string): Promise<void> => {
        if (visitedFolders.has(folderId)) return;
        visitedFolders.add(folderId);
        folderCount += 1;

        const children = await listChildren(folderId);
        const documents = children.filter((entry: any) => entry?.isElectronicDocument || entry?.entryType?.toLowerCase().includes("document"));
        for (const entry of documents) {
          documentsById.set(Number(entry.id), { ...entry, folderName });
        }

        const subfolders = children.filter((entry: any) => entry?.entryType?.toLowerCase().includes("folder"));
        await Promise.all(subfolders.map((folder: any) => walkFolder(Number(folder.id), folder.name || folderName)));
      };

      await Promise.all(roots.map((root) => walkFolder(root.id, root.name)));

      const documents = await Promise.all(
        Array.from(documentsById.values()).map(async (entry: any) => {
          const entryId = Number(entry.id);
          let details: any = entry;
          let rawFields: any[] = [];

          try {
            apiCalls.push(`/v1|v2/Repositories/${config.repositoryId}/Entries/${entryId}`);
            details = await laserficheGetEntry(config, token, entryId);
          } catch (err: any) {
            failedCalls.push({ endpoint: `/v1|v2/Repositories/${config.repositoryId}/Entries/${entryId}`, error: err.message || String(err) });
            console.error("[ArchiveDocuments] failed to load document details", { entryId, error: err.message || String(err) });
          }

          try {
            apiCalls.push(`/v1|v2/Repositories/${config.repositoryId}/Entries/${entryId}/fields`);
            rawFields = await laserficheGetEntryFieldsRaw(config, token, entryId);
          } catch (err: any) {
            failedCalls.push({ endpoint: `/v1|v2/Repositories/${config.repositoryId}/Entries/${entryId}/fields`, error: err.message || String(err) });
            console.error("[ArchiveDocuments] failed to load document metadata", { entryId, error: err.message || String(err) });
          }

          const metadata: Record<string, string[]> = {};
          for (const field of rawFields) {
            const fieldName = String(field?.fieldName || field?.name || "").trim();
            if (!fieldName) continue;
            metadata[fieldName] = Array.isArray(field?.values)
              ? field.values.map((value: any) => String(value?.value ?? "")).filter(Boolean)
              : [];
          }

          return {
            id: entryId,
            name: details.name || entry.name || `Entry ${entryId}`,
            path: details.fullPath || entry.fullPath || "",
            folderName: entry.folderName || "Repository",
            repositoryId: config.repositoryId,
            repositoryName: config.repositoryId,
            metadata,
            extension: details.extension || entry.extension || null,
            pageCount: details.pageCount || entry.pageCount || null,
            isElectronicDocument: entry.isElectronicDocument !== false,
          };
        })
      );

      console.info("[ArchiveDocuments] recursive archive load complete", {
        repositoryId: config.repositoryId,
        requestedRootFolderId,
        roots: roots.map((root) => root.id),
        folderCount,
        documentCount: documents.length,
        apiCallCount: apiCalls.length,
        failedCallCount: failedCalls.length,
      });

      res.json({
        repositoryId: config.repositoryId,
        repositoryName: config.repositoryId,
        roots,
        folderCount,
        documentCount: documents.length,
        apiEndpoints: Array.from(new Set(apiCalls)),
        failedCalls,
        documents,
      });
    } catch (err: any) {
      console.error("[ArchiveDocuments] failed to load recursive archive documents", { error: err.message || String(err) });
      res.status(500).json({ error: err.message });
    }
  });

  app.get("/api/laserfiche/documents", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    const folderId = Number(req.query.folderId || 1);
    if (!Number.isFinite(folderId)) return res.status(400).json({ error: "Invalid folder id" });
    try {
      const token = await getLaserficheToken(config);
      const children = await laserficheGetFolderChildren(config, token, folderId);
      const docs = await Promise.all(
        children
          .filter((e: any) => e.isElectronicDocument)
          .map(async (e: any) => {
            let fields: any[] = [];
            try {
              fields = await laserficheGetEntryFieldsRaw(config, token, e.id);
            } catch {}
            return {
              id: e.id,
              name: e.name,
              path: e.fullPath || "",
              fields: fields.map((f: any) => ({
                name: f?.fieldName || f?.name || "Unknown",
                value: Array.isArray(f?.values) ? f.values.map((v: any) => v?.value ?? "").filter(Boolean).join(", ") : "",
              })),
              isElectronic: !!e.isElectronicDocument,
            };
          })
      );
      res.json({ folderId, documents: docs });
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
    const { messages, query, contextEntryId: contextEntryIdRaw, contextDocumentContext } = req.body as {
      messages: OllamaMessage[];
      query: string;
      contextEntryId?: number | string;
      contextDocumentContext?: string;
    };
    const contextEntryId =
      typeof contextEntryIdRaw === "string"
        ? Number(contextEntryIdRaw)
        : contextEntryIdRaw;

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
          requestSignal(req)
        );
        res.write(`data: ${JSON.stringify({ type: "done" })}\n\n`);
      } catch (err: any) {
        res.write(`data: ${JSON.stringify({ type: "error", error: err.message })}\n\n`);
      } finally {
        res.end();
      }
      return;
    }


    let selectedMetadataContext = "";
    if (Number.isFinite(contextEntryId) && lfConfig) {
      try {
        const token = await getLaserficheToken(lfConfig);
        const [entry, rawFields] = await Promise.all([
          laserficheGetEntry(lfConfig, token, Number(contextEntryId)),
          laserficheGetEntryFieldsRaw(lfConfig, token, Number(contextEntryId)),
        ]);
        const metadataPrompt = buildDocumentMetadataChatPrompt({
          entry: { id: entry.id, name: entry.name, path: entry.fullPath, creationTime: entry.creationTime, creator: entry.creator },
          fields: rawFields,
          userPrompt: userQuery,
          lang,
        });
        selectedMetadataContext = contextDocumentContext
          ? `${metadataPrompt}\n\nCLIENT DOCUMENT CONTEXT:\n${contextDocumentContext}`
          : metadataPrompt;
        res.write(`data: ${JSON.stringify({ type: "lf-entry", entryId: entry.id, name: entry.name })}\n\n`);
      } catch (err: any) {
        res.write(`data: ${JSON.stringify({ type: "error", error: `Failed to load Laserfiche metadata: ${err.message}` })}\n\n`);
        res.end();
        return;
      }
    }

    // ── Detect Laserfiche natural-language search intent ──────────────────
    const lfSearchKeywords = /وثيقة|معاملة|ملف|أرشيف|document|archive|file|report|contract|سجل|تقرير|عقد|ابحث|search|find/iu;
    let lfContextBlock = "";
    let lfEntries: any[] = [];

    const normalizeText = (s: string) =>
      (s || "")
        .toLowerCase()
        .normalize("NFKD")
        .replace(/[\u064B-\u065F\u0670]/g, "")
        .replace(/[إأآا]/g, "ا")
        .replace(/[ة]/g, "ه")
        .replace(/[ى]/g, "ي")
        .replace(/\s+/g, " ")
        .trim();
    const stopWords = new Set([
      "ابحث", "عن", "فيها", "التي", "يكون", "يوجد", "وثيقه", "وثيقة", "وثايق", "الوثيقه", "الوثيقة",
      "search", "find", "for", "the", "with", "document", "documents", "file", "archive"
    ]);
    const extractKeywords = (queryText: string) =>
      normalizeText(queryText)
        .split(" ")
        .map((w) => w.trim())
        .filter((w) => w.length > 2 && !stopWords.has(w));
    const levenshtein = (a: string, b: string): number => {
      const dp = Array.from({ length: a.length + 1 }, () => Array(b.length + 1).fill(0));
      for (let i = 0; i <= a.length; i++) dp[i][0] = i;
      for (let j = 0; j <= b.length; j++) dp[0][j] = j;
      for (let i = 1; i <= a.length; i++) {
        for (let j = 1; j <= b.length; j++) {
          const cost = a[i - 1] === b[j - 1] ? 0 : 1;
          dp[i][j] = Math.min(dp[i - 1][j] + 1, dp[i][j - 1] + 1, dp[i - 1][j - 1] + cost);
        }
      }
      return dp[a.length][b.length];
    };
    const keywordExists = (keyword: string, text: string) => {
      if (text.includes(keyword)) return true;
      const tokens = text.split(/[\s|,:;\n]+/).filter(Boolean);
      return tokens.some((t) => t.startsWith(keyword) || keyword.startsWith(t) || (keyword.length > 3 && levenshtein(t, keyword) <= 1));
    };

    if (lfConfig && lfSearchKeywords.test(userQuery)) {
      try {
        const token = await getLaserficheToken(lfConfig);
        const fieldDefinitions = await laserficheGetFieldDefinitions(lfConfig, token).catch(() => []);
        const fieldNameTokens = new Set(
          (fieldDefinitions || [])
            .flatMap((f: any) => String(f?.name || "").split(/\s+/))
            .map((w: string) => normalizeText(w))
            .filter((w: string) => w.length > 2)
        );
        const visited = new Set<number>();
        const collectDocs = async (folderId: number): Promise<any[]> => {
          if (visited.has(folderId)) return [];
          visited.add(folderId);
          const children = await laserficheGetFolderChildren(lfConfig, token, folderId);
          const docsHere = children.filter((e: any) => e.isElectronicDocument || e.entryType?.toLowerCase().includes("document"));
          const subfolders = children.filter((e: any) => e.entryType?.toLowerCase().includes("folder"));
          const nested = await Promise.all(subfolders.map((f: any) => collectDocs(Number(f.id))));
          return [...docsHere, ...nested.flat()];
        };
        lfEntries = (await collectDocs(1)).slice(0, 300);
        const normalizedQuery = normalizeText(userQuery);
        const keywords = extractKeywords(userQuery);
        const effectiveKeywords = keywords.filter((k) => !fieldNameTokens.has(k));

        const inspected = await Promise.all(
          lfEntries.map(async (entry: any) => {
            let rawFields: any[] = [];
            try {
              rawFields = await laserficheGetEntryFieldsRaw(lfConfig, token, entry.id);
            } catch {}

            const metadataLines = rawFields
              .map((f: any) => {
                const name = f?.fieldName || f?.name || "Unknown";
                const value = Array.isArray(f?.values)
                  ? f.values.map((v: any) => v?.value ?? "").filter(Boolean).join(", ")
                  : "";
                return value ? `${name}: ${value}` : "";
              })
              .filter(Boolean);

            const searchableText = normalizeText(
              [
                `ID: ${entry.id}`,
                `Name: ${entry.name || ""}`,
                `Path: ${entry.fullPath || ""}`,
                "Metadata:",
                ...metadataLines,
              ].join("\n")
            );

            const keywordHits = effectiveKeywords.filter((k) => keywordExists(k, searchableText)).length;
            const score =
              keywordHits * 5 +
              (normalizeText(entry.name || "").includes(normalizedQuery) ? 3 : 0) +
              (normalizeText(entry.fullPath || "").includes(normalizedQuery) ? 2 : 0);

            return {
              id: entry.id,
              name: entry.name,
              path: entry.fullPath || "",
              metadataPreview: metadataLines.slice(0, 6),
              searchableText,
              score,
            };
          })
        );

        const matched = inspected
          .filter((d) => {
            if (!normalizedQuery) return false;
            if (effectiveKeywords.length === 0) return d.searchableText.includes(normalizedQuery);
            return effectiveKeywords.every((k) => keywordExists(k, d.searchableText));
          })
          .sort((a, b) => b.score - a.score)
          .slice(0, 20);

        if (matched.length > 0) {
          const resultLines = matched.map((d, i) => {
            const meta = d.metadataPreview.length ? ` | ${d.metadataPreview.join(" | ")}` : "";
            return `[${i + 1}] ID:${d.id} | ${d.name} | ${d.path}${meta}`;
          });
          lfContextBlock = (lang === "ar"
            ? `نتائج بحث Laserfiche الحقيقية (تمت فلترتها بواسطة النظام):\n${resultLines.join("\n")}\n\nالتعليمات:\n- استخدم النتائج فقط.\n- لا تخترع وثائق غير موجودة.\n- اعرض الاسم وID والمسار لكل نتيجة.`
            : `Real Laserfiche search results (already filtered by system):\n${resultLines.join("\n")}\n\nInstructions:\n- Use only these results.\n- Do not invent documents.\n- Return name, ID, and path for each result.`);
        } else {
          lfContextBlock = lang === "ar"
            ? "نتائج بحث Laserfiche الحقيقية: لم يتم العثور على وثائق مطابقة."
            : "Real Laserfiche search results: no matching documents were found.";
        }
      } catch {}
    }

    // ── Fall back: local document DB context (only when no specific LF entry context) ──
    let contextDocs: any[] = [];
    if (!selectedMetadataContext) {
      try {
        const searchResult = await storage.searchDocuments({ query: userQuery, searchType: "hybrid", page: 1, limit: 5 });
        contextDocs = searchResult.results.map((r) => r.document);
      } catch {}
    }

    const systemPrompt = buildSystemPrompt(lang);
    const localContext = buildContextBlock(contextDocs, lang);
    const fullSystemPrompt = selectedMetadataContext
      ? `${systemPrompt}

You are an AI assistant.
The user is currently referring to THIS document only.
Entry ID: ${contextEntryId}

Document Data:
${selectedMetadataContext}

IMPORTANT:
- The user message ALWAYS refers to this document.
- Even if the user says only "summarize" or "لخص".
- Do NOT ask for clarification about which document.
- Do NOT ignore the document.
- Do NOT invent information.
- If data is missing, say it is not available in the document.
- If the user asks for a summary (e.g., "لخص الوثيقة"), provide a medium-length summary (about 5-7 sentences) with key metadata highlights and useful details.
- Respond in the same language as user input.`
      : `${systemPrompt}\n\n${lfContextBlock || localContext}`;

    const effectiveUserPrompt = selectedMetadataContext
      ? `User request:\n${userQuery}\n\nThis request is about Entry ID ${contextEntryId}.`
      : query;

    const chatMessages: OllamaMessage[] = selectedMetadataContext
      ? [
          { role: "system", content: fullSystemPrompt },
          { role: "user", content: effectiveUserPrompt },
        ]
      : [
          { role: "system", content: fullSystemPrompt },
          ...(messages || []).filter((m) => m.role !== "system"),
          ...(effectiveUserPrompt ? [{ role: "user" as const, content: effectiveUserPrompt }] : []),
        ];

    const sourceDocs = contextDocs.map((d) => ({ id: d.id, title: d.title, titleAr: d.titleAr, department: d.department, year: d.year }));
    res.write(`data: ${JSON.stringify({ type: "sources", sources: sourceDocs })}\n\n`);

    try {
      await ollamaChat(
        chatMessages,
        (tok) => res.write(`data: ${JSON.stringify({ type: "token", token: tok })}\n\n`),
        requestSignal(req)
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
        requestSignal(req)
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
        requestSignal(req)
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


  app.delete("/api/laserfiche/entries/:entryId", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });

    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) return res.status(400).json({ error: "Invalid entry id" });

    try {
      const token = await getLaserficheToken(config);
      await laserficheDeleteEntry(config, token, entryId);
      res.json({ ok: true, entryId });
    } catch (err: any) {
      res.status(500).json({ error: err.message || "Failed to delete entry" });
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

  // ── GET /api/laserfiche/entries/:entryId/edoc ────────────────────────────
  // Returns the backend-proxy URL for the electronic document (JSON).
  app.get("/api/laserfiche/entries/:entryId/edoc", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).json({ error: "Laserfiche not configured" });
    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) return res.status(400).json({ error: "Invalid entry id" });
    res.json({ entryId, url: `/api/laserfiche/entries/${entryId}/open` });
  });

  // ── GET /api/laserfiche/entries/:entryId/open ─────────────────────────────
  // Streams the actual Laserfiche edoc file through the backend with auth.
  // The browser navigates directly to this URL — no intermediate JSON step.
  // Token is fetched server-side; the frontend never touches a raw LF URL.
  app.get("/api/laserfiche/entries/:entryId/open", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) {
      return res.status(503).send("Laserfiche is not configured on this server.");
    }

    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) {
      return res.status(400).send("Invalid entry id.");
    }

    try {
      const token = await getLaserficheToken(config);
      const lfUrl = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}/Laserfiche.Repository.Document/edoc`;

      const lfRes = await fetch(lfUrl, {
        headers: { Authorization: `Bearer ${token}`, Accept: "*/*" },
      });

      if (!lfRes.ok) {
        const text = await lfRes.text();
        const isHtml = /<!DOCTYPE|<html/i.test(text);
        if (lfRes.status === 401 || isHtml) {
          return res.status(401).send("Authentication failed — Laserfiche rejected the token. Re-save your credentials in LF Settings.");
        }
        if (lfRes.status === 404) {
          return res.status(404).send("No electronic document is attached to this entry.");
        }
        return res.status(lfRes.status).send(`Laserfiche returned an error: ${lfRes.status}`);
      }

      const contentType = lfRes.headers.get("content-type") || "application/octet-stream";
      const disposition = lfRes.headers.get("content-disposition") || "";
      const contentLength = lfRes.headers.get("content-length");

      // If LF returned an HTML login page with a 200 status, catch it here
      if (/text\/html/i.test(contentType)) {
        return res.status(401).send("Authentication failed — Laserfiche returned a login page instead of the document. Re-save your credentials in LF Settings.");
      }

      res.setHeader("Content-Type", contentType);
      res.setHeader("Content-Disposition", disposition || `inline; filename="document-${entryId}"`);
      if (contentLength) res.setHeader("Content-Length", contentLength);
      res.setHeader("Cache-Control", "private, max-age=300");

      // Stream the file body directly to the client
      const { Readable } = await import("stream");
      Readable.fromWeb(lfRes.body as any).pipe(res);
    } catch (err: any) {
      res.status(500).send(`Server error: ${err.message}`);
    }
  });

  // ── GET /api/laserfiche/entries/:entryId/content ─────────────────────────
  // Streams the binary edoc to the client with the correct Content-Type.
  // Frontend uses this URL directly as the `src` of an iframe / img tag —
  // the browser never calls Laserfiche; the backend attaches the auth token.
  app.get("/api/laserfiche/entries/:entryId/content", async (req, res) => {
    const config = getLaserficheConfig();
    if (!config) return res.status(503).send("Laserfiche is not configured on this server.");

    const entryId = Number(req.params.entryId);
    if (!Number.isFinite(entryId)) return res.status(400).send("Invalid entry id.");

    try {
      const token = await getLaserficheToken(config);
      const lfUrl = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}/Laserfiche.Repository.Document/edoc`;

      const lfRes = await fetch(lfUrl, {
        headers: { Authorization: `Bearer ${token}`, Accept: "*/*" },
      });

      if (!lfRes.ok) {
        const text = await lfRes.text();
        const isHtml = /<!DOCTYPE|<html/i.test(text);
        if (lfRes.status === 401 || isHtml) return res.status(401).send("Authentication failed — re-save credentials in LF Settings.");
        if (lfRes.status === 404) return res.status(404).send("No electronic document attached to this entry.");
        return res.status(lfRes.status).send(`Laserfiche error: ${lfRes.status}`);
      }

      const contentType = lfRes.headers.get("content-type") || "application/octet-stream";
      if (/text\/html/i.test(contentType)) return res.status(401).send("Authentication failed — Laserfiche returned a login page.");

      const rawDisposition = lfRes.headers.get("content-disposition") || "";
      const filenameMatch = rawDisposition.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
      const safeFilename = (filenameMatch?.[1] || `document-${entryId}`).replace(/[\r\n]/g, "").trim();
      const contentLength = lfRes.headers.get("content-length");

      res.setHeader("Content-Type", contentType);
      res.setHeader("Content-Disposition", `inline; filename="${safeFilename}"`);
      if (contentLength) res.setHeader("Content-Length", contentLength);
      res.setHeader("Cache-Control", "no-store, no-cache, must-revalidate");
      res.setHeader("Pragma", "no-cache");
      res.setHeader("Expires", "0");

      const { Readable } = await import("stream");
      Readable.fromWeb(lfRes.body as any).pipe(res);
    } catch (err: any) {
      res.status(500).send(`Server error: ${err.message}`);
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
    console.info("[DocumentViewerAPI] loading Laserfiche document", {
      entryId,
      repositoryId: config.repositoryId,
      route: req.originalUrl,
    });
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
      const responseBody = {
        id: entry.id,
        name: entry.name,
        path: entry.fullPath,
        createdDate: entry.creationTime || null,
        creator: entry.creator || null,
        extension: entry.extension || null,
        pageCount: entry.pageCount || null,
        repositoryId: config.repositoryId,
        repositoryName: config.repositoryId,
        metadata,
        tags,
      };
      console.info("[DocumentViewerAPI] loaded Laserfiche document", {
        entryId,
        repositoryId: config.repositoryId,
        title: responseBody.name,
        metadataFields: metadata.length,
        tags: tags.length,
        hasPreviewContentEndpoint: true,
      });
      res.json(responseBody);
    } catch (err: any) {
      console.error("[DocumentViewerAPI] failed to load Laserfiche document", { entryId, repositoryId: config.repositoryId, error: err.message });
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

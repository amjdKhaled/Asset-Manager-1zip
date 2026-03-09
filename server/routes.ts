import type { Express } from "express";
import { createServer, type Server } from "http";
import { storage } from "./storage";
import { searchRequestSchema, insertAuditLogSchema } from "@shared/schema";
import { z } from "zod";

export async function registerRoutes(httpServer: Server, app: Express): Promise<Server> {
  app.get("/api/documents", async (req, res) => {
    try {
      const docs = await storage.getDocuments();
      res.json(docs);
    } catch (e) {
      res.status(500).json({ error: "Failed to fetch documents" });
    }
  });

  app.get("/api/documents/:id", async (req, res) => {
    try {
      const doc = await storage.getDocument(req.params.id);
      if (!doc) return res.status(404).json({ error: "Document not found" });
      res.json(doc);
    } catch (e) {
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
    } catch (e) {
      res.status(500).json({ error: "Search failed" });
    }
  });

  app.get("/api/audit-logs", async (req, res) => {
    try {
      const limit = parseInt(req.query.limit as string) || 100;
      const logs = await storage.getAuditLogs(limit);
      res.json(logs);
    } catch (e) {
      res.status(500).json({ error: "Failed to fetch audit logs" });
    }
  });

  app.get("/api/dashboard/stats", async (req, res) => {
    try {
      const stats = await storage.getDashboardStats();
      res.json(stats);
    } catch (e) {
      res.status(500).json({ error: "Failed to fetch stats" });
    }
  });

  return httpServer;
}

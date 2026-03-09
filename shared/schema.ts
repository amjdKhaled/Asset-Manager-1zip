import { sql } from "drizzle-orm";
import { pgTable, text, varchar, integer, real, timestamp, boolean, jsonb } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod";

export const users = pgTable("users", {
  id: varchar("id").primaryKey().default(sql`gen_random_uuid()`),
  username: text("username").notNull().unique(),
  password: text("password").notNull(),
});

export const documents = pgTable("documents", {
  id: varchar("id").primaryKey().default(sql`gen_random_uuid()`),
  title: text("title").notNull(),
  titleAr: text("title_ar"),
  department: text("department").notNull(),
  departmentAr: text("department_ar"),
  classification: text("classification").notNull(),
  securityLevel: text("security_level").notNull(),
  docType: text("doc_type").notNull(),
  docTypeAr: text("doc_type_ar"),
  createdAt: timestamp("created_at").defaultNow(),
  author: text("author"),
  authorAr: text("author_ar"),
  workflowStatus: text("workflow_status").notNull(),
  tags: text("tags").array(),
  content: text("content").notNull(),
  contentAr: text("content_ar"),
  fileSizeKb: integer("file_size_kb"),
  pageCount: integer("page_count"),
  laserficheId: text("laserfiche_id"),
  year: integer("year"),
});

export const auditLogs = pgTable("audit_logs", {
  id: varchar("id").primaryKey().default(sql`gen_random_uuid()`),
  query: text("query").notNull(),
  queryLanguage: text("query_language"),
  userId: text("user_id"),
  username: text("username"),
  resultsCount: integer("results_count"),
  searchType: text("search_type"),
  filters: jsonb("filters"),
  searchedAt: timestamp("searched_at").defaultNow(),
  ipAddress: text("ip_address"),
  department: text("department"),
});

export const insertUserSchema = createInsertSchema(users).pick({ username: true, password: true });
export const insertDocumentSchema = createInsertSchema(documents).omit({ id: true, createdAt: true });
export const insertAuditLogSchema = createInsertSchema(auditLogs).omit({ id: true, searchedAt: true });

export type InsertUser = z.infer<typeof insertUserSchema>;
export type User = typeof users.$inferSelect;
export type Document = typeof documents.$inferSelect;
export type InsertDocument = z.infer<typeof insertDocumentSchema>;
export type AuditLog = typeof auditLogs.$inferSelect;
export type InsertAuditLog = z.infer<typeof insertAuditLogSchema>;

export const searchRequestSchema = z.object({
  query: z.string().min(1),
  searchType: z.enum(["semantic", "keyword", "hybrid"]).default("hybrid"),
  filters: z.object({
    department: z.string().optional(),
    classification: z.string().optional(),
    securityLevel: z.string().optional(),
    docType: z.string().optional(),
    yearFrom: z.number().optional(),
    yearTo: z.number().optional(),
    workflowStatus: z.string().optional(),
  }).optional(),
  page: z.number().default(1),
  limit: z.number().default(10),
});

export type SearchRequest = z.infer<typeof searchRequestSchema>;

export type SearchResult = {
  document: Document;
  score: number;
  scoreBreakdown: { semantic: number; keyword: number; metadata: number };
  snippet: string;
  snippetAr?: string;
  matchedTerms: string[];
};

export type SearchResponse = {
  results: SearchResult[];
  total: number;
  page: number;
  limit: number;
  query: string;
  searchType: string;
  processingTimeMs: number;
};

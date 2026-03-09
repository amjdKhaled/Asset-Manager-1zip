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

function detectLanguage(text: string): "ar" | "en" | "mixed" {
  const arabicPattern = /[\u0600-\u06FF]/;
  const englishPattern = /[a-zA-Z]/;
  const hasArabic = arabicPattern.test(text);
  const hasEnglish = englishPattern.test(text);
  if (hasArabic && hasEnglish) return "mixed";
  if (hasArabic) return "ar";
  return "en";
}

function tokenize(text: string): string[] {
  return text.toLowerCase().replace(/[^\u0600-\u06FFa-zA-Z0-9\s]/g, " ").split(/\s+/).filter(t => t.length > 1);
}

function computeKeywordScore(doc: Document, queryTokens: string[]): number {
  if (queryTokens.length === 0) return 0;
  const docText = `${doc.title} ${doc.titleAr || ""} ${doc.content} ${doc.contentAr || ""} ${(doc.tags || []).join(" ")} ${doc.department} ${doc.departmentAr || ""}`.toLowerCase();
  const matches = queryTokens.filter(t => docText.includes(t));
  return matches.length / queryTokens.length;
}

function computeSemanticScore(doc: Document, query: string): number {
  const queryLower = query.toLowerCase();
  const queryTokens = tokenize(query);

  const docTitle = (doc.title + " " + (doc.titleAr || "")).toLowerCase();
  const docContent = (doc.content + " " + (doc.contentAr || "")).toLowerCase();
  const docTags = (doc.tags || []).join(" ").toLowerCase();

  let score = 0;

  const semanticGroups: Record<string, string[]> = {
    "عقود": ["contract", "agreement", "عقد", "اتفاقية", "maintenance", "صيانة"],
    "تجديد": ["renewal", "extend", "تمديد", "renew", "تجديد"],
    "صيانة": ["maintenance", "service", "خدمة", "repair", "صيانة"],
    "ميزانية": ["budget", "financial", "مالي", "finance", "spending", "إنفاق"],
    "تقرير": ["report", "analysis", "تحليل", "assessment", "تقييم"],
    "موارد": ["hr", "human resources", "staff", "موظف", "personnel"],
    "مشتريات": ["procurement", "purchase", "tender", "مناقصة", "supply"],
    "أمن": ["security", "حماية", "protection", "classified", "سري"],
    "تحويل": ["transfer", "digital", "رقمي", "transformation", "نقل"],
    "برنامج": ["program", "project", "مشروع", "initiative", "خطة"],
  };

  for (const [key, related] of Object.entries(semanticGroups)) {
    const groupMatch = related.some(r => queryLower.includes(r) || r.includes(queryLower));
    if (groupMatch) {
      const docHasRelated = related.some(r =>
        docTitle.includes(r) || docContent.includes(r) || docTags.includes(r)
      );
      if (docHasRelated) score += 0.35;
    }
  }

  const keywordMatch = computeKeywordScore(doc, queryTokens);
  score += keywordMatch * 0.3;

  if (doc.year && (queryLower.includes(doc.year.toString()))) {
    score += 0.15;
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
    this.seedData();
  }

  private seedData() {
    const docs: Omit<Document, "id">[] = [
      {
        title: "Maintenance Contract Renewal - Ministry of Finance 2023",
        titleAr: "تجديد عقود الصيانة - وزارة المالية 2023",
        department: "Ministry of Finance",
        departmentAr: "وزارة المالية",
        classification: "Official",
        securityLevel: "Internal",
        docType: "Contract",
        docTypeAr: "عقد",
        author: "Ahmed Al-Rashidi",
        authorAr: "أحمد الراشدي",
        workflowStatus: "Approved",
        tags: ["maintenance", "contract", "renewal", "2023", "finance", "صيانة", "عقود"],
        content: "This contract outlines the renewal terms for maintenance services for the fiscal year 2023. All maintenance contracts must be reviewed quarterly. The contractor agrees to provide on-site technical support within 24 hours of any reported issue. Payment terms are net 30 days from invoice date. The contract covers HVAC systems, electrical infrastructure, and IT equipment maintenance across all ministry buildings.",
        contentAr: "يحدد هذا العقد شروط تجديد خدمات الصيانة للسنة المالية 2023. يجب مراجعة جميع عقود الصيانة كل ربع سنة. يوافق المقاول على تقديم الدعم الفني في الموقع خلال 24 ساعة من أي مشكلة مبلغ عنها.",
        fileSizeKb: 2840,
        pageCount: 24,
        laserficheId: "LF-2023-FIN-001",
        year: 2023,
        createdAt: new Date("2023-01-15"),
      },
      {
        title: "Annual Budget Report - Public Infrastructure 2023",
        titleAr: "تقرير الميزانية السنوية - البنية التحتية العامة 2023",
        department: "Ministry of Public Works",
        departmentAr: "وزارة الأشغال العامة",
        classification: "Confidential",
        securityLevel: "Restricted",
        docType: "Report",
        docTypeAr: "تقرير",
        author: "Sarah Al-Mutairi",
        authorAr: "سارة المطيري",
        workflowStatus: "Under Review",
        tags: ["budget", "infrastructure", "annual", "2023", "ميزانية", "بنية تحتية"],
        content: "This report provides a comprehensive analysis of the public infrastructure budget for fiscal year 2023. Total allocations amount to SAR 4.2 billion. Key expenditure areas include road construction (35%), water systems (28%), and urban development (22%). The report identifies significant cost overruns in the northern region projects and recommends corrective measures including enhanced procurement controls and contractor performance monitoring.",
        contentAr: "يقدم هذا التقرير تحليلاً شاملاً لميزانية البنية التحتية العامة للسنة المالية 2023. تبلغ التخصيصات الإجمالية 4.2 مليار ريال سعودي.",
        fileSizeKb: 5120,
        pageCount: 68,
        laserficheId: "LF-2023-PWK-002",
        year: 2023,
        createdAt: new Date("2023-03-20"),
      },
      {
        title: "IT Infrastructure Maintenance Service Agreement 2023",
        titleAr: "اتفاقية خدمة صيانة البنية التحتية لتقنية المعلومات 2023",
        department: "Ministry of Communications",
        departmentAr: "وزارة الاتصالات",
        classification: "Official",
        securityLevel: "Internal",
        docType: "Contract",
        docTypeAr: "عقد",
        author: "Khalid Al-Dosari",
        authorAr: "خالد الدوسري",
        workflowStatus: "Active",
        tags: ["IT", "infrastructure", "maintenance", "service", "2023", "تقنية", "صيانة", "اتفاقية"],
        content: "Service agreement for comprehensive IT infrastructure maintenance covering all ministry data centers, network equipment, and end-user computing devices. The service provider guarantees 99.9% uptime SLA for critical systems. Scope includes preventive maintenance schedules, emergency response protocols, and quarterly performance reviews. Contract value: SAR 12.5 million for the period January to December 2023.",
        contentAr: "اتفاقية الخدمة لصيانة البنية التحتية لتقنية المعلومات الشاملة التي تغطي جميع مراكز البيانات الوزارية ومعدات الشبكات وأجهزة الحوسبة للمستخدمين النهائيين.",
        fileSizeKb: 3650,
        pageCount: 42,
        laserficheId: "LF-2023-COM-003",
        year: 2023,
        createdAt: new Date("2023-02-01"),
      },
      {
        title: "Human Resources Policy Update - Remote Work Guidelines",
        titleAr: "تحديث سياسة الموارد البشرية - إرشادات العمل عن بُعد",
        department: "Ministry of Human Resources",
        departmentAr: "وزارة الموارد البشرية",
        classification: "Official",
        securityLevel: "Public",
        docType: "Policy",
        docTypeAr: "سياسة",
        author: "Fatima Al-Harbi",
        authorAr: "فاطمة الحربي",
        workflowStatus: "Published",
        tags: ["HR", "policy", "remote work", "guidelines", "موارد بشرية", "عمل عن بعد"],
        content: "Updated guidelines for remote and hybrid work arrangements across all government entities. Employees eligible for remote work include those with roles classified as knowledge workers. Minimum office attendance requirement is 3 days per week. Performance evaluation criteria for remote employees focus on deliverables and output quality. All remote workers must use approved VPN and secure communication channels.",
        contentAr: "إرشادات محدثة لترتيبات العمل عن بُعد والهجين عبر جميع الجهات الحكومية. الموظفون المؤهلون للعمل عن بُعد هم أصحاب الأدوار المصنفة كعمال معرفة.",
        fileSizeKb: 980,
        pageCount: 18,
        laserficheId: "LF-2023-HR-004",
        year: 2023,
        createdAt: new Date("2023-04-10"),
      },
      {
        title: "Procurement Tender - Office Supplies Q3 2023",
        titleAr: "مناقصة مشتريات - المستلزمات المكتبية الربع الثالث 2023",
        department: "General Authority for Government Procurement",
        departmentAr: "الهيئة العامة للمشتريات الحكومية",
        classification: "Official",
        securityLevel: "Public",
        docType: "Tender",
        docTypeAr: "مناقصة",
        author: "Omar Al-Ghamdi",
        authorAr: "عمر الغامدي",
        workflowStatus: "Closed",
        tags: ["procurement", "tender", "office supplies", "Q3", "مشتريات", "مناقصة", "مستلزمات"],
        content: "Open tender for supply and delivery of office supplies to central government facilities for Q3 2023. Estimated contract value: SAR 850,000. Requirements include delivery within 5 working days of purchase order issuance. Qualified suppliers must hold a valid commercial registration and Zakat certificate. Evaluation criteria: 60% price, 25% quality, 15% delivery capability.",
        contentAr: "مناقصة مفتوحة لتوريد وتسليم المستلزمات المكتبية للمرافق الحكومية المركزية للربع الثالث من عام 2023. القيمة التقديرية للعقد: 850,000 ريال سعودي.",
        fileSizeKb: 1250,
        pageCount: 22,
        laserficheId: "LF-2023-PRO-005",
        year: 2023,
        createdAt: new Date("2023-07-05"),
      },
      {
        title: "Digital Transformation Initiative - Phase 2 Implementation Plan",
        titleAr: "مبادرة التحول الرقمي - خطة تنفيذ المرحلة الثانية",
        department: "Ministry of Digital Economy",
        departmentAr: "وزارة الاقتصاد الرقمي",
        classification: "Confidential",
        securityLevel: "Restricted",
        docType: "Plan",
        docTypeAr: "خطة",
        author: "Noura Al-Qahtani",
        authorAr: "نورة القحطاني",
        workflowStatus: "Active",
        tags: ["digital", "transformation", "phase 2", "technology", "رقمي", "تحول", "خطة"],
        content: "Comprehensive implementation plan for Phase 2 of the national digital transformation initiative. This phase focuses on AI integration across government services, cloud migration of legacy systems, and deployment of citizen-facing digital services. Budget allocation: SAR 2.1 billion. Key milestones include completion of data center consolidation by Q2, deployment of 45 new digital government services by Q3, and achievement of 80% paperless processing by Q4 2023.",
        contentAr: "خطة تنفيذ شاملة للمرحلة الثانية من مبادرة التحول الرقمي الوطنية. تركز هذه المرحلة على تكامل الذكاء الاصطناعي عبر الخدمات الحكومية وترحيل الأنظمة القديمة إلى السحابة.",
        fileSizeKb: 8920,
        pageCount: 112,
        laserficheId: "LF-2023-DIG-006",
        year: 2023,
        createdAt: new Date("2023-01-30"),
      },
      {
        title: "Internal Memo - Budget Freeze Notification Q4 2023",
        titleAr: "مذكرة داخلية - إشعار تجميد الميزانية الربع الرابع 2023",
        department: "Ministry of Finance",
        departmentAr: "وزارة المالية",
        classification: "Confidential",
        securityLevel: "Internal",
        docType: "Memo",
        docTypeAr: "مذكرة",
        author: "Abdulaziz Al-Subaie",
        authorAr: "عبدالعزيز السبيعي",
        workflowStatus: "Distributed",
        tags: ["memo", "budget", "freeze", "Q4", "مذكرة", "ميزانية", "تجميد"],
        content: "This internal memo notifies all department heads of a temporary budget freeze effective from October 1 through December 31, 2023. Exceptions apply to critical operational expenditures and pre-approved projects. All new purchase requests exceeding SAR 50,000 must receive additional Finance Ministry approval. Departments are required to submit revised spending plans by September 25, 2023.",
        contentAr: "تُخطر هذه المذكرة الداخلية جميع رؤساء الأقسام بتجميد مؤقت للميزانية يسري اعتباراً من الأول من أكتوبر حتى 31 ديسمبر 2023.",
        fileSizeKb: 450,
        pageCount: 6,
        laserficheId: "LF-2023-FIN-007",
        year: 2023,
        createdAt: new Date("2023-09-18"),
      },
      {
        title: "Security Assessment Report - Government Data Centers 2023",
        titleAr: "تقرير تقييم الأمن - مراكز البيانات الحكومية 2023",
        department: "National Cybersecurity Authority",
        departmentAr: "الهيئة الوطنية للأمن السيبراني",
        classification: "Top Secret",
        securityLevel: "Classified",
        docType: "Report",
        docTypeAr: "تقرير",
        author: "Ibrahim Al-Zahrani",
        authorAr: "إبراهيم الزهراني",
        workflowStatus: "Completed",
        tags: ["security", "cybersecurity", "assessment", "data center", "أمن", "سيبراني"],
        content: "Classified security assessment of all government data centers conducted during Q2 2023. The assessment evaluated physical security, network perimeter defenses, access control systems, and incident response capabilities. Overall security posture rated at 78/100. Critical vulnerabilities identified in 3 legacy systems requiring immediate remediation. Recommendations include enhanced multi-factor authentication, network segmentation improvements, and accelerated patch management protocols.",
        contentAr: "تقييم أمني سري لجميع مراكز البيانات الحكومية أُجري خلال الربع الثاني من عام 2023.",
        fileSizeKb: 6750,
        pageCount: 89,
        laserficheId: "LF-2023-NCA-008",
        year: 2023,
        createdAt: new Date("2023-08-12"),
      },
      {
        title: "Smart City Infrastructure Contract - Riyadh Municipality 2022",
        titleAr: "عقد البنية التحتية للمدينة الذكية - أمانة الرياض 2022",
        department: "Riyadh Municipality",
        departmentAr: "أمانة الرياض",
        classification: "Official",
        securityLevel: "Internal",
        docType: "Contract",
        docTypeAr: "عقد",
        author: "Mohammed Al-Shehri",
        authorAr: "محمد الشهري",
        workflowStatus: "Completed",
        tags: ["smart city", "infrastructure", "Riyadh", "IoT", "مدينة ذكية", "بنية تحتية"],
        content: "Contract for the design and implementation of smart city infrastructure across 12 districts of Riyadh. Project scope includes deployment of 50,000 IoT sensors, intelligent traffic management systems, smart street lighting, and integrated waste management monitoring. Contract value: SAR 380 million over 3 years. Implementation partner: Consortium of 4 technology firms led by Saudi Aramco Digital.",
        contentAr: "عقد لتصميم وتنفيذ البنية التحتية للمدينة الذكية عبر 12 حياً في الرياض. يشمل نطاق المشروع نشر 50,000 جهاز استشعار إنترنت الأشياء.",
        fileSizeKb: 12400,
        pageCount: 156,
        laserficheId: "LF-2022-RYD-009",
        year: 2022,
        createdAt: new Date("2022-06-01"),
      },
      {
        title: "Employee Training Program - AI and Machine Learning 2024",
        titleAr: "برنامج تدريب الموظفين - الذكاء الاصطناعي والتعلم الآلي 2024",
        department: "Ministry of Human Resources",
        departmentAr: "وزارة الموارد البشرية",
        classification: "Official",
        securityLevel: "Public",
        docType: "Program",
        docTypeAr: "برنامج",
        author: "Reem Al-Otaibi",
        authorAr: "ريم العتيبي",
        workflowStatus: "Active",
        tags: ["training", "AI", "machine learning", "employees", "تدريب", "ذكاء اصطناعي"],
        content: "Government-wide training program for AI and machine learning literacy targeting 12,000 civil servants. Program components include online self-paced modules, instructor-led workshops, and applied project labs. Duration: 6 months per cohort with 4 cohorts planned. Topics covered include AI fundamentals, data literacy, ethical AI, and practical government use cases. Certification awarded upon completion of all modules and project submission.",
        contentAr: "برنامج تدريبي حكومي شامل لمحو الأمية في مجال الذكاء الاصطناعي والتعلم الآلي يستهدف 12,000 موظف مدني.",
        fileSizeKb: 2100,
        pageCount: 35,
        laserficheId: "LF-2024-HR-010",
        year: 2024,
        createdAt: new Date("2024-01-08"),
      },
      {
        title: "Water Infrastructure Maintenance Contract - Eastern Province 2023",
        titleAr: "عقد صيانة البنية التحتية للمياه - المنطقة الشرقية 2023",
        department: "Ministry of Environment and Water",
        departmentAr: "وزارة البيئة والمياه",
        classification: "Official",
        securityLevel: "Internal",
        docType: "Contract",
        docTypeAr: "عقد",
        author: "Youssef Al-Dossary",
        authorAr: "يوسف الدوسري",
        workflowStatus: "Active",
        tags: ["water", "maintenance", "infrastructure", "Eastern Province", "مياه", "صيانة", "عقد"],
        content: "Long-term maintenance contract for water distribution networks and treatment facilities in the Eastern Province. Coverage includes 8 water treatment plants, 1,200 km of pipeline networks, and 340 pumping stations. Annual maintenance schedule with emergency response SLA of 4 hours for critical failures. Contract period: January 2023 to December 2025. Contractor: National Water Company (NWC).",
        contentAr: "عقد صيانة طويل الأمد لشبكات توزيع المياه ومرافق المعالجة في المنطقة الشرقية. يشمل النطاق 8 محطات لمعالجة المياه.",
        fileSizeKb: 4200,
        pageCount: 54,
        laserficheId: "LF-2023-ENV-011",
        year: 2023,
        createdAt: new Date("2023-01-03"),
      },
      {
        title: "National E-Government Portal - User Satisfaction Report Q2 2023",
        titleAr: "بوابة الحكومة الإلكترونية الوطنية - تقرير رضا المستخدمين الربع الثاني 2023",
        department: "Ministry of Digital Economy",
        departmentAr: "وزارة الاقتصاد الرقمي",
        classification: "Official",
        securityLevel: "Internal",
        docType: "Report",
        docTypeAr: "تقرير",
        author: "Lina Al-Kaabi",
        authorAr: "لينا الكعبي",
        workflowStatus: "Completed",
        tags: ["e-government", "portal", "satisfaction", "survey", "حكومة إلكترونية", "رضا"],
        content: "Comprehensive user satisfaction survey results for the national e-government portal for Q2 2023. Survey conducted with 45,000 respondents across all regions. Overall satisfaction score: 82%. Key strengths: ease of navigation (87%), service availability (91%). Areas for improvement: mobile app performance (67%), Arabic language support (71%), accessibility features (63%). Recommendations include dedicated mobile UX improvements and enhanced Arabic OCR for document uploads.",
        contentAr: "نتائج استطلاع رضا المستخدمين الشامل لبوابة الحكومة الإلكترونية الوطنية للربع الثاني من عام 2023. أُجري الاستطلاع مع 45,000 مشارك.",
        fileSizeKb: 3300,
        pageCount: 48,
        laserficheId: "LF-2023-DIG-012",
        year: 2023,
        createdAt: new Date("2023-08-30"),
      },
    ];

    for (const d of docs) {
      const id = randomUUID();
      this.documents.set(id, { ...d, id });
    }

    const logs = [
      { query: "معاملات تجديد عقود الصيانة لعام 2023", queryLanguage: "ar", username: "ahmed.rashidi", resultsCount: 4, searchType: "hybrid", department: "Ministry of Finance" },
      { query: "budget report infrastructure 2023", queryLanguage: "en", username: "sarah.mutairi", resultsCount: 3, searchType: "semantic", department: "Ministry of Public Works" },
      { query: "IT maintenance service agreement", queryLanguage: "en", username: "khalid.dosari", resultsCount: 5, searchType: "hybrid", department: "Ministry of Communications" },
      { query: "سياسة الموارد البشرية العمل عن بعد", queryLanguage: "ar", username: "fatima.harbi", resultsCount: 2, searchType: "semantic", department: "Ministry of Human Resources" },
      { query: "procurement tender office supplies", queryLanguage: "en", username: "omar.ghamdi", resultsCount: 6, searchType: "keyword", department: "Procurement Authority" },
      { query: "التحول الرقمي المرحلة الثانية", queryLanguage: "ar", username: "noura.qahtani", resultsCount: 3, searchType: "hybrid", department: "Ministry of Digital Economy" },
      { query: "cybersecurity assessment data center", queryLanguage: "en", username: "ibrahim.zahrani", resultsCount: 1, searchType: "semantic", department: "NCA" },
      { query: "عقد الصيانة البنية التحتية", queryLanguage: "ar", username: "mohammed.shehri", resultsCount: 7, searchType: "hybrid", department: "Riyadh Municipality" },
    ];

    const daysAgo = [0, 0, 1, 1, 2, 3, 5, 7];
    for (let i = 0; i < logs.length; i++) {
      const id = randomUUID();
      const searchedAt = new Date();
      searchedAt.setDate(searchedAt.getDate() - daysAgo[i]);
      searchedAt.setHours(Math.floor(Math.random() * 10) + 8);
      this.auditLogs.set(id, {
        id,
        query: logs[i].query,
        queryLanguage: logs[i].queryLanguage,
        userId: randomUUID(),
        username: logs[i].username,
        resultsCount: logs[i].resultsCount,
        searchType: logs[i].searchType,
        filters: null,
        searchedAt,
        ipAddress: `10.0.${Math.floor(Math.random() * 5)}.${Math.floor(Math.random() * 254) + 1}`,
        department: logs[i].department,
      });
    }
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
    const document: Document = { ...doc, id, createdAt: new Date() };
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

    const scored: SearchResult[] = docs.map(doc => {
      const keywordScore = computeKeywordScore(doc, queryTokens);
      const semanticScore = computeSemanticScore(doc, query);

      let finalScore = 0;
      if (searchType === "keyword") finalScore = keywordScore;
      else if (searchType === "semantic") finalScore = semanticScore;
      else finalScore = 0.45 * semanticScore + 0.45 * keywordScore + 0.1;

      const matchedTerms = queryTokens.filter(t => {
        const combined = `${doc.title} ${doc.titleAr || ""} ${doc.content} ${(doc.tags || []).join(" ")}`.toLowerCase();
        return combined.includes(t);
      });

      const snippet = extractSnippet(doc.content, queryTokens);
      const snippetAr = doc.contentAr ? extractSnippet(doc.contentAr, queryTokens) : undefined;

      return {
        document: doc,
        score: Math.min(finalScore + (matchedTerms.length > 0 ? 0.15 : 0), 1),
        scoreBreakdown: { semantic: semanticScore, keyword: keywordScore, metadata: 0.1 },
        snippet,
        snippetAr,
        matchedTerms,
      };
    });

    const filtered = scored.filter(r => r.score > 0.05).sort((a, b) => b.score - a.score);
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
    const auditLog: AuditLog = { ...log, id, searchedAt: new Date() };
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
      totalDepartments: Object.keys(docsByDepartment).length,
      avgResponseMs: 142,
      docsByType,
      docsByDepartment,
      searchesByDay: Object.entries(searchesByDayMap).map(([date, count]) => ({ date, count })),
      topSearches,
    };
  }
}

export const storage = new MemStorage();

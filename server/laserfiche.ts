import fs from "fs";
import path from "path";

export interface LaserficheConfig {
  serverUrl: string;
  repositoryId: string;
  username: string;
  password: string;
}

const SECRETS_DIR = path.join(process.cwd(), ".local-secrets");
const CONFIG_PATH = path.join(SECRETS_DIR, "laserfiche.json");

function loadSavedConfig(): LaserficheConfig | null {
  try {
    if (!fs.existsSync(CONFIG_PATH)) return null;
    const raw = fs.readFileSync(CONFIG_PATH, "utf-8");
    const data = JSON.parse(raw) as Partial<LaserficheConfig>;
    if (!data.serverUrl || !data.repositoryId || !data.username || !data.password) return null;
    return {
      serverUrl: data.serverUrl.replace(/\/$/, ""),
      repositoryId: data.repositoryId,
      username: data.username,
      password: data.password,
    };
  } catch {
    return null;
  }
}

export function saveLaserficheConfig(config: LaserficheConfig): void {
  if (!fs.existsSync(SECRETS_DIR)) {
    fs.mkdirSync(SECRETS_DIR, { recursive: true, mode: 0o700 });
  }
  const normalized: LaserficheConfig = {
    serverUrl: config.serverUrl.replace(/\/$/, ""),
    repositoryId: config.repositoryId,
    username: config.username,
    password: config.password,
  };
  fs.writeFileSync(CONFIG_PATH, JSON.stringify(normalized, null, 2), { mode: 0o600 });
  process.env.LF_SERVER_URL = normalized.serverUrl;
  process.env.LF_REPO_ID = normalized.repositoryId;
  process.env.LF_USERNAME = normalized.username;
  process.env.LF_PASSWORD = normalized.password;
}

export function clearLaserficheConfig(): void {
  if (fs.existsSync(CONFIG_PATH)) {
    fs.unlinkSync(CONFIG_PATH);
  }
  delete process.env.LF_SERVER_URL;
  delete process.env.LF_REPO_ID;
  delete process.env.LF_USERNAME;
  delete process.env.LF_PASSWORD;
}

(function hydrateEnvFromSavedConfig() {
  const saved = loadSavedConfig();
  if (!saved) return;
  if (!process.env.LF_SERVER_URL) process.env.LF_SERVER_URL = saved.serverUrl;
  if (!process.env.LF_REPO_ID) process.env.LF_REPO_ID = saved.repositoryId;
  if (!process.env.LF_USERNAME) process.env.LF_USERNAME = saved.username;
  if (!process.env.LF_PASSWORD) process.env.LF_PASSWORD = saved.password;
})();

export interface LFEntry {
  id: number;
  name: string;
  entryType: string;
  fullPath: string;
  creator: string;
  creationTime?: string;
  lastModifiedTime?: string;
  templateName?: string;
  fields?: Record<string, string | number | boolean | null>;
  tags?: string[];
  volumeName?: string;
  extension?: string;
  pageCount?: number;
  electronicDocumentSize?: number;
}

export interface LFSearchResult {
  entryId: number;
  name: string;
  fullPath: string;
  entryType: string;
  score?: number;
  contextHits?: string[];
}

export interface LFSearchResponse {
  entries?: LFEntry[];
  nextLink?: string;
  count?: number;
}

export function getLaserficheConfig(): LaserficheConfig | null {
  const serverUrl = process.env.LF_SERVER_URL;
  const repositoryId = process.env.LF_REPO_ID;
  const username = process.env.LF_USERNAME;
  const password = process.env.LF_PASSWORD;

  if (!serverUrl || !repositoryId || !username || !password) {
    return null;
  }

  return { serverUrl: serverUrl.replace(/\/$/, ""), repositoryId, username, password };
}

export async function getLaserficheToken(config: LaserficheConfig): Promise<string> {
  const params = new URLSearchParams();
  params.append("grant_type", "password");
  params.append("username", config.username);
  params.append("password", config.password);

  const tokenUrl = `${config.serverUrl}/v2/Repositories/${config.repositoryId}/Token`;
  const res = await fetch(tokenUrl, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded", Accept: "application/json" },
    body: params.toString(),
  });

  if (res.status === 404) {
    throw new Error(`No token endpoint at ${tokenUrl}`);
  }

  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`Laserfiche authentication failed: ${res.status} ${text.slice(0, 200)}`);
  }

  const ct = res.headers.get("content-type") || "";
  const bodyText = await res.text();
  if (!/json/i.test(ct) || /^\s*</.test(bodyText)) {
    throw new Error(
      `Server replied with non-JSON content (likely an HTML login page). ` +
      `Your Laserfiche server may require Windows Authentication (NTLM) or be sitting behind an SSO/reverse proxy that doesn't allow basic password auth.`
    );
  }

  let data: { access_token?: string };
  try {
    data = JSON.parse(bodyText);
  } catch {
    throw new Error(`Server returned invalid JSON from token endpoint: ${bodyText.slice(0, 200)}`);
  }
  if (!data.access_token) {
    throw new Error("No access token returned from Laserfiche");
  }
  return data.access_token;
}

export interface LaserficheRepoInfo {
  repoName: string;
  repoId?: string;
  webClientUrl?: string;
}

export interface LaserficheDiscoverResult {
  ok: boolean;
  apiVersion?: "v1" | "v2";
  serverUrl: string;
  repos: LaserficheRepoInfo[];
  message: string;
  status?: number;
}

export function buildLaserficheEntriesUrl(config: LaserficheConfig, version: "v1" | "v2") {
  return `${config.serverUrl}/${version}/Repositories/${config.repositoryId}/Entries`;
}

export async function discoverLaserficheRepos(serverUrl: string): Promise<LaserficheDiscoverResult> {
  const base = serverUrl.replace(/\/$/, "");
  const url = `${base}/v1/Repositories`;
  try {
    const res = await fetch(url, { method: "GET", headers: { Accept: "application/json" } });
    const ct = res.headers.get("content-type") || "";
    const bodyText = await res.text();

    if (res.status === 401) {
      return {
        ok: false,
        serverUrl: base,
        repos: [],
        status: 401,
        apiVersion: "v1",
        message: `Server at ${url} requires authentication just to list repositories. This usually means Windows Authentication (NTLM) is enforced.`,
      };
    }

    if (res.status === 404) {
      return {
        ok: false,
        serverUrl: base,
        repos: [],
        status: 404,
        apiVersion: "v1",
        message: `No v1 repositories endpoint at ${url}`,
      };
    }

    if (!res.ok) {
      return {
        ok: false,
        serverUrl: base,
        repos: [],
        status: res.status,
        apiVersion: "v1",
        message: `Server responded ${res.status} at ${url}: ${bodyText.slice(0, 200)}`,
      };
    }

    if (!/json/i.test(ct) || /^\s*</.test(bodyText)) {
      return {
        ok: false,
        serverUrl: base,
        repos: [],
        status: res.status,
        apiVersion: "v1",
        message: `Server at ${url} returned HTML instead of JSON. Confirm LFRepositoryAPI is installed here.`,
      };
    }

    let data: any = null;
    try {
      data = JSON.parse(bodyText);
    } catch {
      return {
        ok: false,
        serverUrl: base,
        repos: [],
        status: res.status,
        apiVersion: "v1",
        message: `Server returned invalid JSON: ${bodyText.slice(0, 200)}`,
      };
    }

    const list: any[] = Array.isArray(data) ? data : (data?.value || data?.Repositories || []);
    const repos: LaserficheRepoInfo[] = list.map((r) => ({
      repoName: r.repoName || r.RepoName || r.name || r.Name || r.repositoryName || r.RepositoryName || "",
      repoId: r.repoId || r.RepoId || r.id || r.Id || undefined,
      webClientUrl: r.webClientUrl || r.WebClientUrl || undefined,
    })).filter((r) => r.repoName);

    return {
      ok: repos.length > 0,
      apiVersion: "v1",
      serverUrl: base,
      repos,
      message: repos.length > 0
        ? `Found ${repos.length} repository(ies) on v1 API`
        : `Connected to v1 API but no repositories returned.`,
    };
  } catch (err: any) {
    return {
      ok: false,
      serverUrl: base,
      repos: [],
      apiVersion: "v1",
      message: `Cannot reach server: ${err?.message || String(err)}`,
    };
  }
}

export interface LaserficheTestResult {
  ok: boolean;
  status?: number;
  message: string;
  serverUrl: string;
  repositoryId: string;
  username: string;
}

export async function testLaserficheConnection(config: LaserficheConfig): Promise<LaserficheTestResult> {
  const base = {
    serverUrl: config.serverUrl,
    repositoryId: config.repositoryId,
    username: config.username,
  };

  let token: string;
  try {
    token = await getLaserficheToken(config);
  } catch (err: any) {
    const msg = String(err?.message || err);
    if (msg.includes("401")) {
      return { ok: false, status: 401, message: "Invalid credentials (401 Unauthorized)", ...base };
    }
    return { ok: false, message: `Authentication failed: ${msg}`, ...base };
  }
  return {
    ok: true,
    status: 200,
    message: "Connected successfully to Laserfiche (token authentication succeeded)",
    ...base,
  };
}



export interface LFSearchTokenResponse {
  searchToken?: string;
  token?: string;
  id?: string;
}

export async function laserficheRepositorySearch(
  config: LaserficheConfig,
  token: string,
  searchCommand: string,
  maxResults = 50,
): Promise<LFEntry[]> {
  const createUrl = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Searches`;
  const createRes = await fetch(createUrl, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ searchCommand }),
  });

  if (!createRes.ok) {
    const text = await createRes.text().catch(() => "");
    throw new Error(`Failed creating Laserfiche search token: ${createRes.status} ${text.slice(0, 200)}`);
  }

  const createBody = await safeJson<LFSearchTokenResponse>(createRes, "create repository search");
  const searchToken = createBody.searchToken || createBody.token || createBody.id;
  if (!searchToken) {
    throw new Error("Laserfiche did not return a search token.");
  }

  const searchUrl = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Searches/${encodeURIComponent(searchToken)}?$top=${maxResults}`;
  const searchRes = await fetch(searchUrl, {
    method: "GET",
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
  });

  if (!searchRes.ok) {
    const text = await searchRes.text().catch(() => "");
    throw new Error(`Failed executing Laserfiche search: ${searchRes.status} ${text.slice(0, 200)}`);
  }

  const payload = await safeJson<{ value?: LFEntry[]; entries?: LFEntry[] }>(searchRes, "execute repository search");
  return payload.value || payload.entries || [];
}

export async function laserficheSimpleSearch(
  config: LaserficheConfig,
  token: string,
  searchCommand: string,
  maxResults = 100
): Promise<LFEntry[]> {
  const url = `${config.serverUrl}/v2/Repositories/${config.repositoryId}/SimpleSearches`;

  const res = await fetch(url, {
    method: "POST",
    headers: {
      "Authorization": `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ searchCommand }),
  });

  if (!res.ok) {
    const text = await res.text();
    throw new Error(`Laserfiche search failed: ${res.status} ${text}`);
  }

  const data = await res.json() as LFSearchResponse;
  return data.entries || [];
}

/** Parse JSON from a Response, throwing a descriptive error if the body is HTML */
async function safeJson<T>(res: Response, context: string): Promise<T> {
  const ct = res.headers.get("content-type") || "";
  const body = await res.text();
  if (!/json/i.test(ct) || /^\s*</.test(body)) {
    throw new Error(
      `Laserfiche server returned an HTML page instead of JSON for ${context}. ` +
      `This usually means the server is behind an SSO/proxy login wall, or the URL is wrong. ` +
      `Server: ${res.url} — Status: ${res.status}`
    );
  }
  try {
    return JSON.parse(body) as T;
  } catch {
    throw new Error(`Laserfiche API returned invalid JSON for ${context}: ${body.slice(0, 200)}`);
  }
}

export async function laserficheGetEntry(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<LFEntry> {
  const url = `${config.serverUrl}/v2/Repositories/${config.repositoryId}/Entries/${entryId}`;

  const res = await fetch(url, {
    method: "GET",
    headers: { "Authorization": `Bearer ${token}`, Accept: "application/json" },
  });

  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`Failed to get Laserfiche entry ${entryId}: ${res.status} ${text.slice(0, 200)}`);
  }

  return await safeJson<LFEntry>(res, `entry ${entryId}`);
}

export async function laserficheGetEntryFields(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<Record<string, string>> {
  const url = `${config.serverUrl}/v2/Repositories/${config.repositoryId}/Entries/${entryId}/Fields`;

  const res = await fetch(url, {
    method: "GET",
    headers: { "Authorization": `Bearer ${token}` },
  });

  if (!res.ok) return {};

  const data = await res.json() as { value?: Array<{ fieldName: string; values: string[] }> };
  const fields: Record<string, string> = {};
  for (const f of data.value || []) {
    fields[f.fieldName] = f.values?.join(", ") || "";
  }
  return fields;
}


export interface LFRawFieldValue {
  value: string | null;
  position: number;
}

export interface LFRawField {
  fieldId: number;
  fieldName: string;
  fieldType: string;
  isMultiValue: boolean;
  isRequired: boolean;
  hasMoreValues: boolean;
  groupId: number;
  values: LFRawFieldValue[];
}

export async function laserficheGetEntryFieldsRaw(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<LFRawField[]> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}/fields?formatValue=false`;

  const res = await fetch(url, {
    method: "GET",
    headers: { "Authorization": `Bearer ${token}` },
  });

  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`Failed to get Laserfiche fields for entry ${entryId}: ${res.status} ${text.slice(0, 200)}`);
  }

  const data = await safeJson<{ value?: LFRawField[] }>(res, `entry ${entryId} fields`);
  return data.value || [];
}


export interface LFFieldDefinition {
  id: number;
  name: string;
  fieldType?: string;
  isRequired?: boolean;
}

export async function laserficheGetFieldDefinitions(
  config: LaserficheConfig,
  token: string
): Promise<LFFieldDefinition[]> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/FieldDefinitions`;

  const res = await fetch(url, {
    method: "GET",
    headers: { "Authorization": `Bearer ${token}`, Accept: "application/json" },
  });

  if (!res.ok) return [];

  const data = await res.json() as { value?: Array<{ id: number; name: string; fieldType?: string; isRequired?: boolean }> };
  return (data.value || []).map((f) => ({
    id: f.id,
    name: f.name,
    fieldType: f.fieldType,
    isRequired: f.isRequired,
  }));
}

export async function laserficheGetFolderChildren(
  config: LaserficheConfig,
  token: string,
  folderEntryId: number
): Promise<LFEntry[]> {
  const candidateUrls = [
    `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${folderEntryId}/Laserfiche.Repository.Folder/children`,
    `${config.serverUrl}/v2/Repositories/${config.repositoryId}/Entries/${folderEntryId}/Folder/Children`,
  ];

  let lastError = "";
  for (const url of candidateUrls) {
    const res = await fetch(url, {
      method: "GET",
      headers: { "Authorization": `Bearer ${token}`, Accept: "application/json" },
    });
    if (!res.ok) {
      lastError = `${res.status} ${await res.text()}`;
      continue;
    }
    const data = await safeJson<{ value?: LFEntry[] } | LFEntry[]>(res, `folder ${folderEntryId} children`);
    return Array.isArray(data) ? data : data.value || [];
  }

  throw new Error(`Failed to list folder children: ${lastError}`);
}

export async function laserficheListEntries(
  config: LaserficheConfig,
  token: string,
  folderId = 1,
  limit = 50
): Promise<LFEntry[]> {
  const urls = [
    `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${folderId}/Laserfiche.Repository.Folder/children?$top=${limit}`,
    `${config.serverUrl}/v2/Repositories/${config.repositoryId}/Entries/${folderId}/Folder/Children?$top=${limit}&$select=id,name,entryType,creator,creationTime,lastModifiedTime,extension,pageCount,electronicDocumentSize`,
  ];
  let lastError = "";
  for (const url of urls) {
    const res = await fetch(url, {
      method: "GET",
      headers: { "Authorization": `Bearer ${token}` },
    });
    if (!res.ok) {
      lastError = `${res.status} ${await res.text()}`;
      continue;
    }
    const data = await safeJson<{ value?: LFEntry[] }>(res, `folder ${folderId} list`);
    return data.value || [];
  }
  throw new Error(`Failed to list Laserfiche entries: ${lastError}`);
}

export function naturalLanguageToLFSearchCommand(query: string): {
  command: string;
  explanation: string;
  extractedTerms: string[];
} {
  const q = query.trim();
  const qLower = q.toLowerCase();
  const extractedTerms: string[] = [];
  const clauses: string[] = [];

  const yearMatch = q.match(/\b(20\d{2})\b/);
  if (yearMatch) {
    const year = yearMatch[1];
    clauses.push(`{LF:Modified>="${year}-01-01"}`);
    clauses.push(`{LF:Modified<="${year}-12-31"}`);
    extractedTerms.push(year);
  }

  const intentMap: Array<[RegExp, string]> = [
    [/(contract|contracts|عقد|العقود)/i, 'Contract Type'],
    [/(invoice|invoices|فاتورة|فواتير)/i, 'Document Type'],
    [/(hr|human resources|الموارد البشرية)/i, 'Department'],
    [/(maintenance|الصيانة)/i, 'Subject'],
    [/(national id|رقم الهوية)/i, 'National ID'],
    [/(employee|الموظف|اسم)/i, 'Employee Name'],
  ];

  for (const [re, field] of intentMap) {
    if (re.test(q)) extractedTerms.push(field);
  }

  const nameMatch = q.match(/(?:with|name|named|اسم|باسم)\s+([؀-ۿA-Za-z0-9_-]+)/i);
  if (nameMatch) {
    const name = nameMatch[1];
    clauses.push(`{LF:LOOKIN="FIELD:Employee Name"}="${name}"`);
    clauses.push(`{LF:Name~="${name}"}`);
    extractedTerms.push(name);
  }

  const keywords = q
    .replace(/[^؀-ۿ\w\s-]/g, ' ')
    .split(/\s+/)
    .map((t) => t.trim())
    .filter((t) => t.length > 2 && !/^20\d{2}$/.test(t) && !['all','documents','document','المعاملات','الوثائق','جميع','اعطني','from','about','related','containing','contains'].includes(t.toLowerCase()));

  if (keywords.length) {
    clauses.push(`{LF:Basic~="${keywords.slice(0, 5).join(' ')}"}`);
    extractedTerms.push(...keywords);
  }

  if (/(contract|العقود)/i.test(qLower)) {
    clauses.push(`{LF:LOOKIN="FIELD:Contract Type"}="*"`);
  }
  if (/(hr|human resources|الموارد البشرية)/i.test(qLower)) {
    clauses.push(`{LF:LOOKIN="FIELD:Department"}="*HR*" | {LF:LOOKIN="FIELD:Department"}="*الموارد البشرية*"`);
  }

  const command = clauses.length ? clauses.join(' & ') : `{LF:Basic~="${q}"}`;
  return {
    command,
    explanation: 'Generated metadata-aware Laserfiche search command using year, keyword, and field intent detection.',
    extractedTerms: [...new Set(extractedTerms)],
  };
}

export interface LFTag {
  id: number;
  name: string;
  description?: string;
}


export async function laserficheGetEntryTags(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<string[]> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}/tags`;
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
  });
  if (!res.ok) return [];
  const data = await res.json() as { value?: Array<{ name?: string; tagName?: string }> };
  return (data.value || []).map((t) => t.name || t.tagName || "").filter(Boolean);
}

export interface LFPage {
  pageNumber: number;
  width?: number;
  height?: number;
}

export async function laserficheGetEntryPages(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<LFPage[]> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}/pages`;
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
  });
  if (!res.ok) return [];
  const data = await res.json() as { value?: LFPage[] };
  return (data.value || []).map((p, i) => ({
    pageNumber: p.pageNumber ?? i + 1,
    width: p.width,
    height: p.height,
  }));
}

export async function laserficheDeleteEntry(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<void> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}`;
  const res = await fetch(url, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`Delete failed (${res.status}): ${text || "Unknown error"}`);
  }
}

export async function laserficheGetPageImage(
  config: LaserficheConfig,
  token: string,
  entryId: number,
  pageNumber: number
): Promise<{ buffer: Buffer; contentType: string } | null> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}/pages/${pageNumber}/imageComponents/fullRes`;
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}`, Accept: "image/*" },
  });
  if (!res.ok) return null;
  const contentType = res.headers.get("content-type") || "image/png";
  const arrayBuffer = await res.arrayBuffer();
  return { buffer: Buffer.from(arrayBuffer), contentType };
}

export async function laserficheGetEdoc(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<{ buffer: Buffer; contentType: string; fileName: string } | null> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}/Laserfiche.Repository.Document/edoc`;
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}`, Accept: "*/*" },
  });
  if (!res.ok) return null;
  const contentType = res.headers.get("content-type") || "application/octet-stream";
  const disposition = res.headers.get("content-disposition") || "";
  const fileNameMatch = disposition.match(/filename[^;=\n]*=["']?([^"';\n]+)["']?/i);
  const fileName = fileNameMatch?.[1]?.trim() || `document-${entryId}`;
  const arrayBuffer = await res.arrayBuffer();
  return { buffer: Buffer.from(arrayBuffer), contentType, fileName };
}

export interface LFTemplateDefinition {
  id: number;
  name: string;
  description?: string;
}

export async function laserficheGetTemplateDefinitions(
  config: LaserficheConfig,
  token: string
): Promise<LFTemplateDefinition[]> {
  const urls = [
    `${config.serverUrl}/v1/Repositories/${config.repositoryId}/TemplateDefinitions`,
    `${config.serverUrl}/v2/Repositories/${config.repositoryId}/TemplateDefinitions`,
  ];
  for (const url of urls) {
    try {
      const res = await fetch(url, {
        headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
      });
      if (!res.ok) continue;
      const data = await res.json() as { value?: Array<{ id?: number; name?: string; description?: string }> };
      const defs = (data.value || [])
        .map((t) => ({ id: t.id ?? 0, name: (t.name ?? "").trim(), description: t.description }))
        .filter((t) => t.name);
      if (defs.length > 0) return defs;
    } catch {
      continue;
    }
  }
  return [];
}

export async function laserficheCountByTemplate(
  config: LaserficheConfig,
  token: string,
  templateName: string
): Promise<number> {
  try {
    const createUrl = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Searches`;
    const createRes = await fetch(createUrl, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ searchCommand: `{Template:"${templateName}"}` }),
    });
    if (!createRes.ok) return 0;
    const createBody = await safeJson<LFSearchTokenResponse>(createRes, "count by template");
    const searchToken = createBody.searchToken || createBody.token || createBody.id;
    if (!searchToken) return 0;

    const countUrl = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Searches/${encodeURIComponent(searchToken)}?$top=1&$count=true`;
    const countRes = await fetch(countUrl, {
      headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
    });
    if (!countRes.ok) return 0;
    const data = await safeJson<{ "@odata.count"?: number; count?: number; value?: unknown[] }>(countRes, "count result");
    return data["@odata.count"] ?? data.count ?? (Array.isArray(data.value) ? data.value.length : 0);
  } catch {
    return 0;
  }
}

/**
 * Search for a specific value across ALL Laserfiche fields.
 * Runs parallel field-targeted searches on every discovered field.
 */
export async function laserficheFieldValueSearch(
  config: LaserficheConfig,
  token: string,
  value: string,
  fieldNames: string[],
  maxPerField = 10,
): Promise<LFEntry[]> {
  const results: LFEntry[] = [];
  const seen = new Set<number>();
  const safeValue = value.replace(/"/g, '\\"');

  for (const fieldName of fieldNames.slice(0, 15)) {
    try {
      const cmd = `{LF:LOOKIN="FIELD:${fieldName}"}="${safeValue}"`;
      const entries = await laserficheRepositorySearch(config, token, cmd, maxPerField);
      for (const e of entries) {
        if (!seen.has(e.id)) {
          seen.add(e.id);
          results.push(e);
        }
      }
    } catch {
      // Field may not exist or search syntax invalid - skip
    }
  }
  return results;
}

export type { LaserficheConfig as LFConfig };

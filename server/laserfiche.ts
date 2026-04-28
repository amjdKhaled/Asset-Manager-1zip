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

  let lastError = "";
  let lastStatus = 0;

  for (const version of ["v1", "v2"] as const) {
    const tokenUrl = `${config.serverUrl}/${version}/Repositories/${config.repositoryId}/Token`;
    const res = await fetch(tokenUrl, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded", Accept: "application/json" },
      body: params.toString(),
    });

    if (res.status === 404) {
      lastError = `No token endpoint at ${tokenUrl}`;
      lastStatus = 404;
      continue;
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

  throw new Error(
    `Laserfiche authentication failed: ${lastStatus} ${lastError}. ` +
    `Verify the repository name (LF_REPO_ID) — use Discover to list available repositories.`
  );
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

export async function discoverLaserficheRepos(serverUrl: string): Promise<LaserficheDiscoverResult> {
  const base = serverUrl.replace(/\/$/, "");
  const tried: string[] = [];

  for (const version of ["v1", "v2"] as const) {
    const url = `${base}/${version}/Repositories`;
    tried.push(url);
    try {
      const res = await fetch(url, { method: "GET", headers: { Accept: "application/json" } });
      if (res.status === 404) continue;

      const ct = res.headers.get("content-type") || "";
      const bodyText = await res.text();

      if (res.status === 401) {
        return {
          ok: false,
          serverUrl: base,
          repos: [],
          status: 401,
          apiVersion: version,
          message: `Server at ${url} requires authentication just to list repositories. ` +
            `This usually means Windows Authentication (NTLM) is enforced — basic password auth from a remote app won't work.`,
        };
      }

      if (!res.ok) {
        return {
          ok: false,
          serverUrl: base,
          repos: [],
          status: res.status,
          message: `Server responded ${res.status} at ${url}: ${bodyText.slice(0, 200)}`,
        };
      }

      if (!/json/i.test(ct) || /^\s*</.test(bodyText)) {
        return {
          ok: false,
          serverUrl: base,
          repos: [],
          status: res.status,
          apiVersion: version,
          message:
            `Server at ${url} returned an HTML page instead of JSON. ` +
            `This is typically an IIS or SSO login page — the URL is reachable but the API itself isn't responding. ` +
            `Confirm LFRepositoryAPI is installed at this path and that anonymous (or token) auth is enabled.`,
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
          apiVersion: version,
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
        apiVersion: version,
        serverUrl: base,
        repos,
        message: repos.length > 0
          ? `Found ${repos.length} repository(ies) on ${version} API`
          : `Connected to ${version} API but no repositories returned. The server may hide repos until you authenticate.`,
      };
    } catch (err: any) {
      return {
        ok: false,
        serverUrl: base,
        repos: [],
        message: `Cannot reach server: ${err?.message || String(err)}`,
      };
    }
  }

  return {
    ok: false,
    serverUrl: base,
    repos: [],
    status: 404,
    message: `No /Repositories endpoint found on this server. Tried: ${tried.join(", ")}`,
  };
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

  const versions = ["v1", "v2"] as const;
  let lastStatus = 0;
  let lastBody = "";
  for (const version of versions) {
    const url = `${config.serverUrl}/${version}/Repositories/${config.repositoryId}/Entries`;
    try {
      const res = await fetch(url, {
        method: "GET",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.status === 404) {
        lastStatus = 404;
        lastBody = `No entries endpoint at ${url}`;
        continue;
      }
      if (res.status === 401) {
        return { ok: false, status: 401, message: "Invalid credentials (401 Unauthorized)", ...base };
      }
      if (!res.ok) {
        const text = await res.text().catch(() => "");
        return { ok: false, status: res.status, message: `Server responded ${res.status} ${text.slice(0, 200)}`, ...base };
      }
      return { ok: true, status: res.status, message: `Connected successfully to Laserfiche (${version} API)`, ...base };
    } catch (err: any) {
      return { ok: false, message: `Connection error: ${err?.message || String(err)}`, ...base };
    }
  }
  return { ok: false, status: lastStatus, message: lastBody || "Entries endpoint not found on v1 or v2", ...base };
}

export async function laserficheSimpleSearch(
  config: LaserficheConfig,
  token: string,
  searchCommand: string,
  maxResults = 100
): Promise<LFEntry[]> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/SimpleSearches`;

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

export async function laserficheGetEntry(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<LFEntry> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}`;

  const res = await fetch(url, {
    method: "GET",
    headers: { "Authorization": `Bearer ${token}` },
  });

  if (!res.ok) {
    throw new Error(`Failed to get Laserfiche entry ${entryId}: ${res.status}`);
  }

  return await res.json() as LFEntry;
}

export async function laserficheGetEntryFields(
  config: LaserficheConfig,
  token: string,
  entryId: number
): Promise<Record<string, string>> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${entryId}/Fields`;

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

export async function laserficheListEntries(
  config: LaserficheConfig,
  token: string,
  folderId = 1,
  limit = 50
): Promise<LFEntry[]> {
  const url = `${config.serverUrl}/v1/Repositories/${config.repositoryId}/Entries/${folderId}/Folder/Children?$top=${limit}&$select=id,name,entryType,creator,creationTime,lastModifiedTime,extension,pageCount,electronicDocumentSize`;

  const res = await fetch(url, {
    method: "GET",
    headers: { "Authorization": `Bearer ${token}` },
  });

  if (!res.ok) {
    const text = await res.text();
    throw new Error(`Failed to list Laserfiche entries: ${res.status} ${text}`);
  }

  const data = await res.json() as { value?: LFEntry[] };
  return data.value || [];
}

export function naturalLanguageToLFSearchCommand(query: string): {
  command: string;
  explanation: string;
  extractedTerms: string[];
} {
  const q = query.trim();

  const extractedTerms: string[] = [];
  let command = "";
  let explanation = "";

  const namePatterns = [
    /اسم\s+(\S+)/g,
    /باسم\s+(\S+)/g,
    /يحتوي.*?اسم\s+(\S+)/g,
    /contains?\s+name\s+(\S+)/gi,
    /named?\s+(\S+)/gi,
    /author[:\s]+(\S+)/gi,
    /كاتب\s+(\S+)/g,
    /منشئ\s+(\S+)/g,
  ];

  const nameMatches: string[] = [];
  for (const pattern of namePatterns) {
    let m;
    while ((m = pattern.exec(q)) !== null) {
      nameMatches.push(m[1]);
    }
  }

  const datePatterns = [
    /(\d{4})/g,
    /عام\s+(\d{4})/g,
    /سنة\s+(\d{4})/g,
    /year\s+(\d{4})/gi,
  ];

  const years: string[] = [];
  for (const pattern of datePatterns) {
    let m;
    while ((m = pattern.exec(q)) !== null) {
      if (parseInt(m[1]) >= 2000 && parseInt(m[1]) <= 2030) {
        years.push(m[1]);
      }
    }
  }

  const stopwordsAr = ["عطني", "اعطني", "أعطني", "جميع", "كل", "اللتي", "التي", "الذي", "الذين",
    "المعاملات", "الوثائق", "المستندات", "الملفات", "في", "من", "على", "عن", "مع", "تحتوي",
    "تحتوى", "يحتوي", "التي", "بتاريخ", "خلال", "حتى", "بعد", "قبل", "اريد", "أريد", "أحتاج", "احتاج"];
  const stopwordsEn = ["give", "me", "all", "the", "documents", "files", "transactions", "that",
    "contain", "contains", "with", "in", "and", "or", "show", "find", "search", "get"];

  const cleanedTokens = q.replace(/[^\u0600-\u06FFa-zA-Z0-9\s]/g, " ")
    .split(/\s+/)
    .filter(t => t.length > 2)
    .filter(t => !stopwordsAr.includes(t) && !stopwordsEn.includes(t.toLowerCase()))
    .filter(t => !years.includes(t));

  extractedTerms.push(...cleanedTokens, ...years);

  const parts: string[] = [];

  if (nameMatches.length > 0) {
    for (const name of nameMatches) {
      parts.push(`{LF:Basic~="${name}"}`);
      if (!extractedTerms.includes(name)) extractedTerms.push(name);
    }
    explanation = `Full-text search for name: ${nameMatches.join(", ")}`;
  } else if (cleanedTokens.length > 0) {
    const mainTerms = cleanedTokens.slice(0, 3);
    if (mainTerms.length === 1) {
      parts.push(`{LF:Basic~="${mainTerms[0]}"}`);
    } else {
      parts.push(`{LF:Basic~="${mainTerms.join(" ")}"}`);
    }
    explanation = `Full-text search for: ${mainTerms.join(", ")}`;
  }

  if (years.length > 0) {
    const year = years[0];
    parts.push(`{LF:Modified>="${year}-01-01"}`);
    parts.push(`{LF:Modified<="${year}-12-31"}`);
    explanation += (explanation ? " | " : "") + `Year filter: ${year}`;
  }

  if (parts.length === 0) {
    const fallbackTerms = q.replace(/[^\u0600-\u06FFa-zA-Z0-9\s]/g, " ").trim().split(/\s+/).slice(0, 3);
    command = `{LF:Basic~="${fallbackTerms.join(" ")}"}`;
    explanation = `Full-text search for: ${fallbackTerms.join(", ")}`;
    extractedTerms.push(...fallbackTerms);
  } else {
    command = parts.join(" & ");
  }

  return { command, explanation, extractedTerms: [...new Set(extractedTerms)] };
}

export type { LaserficheConfig as LFConfig };

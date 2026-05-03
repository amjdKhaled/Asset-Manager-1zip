import { useParams, useLocation } from "wouter";
import { ArrowLeft, Download, FileText, Image as ImageIcon, FileDown } from "lucide-react";

const DOWNLOAD_ONLY = new Set(["doc", "docx", "xls", "xlsx", "ppt", "pptx", "zip", "rar", "7z"]);

export default function DocumentViewerPage() {
  const params = useParams<{ entryId: string }>();
  const [, navigate] = useLocation();

  const entryId = Number(params.entryId);
  const contentUrl = `/api/laserfiche/entries/${entryId}/content`;

  // Read display name and extension from query string if provided
  const search = new URLSearchParams(window.location.search);
  const name = search.get("name") || `Document ${entryId}`;
  const ext = (search.get("ext") || "").toLowerCase().replace(/^\./, "");
  const canRenderInline = !DOWNLOAD_ONLY.has(ext);

  const fileIcon = ext === "pdf"
    ? <FileText className="w-4 h-4 text-white/70" />
    : ["jpg", "jpeg", "png", "gif", "tiff", "tif", "bmp", "webp"].includes(ext)
      ? <ImageIcon className="w-4 h-4 text-white/70" />
      : <FileDown className="w-4 h-4 text-white/70" />;

  if (!Number.isFinite(entryId) || entryId <= 0) {
    return (
      <div className="w-screen h-screen bg-black flex items-center justify-center text-white text-sm">
        Invalid document ID.
      </div>
    );
  }

  return (
    <div
      style={{ width: "100vw", height: "100vh", margin: 0, padding: 0, overflow: "hidden", background: "#111" }}
      data-testid="fullscreen-viewer"
    >
      {/* Floating top bar */}
      <div
        style={{
          position: "absolute",
          top: 0,
          left: 0,
          right: 0,
          zIndex: 10,
          display: "flex",
          alignItems: "center",
          gap: "10px",
          padding: "8px 14px",
          background: "rgba(0,0,0,0.65)",
          backdropFilter: "blur(6px)",
          borderBottom: "1px solid rgba(255,255,255,0.08)",
        }}
      >
        <button
          onClick={() => navigate(-1 as any)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: "6px",
            color: "rgba(255,255,255,0.8)",
            background: "none",
            border: "none",
            cursor: "pointer",
            fontSize: "12px",
            padding: "4px 8px",
            borderRadius: "4px",
          }}
          onMouseOver={e => (e.currentTarget.style.background = "rgba(255,255,255,0.1)")}
          onMouseOut={e => (e.currentTarget.style.background = "none")}
          data-testid="viewer-back"
        >
          <ArrowLeft style={{ width: 14, height: 14 }} />
          Back
        </button>

        <div style={{ width: 1, height: 16, background: "rgba(255,255,255,0.2)" }} />

        <div style={{ display: "flex", alignItems: "center", gap: "8px", flex: 1, minWidth: 0 }}>
          {fileIcon}
          <span
            style={{
              fontSize: "12px",
              color: "rgba(255,255,255,0.9)",
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap",
            }}
            data-testid="viewer-filename"
          >
            {name}
          </span>
          {ext && (
            <span
              style={{
                fontSize: "10px",
                padding: "2px 6px",
                borderRadius: "3px",
                background: "rgba(255,255,255,0.12)",
                color: "rgba(255,255,255,0.7)",
                textTransform: "uppercase",
                flexShrink: 0,
              }}
            >
              {ext}
            </span>
          )}
        </div>

        <a
          href={contentUrl}
          download={name}
          style={{
            display: "flex",
            alignItems: "center",
            gap: "5px",
            color: "rgba(255,255,255,0.7)",
            textDecoration: "none",
            fontSize: "12px",
            padding: "4px 8px",
            borderRadius: "4px",
            border: "1px solid rgba(255,255,255,0.15)",
            flexShrink: 0,
          }}
          onMouseOver={e => (e.currentTarget.style.background = "rgba(255,255,255,0.1)")}
          onMouseOut={e => (e.currentTarget.style.background = "none")}
          data-testid="viewer-download"
        >
          <Download style={{ width: 13, height: 13 }} />
          Download
        </a>
      </div>

      {/* Document area */}
      {canRenderInline ? (
        <iframe
          key={entryId}
          src={contentUrl}
          title={name}
          style={{ width: "100%", height: "100%", border: "none", display: "block" }}
          data-testid="viewer-iframe"
        />
      ) : (
        <div
          style={{
            width: "100%",
            height: "100%",
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            gap: 16,
            color: "rgba(255,255,255,0.6)",
            textAlign: "center",
            padding: 32,
          }}
        >
          <FileDown style={{ width: 48, height: 48, opacity: 0.3 }} />
          <div>
            <p style={{ fontSize: 14, color: "rgba(255,255,255,0.8)", marginBottom: 6 }}>Preview not available</p>
            <p style={{ fontSize: 12 }}>
              {ext ? `".${ext}" files` : "This file type"} cannot be displayed in the browser.
            </p>
          </div>
          <a
            href={contentUrl}
            download={name}
            style={{
              padding: "8px 16px",
              background: "rgba(255,255,255,0.1)",
              border: "1px solid rgba(255,255,255,0.2)",
              borderRadius: 6,
              color: "rgba(255,255,255,0.9)",
              textDecoration: "none",
              fontSize: 13,
              display: "flex",
              alignItems: "center",
              gap: 6,
            }}
            data-testid="viewer-download-fallback"
          >
            <Download style={{ width: 14, height: 14 }} />
            Download file
          </a>
        </div>
      )}
    </div>
  );
}

# Arabic PDF Extraction API (Offline)

## Features
- Upload PDF
- Hybrid extraction: embedded text first, OCR fallback per page
- Arabic + English OCR (`ara+eng`)
- OCR confidence per page
- Metadata extraction
- Page-by-page and merged output JSON

## Endpoints
- `POST /api/pdf/upload` (multipart/form-data: `file`)
- `POST /api/pdf/extract` JSON body: `{ "uploadId": "..." }`

## Example extraction response
```json
{
  "success": true,
  "combinedText": "...",
  "metadata": {
    "Title": "Gov Letter",
    "Author": "Records Dept"
  },
  "pages": [
    { "pageNumber": 1, "usedOcr": false, "text": "...", "ocrConfidence": null },
    { "pageNumber": 2, "usedOcr": true, "text": "...", "ocrConfidence": 0.86 }
  ]
}
```

## Setup
1. Install .NET 8 SDK
2. Put `ara.traineddata` and `eng.traineddata` inside `tessdata/`
3. Run: `dotnet restore && dotnet run`

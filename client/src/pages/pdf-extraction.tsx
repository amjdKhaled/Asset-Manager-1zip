import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export default function PdfExtractionPage() {
  return (
    <div className="h-full overflow-auto p-6">
      <div className="max-w-4xl space-y-4">
        <h1 className="text-xl font-semibold">Arabic PDF Extraction API</h1>
        <p className="text-sm text-muted-foreground font-arabic" dir="rtl">خدمة استخراج النصوص من ملفات PDF العربية</p>

        <Card>
          <CardHeader>
            <CardTitle>Endpoints</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            <p><code>POST /api/pdf/upload</code> — upload PDF file.</p>
            <p><code>POST /api/pdf/extract</code> — extract embedded text + OCR fallback.</p>
            <p className="text-muted-foreground">Offline OCR with Tesseract (ara+eng), page-by-page results and confidence.</p>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

---
name: Dynamic imports for optional packages
description: How to avoid startup failures when packages exist in Replit env but not in local node_modules.
---

## Problem

Some packages (e.g., `jspdf`, `xlsx`) may be declared in `package.json` and present in Replit's central `node_modules`, but not actually installed in the user's local `node_modules`. Vite then throws:

```
Failed to resolve import "jspdf" from "...". Does the file exist?
```

## Solution

Remove top-level `import` statements for these packages and load them dynamically inside the function/method that actually uses them:

```tsx
// ❌ Top-level import — causes startup crash if package missing
import { jsPDF } from "jspdf";

// ✅ Dynamic import — only runs when the button is clicked
const exportReport = async (format: "pdf" | "excel") => {
  if (format === "pdf") {
    const { jsPDF } = await import("jspdf");
    const autoTable = (await import("jspdf-autotable")).default;
    const doc = new jsPDF();
    // ...
  }
};
```

## Important gotcha

The function containing `await import()` **must be `async`**. If it is a regular (non-async) function, Babel will throw:

```
Unexpected reserved word 'await'. (line:col)
```

**Why:** Babel's React plugin does not allow `await` inside non-async functions, even inside a `try` block.

## When to apply

- Any feature that depends on a library the user may not have installed (e.g., PDF/Excel export, chart libraries, heavy third-party SDKs).
- When you see `Failed to resolve import` for a package that exists in Replit's environment but not locally.

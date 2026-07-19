---
name: Babel JSX Tooltip Pattern
description: Why inline tooltip functions with `as any` fail in Recharts JSX and how to fix.
---

When using Recharts `<Tooltip content={...} />`, writing an inline arrow function inside the JSX attribute and casting with `as any` causes a Babel parse error:

```tsx
// BAD — Babel fails with "Unexpected token, expected '}'"
<Tooltip content={({ active, payload }) => { ... } as any} />
```

The parser sees the `}` closing the JSX expression container before it processes the `as any` cast.

**Fix:** Always extract the tooltip renderer into a separate named component:

```tsx
function MyTooltip({ active, payload }: { active?: boolean; payload?: any[] }) {
  if (!active || !payload?.length) return null;
  return <div>...</div>;
}

// GOOD
<Tooltip content={<MyTooltip /> as any} />
```

**Why:** JSX attribute values are parsed as expression containers. An inline function body with a trailing `as any` breaks the expression boundary detection. A component reference is a clean single expression.

**Applies to:** Recharts Tooltip content prop, any JSX attribute that accepts a function renderer.

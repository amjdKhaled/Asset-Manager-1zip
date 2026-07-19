---
name: Large File Assembly Safety
description: Avoid losing content when building large files from multiple temp parts.
---

When assembling a large source file from multiple temporary parts (e.g., `cat part1 >> final.tsx`), content can be silently lost if:
1. Intermediate temp files get overwritten by later steps
2. A failed `cat` truncates the destination file

**Rules:**
- Use distinct temp filenames for each part (e.g., `/tmp/part_a.txt`, `/tmp/part_b.txt`)
- Verify each part with `wc -l` before appending
- Use a single Python script to build and write the entire file when possible
- After assembly, verify with `wc -l` and `grep "export default"`

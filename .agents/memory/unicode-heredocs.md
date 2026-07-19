---
name: Unicode Escapes in Bash/Python
description: How \uXXXX escape sequences behave when writing files via bash vs Python.
---

When writing files that contain Unicode characters via bash heredocs or Python string literals, `\uXXXX` escape sequences can get double-escaped (becoming literal backslash-u sequences in the output file).

**Fix:** Use Python to write the file with explicit UTF-8 encoding:

```python
with open('file.tsx', 'w', encoding='utf-8') as f:
    f.write(content)
```

If the source content already contains `\uXXXX` escape sequences that need to be decoded:

```python
import re
content = re.sub(r'\\u([0-9a-fA-F]{4})', lambda m: chr(int(m.group(1), 16)), content)
```

**Applies to:** Any large file generation where non-ASCII characters (Arabic, etc.) need to be preserved.

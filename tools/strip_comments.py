import re
import sys
from pathlib import Path


def strip(src: str) -> str:
    out = []
    i = 0
    n = len(src)
    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = src.find("\n", i)
            if j == -1:
                j = n
            k = len(out) - 1
            while k >= 0 and out[k] in " \t":
                k -= 1
            line_start = k < 0 or out[k] == "\n"
            del out[k + 1:]
            if line_start:
                if j < n:
                    j += 1
            i = j
            continue
        if c == "/" and nxt == "*":
            j = src.find("*/", i + 2)
            j = n if j == -1 else j + 2
            i = j
            continue
        if c == '"' and src.startswith('"""', i):
            q = 0
            while i + q < n and src[i + q] == '"':
                q += 1
            closer = '"' * q
            j = src.find(closer, i + q)
            j = n if j == -1 else j + q
            out.append(src[i:j])
            i = j
            continue
        if c == "@" and nxt == '"' or (c == "$" and nxt == "@" and i + 2 < n and src[i + 2] == '"') or (c == "@" and nxt == "$" and i + 2 < n and src[i + 2] == '"'):
            start = i
            i = src.find('"', i) + 1
            while i < n:
                if src[i] == '"':
                    if i + 1 < n and src[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            out.append(src[start:i])
            continue
        if c == '"' or (c == "$" and nxt == '"'):
            start = i
            i = src.find('"', i) + 1
            depth = 0
            while i < n:
                ch = src[i]
                if ch == "\\":
                    i += 2
                    continue
                if ch == "{" and src[start] == "$":
                    depth += 1
                elif ch == "}" and depth > 0:
                    depth -= 1
                elif ch == '"' and depth == 0:
                    i += 1
                    break
                i += 1
            out.append(src[start:i])
            continue
        if c == "'":
            j = i + 1
            while j < n and src[j] != "'":
                if src[j] == "\\":
                    j += 1
                j += 1
            j = min(j + 1, n)
            out.append(src[i:j])
            i = j
            continue
        out.append(c)
        i += 1
    text = "".join(out)
    text = re.sub(r"[ \t]+\n", "\n", text)
    text = re.sub(r"\n{3,}", "\n\n", text)
    text = re.sub(r"\{\n\n", "{\n", text)
    text = re.sub(r"\n\n(\s*)\}", r"\n\1}", text)
    return text


def strip_glsl_in_raw_strings(text: str) -> str:
    def repl(m):
        body = m.group(0)
        lines = []
        for line in body.split("\n"):
            idx = line.find("//")
            if idx >= 0 and "http" not in line:
                line = line[:idx].rstrip()
                if not line.strip():
                    continue
            lines.append(line)
        return "\n".join(lines)
    return re.sub(r'"""[\s\S]*?"""', repl, text)


def main():
    root = Path(sys.argv[1])
    for path in root.rglob("*.cs"):
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        original = path.read_text(encoding="utf-8-sig")
        text = strip(original)
        text = strip_glsl_in_raw_strings(text)
        if text != original:
            path.write_text(text, encoding="utf-8")
            print("stripped", path.relative_to(root))


if __name__ == "__main__":
    main()

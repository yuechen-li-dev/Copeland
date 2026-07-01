import type {
  MachinaBulletItem,
  MachinaInline,
  MachinaTextBlock,
  MachinaTextDiagnostic,
  MachinaTextDiagnosticCode,
  MachinaTextSource,
  ParseMachinaTextResult,
} from "./types";

type LineInfo = { text: string; index: number; line: number };

function makeDiagnostic(
  code: MachinaTextDiagnosticCode,
  message: string,
  index: number,
  length: number,
  line: number,
  column: number,
): MachinaTextDiagnostic {
  return { code, message, index, length, line, column, level: "error" };
}

function toLines(source: string): LineInfo[] {
  const lines: LineInfo[] = [];
  let i = 0;
  let line = 1;
  while (i <= source.length) {
    const start = i;
    while (i < source.length && source[i] !== "\n" && source[i] !== "\r") i += 1;
    const text = source.slice(start, i);
    lines.push({ text, index: start, line });
    if (i >= source.length) break;
    if (source[i] === "\r" && source[i + 1] === "\n") i += 2;
    else i += 1;
    line += 1;
  }
  return lines;
}

function parseInline(
  text: string,
  lineIndex: number,
  line: number,
): { inline: MachinaInline[]; diagnostics: MachinaTextDiagnostic[] } {
  const diagnostics: MachinaTextDiagnostic[] = [];
  const inline: MachinaInline[] = [];
  let cursor = 0;

  const pushText = (t: string) => {
    if (!t) return;
    const prev = inline[inline.length - 1];
    if (prev?.kind === "text") prev.text += t;
    else inline.push({ kind: "text", text: t });
  };
  const allowedEscapes = new Set(["\\", "*", "`", "[", "]", "(", ")", "-"]);
  const consumeEscape = (): boolean => {
    if (text[cursor] !== "\\") return false;
    if (cursor === text.length - 1) {
      diagnostics.push(
        makeDiagnostic(
          "invalid_escape",
          "Dangling escape sequence.",
          lineIndex + cursor,
          1,
          line,
          cursor + 1,
        ),
      );
      pushText("\\");
      cursor += 1;
      return true;
    }
    const escaped = text[cursor + 1];
    if (allowedEscapes.has(escaped)) {
      pushText(escaped);
      cursor += 2;
      return true;
    }
    diagnostics.push(
      makeDiagnostic(
        "invalid_escape",
        `Unsupported escape sequence: \\${escaped}`,
        lineIndex + cursor,
        2,
        line,
        cursor + 1,
      ),
    );
    pushText(escaped);
    cursor += 2;
    return true;
  };

  while (cursor < text.length) {
    if (consumeEscape()) continue;

    if (text.startsWith("![", cursor)) {
      diagnostics.push(
        makeDiagnostic(
          "unsupported_syntax",
          "Images are not supported.",
          lineIndex + cursor,
          2,
          line,
          cursor + 1,
        ),
      );
      pushText("![");
      cursor += 2;
      continue;
    }

    if (text[cursor] === "`") {
      const close = text.indexOf("`", cursor + 1);
      if (close < 0) {
        diagnostics.push(
          makeDiagnostic(
            "unclosed_inline",
            "Unclosed inline code marker.",
            lineIndex + cursor,
            text.length - cursor,
            line,
            cursor + 1,
          ),
        );
        pushText(text.slice(cursor));
        break;
      }
      inline.push({ kind: "code", text: text.slice(cursor + 1, close) });
      cursor = close + 1;
      continue;
    }

    if (text.startsWith("**", cursor)) {
      const close = text.indexOf("**", cursor + 2);
      if (close < 0) {
        diagnostics.push(
          makeDiagnostic(
            "unclosed_inline",
            "Unclosed strong marker.",
            lineIndex + cursor,
            text.length - cursor,
            line,
            cursor + 1,
          ),
        );
        pushText(text.slice(cursor));
        break;
      }
      const children = parseInline(text.slice(cursor + 2, close), lineIndex + cursor + 2, line);
      diagnostics.push(...children.diagnostics);
      inline.push({ kind: "strong", children: children.inline });
      cursor = close + 2;
      continue;
    }

    if (text[cursor] === "*") {
      const close = text.indexOf("*", cursor + 1);
      if (close < 0) {
        diagnostics.push(
          makeDiagnostic(
            "unclosed_inline",
            "Unclosed emphasis marker.",
            lineIndex + cursor,
            text.length - cursor,
            line,
            cursor + 1,
          ),
        );
        pushText(text.slice(cursor));
        break;
      }
      const children = parseInline(text.slice(cursor + 1, close), lineIndex + cursor + 1, line);
      diagnostics.push(...children.diagnostics);
      inline.push({ kind: "emphasis", children: children.inline });
      cursor = close + 1;
      continue;
    }

    if (text[cursor] === "[") {
      const closeBracket = text.indexOf("]", cursor + 1);
      if (closeBracket < 0 || text[closeBracket + 1] !== "(") {
        diagnostics.push(
          makeDiagnostic(
            "malformed_link",
            "Malformed link syntax.",
            lineIndex + cursor,
            Math.max(1, text.length - cursor),
            line,
            cursor + 1,
          ),
        );
        pushText("[");
        cursor += 1;
        continue;
      }
      const closeParen = text.indexOf(")", closeBracket + 2);
      if (closeParen < 0) {
        diagnostics.push(
          makeDiagnostic(
            "malformed_link",
            "Malformed link syntax.",
            lineIndex + cursor,
            text.length - cursor,
            line,
            cursor + 1,
          ),
        );
        pushText(text.slice(cursor));
        break;
      }

      const label = text.slice(cursor + 1, closeBracket);
      const href = text.slice(closeBracket + 2, closeParen);
      if (label.length === 0) {
        diagnostics.push(
          makeDiagnostic(
            "malformed_link",
            "Link label cannot be empty.",
            lineIndex + cursor,
            closeParen - cursor + 1,
            line,
            cursor + 1,
          ),
        );
        pushText(text.slice(cursor, closeParen + 1));
        cursor = closeParen + 1;
        continue;
      }

      const labelInline = parseInline(label, lineIndex + cursor + 1, line);
      diagnostics.push(...labelInline.diagnostics);
      inline.push({ kind: "link", href, children: labelInline.inline });
      cursor = closeParen + 1;
      continue;
    }

    const specials = ["![", "`", "**", "*", "[", "\\"];
    let next = text.length;
    for (const special of specials) {
      const p = text.indexOf(special, cursor);
      if (p >= 0 && p < next) next = p;
    }
    if (next === cursor) {
      pushText(text[cursor]);
      cursor += 1;
      continue;
    }
    pushText(text.slice(cursor, next));
    cursor = next;
  }

  return { inline, diagnostics };
}

function classifyForbiddenBlock(line: string): MachinaTextDiagnosticCode | undefined {
  if (/^#{1,6}\s+/.test(line)) return "heading_forbidden";
  if (/^\d+\.\s+/.test(line)) return "unsupported_syntax";
  if (/^\s*-\s+\[[ xX]\]\s+/.test(line)) return "unsupported_syntax";
  if (/^>\s+/.test(line)) return "unsupported_syntax";
  if (/^```/.test(line)) return "unsupported_syntax";
  if (/^\s*<\/?[a-zA-Z][^>]*>/.test(line)) return "unsupported_syntax";
  if (/^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$/.test(line)) return "unsupported_syntax";
  return undefined;
}
function parseBulletLine(line: string): { depth: number; text: string } | undefined {
  if (line.startsWith("\\- ")) return undefined;
  if (line.startsWith("- ")) return { depth: 1, text: line.slice(2) };
  if (line.startsWith("  - ")) return { depth: 2, text: line.slice(4) };
  if (line.startsWith("    - ")) return { depth: 3, text: line.slice(6) };
  return undefined;
}

export function parseMachinaTextInline(text: string): {
  inline: MachinaInline[];
  diagnostics: MachinaTextDiagnostic[];
} {
  return parseInline(text, 0, 1);
}

export function parseMachinaText(source: MachinaTextSource | string): ParseMachinaTextResult {
  const src: MachinaTextSource =
    typeof source === "string" ? { kind: "machina-text", text: source } : source;

  if (src?.kind !== "plain" && src?.kind !== "machina-text") {
    const diagnostic = makeDiagnostic(
      "unsupported_syntax",
      "Unsupported MachinaText source kind.",
      0,
      0,
      1,
      1,
    );
    return { ok: false, document: { blocks: [] }, diagnostics: [diagnostic] };
  }

  if (src.kind === "plain") {
    return {
      ok: true,
      document: { blocks: [{ kind: "paragraph", inline: [{ kind: "text", text: src.text }] }] },
      diagnostics: [],
    };
  }

  const blocks: MachinaTextBlock[] = [];
  const diagnostics: MachinaTextDiagnostic[] = [];
  const lines = toLines(src.text);

  let i = 0;
  while (i < lines.length) {
    const lineInfo = lines[i];
    const trimmed = lineInfo.text.trim();
    if (trimmed.length === 0) {
      i += 1;
      continue;
    }

    const forbiddenCode = classifyForbiddenBlock(lineInfo.text);

    if (forbiddenCode) {
      const code = forbiddenCode;
      diagnostics.push(
        makeDiagnostic(
          code,
          "Unsupported block syntax.",
          lineInfo.index,
          lineInfo.text.length || 1,
          lineInfo.line,
          1,
        ),
      );
      blocks.push({ kind: "paragraph", inline: [{ kind: "text", text: lineInfo.text }] });
      i += 1;
      continue;
    }

    const bullet = parseBulletLine(lineInfo.text);
    if (bullet) {
      const items: MachinaBulletItem[] = [];
      let lastTop: MachinaBulletItem | undefined;
      while (i < lines.length) {
        const current = lines[i];
        if (current.text.trim().length === 0) break;
        const currentBullet = parseBulletLine(current.text);
        if (!currentBullet) break;

        if (/^\s*-\s+\[[ xX]\]\s+/.test(current.text)) {
          diagnostics.push(
            makeDiagnostic(
              "unsupported_syntax",
              "Task lists are not supported.",
              current.index,
              current.text.length || 1,
              current.line,
              1,
            ),
          );
        }

        if (currentBullet.depth > 2) {
          diagnostics.push(
            makeDiagnostic(
              "max_list_depth_exceeded",
              "Maximum bullet depth is 2.",
              current.index,
              current.text.length || 1,
              current.line,
              1,
            ),
          );
          const parsed = parseInline(
            current.text.trim(),
            current.index + (current.text.length - current.text.trimStart().length),
            current.line,
          );
          diagnostics.push(...parsed.diagnostics);
          blocks.push({
            kind: "paragraph",
            inline: parsed.inline.length ? parsed.inline : [{ kind: "text", text: current.text }],
          });
          i += 1;
          continue;
        }

        const parsed = parseInline(
          currentBullet.text,
          current.index + (currentBullet.depth === 1 ? 2 : 4),
          current.line,
        );
        diagnostics.push(...parsed.diagnostics);
        const item: MachinaBulletItem = { inline: parsed.inline };
        if (currentBullet.depth === 1) {
          items.push(item);
          lastTop = item;
        } else if (lastTop) {
          if (!lastTop.children) lastTop.children = [];
          lastTop.children.push(item);
        } else {
          diagnostics.push(
            makeDiagnostic(
              "unsupported_syntax",
              "Nested bullet requires a parent bullet.",
              current.index,
              current.text.length || 1,
              current.line,
              1,
            ),
          );
          blocks.push({ kind: "paragraph", inline: [{ kind: "text", text: current.text }] });
        }

        i += 1;
      }

      blocks.push({ kind: "bulletList", items });
      continue;
    }

    const paragraphLines: LineInfo[] = [];
    while (
      i < lines.length &&
      lines[i].text.trim().length > 0 &&
      !parseBulletLine(lines[i].text) &&
      !classifyForbiddenBlock(lines[i].text)
    ) {
      paragraphLines.push(lines[i]);
      i += 1;
    }
    const paragraphText = paragraphLines.map((line) => line.text).join("\n");
    const first = paragraphLines[0];
    const parsed = parseInline(paragraphText, first?.index ?? 0, first?.line ?? 1);
    diagnostics.push(...parsed.diagnostics);
    blocks.push({ kind: "paragraph", inline: parsed.inline });
  }

  return { ok: diagnostics.every((d) => d.level !== "error"), document: { blocks }, diagnostics };
}

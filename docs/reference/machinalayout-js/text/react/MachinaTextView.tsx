import React from "react";
import { parseMachinaText } from "../parseMachinaText";
import type {
  MachinaBulletItem,
  MachinaInline,
  MachinaTextAlign,
  MachinaTextDiagnostic,
  MachinaTextDocument,
  MachinaTextLeading,
  MachinaTextOverflow,
  MachinaTextSource,
  MachinaTextSpec,
  MachinaTextVariant,
  MachinaTextVerticalAlign,
  MachinaTextWrap,
} from "../types";

export type MachinaTextViewProps = {
  text: MachinaTextSpec | MachinaTextSource | MachinaTextDocument | string;
  className?: string;
  style?: React.CSSProperties;
  linkTarget?: React.HTMLAttributeAnchorTarget;
  onLinkClick?: (href: string, event: React.MouseEvent<HTMLAnchorElement>) => void;
  showDiagnostics?: boolean;
};

type TextPolicy = {
  variant: MachinaTextVariant;
  wrap: MachinaTextWrap;
  overflow: MachinaTextOverflow;
  align: MachinaTextAlign;
  leading: MachinaTextLeading;
  blockGap: number;
  listGap: number;
  valign: MachinaTextVerticalAlign;
};
type NormalizedText = {
  document: MachinaTextDocument;
  diagnostics: MachinaTextDiagnostic[];
  policy: TextPolicy;
};

const DEFAULT_POLICY: TextPolicy = {
  variant: "body",
  wrap: "word",
  overflow: "clip",
  align: "start",
  leading: "normal",
  blockGap: 8,
  listGap: 2,
  valign: "top",
};
const INLINE_CODE_FONT =
  'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace';

const VARIANT_STYLE: Record<MachinaTextVariant, React.CSSProperties> = {
  body: { fontSize: "14px", fontWeight: 400, lineHeight: 1.4 },
  label: { fontSize: "12px", fontWeight: 500, lineHeight: 1.3 },
  caption: { fontSize: "11px", fontWeight: 400, lineHeight: 1.25, opacity: 0.8 },
  title: { fontSize: "18px", fontWeight: 700, lineHeight: 1.25 },
  mono: { fontSize: "12px", lineHeight: 1.35, fontFamily: INLINE_CODE_FONT },
};

function isMachinaTextDocument(value: MachinaTextViewProps["text"]): value is MachinaTextDocument {
  return typeof value === "object" && value !== null && "blocks" in value;
}
function isMachinaTextSpec(value: MachinaTextViewProps["text"]): value is MachinaTextSpec {
  return typeof value === "object" && value !== null && "kind" in value && value.kind === "text";
}

function normalizePositive(value: number | undefined, fallback: number): number {
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? value : fallback;
}

function normalizeNonNegative(value: number | undefined, fallback: number): number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : fallback;
}

function normalizeLeading(value: MachinaTextLeading | undefined): MachinaTextLeading {
  if (value === undefined) return DEFAULT_POLICY.leading;
  if (value === "tight" || value === "normal" || value === "loose") return value;
  return normalizePositive(value, resolveLineHeight(DEFAULT_POLICY));
}

function normalizeSpecPolicy(spec: MachinaTextSpec): TextPolicy {
  return {
    variant: spec.variant ?? DEFAULT_POLICY.variant,
    wrap: spec.wrap ?? DEFAULT_POLICY.wrap,
    overflow: spec.overflow ?? DEFAULT_POLICY.overflow,
    align: spec.align ?? DEFAULT_POLICY.align,
    leading: normalizeLeading(spec.leading),
    blockGap: normalizeNonNegative(spec.blockGap, DEFAULT_POLICY.blockGap),
    listGap: normalizeNonNegative(spec.listGap, DEFAULT_POLICY.listGap),
    valign: spec.valign ?? DEFAULT_POLICY.valign,
  };
}

function normalizeText(text: MachinaTextViewProps["text"]): NormalizedText {
  if (isMachinaTextDocument(text))
    return { document: text, diagnostics: [], policy: DEFAULT_POLICY };
  if (isMachinaTextSpec(text)) {
    const result = parseMachinaText(text.source);
    return {
      document: result.document,
      diagnostics: result.diagnostics,
      policy: normalizeSpecPolicy(text),
    };
  }
  const result = parseMachinaText(typeof text === "string" ? { kind: "machina-text", text } : text);
  return { document: result.document, diagnostics: result.diagnostics, policy: DEFAULT_POLICY };
}

function resolveLineHeight(policy: TextPolicy): number {
  if (policy.leading === "tight") return 1.15;
  if (policy.leading === "loose") return 1.6;
  if (typeof policy.leading === "number") return policy.leading;
  return VARIANT_STYLE[policy.variant].lineHeight as number;
}

function policyStyle(policy: TextPolicy): React.CSSProperties {
  const wrapStyle: Record<MachinaTextWrap, React.CSSProperties> = {
    word: { whiteSpace: "normal", overflowWrap: "anywhere" },
    none: { whiteSpace: "nowrap" },
  };
  const overflowStyle: Record<MachinaTextOverflow, React.CSSProperties> = {
    clip: { overflow: "hidden" },
    ellipsis: { overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
    scroll: { overflow: "auto" },
  };
  const alignStyle: Record<MachinaTextAlign, React.CSSProperties> = {
    start: { textAlign: "left" },
    center: { textAlign: "center" },
    end: { textAlign: "right" },
  };
  const justifyContent: Record<MachinaTextVerticalAlign, React.CSSProperties["justifyContent"]> = {
    top: "flex-start",
    center: "center",
    bottom: "flex-end",
  };
  return {
    width: "100%",
    height: "100%",
    boxSizing: "border-box",
    display: "flex",
    flexDirection: "column",
    justifyContent: justifyContent[policy.valign],
    minWidth: 0,
    ...VARIANT_STYLE[policy.variant],
    lineHeight: resolveLineHeight(policy),
    ...wrapStyle[policy.wrap],
    ...overflowStyle[policy.overflow],
    ...alignStyle[policy.align],
  };
}

function renderInline(
  inline: MachinaInline,
  key: string,
  props: MachinaTextViewProps,
): React.ReactNode {
  /* unchanged */
  switch (inline.kind) {
    case "text":
      return <React.Fragment key={key}>{inline.text}</React.Fragment>;
    case "strong":
      return (
        <strong key={key}>
          {inline.children.map((c, i) => renderInline(c, `${key}-s-${i}`, props))}
        </strong>
      );
    case "emphasis":
      return (
        <em key={key}>{inline.children.map((c, i) => renderInline(c, `${key}-e-${i}`, props))}</em>
      );
    case "code":
      return (
        <code
          key={key}
          style={{
            fontFamily: INLINE_CODE_FONT,
            backgroundColor: "rgba(127, 127, 127, 0.15)",
            borderRadius: 3,
            padding: "0 0.25em",
          }}
        >
          {inline.text}
        </code>
      );
    case "link": {
      const rel = props.linkTarget === "_blank" ? "noreferrer noopener" : undefined;
      return (
        <a
          key={key}
          href={inline.href}
          target={props.linkTarget}
          rel={rel}
          onClick={(event) => props.onLinkClick?.(inline.href, event)}
        >
          {inline.children.map((c, i) => renderInline(c, `${key}-l-${i}`, props))}
        </a>
      );
    }
  }
}

function renderBulletItem(
  item: MachinaBulletItem,
  path: string,
  props: MachinaTextViewProps,
  listGap: number,
): React.ReactNode {
  return (
    <li key={path} style={{ marginBottom: listGap }}>
      {item.inline.map((i, idx) => renderInline(i, `${path}-i-${idx}`, props))}
      {item.children?.length ? (
        <ul style={{ margin: "0.25em 0 0 0", paddingLeft: "1.25em" }}>
          {item.children.map((c, idx) => renderBulletItem(c, `${path}-c-${idx}`, props, listGap))}
        </ul>
      ) : null}
    </li>
  );
}

export function MachinaTextView(props: MachinaTextViewProps): React.JSX.Element {
  const normalized = normalizeText(props.text);
  return (
    <div className={props.className} style={{ ...policyStyle(normalized.policy), ...props.style }}>
      <div style={{ minWidth: 0 }}>
        {normalized.document.blocks.map((block, index) =>
          block.kind === "paragraph" ? (
            <p
              key={`b-${index}`}
              style={{
                margin:
                  index === normalized.document.blocks.length - 1
                    ? "0"
                    : `0 0 ${normalized.policy.blockGap}px 0`,
              }}
            >
              {block.inline.map((i, idx) => renderInline(i, `b-${index}-${idx}`, props))}
            </p>
          ) : (
            <ul
              key={`b-${index}`}
              style={{
                margin:
                  index === normalized.document.blocks.length - 1
                    ? "0"
                    : `0 0 ${normalized.policy.blockGap}px 0`,
                paddingLeft: "1.25em",
              }}
            >
              {block.items.map((item, itemIndex) =>
                renderBulletItem(
                  item,
                  `b-${index}-item-${itemIndex}`,
                  props,
                  normalized.policy.listGap,
                ),
              )}
            </ul>
          ),
        )}
        {props.showDiagnostics && normalized.diagnostics.length > 0 ? (
          <pre
            style={{
              margin: `${normalized.policy.blockGap}px 0 0 0`,
              padding: "0.5em",
              fontSize: "11px",
              fontFamily: INLINE_CODE_FONT,
              whiteSpace: "pre-wrap",
              background: "rgba(127, 127, 127, 0.12)",
            }}
          >
            {normalized.diagnostics
              .map((d) => `${d.code} (${d.line}:${d.column}) ${d.message}`)
              .join("\n")}
          </pre>
        ) : null}
      </div>
    </div>
  );
}

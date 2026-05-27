import React from "react";
import { Text, View } from "react-native";
import type { StyleProp, TextStyle, ViewStyle } from "react-native";
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

export type MachinaNativeTextViewProps = {
  text: MachinaTextSpec | MachinaTextSource | MachinaTextDocument | string;
  style?: StyleProp<TextStyle>;
  contentStyle?: StyleProp<ViewStyle>;
  linkStyle?: StyleProp<TextStyle>;
  codeStyle?: StyleProp<TextStyle>;
  onLinkPress?: (href: string) => void;
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
const VARIANT_STYLE: Record<MachinaTextVariant, TextStyle> = {
  body: { fontSize: 14, fontWeight: "400" },
  label: { fontSize: 12, fontWeight: "500" },
  caption: { fontSize: 11, fontWeight: "400", opacity: 0.8 },
  title: { fontSize: 18, fontWeight: "700" },
  mono: { fontSize: 12, fontFamily: "monospace" },
};

const isDoc = (v: MachinaNativeTextViewProps["text"]): v is MachinaTextDocument =>
  typeof v === "object" && v !== null && "blocks" in v;
const isSpec = (v: MachinaNativeTextViewProps["text"]): v is MachinaTextSpec =>
  typeof v === "object" && v !== null && "kind" in v && v.kind === "text";
const normalizePositive = (v: number | undefined, f: number) =>
  typeof v === "number" && Number.isFinite(v) && v > 0 ? v : f;
const normalizeNonNegative = (v: number | undefined, f: number) =>
  typeof v === "number" && Number.isFinite(v) && v >= 0 ? v : f;

function resolveLeadingMultiplier(policy: TextPolicy): number {
  if (policy.leading === "tight") return 1.15;
  if (policy.leading === "loose") return 1.6;
  if (typeof policy.leading === "number") return policy.leading;
  if (policy.variant === "body") return 1.4;
  if (policy.variant === "label") return 1.3;
  if (policy.variant === "caption") return 1.25;
  if (policy.variant === "title") return 1.25;
  return 1.35;
}

function normalizeLeading(value: MachinaTextLeading | undefined): MachinaTextLeading {
  if (value === undefined) return DEFAULT_POLICY.leading;
  if (value === "tight" || value === "normal" || value === "loose") return value;
  return normalizePositive(value, resolveLeadingMultiplier(DEFAULT_POLICY));
}

function normalizeText(text: MachinaNativeTextViewProps["text"]): NormalizedText {
  if (isDoc(text)) return { document: text, diagnostics: [], policy: DEFAULT_POLICY };
  if (isSpec(text)) {
    const result = parseMachinaText(text.source);
    return {
      document: result.document,
      diagnostics: result.diagnostics,
      policy: {
        variant: text.variant ?? DEFAULT_POLICY.variant,
        wrap: text.wrap ?? DEFAULT_POLICY.wrap,
        overflow: text.overflow ?? DEFAULT_POLICY.overflow,
        align: text.align ?? DEFAULT_POLICY.align,
        leading: normalizeLeading(text.leading),
        blockGap: normalizeNonNegative(text.blockGap, DEFAULT_POLICY.blockGap),
        listGap: normalizeNonNegative(text.listGap, DEFAULT_POLICY.listGap),
        valign: text.valign ?? DEFAULT_POLICY.valign,
      },
    };
  }
  const result = parseMachinaText(typeof text === "string" ? { kind: "machina-text", text } : text);
  return { document: result.document, diagnostics: result.diagnostics, policy: DEFAULT_POLICY };
}

function textStyle(policy: TextPolicy): TextStyle {
  const base = VARIANT_STYLE[policy.variant];
  const fontSize = base.fontSize ?? 14;
  return {
    ...base,
    lineHeight: fontSize * resolveLeadingMultiplier(policy),
    textAlign: policy.align === "center" ? "center" : policy.align === "end" ? "right" : "left",
  };
}
const pProps = (policy: TextPolicy) =>
  policy.overflow === "ellipsis"
    ? { numberOfLines: 1 as const, ellipsizeMode: "tail" as const }
    : policy.wrap === "none"
      ? { numberOfLines: 1 as const }
      : {};

function renderInline(
  inline: MachinaInline,
  key: string,
  props: MachinaNativeTextViewProps,
): React.ReactNode {
  switch (inline.kind) {
    case "text":
      return <React.Fragment key={key}>{inline.text}</React.Fragment>;
    case "strong":
      return (
        <Text key={key} style={{ fontWeight: "700" }}>
          {inline.children.map((c, i) => renderInline(c, `${key}-s-${i}`, props))}
        </Text>
      );
    case "emphasis":
      return (
        <Text key={key} style={{ fontStyle: "italic" }}>
          {inline.children.map((c, i) => renderInline(c, `${key}-e-${i}`, props))}
        </Text>
      );
    case "code":
      return (
        <Text key={key} style={[{ fontFamily: "monospace" }, props.codeStyle]}>
          {inline.text}
        </Text>
      );
    case "link":
      return (
        <Text
          key={key}
          style={[{ textDecorationLine: "underline", color: "#2563eb" }, props.linkStyle]}
          onPress={() => props.onLinkPress?.(inline.href)}
        >
          {inline.children.map((c, i) => renderInline(c, `${key}-l-${i}`, props))}
        </Text>
      );
  }
}

function renderBullet(
  item: MachinaBulletItem,
  path: string,
  props: MachinaNativeTextViewProps,
  policy: TextPolicy,
  depth: number,
): React.ReactNode {
  return (
    <View key={path} style={{ marginBottom: policy.listGap, marginLeft: depth * 12 }}>
      <View style={{ flexDirection: "row", alignItems: "flex-start" }}>
        <Text style={textStyle(policy)}>{"• "}</Text>
        <Text style={[{ flexShrink: 1 }, textStyle(policy)]} {...pProps(policy)}>
          {item.inline.map((i, idx) => renderInline(i, `${path}-i-${idx}`, props))}
        </Text>
      </View>
      {item.children?.map((child, idx) =>
        renderBullet(child, `${path}-c-${idx}`, props, policy, depth + 1),
      )}
    </View>
  );
}

export function MachinaNativeTextView(props: MachinaNativeTextViewProps): React.ReactElement {
  const normalized = normalizeText(props.text);
  const justifyContent: ViewStyle["justifyContent"] =
    normalized.policy.valign === "center"
      ? "center"
      : normalized.policy.valign === "bottom"
        ? "flex-end"
        : "flex-start";
  return (
    <View style={[{ width: "100%", height: "100%", justifyContent }, props.contentStyle]}>
      <View style={{ minWidth: 0 }}>
        {normalized.document.blocks.map((block, idx) =>
          block.kind === "paragraph" ? (
            <Text
              key={`b-${idx}`}
              style={[
                textStyle(normalized.policy),
                idx === normalized.document.blocks.length - 1
                  ? undefined
                  : { marginBottom: normalized.policy.blockGap },
                props.style,
              ]}
              {...pProps(normalized.policy)}
            >
              {block.inline.map((i, iIdx) => renderInline(i, `b-${idx}-${iIdx}`, props))}
            </Text>
          ) : (
            <View
              key={`b-${idx}`}
              style={
                idx === normalized.document.blocks.length - 1
                  ? undefined
                  : { marginBottom: normalized.policy.blockGap }
              }
            >
              {block.items.map((item, itemIdx) =>
                renderBullet(item, `b-${idx}-item-${itemIdx}`, props, normalized.policy, 0),
              )}
            </View>
          ),
        )}
        {props.showDiagnostics && normalized.diagnostics.length > 0 ? (
          <Text
            style={{
              marginTop: normalized.policy.blockGap,
              padding: 8,
              fontSize: 11,
              fontFamily: "monospace",
              backgroundColor: "rgba(127, 127, 127, 0.12)",
            }}
          >
            {normalized.diagnostics
              .map((d) => `${d.code} (${d.line}:${d.column}) ${d.message}`)
              .join("\n")}
          </Text>
        ) : null}
      </View>
    </View>
  );
}

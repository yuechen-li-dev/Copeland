import { computed, defineComponent, h, type PropType, type StyleValue, type VNodeChild } from "vue";
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
export type MachinaVueTextViewProps = {
  text: MachinaTextSpec | MachinaTextSource | MachinaTextDocument | string;
  rootClass?: unknown;
  rootStyle?: StyleValue;
  linkTarget?: string;
  onLinkClick?: (href: string, event: MouseEvent) => void;
  showDiagnostics?: boolean;
  linkClass?: unknown;
  linkStyle?: StyleValue;
  codeClass?: unknown;
  codeStyle?: StyleValue;
};
// ...same logic

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
const VARIANT_STYLE: Record<MachinaTextVariant, Record<string, string | number>> = {
  body: { fontSize: "14px", fontWeight: 400, lineHeight: 1.4 },
  label: { fontSize: "12px", fontWeight: 500, lineHeight: 1.3 },
  caption: { fontSize: "11px", fontWeight: 400, lineHeight: 1.25, opacity: 0.8 },
  title: { fontSize: "18px", fontWeight: 700, lineHeight: 1.25 },
  mono: { fontSize: "12px", lineHeight: 1.35, fontFamily: INLINE_CODE_FONT },
};
const isDocument = (v: MachinaVueTextViewProps["text"]): v is MachinaTextDocument =>
  typeof v === "object" && v !== null && "blocks" in v;
const isSpec = (v: MachinaVueTextViewProps["text"]): v is MachinaTextSpec =>
  typeof v === "object" && v !== null && "kind" in v && v.kind === "text";
const np = (v: number | undefined, f: number) =>
  typeof v === "number" && Number.isFinite(v) && v > 0 ? v : f;
const nnn = (v: number | undefined, f: number) =>
  typeof v === "number" && Number.isFinite(v) && v >= 0 ? v : f;
const resolveLineHeight = (p: TextPolicy) =>
  p.leading === "tight"
    ? 1.15
    : p.leading === "loose"
      ? 1.6
      : typeof p.leading === "number"
        ? p.leading
        : (VARIANT_STYLE[p.variant].lineHeight as number);
const normalizeLeading = (v: MachinaTextLeading | undefined): MachinaTextLeading =>
  v === undefined
    ? DEFAULT_POLICY.leading
    : v === "tight" || v === "normal" || v === "loose"
      ? v
      : np(v, resolveLineHeight(DEFAULT_POLICY));
const normalizeSpecPolicy = (s: MachinaTextSpec): TextPolicy => ({
  variant: s.variant ?? DEFAULT_POLICY.variant,
  wrap: s.wrap ?? DEFAULT_POLICY.wrap,
  overflow: s.overflow ?? DEFAULT_POLICY.overflow,
  align: s.align ?? DEFAULT_POLICY.align,
  leading: normalizeLeading(s.leading),
  blockGap: nnn(s.blockGap, DEFAULT_POLICY.blockGap),
  listGap: nnn(s.listGap, DEFAULT_POLICY.listGap),
  valign: s.valign ?? DEFAULT_POLICY.valign,
});
function normalizeText(text: MachinaVueTextViewProps["text"]): NormalizedText {
  if (isDocument(text)) return { document: text, diagnostics: [], policy: DEFAULT_POLICY };
  if (isSpec(text)) {
    const r = parseMachinaText(text.source);
    return { document: r.document, diagnostics: r.diagnostics, policy: normalizeSpecPolicy(text) };
  }
  const r = parseMachinaText(typeof text === "string" ? { kind: "machina-text", text } : text);
  return { document: r.document, diagnostics: r.diagnostics, policy: DEFAULT_POLICY };
}
function policyStyle(policy: TextPolicy): Record<string, string | number | undefined> {
  const wrap =
    policy.wrap === "word"
      ? { whiteSpace: "normal", overflowWrap: "anywhere" }
      : { whiteSpace: "nowrap" };
  const ov =
    policy.overflow === "clip"
      ? { overflow: "hidden" }
      : policy.overflow === "ellipsis"
        ? { overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }
        : { overflow: "auto" };
  const align =
    policy.align === "center"
      ? { textAlign: "center" }
      : policy.align === "end"
        ? { textAlign: "right" }
        : { textAlign: "left" };
  const jc =
    policy.valign === "center" ? "center" : policy.valign === "bottom" ? "flex-end" : "flex-start";
  return {
    width: "100%",
    height: "100%",
    boxSizing: "border-box",
    display: "flex",
    flexDirection: "column",
    justifyContent: jc,
    minWidth: 0,
    ...VARIANT_STYLE[policy.variant],
    lineHeight: resolveLineHeight(policy),
    ...wrap,
    ...ov,
    ...align,
  };
}
function renderInline(
  inline: MachinaInline,
  key: string,
  props: MachinaVueTextViewProps,
): VNodeChild {
  switch (inline.kind) {
    case "text":
      return inline.text;
    case "strong":
      return h(
        "strong",
        { key },
        inline.children.map((c, i) => renderInline(c, `${key}-s-${i}`, props)),
      );
    case "emphasis":
      return h(
        "em",
        { key },
        inline.children.map((c, i) => renderInline(c, `${key}-e-${i}`, props)),
      );
    case "code":
      return h(
        "code",
        {
          key,
          class: props.codeClass,
          style: [
            {
              fontFamily: INLINE_CODE_FONT,
              backgroundColor: "rgba(127, 127, 127, 0.15)",
              borderRadius: "3px",
              padding: "0 0.25em",
            },
            props.codeStyle,
          ],
        },
        inline.text,
      );
    case "link": {
      const rel = props.linkTarget === "_blank" ? "noreferrer noopener" : undefined;
      return h(
        "a",
        {
          key,
          href: inline.href,
          target: props.linkTarget,
          rel,
          class: props.linkClass,
          style: props.linkStyle,
          onClick: (e: MouseEvent) => props.onLinkClick?.(inline.href, e),
        },
        inline.children.map((c, i) => renderInline(c, `${key}-l-${i}`, props)),
      );
    }
  }
}
function renderBulletItem(
  item: MachinaBulletItem,
  path: string,
  props: MachinaVueTextViewProps,
  listGap: number,
): VNodeChild {
  return h("li", { key: path, style: { marginBottom: `${listGap}px` } }, [
    ...item.inline.map((i, idx) => renderInline(i, `${path}-i-${idx}`, props)),
    ...(item.children?.length
      ? [
          h(
            "ul",
            { style: { margin: "0.25em 0 0 0", paddingLeft: "1.25em" } },
            item.children.map((c, idx) => renderBulletItem(c, `${path}-c-${idx}`, props, listGap)),
          ),
        ]
      : []),
  ]);
}

export const MachinaVueTextView = defineComponent({
  name: "MachinaVueTextView",
  props: {
    text: { type: [String, Object] as PropType<MachinaVueTextViewProps["text"]>, required: true },
    rootClass: { type: null as unknown as PropType<unknown>, default: undefined },
    rootStyle: { type: null as unknown as PropType<StyleValue>, default: undefined },
    linkTarget: { type: String, default: undefined },
    onLinkClick: {
      type: Function as PropType<MachinaVueTextViewProps["onLinkClick"]>,
      default: undefined,
    },
    showDiagnostics: { type: Boolean, default: false },
    linkClass: { type: null as unknown as PropType<unknown>, default: undefined },
    linkStyle: { type: null as unknown as PropType<StyleValue>, default: undefined },
    codeClass: { type: null as unknown as PropType<unknown>, default: undefined },
    codeStyle: { type: null as unknown as PropType<StyleValue>, default: undefined },
  },
  setup(props) {
    const normalized = computed(() => normalizeText(props.text));
    return () =>
      h(
        "div",
        { class: props.rootClass, style: [policyStyle(normalized.value.policy), props.rootStyle] },
        [
          h("div", { style: { minWidth: 0 } }, [
            ...normalized.value.document.blocks.map((block, index) => {
              const blockStyle = {
                margin:
                  index === normalized.value.document.blocks.length - 1
                    ? "0"
                    : `0 0 ${normalized.value.policy.blockGap}px 0`,
              };
              return block.kind === "paragraph"
                ? h(
                    "p",
                    { key: `b-${index}`, style: blockStyle },
                    block.inline.map((i, idx) => renderInline(i, `b-${index}-${idx}`, props)),
                  )
                : h(
                    "ul",
                    { key: `b-${index}`, style: { ...blockStyle, paddingLeft: "1.25em" } },
                    block.items.map((item, itemIndex) =>
                      renderBulletItem(
                        item,
                        `b-${index}-item-${itemIndex}`,
                        props,
                        normalized.value.policy.listGap,
                      ),
                    ),
                  );
            }),
            ...(props.showDiagnostics && normalized.value.diagnostics.length > 0
              ? [
                  h(
                    "pre",
                    {
                      style: {
                        margin: `${normalized.value.policy.blockGap}px 0 0 0`,
                        padding: "0.5em",
                        fontSize: "11px",
                        fontFamily: INLINE_CODE_FONT,
                        whiteSpace: "pre-wrap",
                        background: "rgba(127, 127, 127, 0.12)",
                      },
                    },
                    normalized.value.diagnostics
                      .map((d) => `${d.code} (${d.line}:${d.column}) ${d.message}`)
                      .join("\n"),
                  ),
                ]
              : []),
          ]),
        ],
      );
  },
});

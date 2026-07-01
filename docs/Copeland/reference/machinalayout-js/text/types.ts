export type MachinaTextSource =
  | { kind: "plain"; text: string }
  | { kind: "machina-text"; text: string };

export type MachinaTextVariant = "body" | "label" | "caption" | "title" | "mono";
export type MachinaTextWrap = "word" | "none";
export type MachinaTextOverflow = "clip" | "ellipsis" | "scroll";
export type MachinaTextAlign = "start" | "center" | "end";
export type MachinaTextLeading = "tight" | "normal" | "loose" | number;
export type MachinaTextVerticalAlign = "top" | "center" | "bottom";

export type MachinaTextSpec = {
  kind: "text";
  source: MachinaTextSource;
  variant?: MachinaTextVariant;
  wrap?: MachinaTextWrap;
  overflow?: MachinaTextOverflow;
  align?: MachinaTextAlign;
  leading?: MachinaTextLeading;
  blockGap?: number;
  listGap?: number;
  valign?: MachinaTextVerticalAlign;
};

export type MachinaTextDocument = {
  blocks: MachinaTextBlock[];
};

export type MachinaTextBlock =
  | { kind: "paragraph"; inline: MachinaInline[] }
  | { kind: "bulletList"; items: MachinaBulletItem[] };

export type MachinaBulletItem = {
  inline: MachinaInline[];
  children?: MachinaBulletItem[];
};

export type MachinaInline =
  | { kind: "text"; text: string }
  | { kind: "strong"; children: MachinaInline[] }
  | { kind: "emphasis"; children: MachinaInline[] }
  | { kind: "code"; text: string }
  | { kind: "link"; href: string; children: MachinaInline[] };

export type MachinaTextDiagnosticLevel = "error" | "warning";

export type MachinaTextDiagnosticCode =
  | "unsupported_syntax"
  | "heading_forbidden"
  | "max_list_depth_exceeded"
  | "malformed_link"
  | "unclosed_inline"
  | "invalid_escape";

export type MachinaTextDiagnostic = {
  code: MachinaTextDiagnosticCode;
  message: string;
  index: number;
  length: number;
  line: number;
  column: number;
  level: MachinaTextDiagnosticLevel;
};

export type ParseMachinaTextResult = {
  ok: boolean;
  document: MachinaTextDocument;
  diagnostics: MachinaTextDiagnostic[];
};

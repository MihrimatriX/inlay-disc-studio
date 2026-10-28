// The template code editor (§7.3 level 3): CodeMirror 6 with HTML/Liquid, CSS, JS and JSON, Liquid variable and
// filter completion from the disc's own data, Geist Mono on the builder's dark tokens. Lazy-loaded by the builder.
import { EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter, drawSelection, Decoration, MatchDecorator, ViewPlugin, type DecorationSet, type ViewUpdate } from "@codemirror/view";
import { EditorState, Compartment, type Extension } from "@codemirror/state";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { autocompletion, closeBrackets, type CompletionContext, type CompletionResult } from "@codemirror/autocomplete";
import { bracketMatching, indentOnInput, syntaxHighlighting, HighlightStyle } from "@codemirror/language";
import { html } from "@codemirror/lang-html";
import { css } from "@codemirror/lang-css";
import { javascript } from "@codemirror/lang-javascript";
import { json } from "@codemirror/lang-json";
import { tags } from "@lezer/highlight";

export interface CodeEditor {
  open(path: string, text: string, readOnly: boolean): void;
  text(): string;
  insert(text: string): void;
  focus(): void;
  destroy(): void;
}

/** Fluid's standard filters plus the §7.1.2 ones. */
const FILTERS = ["duration", "clock", "filesize", "relurl", "t", "json", "escape", "default", "date", "upcase", "downcase", "capitalize",
  "size", "first", "last", "join", "split", "map", "where", "sort", "reverse", "slice", "truncate", "append", "prepend", "replace", "strip", "plus", "minus", "times", "divided_by", "round"];
const TAGS = ["if", "elsif", "else", "endif", "unless", "endunless", "for", "endfor", "case", "when", "endcase", "assign", "capture", "endcapture", "render", "include", "comment", "endcomment", "raw", "endraw", "break", "continue"];

const highlight = HighlightStyle.define([
  { tag: tags.tagName, color: "#ffb38a" },
  { tag: tags.attributeName, color: "#c9d3ff" },
  { tag: [tags.string, tags.attributeValue], color: "#c6efe0" },
  { tag: [tags.comment, tags.meta], color: "#8f919a", fontStyle: "italic" },
  { tag: [tags.keyword, tags.operator], color: "#e7c3ef" },
  { tag: [tags.number, tags.bool, tags.null], color: "#ffdcb4" },
  { tag: [tags.propertyName, tags.variableName], color: "#ececef" },
  { tag: tags.className, color: "#fff1d6" },
]);

const theme = EditorView.theme({
  "&": { height: "100%", background: "#111215", color: "#ececef", fontSize: "13.5px" },
  ".cm-scroller": { fontFamily: "var(--mono)", lineHeight: "1.6" },
  ".cm-gutters": { background: "#111215", color: "#84868f", border: "0" },
  ".cm-activeLine, .cm-activeLineGutter": { background: "rgba(255,255,255,.035)" },
  ".cm-cursor": { borderLeftColor: "var(--accent)" },
  "&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection": { background: "rgba(var(--accent-rgb), .28) !important" },
  ".cm-liquid": { color: "#ff8a3d" },
  ".cm-tooltip": { background: "#1d1e23", border: "0", borderRadius: "10px", boxShadow: "0 20px 50px -20px rgba(0,0,0,.9), inset 0 0 0 1px rgba(255,255,255,.08)" },
  ".cm-tooltip-autocomplete ul li[aria-selected]": { background: "rgba(var(--accent-rgb), .25)", color: "#fff" },
}, { dark: true });

/** {{ … }} and {% … %} drawn in the accent so Liquid stands out inside the HTML. */
const liquidMarks = ViewPlugin.fromClass(class {
  decorations: DecorationSet;
  matcher = new MatchDecorator({ regexp: /\{\{-?[\s\S]*?-?\}\}|\{%-?[\s\S]*?-?%\}/g, decoration: Decoration.mark({ class: "cm-liquid" }) });
  constructor(view: EditorView) { this.decorations = this.matcher.createDeco(view); }
  update(u: ViewUpdate) { this.decorations = this.matcher.updateDeco(u, this.decorations); }
}, { decorations: (v) => v.decorations });

function liquidCompletion(vars: () => string[]) {
  return (ctx: CompletionContext): CompletionResult | null => {
    const line = ctx.state.doc.lineAt(ctx.pos);
    const before = line.text.slice(0, ctx.pos - line.from);
    const open = Math.max(before.lastIndexOf("{{"), before.lastIndexOf("{%"));
    if (open < 0 || before.lastIndexOf("}}") > open || before.lastIndexOf("%}") > open) return null;
    const inside = before.slice(open + 2);
    const filter = /\|\s*([\w]*)$/.exec(inside);
    if (filter) return { from: ctx.pos - filter[1].length, options: FILTERS.map((f) => ({ label: f, type: "function" })) };
    const word = /([\w.'\[\]-]*)$/.exec(inside)!;
    const isTag = before[open + 1] === "%" && /^\s*-?\s*[\w]*$/.test(inside);
    const options = isTag ? TAGS.map((t) => ({ label: t, type: "keyword" })) : vars().map((v) => ({ label: v, type: "variable" }));
    return { from: ctx.pos - word[1].length, options, validFor: /^[\w.'\[\]-]*$/ };
  };
}

function language(path: string): Extension {
  if (path.endsWith(".css")) return css();
  if (path.endsWith(".js")) return javascript();
  if (path.endsWith(".json")) return json();
  return html({ selfClosingTags: true });
}

export function createEditor(parent: HTMLElement, o: { label: string; onChange: (text: string) => void; onSave: () => void; vars: () => string[] }): CodeEditor {
  const lang = new Compartment();
  const readOnly = new Compartment();
  const extensions: Extension[] = [
    lineNumbers(), highlightActiveLineGutter(), highlightActiveLine(), drawSelection(), history(), indentOnInput(), bracketMatching(), closeBrackets(),
    syntaxHighlighting(highlight), theme, liquidMarks, lang.of([]), readOnly.of(EditorState.readOnly.of(true)),
    autocompletion({ override: [liquidCompletion(o.vars)] }),
    keymap.of([{ key: "Mod-s", preventDefault: true, run: () => { o.onSave(); return true; } }, indentWithTab, ...defaultKeymap, ...historyKeymap]),
    EditorView.updateListener.of((u) => { if (u.docChanged) o.onChange(u.state.doc.toString()); }),
    EditorView.contentAttributes.of({ "aria-label": o.label }),
  ];
  const view = new EditorView({ parent, state: EditorState.create({ extensions }) });
  view.scrollDOM.tabIndex = 0; // a keyboard user can scroll a read-only file too
  return {
    // Each file gets a fresh state: its own undo history, language and read-only flag.
    open(path, text, ro) {
      view.setState(EditorState.create({ doc: text, extensions }));
      view.dispatch({ effects: [lang.reconfigure(language(path)), readOnly.reconfigure(EditorState.readOnly.of(ro))] });
    },
    text: () => view.state.doc.toString(),
    insert(text) {
      const { from, to } = view.state.selection.main;
      view.dispatch({ changes: { from, to, insert: text }, selection: { anchor: from + text.length } });
      view.focus();
    },
    focus: () => view.focus(),
    destroy: () => view.destroy(),
  };
}

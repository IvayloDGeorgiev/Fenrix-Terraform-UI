// Fenrix hand-rolled, dependency-free Terraform/HCL code editor.
// A transparent <textarea> (the real caret + native undo) overlaid on a syntax-highlighted <pre>, plus a
// synced line-number gutter. No external library, no CDN, no build step — matches the offline, dependency-free
// house style (see fenrix-graph.js). Themed via CSS variables in fenrix.css (Dark/Light + reduced-motion).
// See docs/05-terraform-engine.md, docs/13-ui-design.md.
(function () {
  "use strict";

  var OPEN = { "{": "}", "[": "]", "(": ")" };
  var CLOSE = { "}": "{", "]": "[", ")": "(" };
  var BRACKETS = "{}[]()";

  var DECL_KEYWORDS = {
    resource: 1, variable: 1, output: 1, provider: 1, module: 1, data: 1, locals: 1,
    terraform: 1, moved: 1, import: 1, check: 1, removed: 1, backend: 1, provisioner: 1,
    connection: 1, dynamic: 1, lifecycle: 1
  };
  var EXPR_KEYWORDS = { "for": 1, "in": 1, "if": 1, "else": 1, "endif": 1, "endfor": 1 };
  var CONST_KEYWORDS = { "true": 1, "false": 1, "null": 1 };

  var instances = {};
  var counter = 0;

  function esc(s) {
    return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  }

  // ---- Tokenizer (whole-buffer, state-carrying) ----
  // Produces [{t: type, s: startOffset, x: text}]. Tokens may contain newlines (block comments / heredocs /
  // interpolations); the renderer splits them into per-line spans.
  function tokenize(src) {
    var toks = [];
    var i = 0, n = src.length;

    function push(t, s, x) { if (x.length) toks.push({ t: t, s: s, x: x }); }

    while (i < n) {
      var c = src[i];

      // Line comments
      if (c === "#" || (c === "/" && src[i + 1] === "/")) {
        var j = i;
        while (j < n && src[j] !== "\n") j++;
        push("com", i, src.slice(i, j));
        i = j;
        continue;
      }
      // Block comment
      if (c === "/" && src[i + 1] === "*") {
        var k = i + 2;
        while (k < n && !(src[k] === "*" && src[k + 1] === "/")) k++;
        k = Math.min(n, k + 2);
        push("com", i, src.slice(i, k));
        i = k;
        continue;
      }
      // Heredoc: << or <<- MARKER ... MARKER
      if (c === "<" && src[i + 1] === "<") {
        var h = i + 2;
        if (src[h] === "-") h++;
        var mStart = h;
        while (h < n && /[A-Za-z0-9_]/.test(src[h])) h++;
        var marker = src.slice(mStart, h);
        if (marker.length) {
          // consume to end of a line that is (optional ws)marker
          var end = n;
          var lineStart = src.indexOf("\n", h);
          var p = lineStart < 0 ? n : lineStart + 1;
          while (p <= n) {
            var nl = src.indexOf("\n", p);
            var lineEnd = nl < 0 ? n : nl;
            var line = src.slice(p, lineEnd);
            if (line.replace(/^[ \t]+/, "") === marker) { end = lineEnd; break; }
            if (nl < 0) { end = n; break; }
            p = nl + 1;
          }
          push("str", i, src.slice(i, end));
          i = end;
          continue;
        }
      }
      // Strings with ${ } / %{ } interpolation
      if (c === '"') {
        var start = i;
        i++;
        var seg = '"';
        while (i < n) {
          var ch = src[i];
          if (ch === "\\") { seg += src.slice(i, i + 2); i += 2; continue; }
          if (ch === "\n") break;
          if ((ch === "$" || ch === "%") && src[i + 1] === "{" && src[i - 1] !== "$" && src[i - 1] !== "%") {
            push("str", start, seg); seg = "";
            var depth = 0, q = i;
            q += 2; depth = 1;
            while (q < n && depth > 0) {
              if (src[q] === "{") depth++;
              else if (src[q] === "}") depth--;
              if (depth === 0) break;
              q++;
            }
            q = Math.min(n, q + 1);
            push("intp", i, src.slice(i, q));
            i = q; start = i;
            continue;
          }
          if (ch === '"') { seg += '"'; i++; break; }
          seg += ch; i++;
        }
        push("str", start, seg);
        continue;
      }
      // Numbers
      if (/[0-9]/.test(c) || (c === "." && /[0-9]/.test(src[i + 1]))) {
        var d = i;
        while (d < n && /[0-9._eE+\-]/.test(src[d])) {
          // stop a trailing +/- that isn't an exponent sign
          if ((src[d] === "+" || src[d] === "-") && !/[eE]/.test(src[d - 1])) break;
          d++;
        }
        push("num", i, src.slice(i, d));
        i = d;
        continue;
      }
      // Identifiers / keywords
      if (/[A-Za-z_]/.test(c)) {
        var w = i;
        while (w < n && /[A-Za-z0-9_\-]/.test(src[w])) w++;
        var word = src.slice(i, w);
        var type = "id";
        if (DECL_KEYWORDS[word]) type = "kw";
        else if (CONST_KEYWORDS[word]) type = "const";
        else if (EXPR_KEYWORDS[word]) type = "kw";
        else {
          // function call?  name(
          var t = w;
          while (t < n && (src[t] === " " || src[t] === "\t")) t++;
          if (src[t] === "(") type = "fn";
        }
        push(type, i, word);
        i = w;
        continue;
      }
      // Whitespace (kept verbatim, may contain newlines)
      if (c === " " || c === "\t" || c === "\n" || c === "\r") {
        var ws = i;
        while (ws < n && (src[ws] === " " || src[ws] === "\t" || src[ws] === "\n" || src[ws] === "\r")) ws++;
        push("ws", i, src.slice(i, ws));
        i = ws;
        continue;
      }
      // Punctuation / operators (single char so brackets get their own offset)
      push(BRACKETS.indexOf(c) >= 0 ? "br" : "op", i, c);
      i++;
    }
    return toks;
  }

  // Build per-line HTML from tokens. Returns { html, lineCount }. Records bracket spans by offset via data-off.
  function buildHighlight(src, tokens, markers) {
    var lines = [];
    var cur = "";
    var lineNo = 0;

    function flush() {
      lines.push(cur);
      cur = "";
      lineNo++;
    }

    for (var ti = 0; ti < tokens.length; ti++) {
      var tok = tokens[ti];
      var parts = tok.x.split("\n");
      for (var pi = 0; pi < parts.length; pi++) {
        if (pi > 0) flush();
        var piece = parts[pi];
        if (!piece.length) continue;
        if (tok.t === "ws") { cur += esc(piece); continue; }
        var cls = "fx-cm-" + tok.t;
        if (tok.t === "br") {
          var off = tok.s; // single-char bracket token
          cur += '<span class="' + cls + '" data-off="' + off + '">' + esc(piece) + "</span>";
        } else {
          cur += '<span class="' + cls + '">' + esc(piece) + "</span>";
        }
      }
    }
    flush(); // final line

    var markByLine = {};
    if (markers) for (var m = 0; m < markers.length; m++) {
      var ml = markers[m].line;
      if (!markByLine[ml] || sev(markers[m].severity) > sev(markByLine[ml].severity)) markByLine[ml] = markers[m];
    }

    var html = "";
    for (var li = 0; li < lines.length; li++) {
      var mk = markByLine[li + 1];
      var extra = mk ? (" " + (mk.severity === "warning" ? "fx-cm-warnline" : "fx-cm-errline")) : "";
      html += '<div class="fx-cm-line' + extra + '">' + (lines[li].length ? lines[li] : "​") + "</div>";
    }
    return { html: html, lineCount: lines.length, markByLine: markByLine };
  }

  function sev(s) { return s === "error" ? 2 : 1; }

  function gutterHtml(lineCount, markByLine, currentLine) {
    var h = "";
    for (var i = 1; i <= lineCount; i++) {
      var mk = markByLine[i];
      var cls = "fx-cm-gline" + (i === currentLine ? " current" : "");
      var dot = "";
      if (mk) {
        var mc = mk.severity === "warning" ? "fx-cm-gwarn" : "fx-cm-gerr";
        var title = esc(mk.message || "").replace(/"/g, "&quot;");
        dot = '<span class="fx-cm-gmark ' + mc + '" title="' + title + '"></span>';
      }
      h += '<div class="' + cls + '">' + dot + '<span class="fx-cm-gnum">' + i + "</span></div>";
    }
    return h;
  }

  // ---- Rendering ----
  function render(st) {
    var val = st.ta.value;
    var toks = tokenize(val);
    var hl = buildHighlight(val, toks, st.markers);
    st.pre.innerHTML = hl.html;
    st.gutter.innerHTML = gutterHtml(hl.lineCount, hl.markByLine, st.currentLine);
    st.lineCount = hl.lineCount;
    st.markByLine = hl.markByLine;
    indexBrackets(st);
    syncScroll(st);
    if (st.wrap) syncLineHeights(st);
    updateActiveLine(st);
  }

  function indexBrackets(st) {
    st.bracketSpans = {};
    var nodes = st.pre.querySelectorAll("span[data-off]");
    for (var i = 0; i < nodes.length; i++) st.bracketSpans[nodes[i].getAttribute("data-off")] = nodes[i];
  }

  function syncScroll(st) {
    st.pre.scrollTop = st.ta.scrollTop;
    st.pre.scrollLeft = st.ta.scrollLeft;
    st.gutter.scrollTop = st.ta.scrollTop;
  }

  // In word-wrap mode, match each gutter line's height to the (possibly multi-row) rendered line.
  function syncLineHeights(st) {
    var preLines = st.pre.children;
    var gLines = st.gutter.children;
    for (var i = 0; i < preLines.length && i < gLines.length; i++) {
      gLines[i].style.height = preLines[i].offsetHeight + "px";
    }
  }

  function lineOfOffset(val, off) {
    var line = 0;
    for (var i = 0; i < off && i < val.length; i++) if (val[i] === "\n") line++;
    return line + 1; // 1-based
  }

  function offsetOfLine(val, line) {
    if (line <= 1) return 0;
    var seen = 1, i = 0;
    for (; i < val.length; i++) {
      if (val[i] === "\n") { seen++; if (seen === line) return i + 1; }
    }
    return val.length;
  }

  function updateActiveLine(st) {
    var line = lineOfOffset(st.ta.value, st.ta.selectionStart);
    if (line !== st.currentLine) {
      st.currentLine = line;
      // update gutter current class cheaply
      var g = st.gutter.children;
      for (var i = 0; i < g.length; i++) {
        if (i === line - 1) g[i].classList.add("current"); else g[i].classList.remove("current");
      }
    }
    // active-line overlay (no-wrap only; fixed line height)
    if (!st.wrap) {
      var lh = st.pre.children.length ? st.pre.children[0].offsetHeight : 0;
      if (!lh) lh = parseFloat(getComputedStyle(st.ta).lineHeight) || 20;
      st.active.style.display = "block";
      st.active.style.height = lh + "px";
      st.active.style.transform = "translateY(" + ((line - 1) * lh - st.ta.scrollTop) + "px)";
    } else {
      st.active.style.display = "none";
    }
    updateBracketMatch(st);
  }

  function updateBracketMatch(st) {
    // clear old
    if (st.matched) for (var i = 0; i < st.matched.length; i++) st.matched[i].classList.remove("fx-cm-match");
    st.matched = [];
    var val = st.ta.value, pos = st.ta.selectionStart;
    if (st.ta.selectionStart !== st.ta.selectionEnd) return;
    var offA = -1;
    if (pos < val.length && BRACKETS.indexOf(val[pos]) >= 0) offA = pos;
    else if (pos > 0 && BRACKETS.indexOf(val[pos - 1]) >= 0) offA = pos - 1;
    if (offA < 0) return;
    var offB = matchBracket(val, offA);
    if (offB < 0) return;
    [offA, offB].forEach(function (o) {
      var sp = st.bracketSpans[o];
      if (sp) { sp.classList.add("fx-cm-match"); st.matched.push(sp); }
    });
  }

  function matchBracket(val, off) {
    var ch = val[off];
    if (OPEN[ch]) {
      var depth = 0;
      for (var i = off; i < val.length; i++) {
        if (val[i] === ch) depth++;
        else if (val[i] === OPEN[ch]) { depth--; if (depth === 0) return i; }
      }
    } else if (CLOSE[ch]) {
      var d = 0;
      for (var j = off; j >= 0; j--) {
        if (val[j] === ch) d++;
        else if (val[j] === CLOSE[ch]) { d--; if (d === 0) return j; }
      }
    }
    return -1;
  }

  // ---- Editing primitives (undo-friendly via execCommand where available) ----
  function replaceRange(st, start, end, text, selStart, selEnd) {
    var ta = st.ta;
    ta.focus();
    ta.setSelectionRange(start, end);
    var ok = false;
    try { ok = document.execCommand("insertText", false, text); } catch (e) { ok = false; }
    if (!ok) {
      var v = ta.value;
      ta.value = v.slice(0, start) + text + v.slice(end);
      ta.dispatchEvent(new Event("input", { bubbles: true }));
    }
    if (typeof selStart === "number") ta.setSelectionRange(selStart, selEnd == null ? selStart : selEnd);
  }

  function currentLineBounds(val, pos) {
    var s = val.lastIndexOf("\n", pos - 1) + 1;
    var e = val.indexOf("\n", pos);
    if (e < 0) e = val.length;
    return { s: s, e: e };
  }

  function leadingWs(line) {
    var m = /^[ \t]*/.exec(line);
    return m ? m[0] : "";
  }

  // ---- Key handling ----
  function onKeyDown(st, ev) {
    var ta = st.ta;
    var val = ta.value;
    var s = ta.selectionStart, e = ta.selectionEnd;

    // Ctrl/Cmd + /  → toggle comment
    if ((ev.ctrlKey || ev.metaKey) && ev.key === "/") {
      ev.preventDefault();
      toggleComment(st);
      return;
    }
    // Tab / Shift-Tab
    if (ev.key === "Tab") {
      ev.preventDefault();
      if (s !== e || ev.shiftKey) {
        indentSelection(st, ev.shiftKey);
      } else {
        replaceRange(st, s, e, "  ", s + 2, s + 2);
      }
      return;
    }
    // Enter → auto-indent
    if (ev.key === "Enter") {
      ev.preventDefault();
      var lb = currentLineBounds(val, s);
      var line = val.slice(lb.s, s);
      var indent = leadingWs(line);
      var trimmed = line.replace(/\s+$/, "");
      var opensBlock = /[\{\[\(]$/.test(trimmed);
      var nextChar = val[e] || "";
      if (opensBlock && CLOSE[nextChar]) {
        // { | }  → newline+indent+2, newline+indent, caret on the middle line
        var inner = indent + "  ";
        var text = "\n" + inner + "\n" + indent;
        replaceRange(st, s, e, text, s + 1 + inner.length, s + 1 + inner.length);
      } else {
        var newIndent = opensBlock ? indent + "  " : indent;
        replaceRange(st, s, e, "\n" + newIndent, s + 1 + newIndent.length, s + 1 + newIndent.length);
      }
      return;
    }
    // Auto-close pairs
    if (OPEN[ev.key] || ev.key === '"') {
      var open = ev.key;
      var close = ev.key === '"' ? '"' : OPEN[open];
      // typing a quote where next char is a matching quote → step over
      if (ev.key === '"' && s === e && val[s] === '"') { ev.preventDefault(); ta.setSelectionRange(s + 1, s + 1); return; }
      ev.preventDefault();
      if (s !== e) {
        // wrap selection
        replaceRange(st, s, e, open + val.slice(s, e) + close, s + 1, e + 1);
      } else {
        replaceRange(st, s, e, open + close, s + 1, s + 1);
      }
      return;
    }
    // Typing a closing bracket that already sits under the caret → step over it
    if (CLOSE[ev.key] && s === e && val[s] === ev.key) {
      ev.preventDefault();
      ta.setSelectionRange(s + 1, s + 1);
      return;
    }
    // Backspace inside an empty pair → delete both
    if (ev.key === "Backspace" && s === e && s > 0) {
      var before = val[s - 1], after = val[s];
      if ((OPEN[before] && after === OPEN[before]) || (before === '"' && after === '"')) {
        ev.preventDefault();
        replaceRange(st, s - 1, s + 1, "", s - 1, s - 1);
        return;
      }
    }
  }

  function indentSelection(st, dedent) {
    var val = st.ta.value, s = st.ta.selectionStart, e = st.ta.selectionEnd;
    var startLine = val.lastIndexOf("\n", s - 1) + 1;
    var endLine = val.indexOf("\n", e - 1 >= s ? e - 1 : e);
    // Determine the block of full lines covered by the selection.
    var blockStart = startLine;
    var blockEnd = val.indexOf("\n", e);
    if (blockEnd < 0) blockEnd = val.length;
    var block = val.slice(blockStart, blockEnd);
    var lines = block.split("\n");
    var delta = 0, firstDelta = 0;
    var out = lines.map(function (ln, idx) {
      if (dedent) {
        var removed = 0;
        if (ln.startsWith("  ")) { ln = ln.slice(2); removed = 2; }
        else if (ln.startsWith("\t")) { ln = ln.slice(1); removed = 1; }
        else { var mm = /^ +/.exec(ln); if (mm) { var r = Math.min(2, mm[0].length); ln = ln.slice(r); removed = r; } }
        if (idx === 0) firstDelta = -removed;
        delta -= removed;
      } else {
        ln = "  " + ln;
        if (idx === 0) firstDelta = 2;
        delta += 2;
      }
      return ln;
    }).join("\n");
    replaceRange(st, blockStart, blockEnd, out, s + firstDelta, e + delta);
  }

  function toggleComment(st) {
    var val = st.ta.value, s = st.ta.selectionStart, e = st.ta.selectionEnd;
    var blockStart = val.lastIndexOf("\n", s - 1) + 1;
    var blockEnd = val.indexOf("\n", e);
    if (blockEnd < 0) blockEnd = val.length;
    var block = val.slice(blockStart, blockEnd);
    var lines = block.split("\n");
    var nonEmpty = lines.filter(function (l) { return l.trim().length; });
    var allCommented = nonEmpty.length > 0 && nonEmpty.every(function (l) { return /^\s*#/.test(l); });
    var out = lines.map(function (l) {
      if (!l.trim().length) return l;
      if (allCommented) return l.replace(/^(\s*)#\s?/, "$1");
      var m = /^(\s*)/.exec(l);
      return m[0] + "# " + l.slice(m[0].length);
    }).join("\n");
    replaceRange(st, blockStart, blockEnd, out, blockStart, blockStart + out.length);
  }

  // ---- Public API ----
  function create(el, dotNetRef, options) {
    options = options || {};
    var id = "cm" + (++counter);

    el.classList.add("fx-cm");
    el.innerHTML =
      '<div class="fx-cm-gutter"></div>' +
      '<div class="fx-cm-area">' +
      '<div class="fx-cm-activeline"></div>' +
      '<pre class="fx-cm-highlight" aria-hidden="true"></pre>' +
      '<textarea class="fx-cm-input" spellcheck="false" autocomplete="off" autocapitalize="off" autocorrect="off" wrap="off"></textarea>' +
      "</div>";

    var st = {
      id: id, el: el,
      gutter: el.querySelector(".fx-cm-gutter"),
      area: el.querySelector(".fx-cm-area"),
      active: el.querySelector(".fx-cm-activeline"),
      pre: el.querySelector(".fx-cm-highlight"),
      ta: el.querySelector(".fx-cm-input"),
      dotNet: dotNetRef,
      markers: [], markByLine: {}, bracketSpans: {}, matched: [],
      currentLine: 1, lineCount: 1, wrap: !!options.wrap
    };
    instances[id] = st;

    if (options.value != null) st.ta.value = options.value;
    applyOptions(st, options);

    st.ta.addEventListener("input", function () {
      render(st);
      if (st.dotNet) { try { st.dotNet.invokeMethodAsync("OnBufferChanged"); } catch (e) { } }
    });
    st.ta.addEventListener("scroll", function () { syncScroll(st); updateActiveLine(st); });
    var caretEvents = ["keyup", "click", "select", "focus"];
    caretEvents.forEach(function (evt) { st.ta.addEventListener(evt, function () { updateActiveLine(st); }); });
    st.ta.addEventListener("keydown", function (ev) { onKeyDown(st, ev); });

    render(st);
    return id;
  }

  function applyOptions(st, o) {
    if (o.fontSize) st.el.style.setProperty("--fx-cm-fs", o.fontSize + "px");
    if (o.wrap != null) {
      st.wrap = !!o.wrap;
      st.el.classList.toggle("wrap", st.wrap);
      st.ta.setAttribute("wrap", st.wrap ? "soft" : "off");
    }
  }

  function withInst(id, fn) { var st = instances[id]; if (st) return fn(st); }

  window.fenrixEditor = {
    create: create,

    dispose: function (id) { delete instances[id]; },

    getValue: function (id) { return withInst(id, function (st) { return st.ta.value; }); },

    setValue: function (id, text, keepCaret) {
      withInst(id, function (st) {
        var pos = keepCaret ? st.ta.selectionStart : 0;
        st.ta.value = text || "";
        render(st);
        var p = Math.min(pos, st.ta.value.length);
        st.ta.setSelectionRange(p, p);
        updateActiveLine(st);
      });
    },

    setOptions: function (id, o) { withInst(id, function (st) { applyOptions(st, o || {}); render(st); }); },

    setMarkers: function (id, markers) {
      withInst(id, function (st) { st.markers = markers || []; render(st); });
    },

    clearMarkers: function (id) { withInst(id, function (st) { st.markers = []; render(st); }); },

    focus: function (id) { withInst(id, function (st) { st.ta.focus(); }); },

    insertText: function (id, text) {
      withInst(id, function (st) {
        var s = st.ta.selectionStart, e = st.ta.selectionEnd;
        // Indent multi-line inserts to the current line's indentation.
        var val = st.ta.value;
        var lb = currentLineBounds(val, s);
        var indent = leadingWs(val.slice(lb.s, s));
        var body = String(text);
        if (indent && body.indexOf("\n") >= 0) {
          body = body.split("\n").map(function (l, i) { return i === 0 ? l : (indent + l); }).join("\n");
        }
        replaceRange(st, s, e, body, s + body.length, s + body.length);
        st.ta.focus();
      });
    },

    gotoLine: function (id, line) {
      withInst(id, function (st) {
        var off = offsetOfLine(st.ta.value, line);
        st.ta.focus();
        st.ta.setSelectionRange(off, off);
        // scroll so the line is roughly centered
        var lh = st.pre.children.length ? st.pre.children[0].offsetHeight : 20;
        st.ta.scrollTop = Math.max(0, (line - 1) * lh - st.ta.clientHeight / 2);
        syncScroll(st);
        updateActiveLine(st);
      });
    },

    toggleComment: function (id) { withInst(id, function (st) { toggleComment(st); }); },

    // Find/replace. Returns a small status object consumed by the Blazor find bar.
    find: function (id, opts) {
      return withInst(id, function (st) {
        opts = opts || {};
        var q = opts.query || "";
        if (!q) return { found: false, count: 0 };
        var val = st.ta.value;
        var hay = opts.caseSensitive ? val : val.toLowerCase();
        var needle = opts.caseSensitive ? q : q.toLowerCase();
        var count = countOcc(hay, needle);
        var from = opts.backwards
          ? hay.lastIndexOf(needle, Math.max(0, st.ta.selectionStart - 1))
          : hay.indexOf(needle, st.ta.selectionEnd);
        if (from < 0) from = opts.backwards ? hay.lastIndexOf(needle) : hay.indexOf(needle); // wrap
        if (from < 0) return { found: false, count: count };
        st.ta.focus();
        st.ta.setSelectionRange(from, from + q.length);
        var lh = st.pre.children.length ? st.pre.children[0].offsetHeight : 20;
        var ln = lineOfOffset(val, from);
        st.ta.scrollTop = Math.max(0, (ln - 1) * lh - st.ta.clientHeight / 2);
        syncScroll(st); updateActiveLine(st);
        return { found: true, count: count };
      });
    },

    replaceCurrent: function (id, opts) {
      return withInst(id, function (st) {
        opts = opts || {};
        var q = opts.query || "";
        var sel = st.ta.value.slice(st.ta.selectionStart, st.ta.selectionEnd);
        var eq = opts.caseSensitive ? (sel === q) : (sel.toLowerCase() === q.toLowerCase());
        if (eq && q) {
          replaceRange(st, st.ta.selectionStart, st.ta.selectionEnd, opts.replacement || "");
        }
        return window.fenrixEditor.find(id, opts);
      });
    },

    replaceAll: function (id, opts) {
      return withInst(id, function (st) {
        opts = opts || {};
        var q = opts.query || "";
        if (!q) return { count: 0 };
        var val = st.ta.value;
        var out = "", count = 0, i = 0;
        var hay = opts.caseSensitive ? val : val.toLowerCase();
        var needle = opts.caseSensitive ? q : q.toLowerCase();
        while (i < val.length) {
          var idx = hay.indexOf(needle, i);
          if (idx < 0) { out += val.slice(i); break; }
          out += val.slice(i, idx) + (opts.replacement || "");
          i = idx + q.length;
          count++;
        }
        if (count > 0) replaceRange(st, 0, val.length, out, 0, 0);
        return { count: count };
      });
    }
  };

  function countOcc(hay, needle) {
    if (!needle) return 0;
    var c = 0, i = 0;
    while ((i = hay.indexOf(needle, i)) >= 0) { c++; i += needle.length; }
    return c;
  }
})();

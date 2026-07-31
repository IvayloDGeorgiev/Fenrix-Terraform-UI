// Fenrix embedded terminal (Phase 12) — a dependency-free, minimal VT renderer for the ConPTY session.
// Matches the hand-rolled house style (fenrix-editor.js / fenrix-graph.js): no xterm.js, no CDN.
// Handles the common cases for shell + terraform output: printable text, \n, \r, backspace, SGR colours,
// erase-line and clear-screen. Full-screen TUIs (vim, less) are out of scope — documented in docs/31.
(function () {
    "use strict";

    const PALETTE = [
        "#2e3440", "#fb5c6b", "#34d399", "#fbbf24", "#7f7dff", "#c678dd", "#22d3ee", "#e8eaf2",
        "#6b7186", "#ff8a8a", "#5be0a8", "#ffd166", "#9b8cff", "#d68bea", "#67e8f9", "#ffffff"
    ];
    const MAX_LINES = 5000;

    const sessions = new Map();

    function makeCell(ch, state) {
        return { ch: ch, fg: state.fg, bold: state.bold };
    }

    function create(elementId) {
        const root = document.getElementById(elementId);
        if (!root) return null;
        root.classList.add("fx-term");
        root.tabIndex = 0;
        root.innerHTML = "<div class='fx-term-screen'></div>";
        const screen = root.querySelector(".fx-term-screen");

        const s = {
            root: root,
            screen: screen,
            lines: [[]],
            row: 0,
            col: 0,
            state: { fg: -1, bold: false },
            dotnet: null,
            keyHandler: null,
        };
        sessions.set(elementId, s);
        return s;
    }

    function ensure(s, row, col) {
        while (s.lines.length <= row) s.lines.push([]);
        const line = s.lines[row];
        while (line.length < col) line.push(makeCell(" ", { fg: -1, bold: false }));
    }

    function trimScroll(s) {
        if (s.lines.length > MAX_LINES) {
            const drop = s.lines.length - MAX_LINES;
            s.lines.splice(0, drop);
            s.row = Math.max(0, s.row - drop);
        }
    }

    function applySgr(s, params) {
        const codes = params.length ? params.split(";") : ["0"];
        for (const raw of codes) {
            const n = parseInt(raw || "0", 10);
            if (n === 0) { s.state.fg = -1; s.state.bold = false; }
            else if (n === 1) s.state.bold = true;
            else if (n === 22) s.state.bold = false;
            else if (n === 39) s.state.fg = -1;
            else if (n >= 30 && n <= 37) s.state.fg = n - 30;
            else if (n >= 90 && n <= 97) s.state.fg = n - 90 + 8;
        }
    }

    function write(elementId, text) {
        const s = sessions.get(elementId);
        if (!s || !text) return;

        for (let i = 0; i < text.length; i++) {
            const c = text[i];
            if (c === "\x1b") {
                // Escape sequence.
                const next = text[i + 1];
                if (next === "[") {
                    // CSI: ESC [ params letter
                    let j = i + 2, params = "";
                    while (j < text.length && !/[A-Za-z]/.test(text[j])) { params += text[j]; j++; }
                    const cmd = text[j];
                    if (cmd === "m") applySgr(s, params);
                    else if (cmd === "K") {
                        const mode = parseInt(params || "0", 10);
                        if (mode === 0) s.lines[s.row] = s.lines[s.row].slice(0, s.col);
                        else if (mode === 2) s.lines[s.row] = [];
                    } else if (cmd === "J") {
                        const mode = parseInt(params || "0", 10);
                        if (mode === 2 || mode === 3) { s.lines = [[]]; s.row = 0; s.col = 0; }
                    }
                    // Other CSI commands (cursor moves, etc.) are consumed and ignored.
                    i = j;
                } else if (next === "]") {
                    // OSC (e.g. window title): consume up to BEL or ESC \.
                    let j = i + 2;
                    while (j < text.length && text[j] !== "\x07" && !(text[j] === "\x1b" && text[j + 1] === "\\")) j++;
                    if (text[j] === "\x1b") j++;
                    i = j;
                } else {
                    i++; // skip the single following char of a 2-char escape
                }
                continue;
            }

            if (c === "\n") { s.row++; s.col = 0; ensure(s, s.row, 0); trimScroll(s); continue; }
            if (c === "\r") { s.col = 0; continue; }
            if (c === "\b" || c === "\x7f") { s.col = Math.max(0, s.col - 1); continue; }
            if (c === "\t") { const n = 8 - (s.col % 8); for (let k = 0; k < n; k++) { ensure(s, s.row, s.col); s.lines[s.row][s.col] = makeCell(" ", s.state); s.col++; } continue; }
            if (c === "\x07") continue; // bell

            ensure(s, s.row, s.col);
            s.lines[s.row][s.col] = makeCell(c, s.state);
            s.col++;
        }

        render(s);
    }

    function escapeHtml(ch) {
        return ch === "&" ? "&amp;" : ch === "<" ? "&lt;" : ch === ">" ? "&gt;" : ch;
    }

    function render(s) {
        let html = "";
        for (const line of s.lines) {
            let cur = null, run = "";
            const flush = () => {
                if (run.length === 0) return;
                const cls = cur && (cur.fg >= 0 || cur.bold);
                if (cls) {
                    const style = (cur.fg >= 0 ? "color:" + PALETTE[cur.fg] + ";" : "") + (cur.bold ? "font-weight:700;" : "");
                    html += "<span style='" + style + "'>" + run + "</span>";
                } else html += run;
                run = "";
            };
            for (const cell of line) {
                if (!cur || cell.fg !== cur.fg || cell.bold !== cur.bold) { flush(); cur = cell; }
                run += escapeHtml(cell.ch);
            }
            flush();
            html += "\n";
        }
        s.screen.innerHTML = html;
        s.root.scrollTop = s.root.scrollHeight;
    }

    function keyToData(e) {
        if (e.ctrlKey && e.key.length === 1) {
            const code = e.key.toUpperCase().charCodeAt(0);
            if (code >= 64 && code <= 95) return String.fromCharCode(code - 64); // Ctrl+A.._
        }
        switch (e.key) {
            case "Enter": return "\r";
            case "Backspace": return "\x7f";
            case "Tab": return "\t";
            case "Escape": return "\x1b";
            case "ArrowUp": return "\x1b[A";
            case "ArrowDown": return "\x1b[B";
            case "ArrowRight": return "\x1b[C";
            case "ArrowLeft": return "\x1b[D";
            case "Home": return "\x1b[H";
            case "End": return "\x1b[F";
            case "Delete": return "\x1b[3~";
            default: return e.key.length === 1 ? e.key : null;
        }
    }

    function measure(s) {
        const probe = document.createElement("span");
        probe.textContent = "M";
        probe.style.cssText = "position:absolute;visibility:hidden;font-family:inherit;font-size:inherit;";
        s.screen.appendChild(probe);
        const cw = probe.getBoundingClientRect().width || 8;
        const chLine = probe.getBoundingClientRect().height || 16;
        s.screen.removeChild(probe);
        const cols = Math.max(20, Math.floor(s.root.clientWidth / cw) - 1);
        const rows = Math.max(6, Math.floor(s.root.clientHeight / chLine));
        return { cols: cols, rows: rows };
    }

    window.fenrixTerminal = {
        init: function (elementId, dotnetRef) {
            let s = sessions.get(elementId) || create(elementId);
            if (!s) return { cols: 80, rows: 24 };
            s.dotnet = dotnetRef;
            s.keyHandler = function (e) {
                const data = keyToData(e);
                if (data !== null) {
                    e.preventDefault();
                    s.dotnet.invokeMethodAsync("OnInput", data);
                }
            };
            s.root.addEventListener("keydown", s.keyHandler);
            s.root.addEventListener("click", () => s.root.focus());
            const size = measure(s);
            s.root.focus();
            return size;
        },
        write: function (elementId, text) { write(elementId, text); },
        fit: function (elementId) {
            const s = sessions.get(elementId);
            if (!s) return { cols: 80, rows: 24 };
            const size = measure(s);
            if (s.dotnet) s.dotnet.invokeMethodAsync("OnResize", size.cols, size.rows);
            return size;
        },
        focus: function (elementId) { const s = sessions.get(elementId); if (s) s.root.focus(); },
        clear: function (elementId) { const s = sessions.get(elementId); if (s) { s.lines = [[]]; s.row = 0; s.col = 0; render(s); } },
        dispose: function (elementId) {
            const s = sessions.get(elementId);
            if (!s) return;
            if (s.keyHandler) s.root.removeEventListener("keydown", s.keyHandler);
            sessions.delete(elementId);
        }
    };
})();

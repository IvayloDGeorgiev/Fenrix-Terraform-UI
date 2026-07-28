// Fenrix dependency-graph renderer. Offline, dependency-free layered-DAG layout in SVG with pan/zoom and
// click-to-focus. Fed the parsed { nodes:[{id,label,kind}], edges:[{from,to}] } from GraphDotParser.
// See docs/25-execution-lifecycle.md (Read-only inspection).
(function () {
  const SVGNS = "http://www.w3.org/2000/svg";
  const NODE_H = 34;
  const CHAR_W = 7.2;      // approx monospace width for sizing
  const PAD_X = 14;
  const LAYER_GAP = 84;    // vertical gap between layers
  const NODE_GAP = 26;     // horizontal gap between nodes in a layer
  const instances = {};    // containerId -> { cleanup }

  function measure(label) {
    return Math.max(90, Math.min(320, PAD_X * 2 + (label || "").length * CHAR_W));
  }

  // Longest-path layering over edges (source -> target). Guards against cycles with an iteration cap.
  function assignLayers(nodes, edges) {
    const layer = new Map();
    nodes.forEach(n => layer.set(n.id, 0));
    const incoming = new Map();
    nodes.forEach(n => incoming.set(n.id, 0));
    edges.forEach(e => { if (incoming.has(e.to)) incoming.set(e.to, incoming.get(e.to) + 1); });

    const cap = nodes.length + 2;
    for (let i = 0; i < cap; i++) {
      let changed = false;
      edges.forEach(e => {
        if (!layer.has(e.from) || !layer.has(e.to)) return;
        const want = layer.get(e.from) + 1;
        if (want > layer.get(e.to)) { layer.set(e.to, want); changed = true; }
      });
      if (!changed) break;
    }
    return layer;
  }

  function layout(data) {
    const nodes = data.nodes || [];
    const edges = (data.edges || []).filter(e => e.from && e.to);
    const layer = assignLayers(nodes, edges);

    const byLayer = new Map();
    nodes.forEach(n => {
      const l = layer.get(n.id) || 0;
      if (!byLayer.has(l)) byLayer.set(l, []);
      byLayer.get(l).push(n);
    });

    const pos = new Map();
    let maxWidth = 0;
    const layers = [...byLayer.keys()].sort((a, b) => a - b);
    layers.forEach(l => {
      const row = byLayer.get(l).sort((a, b) => (a.label || "").localeCompare(b.label || ""));
      let x = 0;
      row.forEach(n => {
        const w = measure(n.label);
        pos.set(n.id, { x, y: l * LAYER_GAP, w, h: NODE_H });
        x += w + NODE_GAP;
      });
      maxWidth = Math.max(maxWidth, x - NODE_GAP);
    });

    // Centre each layer horizontally against the widest layer.
    layers.forEach(l => {
      const row = byLayer.get(l);
      const rowWidth = row.reduce((s, n) => s + pos.get(n.id).w, 0) + NODE_GAP * (row.length - 1);
      const offset = (maxWidth - rowWidth) / 2;
      row.forEach(n => { pos.get(n.id).x += offset; });
    });

    const height = (layers.length ? layers[layers.length - 1] * LAYER_GAP : 0) + NODE_H;
    return { pos, edges, width: Math.max(maxWidth, 120), height: Math.max(height, 60) };
  }

  function el(tag, attrs) {
    const e = document.createElementNS(SVGNS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    return e;
  }

  function render(containerId, json) {
    const host = document.getElementById(containerId);
    if (!host) return;
    dispose(containerId);
    host.innerHTML = "";

    let data;
    try { data = typeof json === "string" ? JSON.parse(json) : json; }
    catch (e) { return; }
    if (!data || !data.nodes || data.nodes.length === 0) return;

    const lay = layout(data);
    const svg = el("svg", { class: "fx-graph-svg", width: "100%", height: "100%" });

    const defs = el("defs", {});
    const marker = el("marker", {
      id: "fx-arrow-" + containerId, viewBox: "0 0 10 10", refX: "9", refY: "5",
      markerWidth: "7", markerHeight: "7", orient: "auto-start-reverse"
    });
    marker.appendChild(el("path", { d: "M0 0 L10 5 L0 10 z", class: "fx-graph-arrowhead" }));
    defs.appendChild(marker);
    svg.appendChild(defs);

    const viewport = el("g", { class: "fx-graph-viewport" });
    svg.appendChild(viewport);

    const edgeLayer = el("g", { class: "fx-graph-edges" });
    const nodeLayer = el("g", { class: "fx-graph-nodes" });
    viewport.appendChild(edgeLayer);
    viewport.appendChild(nodeLayer);

    data.edges.forEach(e => {
      const a = lay.pos.get(e.from), b = lay.pos.get(e.to);
      if (!a || !b) return;
      const x1 = a.x + a.w / 2, y1 = a.y + a.h;
      const x2 = b.x + b.w / 2, y2 = b.y;
      const my = (y1 + y2) / 2;
      const path = el("path", {
        d: `M ${x1} ${y1} C ${x1} ${my}, ${x2} ${my}, ${x2} ${y2}`,
        class: "fx-graph-edge",
        "marker-end": `url(#fx-arrow-${containerId})`,
        "data-from": e.from, "data-to": e.to
      });
      edgeLayer.appendChild(path);
    });

    data.nodes.forEach(n => {
      const p = lay.pos.get(n.id);
      if (!p) return;
      const g = el("g", { class: "fx-graph-node kind-" + (n.kind || "resource").toLowerCase(), "data-id": n.id, tabindex: "0" });
      g.appendChild(el("rect", { x: p.x, y: p.y, width: p.w, height: p.h, rx: "7" }));
      const text = el("text", { x: p.x + p.w / 2, y: p.y + p.h / 2 + 4, "text-anchor": "middle" });
      text.textContent = n.label || n.id;
      g.appendChild(text);
      g.addEventListener("click", () => focusNode(nodeLayer, edgeLayer, n.id));
      nodeLayer.appendChild(g);
    });

    host.appendChild(svg);

    // Fit-to-view: set viewBox to the content bbox with a margin.
    const margin = 40;
    svg.setAttribute("viewBox", `${-margin} ${-margin} ${lay.width + margin * 2} ${lay.height + margin * 2}`);

    const state = { scale: 1, tx: 0, ty: 0, dragging: false, sx: 0, sy: 0 };
    function apply() { viewport.setAttribute("transform", `translate(${state.tx} ${state.ty}) scale(${state.scale})`); }

    function onWheel(ev) {
      ev.preventDefault();
      const factor = ev.deltaY < 0 ? 1.1 : 1 / 1.1;
      const next = Math.max(0.2, Math.min(4, state.scale * factor));
      state.scale = next;
      apply();
    }
    function onDown(ev) { state.dragging = true; state.sx = ev.clientX - state.tx; state.sy = ev.clientY - state.ty; svg.classList.add("dragging"); }
    function onMove(ev) { if (!state.dragging) return; state.tx = ev.clientX - state.sx; state.ty = ev.clientY - state.sy; apply(); }
    function onUp() { state.dragging = false; svg.classList.remove("dragging"); }

    svg.addEventListener("wheel", onWheel, { passive: false });
    svg.addEventListener("mousedown", onDown);
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);

    instances[containerId] = {
      cleanup() {
        svg.removeEventListener("wheel", onWheel);
        svg.removeEventListener("mousedown", onDown);
        window.removeEventListener("mousemove", onMove);
        window.removeEventListener("mouseup", onUp);
      }
    };
  }

  function focusNode(nodeLayer, edgeLayer, id) {
    const already = nodeLayer.querySelector(`.fx-graph-node[data-id="${cssEscape(id)}"]`)?.classList.contains("focused");
    nodeLayer.querySelectorAll(".fx-graph-node").forEach(n => n.classList.remove("focused", "dimmed"));
    edgeLayer.querySelectorAll(".fx-graph-edge").forEach(e => e.classList.remove("active", "dimmed"));
    if (already) return; // toggle off

    const neighbours = new Set([id]);
    edgeLayer.querySelectorAll(".fx-graph-edge").forEach(e => {
      const f = e.getAttribute("data-from"), t = e.getAttribute("data-to");
      if (f === id || t === id) { e.classList.add("active"); neighbours.add(f); neighbours.add(t); }
      else e.classList.add("dimmed");
    });
    nodeLayer.querySelectorAll(".fx-graph-node").forEach(n => {
      const nid = n.getAttribute("data-id");
      if (nid === id) n.classList.add("focused");
      else if (!neighbours.has(nid)) n.classList.add("dimmed");
    });
  }

  function cssEscape(s) { return (window.CSS && CSS.escape) ? CSS.escape(s) : s.replace(/["\\]/g, "\\$&"); }

  function dispose(containerId) {
    if (instances[containerId]) { try { instances[containerId].cleanup(); } catch (e) { } delete instances[containerId]; }
  }

  window.fenrixGraph = { render, dispose };
})();

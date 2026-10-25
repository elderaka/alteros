const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
function at(root, path) { return (path || "").split(".").filter(Boolean).reduce((value, key) => value == null ? undefined : value[key], root); }

export function renderGraph(container, graph, sourceDocument) {
  if (!graph?.enabled) { container.innerHTML = `<p class="empty-state">No graph structure is declared for this kind.</p>`; return; }
  const nodes = at(sourceDocument, graph.nodesPath);
  if (!Array.isArray(nodes) || !nodes.length) { container.innerHTML = `<p class="empty-state">No nodes yet. Add an item in the ${esc(graph.nodesPath)} section.</p>`; return; }
  const idField = graph.idField || "id";
  const edges = graph.edgeFields || [];
  container.innerHTML = `<div class="graph-note">${esc(graph.note || (graph.readOnly ? "Read-only source structure" : "Flow structure"))}</div><div class="flow-list">${nodes.map((node, index) => {
    const id = node?.[idField] || `item-${index + 1}`;
    const transitions = edges.map((field) => node?.[field] ? `<span class="edge"><i>${esc(field)}</i> → ${esc(node[field])}</span>` : "").filter(Boolean).join("");
    const kind = node?.kind || node?.type || "record";
    return `<button class="flow-node ${graph.readOnly ? "read-only" : ""}" data-index="${index}"><span class="flow-node-kind">${esc(kind)}</span><strong>${esc(id)}</strong>${transitions || `<small>${graph.readOnly ? "Legacy item" : "No outgoing edge"}</small>`}</button>`;
  }).join(`<div class="flow-connector" aria-hidden="true">↓</div>`)}</div>`;
  container.querySelectorAll(".flow-node").forEach((button) => button.addEventListener("click", () => {
    container.querySelectorAll(".flow-node").forEach((item) => item.classList.remove("active")); button.classList.add("active");
    if (!graph.readOnly) {
      const target = globalThis.document.querySelector(`[data-object-path="${graph.nodesPath}.${button.dataset.index}"]`);
      if (target) {
        globalThis.document.querySelectorAll(".array-row.focused").forEach((item) => item.classList.remove("focused"));
        target.classList.add("focused"); target.scrollIntoView({ behavior: "smooth", block: "center" });
      }
    }
  }));
}

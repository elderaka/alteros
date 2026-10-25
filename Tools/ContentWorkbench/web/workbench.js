import { beginEditorSession, applySaveResult, captureSave, isEditorDirty, listAllRecords, mayLeaveEditor, recordEditorChange } from "./editor-state.js";
import { cloneLossless, parseLossless, stringifyLossless } from "./lossless-json.js";
import { mountDraftEditor } from "./draft-editor.js";
import { renderGraph } from "./graph-editor.js";
import { renderPreview } from "./playground.js";

const token = document.querySelector('meta[name="workbench-token"]').content;
const kinds = ["dialogue", "scenario", "topic", "furniture", "item", "action"];
const state = { kindId: "", descriptor: null, records: [], document: null, sourceKind: "", documentId: "", logicalPath: "", hash: null, isNew: false, sessionId: 0, navigationVersion: 0, savePending: false, conflict: null, referenceCandidates: [], referenceSources: [], referenceCursor: "", referenceStore: null, referenceSourceKind: "", selectedReferencePins: new Map(), referenceGeneration: 0, referencePageGeneration: 0, referenceSnapshot: null, referenceStatus: "unresolved" };
const $ = (selector) => document.querySelector(selector);
const escapeHtml = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

async function command(operation, payload = {}) {
  const response = await fetch("/api/command", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Workbench-Token": token },
    body: stringifyLossless({ protocolVersion: 1, requestId: `ui-${crypto.randomUUID()}`, operation, payload })
  });
  const result = parseLossless(await response.text());
  if (!response.ok || result.status !== "ok") throw Object.assign(new Error(result.diagnostics?.map((item) => item.message).join("\n") || result.status), { result });
  return result.result;
}

function toast(message, bad = false) {
  const box = $("#toast"); box.textContent = message; box.className = `toast visible${bad ? " error" : ""}`;
  window.clearTimeout(toast.timer); toast.timer = window.setTimeout(() => box.classList.remove("visible"), 3200);
}

function setDiagnostics(result) {
  const list = result?.diagnostics || [];
  $("#diagnostics").innerHTML = list.length ? list.map((item) => `<div class="diagnostic"><strong>${escapeHtml(item.code)}</strong><code>${escapeHtml(item.path || "/")}</code><p>${escapeHtml(item.message)}</p></div>`).join("") : `<p class="empty-state">${result?.valid === false ? "Validation failed without diagnostics." : "No diagnostics."}</p>`;
  const box = $("#validation-result"); box.textContent = result ? (result.valid ? "Source validation passed" : "Source validation found issues") : "";
  box.className = result ? `validation-banner ${result.valid ? "success" : "failure"}` : "";
}

function recordId(summary) { return summary.documentId || summary.id || ""; }
function currentRawSource() { return state.document ? $("#source-json").value : undefined; }
function dirty() { return isEditorDirty(state, currentRawSource()); }
function pendingSourceJson() { return state.document != null && currentRawSource() !== stringifyLossless(state.document, 2); }

function updateHistoryControls() {
  const pending = pendingSourceJson();
  $("#undo").disabled = state.savePending || pending || !state.history?.canUndo;
  $("#redo").disabled = state.savePending || pending || !state.history?.canRedo;
}

function updateDirtyPresentation() {
  const pending = pendingSourceJson();
  $("#raw-json-pending").hidden = !pending;
  $("#editor-content").inert = pending;
  $("#editor-content").setAttribute("aria-disabled", String(pending));
  if (state.document) {
    const unsaved = dirty();
    $("#revision-meta").textContent = state.isNew ? "New draft · unsaved" : `Revision ${state.hash || "unknown"}${unsaved ? " · unsaved changes" : ""}`;
  }
  updateHistoryControls();
  $("#save").disabled = state.savePending || !state.document || pending;
  $("#validate").disabled = !state.document || pending;
  $("#preview").disabled = pending;
}

function editorReferenceCandidates() {
  return state.referenceCandidates.reduce((grouped, candidate) => {
    (grouped[candidate.targetKind] ||= []).push(candidate);
    return grouped;
  }, {});
}

function renderEditorContent() {
  if (!state.document || !state.descriptor) return;
  $("#editor-content").replaceChildren(mountDraftEditor({ descriptor: state.descriptor, document: cloneLossless(state.document),
    referenceCandidates: editorReferenceCandidates(), referenceGeneration: state.referenceGeneration,
    getReferenceGeneration: () => state.referenceGeneration, canEdit: () => !pendingSourceJson(), onChange: (doc) => {
      if (pendingSourceJson()) return;
      const accepted = cloneLossless(doc);
      recordEditorChange(state, accepted); $("#source-json").value = stringifyLossless(accepted, 2);
      renderGraph($("#graph-panel"), state.descriptor.graph, accepted); updateDirtyPresentation();
    } }));
}

function mayDiscardCurrent() {
  return mayLeaveEditor(state, currentRawSource(), () => window.confirm("This document has unsaved changes. Discard them and continue? The current editor will remain open if you cancel."));
}

function editorChangedSince(sessionId, editVersion, rawSource) {
  return state.sessionId !== sessionId || state.editVersion !== editVersion || currentRawSource() !== rawSource;
}

function renderKinds(filter = "") {
  $("#kind-list").innerHTML = kinds.filter((kind) => kind.includes(filter.toLowerCase())).map((kind) => `<button class="kind-item ${state.kindId === kind ? "selected" : ""}" data-kind="${kind}"><span class="kind-dot ${kind}"></span>${kind[0].toUpperCase() + kind.slice(1)}<span class="kind-chevron">›</span></button>`).join("");
  $("#kind-list").querySelectorAll("[data-kind]").forEach((button) => button.addEventListener("click", () => selectKind(button.dataset.kind)));
}

function renderRecords(filter = "") {
  const needle = filter.toLowerCase();
  $("#record-list").innerHTML = state.records.filter((record) => recordId(record).toLowerCase().includes(needle)).map((record) => {
    const id = recordId(record);
    return `<button class="record-item ${state.documentId === id ? "selected" : ""}" data-id="${escapeHtml(id)}" data-kind="${escapeHtml(record.kind || "")}"><span>${escapeHtml(id)}</span><small>${escapeHtml(record.kind || "")} · ${escapeHtml((record.revisionHash || record.contentHash || "").slice(0, 10))}</small></button>`;
  }).join("") || `<p class="empty-state">No records yet. Use New to create a draft.</p>`;
  $("#record-list").querySelectorAll("[data-id]").forEach((button) => button.addEventListener("click", () => openRecord(button.dataset.id, button.dataset.kind)));
}

async function recordsFor(descriptor) {
  const pages = await Promise.all(descriptor.sourceKinds.map((sourceKind) => listAllRecords(command, sourceKind)));
  return pages.flat().sort((a, b) => recordId(a).localeCompare(recordId(b)) || a.kind.localeCompare(b.kind));
}

async function refreshRecords(kindId = state.kindId, descriptor = state.descriptor) {
  if (!descriptor) return;
  const records = await recordsFor(descriptor);
  if (kindId === state.kindId) { state.records = records; renderRecords($("#record-filter").value); }
}

function clearEditor() {
  state.sessionId++;
  state.document = null; state.sourceKind = ""; state.documentId = ""; state.logicalPath = "";
  state.hash = null; state.isNew = false; state.history = null; state.conflict = null;
  $("#editor").hidden = true; $("#welcome").hidden = false;
}

async function selectKind(kindId) {
  if (!kindId || !mayDiscardCurrent()) { renderKinds($("#kind-filter").value); return; }
  const sessionId = state.sessionId; const editVersion = state.editVersion; const rawSource = currentRawSource();
  const requestVersion = ++state.navigationVersion;
  try {
    const descriptor = (await command("schema.describe", { kindId })).descriptor;
    const records = await recordsFor(descriptor);
    if (requestVersion !== state.navigationVersion) return;
    if (editorChangedSince(sessionId, editVersion, rawSource) && !mayDiscardCurrent()) { renderKinds($("#kind-filter").value); return; }
    state.kindId = kindId; state.descriptor = descriptor; state.records = records;
    clearEditor(); renderKinds($("#kind-filter").value);
    $("#new-record").disabled = !descriptor.capabilities.create;
    $("#kind-kicker").textContent = `${descriptor.displayName} · ${descriptor.defaultSourceKind}`;
    $("#welcome").hidden = true;
    renderRecords($("#record-filter").value);
  } catch (error) { toast(error.message, true); }
}

async function openRecord(id, sourceKind = "") {
  if (!mayDiscardCurrent()) return;
  const sessionId = state.sessionId; const editVersion = state.editVersion; const rawSource = currentRawSource();
  const requestVersion = ++state.navigationVersion;
  try {
    const result = await command("authoring.read", { documentId: id });
    const selectedSourceKind = sourceKind || result.kind;
    const descriptor = (await command("schema.describe", { kindId: state.kindId, sourceKind: selectedSourceKind })).descriptor;
    if (requestVersion !== state.navigationVersion) return;
    if (editorChangedSince(sessionId, editVersion, rawSource) && !mayDiscardCurrent()) { renderRecords($("#record-filter").value); return; }
    state.descriptor = descriptor;
    beginEditorSession(state, { kindId: state.kindId, sourceKind: selectedSourceKind,
      documentId: result.documentId || id, logicalPath: result.logicalPath, hash: result.contentHash,
      document: result.document, isNew: false });
    renderEditor(); renderRecords($("#record-filter").value);
  } catch (error) { toast(error.message, true); }
}

function startNew() {
  if (!state.descriptor || !mayDiscardCurrent()) return;
  state.navigationVersion++;
  beginEditorSession(state, { kindId: state.kindId, sourceKind: state.descriptor.defaultSourceKind,
    document: cloneLossless(state.descriptor.documentTemplate), isNew: true });
  renderEditor();
}

function identityField() {
  return state.descriptor.sourceContracts?.find((item) => item.kind === state.sourceKind)?.identityField || state.descriptor.identityField || "id";
}

function currentSourceContract() {
  return state.descriptor.sourceContracts?.find((item) => item.kind === state.sourceKind) || {};
}

function documentTitle() {
  if (currentSourceContract().fixedDocumentId) return "Furniture catalogue settings";
  return state.isNew ? `New ${state.descriptor.displayName}` : (state.document[identityField()] || state.documentId);
}

function renderEditor() {
  $("#welcome").hidden = true; $("#editor").hidden = false;
  $("#document-title").textContent = documentTitle();
  $("#kind-kicker").textContent = `${state.descriptor.displayName} · ${state.sourceKind}`;
  const sourceKindWrap = $("#source-kind-wrap");
  sourceKindWrap.hidden = !state.isNew || state.descriptor.sourceKinds.length < 2;
  if (!sourceKindWrap.hidden) {
    const select = $("#source-kind");
    select.replaceChildren(...state.descriptor.sourceKinds.map((kind) => new Option(kind, kind)));
    select.value = state.sourceKind;
    select.onchange = async () => {
      const oldKind = state.sourceKind;
      if (!mayDiscardCurrent()) { select.value = oldKind; return; }
      const sessionId = state.sessionId; const editVersion = state.editVersion; const rawSource = currentRawSource();
      const requestVersion = ++state.navigationVersion;
      try {
        const descriptor = (await command("schema.describe", { kindId: state.kindId, sourceKind: select.value })).descriptor;
        if (requestVersion !== state.navigationVersion) return;
        if (editorChangedSince(sessionId, editVersion, rawSource) && !mayDiscardCurrent()) { select.value = oldKind; return; }
        state.descriptor = descriptor;
        beginEditorSession(state, { kindId: state.kindId, sourceKind: select.value,
          document: cloneLossless(descriptor.documentTemplate), isNew: true });
        renderEditor();
      } catch (error) { select.value = oldKind; toast(error.message, true); }
    };
  }
  $("#source-json").value = stringifyLossless(state.document, 2);
  const deferred = state.descriptor.deferredFields || [];
  $("#deferred-notice").hidden = deferred.length === 0;
  $("#deferred-notice").innerHTML = deferred.map((item) => `<strong>${escapeHtml(item.fieldId)} unavailable:</strong> ${escapeHtml(item.reason)}`).join("<br>");
  const conflictNotice = $("#conflict-notice");
  conflictNotice.hidden = !state.conflict;
  conflictNotice.textContent = state.conflict ? "The stored revision changed elsewhere. Your local version remains in this editor; copy it from Source JSON before explicitly reloading or opening another revision." : "";
  renderEditorContent();
  renderGraph($("#graph-panel"), state.descriptor.graph, state.document);
  $("#preview").hidden = !state.descriptor.capabilities.preview;
  $("#preview").onclick = async () => {
    try {
      $("#preview-result").textContent = "";
      if (pendingSourceJson()) throw new Error("Apply or discard pending Source JSON before previewing.");
      const sourcePins = normalizedReferencePins().filter((pin) => pin.documentId !== state.documentId);
      if (sourcePins.length > 0 && !state.referenceStore)
        throw new Error("Refresh the exact source snapshot before previewing these pinned references.");
      const sourceContext = sourcePins.length === 0 ? {} : {
        sourcePins,
        expectedStoreId: state.referenceStore.storeId,
        expectedStoreRevision: state.referenceStore.storeRevision,
      };
      const payload = await renderPreview(state.kindId, state.document, sourceContext);
      const result = await command("preview.run", payload);
      $("#preview-result").textContent = JSON.stringify(result, null, 2);
      setDiagnostics(result); toast("Synthetic preview completed");
    } catch (error) { toast(error.message, true); if (error.result) setDiagnostics(error.result.result || error.result); }
  };
  setDiagnostics(null); updateDirtyPresentation(); renderRecords($("#record-filter").value);
}

function commitPendingJson() {
  const raw = $("#source-json").value;
  if (raw === stringifyLossless(state.document, 2)) return;
  const parsed = parseLossless(raw);
  if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") throw new Error("Source JSON root must be an object.");
  recordEditorChange(state, parsed);
  renderEditor();
}

async function validateDocument() {
  try {
    if (pendingSourceJson()) throw new Error("Apply or discard pending Source JSON before validation.");
    const result = await command("content.validate", { kindId: state.kindId, sourceKind: state.sourceKind, document: state.document });
    setDiagnostics(result); toast(result.valid ? "Producer validation passed" : "Validation needs attention", !result.valid);
  } catch (error) { toast(`Validation could not run: ${error.message}`, true); setDiagnostics(error.result?.result || error.result); }
}

async function saveDocument() {
  if (state.savePending || !state.document) return;
  let capture;
  try {
    if (pendingSourceJson()) throw new Error("Apply or discard pending Source JSON before saving.");
    capture = captureSave(state, identityField(), `ui-${crypto.randomUUID()}`, currentSourceContract());
  } catch (error) { toast(error.message, true); return; }
  state.savePending = true; updateDirtyPresentation();
  try {
    const result = await command("authoring.write", capture.payload);
    applySaveResult(state, capture, result);
    toast("Draft revision saved");
    if (capture.kindId === state.kindId) {
      try { await refreshRecords(capture.kindId, state.descriptor); }
      catch (refreshError) { toast(`Saved revision, but the catalogue refresh failed: ${refreshError.message}`, true); }
    }
    if (state.sessionId === capture.sessionId) {
      $("#document-title").textContent = documentTitle();
      state.conflict = null;
      $("#conflict-notice").hidden = true;
    }
  } catch (error) {
    if (error.result?.status === "conflict") {
      if (state.sessionId === capture.sessionId) {
        state.conflict = { expectedHash: capture.payload.expectedHash, localDocument: capture.document };
        $("#conflict-notice").hidden = false;
        $("#conflict-notice").textContent = "The stored revision changed elsewhere. Your local version remains in this editor; copy it from Source JSON before explicitly reloading or opening another revision.";
      }
      toast("Save conflict. Local edits are retained in the editor.", true);
    } else toast(error.message, true);
  } finally {
    state.savePending = false; updateDirtyPresentation();
  }
}

function normalizedReferencePins() {
  return [...state.selectedReferencePins.values()]
    .map((item) => ({ documentId: item.documentId, revisionHash: item.revisionHash }))
    .sort((left, right) => left.documentId.localeCompare(right.documentId) || left.revisionHash.localeCompare(right.revisionHash));
}

function referenceContext(sourceKind = "") {
  return {
    generation: state.referenceGeneration,
    targetKind: $("#reference-kind").value,
    sourceKind,
    storeId: state.referenceStore?.storeId || "",
    storeRevision: state.referenceStore?.storeRevision,
    pinsJson: JSON.stringify(normalizedReferencePins())
  };
}

function referenceContextIsCurrent(context) {
  return context.generation === state.referenceGeneration && context.targetKind === $("#reference-kind").value &&
    context.sourceKind === $("#reference-source-kind").value && context.storeId === (state.referenceStore?.storeId || "") &&
    String(context.storeRevision) === String(state.referenceStore?.storeRevision) && context.pinsJson === JSON.stringify(normalizedReferencePins());
}

function invalidateReferenceCandidates(status = "Selection changed; resolve references again.") {
  state.referenceGeneration++;
  state.referenceCandidates = [];
  state.referenceSnapshot = null;
  state.referenceStatus = status;
  renderReferenceSnapshot();
  updateReferenceResolveButton();
  if (state.document && !pendingSourceJson()) renderEditorContent();
  return state.referenceGeneration;
}

function renderReferenceSnapshot() {
  const lines = [];
  if (state.referenceStore) lines.push(`Store ${state.referenceStore.storeId} · revision ${state.referenceStore.storeRevision}`);
  lines.push(state.referenceStatus);
  if (state.referenceSnapshot) lines.push(`Snapshot ${state.referenceSnapshot.snapshotHash}`);
  $("#reference-snapshot").textContent = lines.join(" · ");
  const targetKind = $("#reference-kind").value;
  const root = $("#reference-list");
  root.replaceChildren(...state.referenceCandidates.map((candidate) => {
    const row = document.createElement("div"); row.className = "reference-item";
    const title = document.createElement("span"); title.textContent = `${candidate.targetKind}: ${candidate.refId}`;
    const detail = document.createElement("small"); detail.textContent = `store ${candidate.ref.store} · rev ${candidate.ref.revision} · hash ${candidate.ref.expectedHash}`;
    row.append(title, detail);
    for (const source of candidate.ownerSources || []) { const owner = document.createElement("small"); owner.textContent = `Source: ${source.sourceKind} · ${source.logicalPath} · ${source.sourceId}`; row.append(owner); }
    return row;
  }));
  if (state.referenceSnapshot && state.referenceCandidates.length === 0) {
    const empty = document.createElement("p"); empty.className = "empty-state";
    empty.textContent = `No resolved ${targetKind} reference exists in this selected snapshot.`; root.append(empty);
  }
}

function renderSelectedReferencePins() {
  const root = $("#reference-selected-sources");
  if (!root) return;
  const selected = [...state.selectedReferencePins.values()].sort((a, b) => a.documentId.localeCompare(b.documentId));
  root.replaceChildren(...selected.map((pin) => {
    const row = document.createElement("div"); row.className = "reference-item";
    const detail = document.createElement("span"); detail.textContent = `${pin.documentId} · ${pin.kind} · ${pin.revisionHash}`;
    const remove = document.createElement("button"); remove.type = "button"; remove.className = "button small"; remove.textContent = "Remove";
    remove.addEventListener("click", () => {
      state.selectedReferencePins.delete(pin.documentId); invalidateReferenceCandidates(); renderSelectedReferencePins(); renderReferenceSources();
    });
    row.append(detail, remove); return row;
  }));
  if (selected.length === 0) { const empty = document.createElement("p"); empty.className = "empty-state"; empty.textContent = "No source revisions selected."; root.append(empty); }
}

function renderReferenceSources() {
  const root = $("#reference-list");
  root.replaceChildren(...state.referenceSources.map((item) => {
    const row = document.createElement("label"); row.className = "reference-item";
    const checkbox = document.createElement("input"); checkbox.type = "checkbox"; checkbox.dataset.documentId = item.documentId;
    checkbox.dataset.revisionHash = item.revisionHash; checkbox.checked = state.selectedReferencePins.get(item.documentId)?.revisionHash === item.revisionHash;
    checkbox.addEventListener("change", () => {
      if (checkbox.checked) state.selectedReferencePins.set(item.documentId, {
        documentId: item.documentId, revisionHash: item.revisionHash, kind: item.kind, logicalPath: item.logicalPath
      });
      else if (state.selectedReferencePins.get(item.documentId)?.revisionHash === item.revisionHash) state.selectedReferencePins.delete(item.documentId);
      invalidateReferenceCandidates(); renderSelectedReferencePins(); renderReferenceSources();
    });
    const name = document.createElement("span"); name.textContent = item.documentId;
    const revision = document.createElement("small"); revision.textContent = `${item.kind} · ${item.revisionHash}`;
    row.append(checkbox, name, revision); return row;
  }));
  if (state.referenceCursor) {
    const more = document.createElement("button"); more.type = "button"; more.className = "button small"; more.textContent = "Load next page (100 max)";
    more.addEventListener("click", async () => { more.disabled = true; try { await loadReferenceSourcePage(false); } catch (error) { toast(error.message, true); } });
    root.append(more);
  }
}

function updateReferenceResolveButton() {
  $("#resolve-references").disabled = state.selectedReferencePins.size === 0 || state.selectedReferencePins.size > 100 || !state.referenceStore;
}

async function browseReferences() {
  const generation = invalidateReferenceCandidates("Loading source kinds and authoring revision…");
  const targetKind = $("#reference-kind").value;
  const selector = $("#reference-source-kind");
  state.referenceStore = null; state.referenceSources = []; state.referenceCursor = "";
  renderSelectedReferencePins(); renderReferenceSources(); updateReferenceResolveButton();
  try {
    const descriptors = await Promise.all(kinds.map(async (kindId) => (await command("schema.describe", { kindId })).descriptor));
    const sourceKinds = [...new Set(descriptors.flatMap((descriptor) => descriptor.referenceSourceKinds || descriptor.sourceKinds))].sort();
    const priorSourceKind = state.referenceSourceKind;
    if (generation !== state.referenceGeneration || targetKind !== $("#reference-kind").value) return;
    selector.replaceChildren(...sourceKinds.map((sourceKind) => new Option(sourceKind, sourceKind)));
    state.referenceSourceKind = sourceKinds.includes(priorSourceKind) ? priorSourceKind : (sourceKinds[0] || "");
    selector.value = state.referenceSourceKind; selector.disabled = sourceKinds.length === 0;
    const info = await command("authoring.info");
    if (generation !== state.referenceGeneration || targetKind !== $("#reference-kind").value) return;
    state.referenceStore = info.store;
    state.referenceStatus = "Choose exact source document revisions; results remain detached and unpublished.";
    renderReferenceSnapshot(); updateReferenceResolveButton();
    await loadReferenceSourcePage(true);
  } catch (error) {
    if (generation !== state.referenceGeneration) return;
    state.referenceCandidates = []; state.referenceStatus = `Source listing unavailable: ${error.message}`;
    renderReferenceSnapshot(); toast(error.message, true);
  }
}

async function loadReferenceSourcePage(reset = false) {
  const sourceKind = $("#reference-source-kind").value;
  if (!sourceKind || !state.referenceStore) return;
  const context = referenceContext(sourceKind);
  const pageGeneration = ++state.referencePageGeneration;
  const cursor = reset ? "" : state.referenceCursor;
  if (reset) { state.referenceSources = []; state.referenceCursor = ""; }
  try {
    const page = await command("authoring.list", { kind: sourceKind, cursor, limit: 100 });
    if (!referenceContextIsCurrent(context) || pageGeneration !== state.referencePageGeneration) return;
    if (!Array.isArray(page.documents)) throw new Error("The source page response is malformed.");
    state.referenceSources = [...(reset ? [] : state.referenceSources), ...page.documents];
    state.referenceCursor = page.nextCursor || "";
    state.referenceStatus = `Showing ${state.referenceSources.length} ${sourceKind} source(s); choose at most 100 exact revisions.`;
    renderReferenceSnapshot(); renderReferenceSources(); renderSelectedReferencePins(); updateReferenceResolveButton();
  } catch (error) {
    if (!referenceContextIsCurrent(context) || pageGeneration !== state.referencePageGeneration) return;
    state.referenceSources = []; state.referenceCursor = "";
    state.referenceStatus = `Source page unavailable: ${error.message}`;
    invalidateReferenceCandidates(state.referenceStatus); renderReferenceSources(); toast(error.message, true);
  }
}

async function resolveReferences() {
  if (state.selectedReferencePins.size === 0 || state.selectedReferencePins.size > 100 || !state.referenceStore) return;
  state.referenceGeneration++;
  state.referenceCandidates = []; state.referenceSnapshot = null; state.referenceStatus = "Resolving exact selected snapshot…";
  const context = referenceContext($("#reference-source-kind").value);
  const sourcePins = normalizedReferencePins();
  renderReferenceSnapshot(); updateReferenceResolveButton();
  if (state.document && !pendingSourceJson()) renderEditorContent();
  try {
    const result = await command("references.list", { targetKind: context.targetKind, sourcePins,
      expectedStoreId: state.referenceStore.storeId, expectedStoreRevision: state.referenceStore.storeRevision });
    if (!referenceContextIsCurrent(context)) return;
    if (result.storeId !== context.storeId || String(result.storeRevision) !== String(context.storeRevision) ||
        JSON.stringify((result.sourcePins || []).map(({ documentId, revisionHash }) => ({ documentId, revisionHash }))
          .sort((left, right) => left.documentId.localeCompare(right.documentId))) !== context.pinsJson ||
        !/^[a-f0-9]{64}$/.test(result.snapshotHash || ""))
      throw new Error("The resolver returned a different source context than the one selected.");
    state.referenceCandidates = (result.references || []).filter((candidate) => candidate.targetKind === context.targetKind);
    state.referenceSnapshot = { snapshotHash: result.snapshotHash, storeId: result.storeId, storeRevision: result.storeRevision };
    state.referenceStatus = `Resolved ${state.referenceCandidates.length} ${context.targetKind} reference(s) from ${result.sourcePins.length} explicitly pinned source(s) · store rev ${result.storeRevision}.`;
    renderReferenceSnapshot();
    if (state.document && !pendingSourceJson()) renderEditorContent();
  } catch (error) {
    if (!referenceContextIsCurrent(context)) return;
    state.referenceGeneration++;
    state.referenceCandidates = []; state.referenceSnapshot = null; state.referenceStatus = `Resolution unavailable: ${error.message}`;
    renderReferenceSnapshot(); if (state.document && !pendingSourceJson()) renderEditorContent();
    toast(error.message, true); if (error.result) setDiagnostics(error.result);
  }
}

function applyHistoryMove(move) {
  if (pendingSourceJson()) { toast("Apply or discard pending Source JSON before using history.", true); return; }
  const value = state.history?.[move]();
  if (!value) return;
  state.document = value; state.editVersion++;
  renderEditor();
}

$("#refresh").addEventListener("click", () => state.kindId && selectKind(state.kindId));
$("#new-record").addEventListener("click", startNew);
$("#kind-filter").addEventListener("input", (event) => renderKinds(event.target.value));
$("#record-filter").addEventListener("input", (event) => renderRecords(event.target.value));
$("#validate").addEventListener("click", validateDocument);
$("#save").addEventListener("click", saveDocument);
$("#undo").addEventListener("click", () => applyHistoryMove("undo"));
$("#redo").addEventListener("click", () => applyHistoryMove("redo"));
$("#load-references").addEventListener("click", browseReferences);
$("#reference-source-kind").addEventListener("change", () => { state.referenceSourceKind = $("#reference-source-kind").value; return browseReferences(); });
$("#reference-kind").addEventListener("change", browseReferences);
$("#resolve-references").addEventListener("click", resolveReferences);
$("#source-json").addEventListener("input", updateDirtyPresentation);
$("#apply-json").addEventListener("click", () => {
  try { commitPendingJson(); toast("Source JSON applied"); }
  catch (error) { toast(`Invalid JSON: ${error.message}`, true); }
});
$("#discard-json").addEventListener("click", () => {
  if (!pendingSourceJson()) return;
  $("#source-json").value = stringifyLossless(state.document, 2);
  renderEditor();
  toast("Pending Source JSON edits discarded");
});
window.addEventListener("beforeunload", (event) => { if (dirty()) { event.preventDefault(); event.returnValue = ""; } });

renderKinds();
command("authoring.info").then(() => { $("#connection").textContent = "Local workspace connected"; $("#connection").classList.add("connected"); }).catch((error) => { $("#connection").textContent = "Host unavailable"; toast(error.message, true); });

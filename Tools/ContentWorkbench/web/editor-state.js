import { cloneLossless, stringifyLossless } from "./lossless-json.js";

export class DocumentHistory {
  constructor(initial, limit = 50) {
    this.limit = limit; this.entries = [cloneLossless(initial)]; this.index = 0;
  }
  get canUndo() { return this.index > 0; }
  get canRedo() { return this.index < this.entries.length - 1; }
  record(document) {
    const value = cloneLossless(document);
    if (stringifyLossless(value) === stringifyLossless(this.entries[this.index])) return false;
    this.entries = this.entries.slice(0, this.index + 1); this.entries.push(value);
    if (this.entries.length > this.limit) this.entries.shift();
    this.index = this.entries.length - 1; return true;
  }
  undo() { if (!this.canUndo) return null; this.index--; return cloneLossless(this.entries[this.index]); }
  redo() { if (!this.canRedo) return null; this.index++; return cloneLossless(this.entries[this.index]); }
}

export function beginEditorSession(session, options) {
  const document = cloneLossless(options.document);
  Object.assign(session, {
    sessionId: (session.sessionId || 0) + 1,
    kindId: options.kindId,
    sourceKind: options.sourceKind,
    documentId: options.documentId || "",
    logicalPath: options.logicalPath || "",
    hash: options.hash || null,
    isNew: options.isNew === true,
    document,
    originalDocument: cloneLossless(document),
    editVersion: 0,
    history: new DocumentHistory(document),
    conflict: null,
  });
  return session;
}

export function recordEditorChange(session, document) {
  session.document = document;
  if (session.history.record(document)) session.editVersion++;
  return session.editVersion;
}

export function isEditorDirty(session, rawSource) {
  if (!session.document) return false;
  if (session.isNew || stringifyLossless(session.document) !== stringifyLossless(session.originalDocument)) return true;
  return typeof rawSource === "string" && rawSource !== stringifyLossless(session.document, 2);
}

export function mayLeaveEditor(session, rawSource, confirmDiscard) {
  if (!isEditorDirty(session, rawSource)) return true;
  return typeof confirmDiscard === "function" && confirmDiscard() === true;
}

export function captureSave(session, identityField, operationId, sourceContract = {}) {
  const document = cloneLossless(session.document);
  const documentId = session.isNew ? (sourceContract.fixedDocumentId || String(document[identityField] ?? "").trim()) : session.documentId;
  if (!documentId) throw new Error(`Enter ${identityField} before saving.`);
  const logicalPath = session.isNew ? (sourceContract.fixedLogicalPath || `Workbench/${session.kindId}/${documentId}.json`) : session.logicalPath;
  if (!logicalPath) throw new Error("The stored logical path is missing; this record cannot be safely overwritten.");
  return {
    sessionId: session.sessionId,
    kindId: session.kindId,
    editVersion: session.editVersion,
    document,
    payload: {
      kind: session.sourceKind,
      documentId,
      logicalPath,
      document,
      expectedHash: session.isNew ? null : session.hash,
      operationId,
    },
  };
}

export function applySaveResult(session, capture, result) {
  if (session.sessionId !== capture.sessionId) return false;
  session.documentId = capture.payload.documentId;
  session.logicalPath = capture.payload.logicalPath;
  session.sourceKind = capture.payload.kind;
  session.hash = result.contentHash;
  session.originalDocument = cloneLossless(capture.document);
  session.isNew = false;
  session.conflict = null;
  return true;
}

export async function listAllRecords(command, kind) {
  const records = []; const seenCursors = new Set(); let cursor = "";
  for (let pageIndex = 0; pageIndex < 1000; pageIndex++) {
    const page = await command("authoring.list", { kind, cursor, limit: 100 });
    records.push(...(page.documents || []));
    cursor = page.nextCursor || "";
    if (!cursor) return records;
    if (seenCursors.has(cursor)) throw new Error("Catalogue pagination returned a repeated cursor.");
    seenCursors.add(cursor);
  }
  throw new Error("Catalogue pagination exceeded 1,000 pages; narrow the source kind before retrying.");
}

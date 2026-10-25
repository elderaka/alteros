import assert from "node:assert/strict";
import test from "node:test";
import { applySaveResult, beginEditorSession, captureSave, isEditorDirty, listAllRecords, mayLeaveEditor, recordEditorChange } from "../web/editor-state.js";
import { isRawNumber, parseLossless, stringifyLossless } from "../web/lossless-json.js";

test("editing retains repository identity/path while new drafts derive them once", () => {
  const existing = {};
  beginEditorSession(existing, { kindId: "dialogue", sourceKind: "generic_dialogue", documentId: "src_0062", logicalPath: "Generic/route/line.json", hash: "rev-1", document: { id: "generic.line" }, isNew: false });
  const edit = captureSave(existing, "id", "edit-op");
  assert.equal(edit.payload.documentId, "src_0062");
  assert.equal(edit.payload.logicalPath, "Generic/route/line.json");
  assert.equal(edit.payload.kind, "generic_dialogue");
  assert.equal(edit.payload.expectedHash, "rev-1");

  const created = {};
  beginEditorSession(created, { kindId: "dialogue", sourceKind: "dialogue_definition", document: { id: "dialogue.new" }, isNew: true });
  const create = captureSave(created, "id", "create-op");
  assert.equal(create.payload.documentId, "dialogue.new");
  assert.equal(create.payload.logicalPath, "Workbench/dialogue/dialogue.new.json");
  assert.equal(create.payload.expectedHash, null);
});

test("save completion updates revision baseline without replacing newer local edits or another session", () => {
  const session = {};
  beginEditorSession(session, { kindId: "item", sourceKind: "item_definition", documentId: "doc.item", logicalPath: "Items/water.json", hash: "old", document: { id: "item.water", stackLimit: 9 }, isNew: false });
  const capture = captureSave(session, "id", "save-op");
  recordEditorChange(session, { id: "item.water", stackLimit: 10 });
  assert.equal(applySaveResult(session, capture, { contentHash: "new" }), true);
  assert.equal(session.document.stackLimit, 10);
  assert.equal(session.documentId, "doc.item");
  assert.equal(session.hash, "new");
  assert.equal(isEditorDirty(session), true);
  const other = captureSave(session, "id", "other-op");
  beginEditorSession(session, { kindId: "action", sourceKind: "action_definition", documentId: "doc.action", logicalPath: "Actions/a.json", hash: "a1", document: { id: "action.a" }, isNew: false });
  assert.equal(applySaveResult(session, other, { contentHash: "late" }), false);
  assert.equal(session.documentId, "doc.action");
});

test("dirty state includes pending raw JSON and history supports undo and redo", async () => {
  const session = {};
  beginEditorSession(session, { kindId: "scenario", sourceKind: "scenario_definition", documentId: "scenario.a", logicalPath: "Scenarios/a.json", hash: "h1", document: { scenarioId: "a", value: 1 }, isNew: false });
  assert.equal(isEditorDirty(session), false);
  assert.equal(mayLeaveEditor(session, undefined, () => { throw new Error("clean navigation must not prompt"); }), true);
  assert.equal(isEditorDirty(session, "{ invalid"), true);
  assert.equal(mayLeaveEditor(session, "{ invalid", () => false), false);
  assert.equal(mayLeaveEditor(session, "{ invalid", () => true), true);
  recordEditorChange(session, { scenarioId: "a", value: 2 });
  assert.equal(session.history.canUndo, true);
  assert.deepEqual(session.history.undo(), { scenarioId: "a", value: 1 });
  assert.deepEqual(session.history.redo(), { scenarioId: "a", value: 2 });
  assert.equal(session.history.canUndo, true);

  let calls = 0;
  const items = await listAllRecords(async (_operation, payload) => {
    calls++;
    if (!payload.cursor) return { documents: Array.from({ length: 100 }, (_, index) => ({ documentId: `record-${index + 1}` })), nextCursor: "page-2" };
    return { documents: [{ documentId: "desired-on-page-two" }], nextCursor: "" };
  }, "generic_dialogue");
  assert.equal(calls, 2);
  assert.equal(items.length, 101);
  assert.equal(items.at(-1).documentId, "desired-on-page-two");
});

test("lossless JSON retains unsafe Int64 and unknown nested number tokens through edits", () => {
  const source = '{"stackLimit":9007199254740993,"extension":{"unseen":9223372036854775807,"safe":4}}';
  const document = parseLossless(source);
  assert.equal(isRawNumber(document.stackLimit), true);
  assert.equal(isRawNumber(document.extension.unseen), true);
  assert.equal(document.extension.safe, 4);
  document.note = "edited safely";
  const saved = stringifyLossless(document);
  assert.match(saved, /"stackLimit":9007199254740993/);
  assert.match(saved, /"unseen":9223372036854775807/);
  assert.equal(parseLossless(saved).stackLimit.raw, "9007199254740993");
  assert.throws(() => parseLossless('{"unsupported":1e400}'), /outside the finite browser range/);
});

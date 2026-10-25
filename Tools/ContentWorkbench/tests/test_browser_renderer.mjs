import assert from "node:assert/strict";
import test from "node:test";
import { mountDraftEditor } from "../web/draft-editor.js";
import { isRawNumber, RawNumber, stringifyLossless } from "../web/lossless-json.js";

class Element {
  constructor(tag) {
    this.tagName = tag.toUpperCase(); this.children = []; this.attributes = {}; this.listeners = {};
    this.dataset = {}; this.style = {}; const classes = new Set(); this.classList = { add: (name) => classes.add(name), remove: (name) => classes.delete(name), contains: (name) => classes.has(name) };
    this.value = ""; this.checked = false; this.disabled = false; this.textContent = "";
  }
  setAttribute(name, value) { this.attributes[name] = value; if (name.startsWith("data-")) this.dataset[name.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = value; }
  append(...items) { for (const item of items) { if (item && typeof item === "object") item.parentNode = this; this.children.push(item); } }
  replaceChildren(...items) { this.children = []; this.append(...items); }
  addEventListener(name, callback) { (this.listeners[name] ||= []).push(callback); }
  dispatch(name) { for (const callback of this.listeners[name] || []) callback({ target: this }); }
  click() { if (!this.disabled) this.dispatch("click"); }
  replaceWith(next) { const siblings = this.parentNode?.children; if (!siblings) return; const index = siblings.indexOf(this); if (index >= 0) { next.parentNode = this.parentNode; siblings[index] = next; } }
  querySelectorAll(selector) {
    const found = []; const match = (node) => selector.startsWith(".") ? String(node.className || "").split(" ").includes(selector.slice(1)) : false;
    const visit = (node) => { for (const child of node.children || []) { if (match(child)) found.push(child); visit(child); } }; visit(this); return found;
  }
  scrollIntoView() {}
}
class FakeDocument { createElement(tag) { return new Element(tag); } }
globalThis.document = new FakeDocument();

function descendants(root) {
  const all = []; const visit = (node) => { all.push(node); for (const child of node.children || []) visit(child); };
  visit(root); return all;
}
function byText(root, text) { return descendants(root).find((node) => node.textContent === text); }
function byPlaceholder(root, text) { return descendants(root).find((node) => node.attributes.placeholder === text); }
function change(node, value) { node.value = value; node.dispatch("change"); }

test("textarea arrays add, edit, and remove strings instead of object records", () => {
  const document = { anchors: [] };
  let editor = mountDraftEditor({ descriptor: { sections: [{ id: "topic", title: "Topic", fields: [
    { fieldId: "anchors", path: "anchors", label: "Anchors", control: "array", itemControl: "textarea" }
  ] }] }, document, onChange() {} });
  byText(editor, "Add item").click();
  assert.deepEqual(document.anchors, [""]);
  editor = mountDraftEditor({ descriptor: { sections: [{ id: "topic", title: "Topic", fields: [
    { fieldId: "anchors", path: "anchors", label: "Anchors", control: "array", itemControl: "textarea" }
  ] }] }, document, onChange() {} });
  const text = descendants(editor).find((node) => node.tagName === "TEXTAREA");
  change(text, "good morning, sleepyhead");
  assert.deepEqual(document.anchors, ["good morning, sleepyhead"]);
  byText(editor, "×").click();
  assert.deepEqual(document.anchors, []);
});

test("adding a translation preserves edited text, language, and unknown translation fields", () => {
  const document = { displayName: { id: "text.name", translations: [
    { language: "en", text: "old", editorialNote: "keep" }
  ] } };
  const descriptor = { sections: [{ id: "identity", title: "Identity", fields: [
    { fieldId: "displayName", path: "", label: "Display name", control: "localizedText", ownedPath: "displayName", referencePath: "displayNameRef" }
  ] }] };
  const editor = mountDraftEditor({ descriptor, document, onChange() {} });
  change(byPlaceholder(editor, "Language (e.g. en)"), "en-GB");
  change(byPlaceholder(editor, "Localized text"), "edited before adding");
  byText(editor, "Add translation").click();
  assert.deepEqual(document.displayName.translations[0], { language: "en-GB", text: "edited before adding", editorialNote: "keep" });
  assert.equal(document.displayName.translations.length, 2);
});

test("Furniture form mirrors definition ID and synchronizes function bindings while preserving extensions", () => {
  const document = {
    id: "furniture.old",
    definition: { definitionId: "furniture.old", functionBindings: [
      { functionId: "furniture.power.set", pointId: "point.use", useId: "use.power", extension: "keep" }
    ] },
    functions: [{ functionId: "furniture.power.set", pointId: "point.use", useId: "use.power", contractVersion: 1, contract: { desiredState: ["Off", "On"] } }]
  };
  const descriptor = { sections: [{ id: "furniture", title: "Furniture", fields: [
    { fieldId: "definition", path: "definition", label: "Definition", control: "object", fields: [
      { fieldId: "definitionId", path: "definitionId", label: "Definition ID", control: "text", mirrorPath: "id" }
    ] },
    { fieldId: "functions", path: "functions", label: "Functions", control: "array", syncPath: "definition.functionBindings", fields: [
      { fieldId: "functionId", path: "functionId", label: "Function ID", control: "text" },
      { fieldId: "pointId", path: "pointId", label: "Point ID", control: "text" },
      { fieldId: "useId", path: "useId", label: "Use ID", control: "text" },
      { fieldId: "contractVersion", path: "contractVersion", label: "Contract version", control: "integer" },
      { fieldId: "contract", path: "contract", label: "Contract", control: "json" }
    ] }
  ] }] };
  let editor = mountDraftEditor({ descriptor, document, onChange() {} });
  const definitionLabel = descendants(editor).find((node) => node.textContent === "Definition ID");
  const definitionId = definitionLabel.parentNode.children.find((node) => node.tagName === "INPUT");
  change(definitionId, "furniture.new");
  assert.equal(document.id, "furniture.new");
  editor = mountDraftEditor({ descriptor, document, onChange() {} });
  const contractLabel = descendants(editor).find((node) => node.textContent === "Contract version");
  change(contractLabel.parentNode.children.find((node) => node.tagName === "INPUT"), "2");
  assert.equal(document.definition.functionBindings[0].extension, "keep");
  editor = mountDraftEditor({ descriptor, document, onChange() {} });
  byText(editor, "×").click();
  assert.deepEqual(document.functions, []);
  assert.deepEqual(document.definition.functionBindings, []);
  byText(editor, "Add item").click();
  const enter = (labelText, value) => {
    editor = mountDraftEditor({ descriptor, document, onChange() {} });
    const label = descendants(editor).find((node) => node.textContent === labelText);
    change(label.parentNode.children.find((node) => node.tagName === "INPUT"), value);
  };
  enter("Function ID", "furniture.new.function");
  enter("Point ID", "point.use");
  enter("Use ID", "use.power");
  assert.equal(document.definition.functionBindings.length, 1);
  assert.equal(document.definition.functionBindings[0].functionId, "furniture.new.function");
});

test("Int64 editing remains text-based and does not round existing or newly entered large integers", () => {
  const document = { stackLimit: new RawNumber("9223372036854775807"), extension: { futureCount: new RawNumber("9007199254740993") } };
  const descriptor = { sections: [{ id: "item", title: "Item", fields: [
    { fieldId: "stackLimit", path: "stackLimit", label: "Stack limit", control: "int64", minimum: new RawNumber("1") }
  ] }] };
  const editor = mountDraftEditor({ descriptor, document, onChange() {} });
  const input = descendants(editor).find((node) => node.tagName === "INPUT");
  assert.equal(input.attributes.type, "text");
  assert.equal(input.value, "9223372036854775807");
  change(input, "9007199254740993");
  assert.equal(isRawNumber(document.stackLimit), true);
  assert.equal(document.stackLimit.raw, "9007199254740993");
  assert.match(stringifyLossless(document), /"futureCount":9007199254740993/);
  change(input, "9223372036854775808");
  assert.equal(input.classList.contains("invalid"), true);
  assert.equal(document.stackLimit.raw, "9007199254740993");
});

test("typed reference control selects importer-pinned values and preserves unknown reference fields", () => {
  const document = { scenarios: [{ scenarioId: "entry.one", futureEntryField: "keep" }] };
  const exactRef = { store: "11111111-1111-4111-8111-111111111111", id: "scenario.one", revision: "a".repeat(64), expectedHash: "b".repeat(64) };
  const descriptor = { sections: [{ id: "scenarios", title: "Scenarios", fields: [{
    fieldId: "scenarios", path: "scenarios", label: "Scenarios", control: "array", fields: [
      { fieldId: "scenarioId", path: "scenarioId", label: "Entry ID", control: "text" },
      { fieldId: "scenarioRef", path: "scenarioRef", label: "Reusable Scenario", control: "reference", targetKind: "scenario" },
      { fieldId: "nodes", path: "nodes", label: "Legacy nodes", control: "readOnlyArray", readOnly: true }
    ]
  }] }] };
  const editor = mountDraftEditor({ descriptor, document, referenceCandidates: { scenario: [{ targetKind: "scenario", refId: "scenario.one", ref: exactRef, ownerSources: [] }] }, onChange() {} });
  const select = descendants(editor).find((node) => node.tagName === "SELECT");
  assert.ok(select);
  change(select, "0");
  assert.deepEqual(document.scenarios[0].scenarioRef, exactRef);
  assert.equal(document.scenarios[0].futureEntryField, "keep");
});

test("typed reference control leaves legacy inline nodes intact and unavailable for silent conversion", () => {
  const legacyNodes = [{ id: "node.one", text: "preserve me" }];
  const document = { scenarios: [{ scenarioId: "legacy.entry", nodes: legacyNodes }] };
  const descriptor = { sections: [{ id: "scenarios", title: "Scenarios", fields: [{
    fieldId: "scenarios", path: "scenarios", label: "Scenarios", control: "array", fields: [
      { fieldId: "scenarioId", path: "scenarioId", label: "Entry ID", control: "text" },
      { fieldId: "scenarioRef", path: "scenarioRef", label: "Reusable Scenario", control: "reference", targetKind: "scenario" },
      { fieldId: "nodes", path: "nodes", label: "Legacy nodes", control: "readOnlyArray", readOnly: true }
    ]
  }] }] };
  const editor = mountDraftEditor({ descriptor, document, referenceCandidates: { scenario: [{ refId: "scenario.new", ref: { store: "s", id: "scenario.new", revision: "a", expectedHash: "a" } }] }, onChange() {} });
  const select = descendants(editor).find((node) => node.tagName === "SELECT");
  assert.equal(select.disabled, true);
  assert.deepEqual(document.scenarios[0].nodes, legacyNodes);
  assert.equal(document.scenarios[0].scenarioRef, undefined);
});

test("localized Text selection preserves existing pins and mirror fields while keeping owned/reference exclusive", () => {
  const existing = { store: "store", id: "text.old", revision: "old-revision", expectedHash: "old-hash", futureRefField: 9 };
  const selected = { store: "store", id: "text.new", revision: "new-revision", expectedHash: "new-hash" };
  const document = { displayNameRef: existing, definition: { displayNameKey: "text.old", extension: true } };
  const descriptor = { sections: [{ id: "identity", title: "Identity", fields: [{ fieldId: "displayName", path: "", label: "Display name", control: "localizedText", ownedPath: "displayName", referencePath: "displayNameRef", referenceKind: "text", mirrorPath: "definition.displayNameKey" }] }] };
  let editor = mountDraftEditor({ descriptor, document, referenceCandidates: { text: [{ refId: "text.new", ref: selected }] }, onChange() {} });
  let selects = descendants(editor).filter((node) => node.tagName === "SELECT");
  assert.ok(selects.some((node) => node.children.some((option) => option.textContent.includes("text.old"))), "the current pin is displayed even outside the selected snapshot");
  assert.ok(selects.some((node) => node.children.some((option) => option.textContent.includes("text.new"))), "resolved Text candidates are available in the localized field");
  const refSelect = selects.find((node) => node.children.some((option) => option.textContent.includes("text.new")));
  change(refSelect, "0");
  assert.deepEqual(document.displayNameRef, { ...existing, ...selected });
  assert.equal(document.definition.displayNameKey, "text.new");
  editor = mountDraftEditor({ descriptor, document, referenceCandidates: { text: [{ refId: "text.new", ref: selected }] }, onChange() {} });
  const mode = descendants(editor).find((node) => node.tagName === "SELECT" && node.className === "localized-mode");
  change(mode, "owned");
  assert.equal(document.displayNameRef, undefined);
  assert.equal(document.definition.displayNameKey, "");
  editor = mountDraftEditor({ descriptor, document, referenceCandidates: { text: [{ refId: "text.new", ref: selected }] }, onChange() {} });
  const ownerId = descendants(editor).find((node) => node.tagName === "INPUT");
  change(ownerId, "text.owned");
  assert.equal(document.definition.displayNameKey, "text.owned");
  assert.equal(document.definition.extension, true);
});

test("actual descriptor-shaped Scenario and Action rows expose Action and nested host-effect Item pickers", () => {
  const actionRef = { store: "store", id: "action.set", revision: "rev", expectedHash: "hash" };
  const itemRef = { store: "store", id: "item.water", revision: "item-rev", expectedHash: "item-hash" };
  const descriptor = { sections: [
    { id: "steps", title: "Steps", fields: [{ fieldId: "steps", path: "steps", label: "Steps", control: "array", fields: [
      { fieldId: "id", path: "id", label: "Step ID", control: "text" }, { fieldId: "kind", path: "kind", label: "Step kind", control: "enum" },
      { fieldId: "actionRef", path: "actionRef", label: "Pinned Action", control: "reference", targetKind: "action" },
      { fieldId: "successNextStepId", path: "successNextStepId", label: "Success", control: "text" }
    ] }] },
    { id: "action", title: "Action", fields: [
      { fieldId: "implementationKind", path: "implementationKind", label: "Implementation", control: "enum" },
      { fieldId: "data", path: "data", label: "Sequence data", control: "actionData", visibleWhenPath: "implementationKind", visibleWhenValue: "activity_sequence" },
      { fieldId: "functionId", path: "data.functionId", label: "Host function", control: "enum", options: ["player.item.grant", "power.set"], visibleWhenPath: "implementationKind", visibleWhenValue: "host_effect" },
      { fieldId: "itemRef", path: "data.arguments.itemRef", label: "Granted Item", control: "reference", targetKind: "item", visibleWhenPath: "data.functionId", visibleWhenValue: "player.item.grant" }
    ] }
  ] };
  const document = { steps: [{ id: "step.action", kind: "action" }], implementationKind: "host_effect", data: { functionId: "player.item.grant", arguments: { quantity: 1 } } };
  const editor = mountDraftEditor({ descriptor, document, referenceCandidates: { action: [{ refId: "action.set", ref: actionRef }], item: [{ refId: "item.water", ref: itemRef }] }, onChange() {} });
  const selectors = descendants(editor).filter((node) => node.tagName === "SELECT");
  assert.ok(selectors.some((node) => node.children.some((option) => option.textContent.includes("action.set"))), "Scenario action rows consume Action candidates");
  assert.ok(selectors.some((node) => node.children.some((option) => option.textContent.includes("item.water"))), "host-effect data consumes Item candidates");
  const itemSelector = selectors.find((node) => node.children.some((option) => option.textContent.includes("item.water")));
  change(itemSelector, "0");
  assert.deepEqual(document.data.arguments.itemRef, itemRef);
});

test("Action bark argument consumes exact Dialogue candidates through the producer reference path", () => {
  const dialogue = { store: "store", id: "dialogue.goodnight", revision: "revision", expectedHash: "hash" };
  const descriptor = {
    actionCatalog: { primitives: [{ primitiveId: "bark", arguments: [{ name: "dialogueId", valueType: "text" }] }] },
    references: [{ targetKind: "dialogue", paths: ["data.steps[].arguments[].value"], argumentName: "dialogueId" }],
    sections: [{ id: "action", title: "Action", fields: [
      { fieldId: "implementationKind", path: "implementationKind", label: "Implementation", control: "enum" },
      { fieldId: "data", path: "data", label: "Activity sequence data", control: "actionData", visibleWhenPath: "implementationKind", visibleWhenValue: "activity_sequence" }
    ] }]
  };
  const document = { implementationKind: "activity_sequence", data: { steps: [{ stepId: "step.bark", primitiveId: "bark", arguments: [
    { name: "dialogueId", value: { type: "text", literal: "dialogue.old" } }
  ] }] } };
  const editor = mountDraftEditor({ descriptor, document, referenceCandidates: { dialogue: [{ refId: dialogue.id, ref: dialogue }] }, onChange() {} });
  const picker = descendants(editor).find((node) => node.tagName === "SELECT" && node.children.some((option) => option.textContent.includes(dialogue.id)));
  assert.ok(picker, "the registered bark literal field has a typed Dialogue selector");
  change(picker, "0");
  assert.equal(document.data.steps[0].arguments[0].value.type, "text");
  assert.equal(document.data.steps[0].arguments[0].value.literal, "dialogue.goodnight");
});

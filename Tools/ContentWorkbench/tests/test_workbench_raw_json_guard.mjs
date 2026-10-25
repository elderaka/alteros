import assert from "node:assert/strict";
import test from "node:test";
import { parseLossless, stringifyLossless } from "../web/lossless-json.js";

class FakeElement {
  constructor(tagName = "div", ownerDocument = null) {
    this.tagName = tagName.toUpperCase(); this.ownerDocument = ownerDocument; this.children = []; this.parentNode = null;
    this.attributes = {}; this.listeners = {}; this.dataset = {}; this.className = "";
    const classes = new Set(); this.classList = { add: (name) => classes.add(name), remove: (name) => classes.delete(name), contains: (name) => classes.has(name) };
    this.value = ""; this.textContent = ""; this.hidden = false; this.disabled = false; this.inert = false;
    this.checked = false; this._innerHTML = "";
  }
  setAttribute(name, value) {
    this.attributes[name] = String(value);
    if (name.startsWith("data-")) this.dataset[name.slice(5).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())] = String(value);
  }
  set innerHTML(value) {
    this._innerHTML = String(value); this.replaceChildren();
    if (this.id === "kind-list") {
      for (const match of this._innerHTML.matchAll(/data-kind="([^"]+)"/g)) {
        const button = new FakeElement("button", this.ownerDocument); button.dataset.kind = match[1]; this.append(button);
      }
    } else if (this.id === "record-list") {
      for (const match of this._innerHTML.matchAll(/data-id="([^"]+)" data-kind="([^"]*)"/g)) {
        const button = new FakeElement("button", this.ownerDocument); button.dataset.id = match[1]; button.dataset.kind = match[2]; this.append(button);
      }
    }
  }
  get innerHTML() { return this._innerHTML; }
  append(...nodes) { for (const node of nodes) { if (!node || typeof node !== "object") continue; node.parentNode = this; this.children.push(node); } }
  replaceChildren(...nodes) { this.children = []; this.append(...nodes); }
  replaceWith(node) { const siblings = this.parentNode?.children; if (!siblings) return; const index = siblings.indexOf(this); if (index >= 0) { node.parentNode = this.parentNode; siblings[index] = node; } }
  addEventListener(type, callback) { (this.listeners[type] ||= []).push(callback); }
  async dispatch(type) {
    for (const callback of this.listeners[type] || []) await callback({ target: this });
    if (typeof this[`on${type}`] === "function") await this[`on${type}`]({ target: this });
  }
  async click() { if (!this.disabled) await this.dispatch("click"); }
  querySelectorAll(selector) {
    const found = [];
    const match = selector === "[data-kind]" ? (node) => node.dataset.kind !== undefined : selector === "[data-id]" ? (node) => node.dataset.id !== undefined :
      selector.startsWith("input[type=checkbox]") ? (node) => node.tagName === "INPUT" && node.type === "checkbox" && (!selector.endsWith(":checked") || node.checked) : () => false;
    const visit = (node) => { for (const child of node.children) { if (match(child)) found.push(child); visit(child); } };
    visit(this); return found;
  }
  scrollIntoView() {}
  contains(node) { for (let current = node; current; current = current.parentNode) if (current === this) return true; return false; }
}

class FakeDocument {
  constructor() { this.nodes = new Map(); this.created = []; this.documentElement = new FakeElement("html", this); }
  createElement(tagName) { const node = new FakeElement(tagName, this); this.created.push(node); return node; }
  querySelector(selector) {
    if (selector === 'meta[name="workbench-token"]') return { content: "synthetic-ui-token" };
    const id = selector.startsWith("#") ? selector.slice(1) : selector;
    if (!this.nodes.has(id)) { const node = new FakeElement("div", this); node.id = id; this.nodes.set(id, node); }
    return this.nodes.get(id);
  }
  byId(id) { return this.created.find((node) => node.id === id) || this.nodes.get(id); }
  byLabel(root, label) {
    const visit = (node) => {
      if (node.tagName === "SPAN" && node.textContent === label) return node.parentNode;
      for (const child of node.children) { const result = visit(child); if (result) return result; }
      return null;
    };
    return visit(root);
  }
}

test("actual workbench keeps valid and invalid raw JSON pending until explicit Apply or Discard", async () => {
  const previous = { document: globalThis.document, window: globalThis.window, fetch: globalThis.fetch, Option: globalThis.Option };
  const document = new FakeDocument();
  const descriptor = {
    kindId: "item", displayName: "Item", defaultSourceKind: "item_definition", sourceKinds: ["item_definition"],
    sourceContracts: [{ kind: "item_definition", identityField: "id" }],
    capabilities: { create: true, preview: false }, sections: [{ id: "identity", title: "Item", fields: [
      { fieldId: "id", path: "id", label: "Item ID", control: "text" },
      { fieldId: "note", path: "note", label: "Note", control: "text" },
      { fieldId: "stackLimit", path: "stackLimit", label: "Stack limit", control: "int64", minimum: 1 }
    ] }], documentTemplate: { schemaVersion: 1, id: "item.raw_guard", note: "stored", stackLimit: 1 }, graph: { enabled: false }, deferredFields: []
  };
  const knownSelectors = ["#editor", "#welcome", "#kind-list", "#new-record", "#kind-filter", "#record-filter", "#record-list",
    "#kind-kicker", "#document-title", "#revision-meta", "#source-kind-wrap", "#source-kind", "#source-json", "#editor-content",
    "#deferred-notice", "#conflict-notice", "#graph-panel", "#preview", "#diagnostics", "#validation-result", "#undo", "#redo",
    "#save", "#validate", "#refresh", "#load-references", "#reference-kind", "#reference-list", "#fixture-id", "#preview-draw",
    "#apply-json", "#discard-json", "#raw-json-pending", "#toast", "#connection"];
  for (const selector of knownSelectors) document.querySelector(selector);
  const window = { confirm: () => true, addEventListener() {}, setTimeout: () => 0, clearTimeout() {} };
  const requests = [];
  const fetch = async (_url, options) => {
    const request = parseLossless(options.body);
    requests.push(request);
    let result = {};
    if (request.operation === "schema.describe") result = { descriptor };
    else if (request.operation === "authoring.list") result = { documents: [], nextCursor: "" };
    else if (request.operation === "authoring.info") result = { store: { storeId: "fixture" } };
    else if (request.operation === "authoring.write") result = { contentHash: "synthetic-saved-hash" };
    return { ok: true, status: 200, text: async () => stringifyLossless({ protocolVersion: 1, requestId: request.requestId, status: "ok", result, diagnostics: [] }) };
  };
  globalThis.document = document; globalThis.window = window; globalThis.fetch = fetch; globalThis.Option = class { constructor(text, value) { this.text = text; this.value = value; } };
  try {
    await import(`../web/workbench.js?raw-json-guard=${Date.now()}`);
    await document.querySelector("#kind-list").children[0].click();
    await document.querySelector("#new-record").click();
    await new Promise((resolve) => setImmediate(resolve));
    const content = document.querySelector("#editor-content");
    const source = document.querySelector("#source-json");
    const apply = document.querySelector("#apply-json");
    const discard = document.querySelector("#discard-json");

    source.value = '{"schemaVersion":1,"id":"item.raw_guard","note":"raw edit survives","stackLimit":1}';
    await source.dispatch("input");
    assert.equal(content.inert, true);
    assert.equal(document.querySelector("#raw-json-pending").hidden, false);
    const stackLabel = document.byLabel(content, "Stack limit");
    const stackInput = stackLabel.children.find((node) => node.tagName === "INPUT");
    stackInput.value = "2"; await stackInput.dispatch("change");
    assert.equal(source.value, '{"schemaVersion":1,"id":"item.raw_guard","note":"raw edit survives","stackLimit":1}');
    assert.equal(document.querySelector("#undo").disabled, true);
    await apply.click();
    assert.equal(content.inert, false);
    assert.equal(document.byLabel(document.querySelector("#editor-content"), "Note").children.find((node) => node.tagName === "INPUT").value, "raw edit survives");
    assert.equal(document.querySelector("#undo").disabled, false);
    await document.querySelector("#undo").click();
    assert.equal(document.byLabel(document.querySelector("#editor-content"), "Note").children.find((node) => node.tagName === "INPUT").value, "stored");

    const changeFormField = async (labelText, value) => {
      const label = document.byLabel(document.querySelector("#editor-content"), labelText);
      const input = label.children.find((node) => node.tagName === "INPUT"); input.value = value; await input.dispatch("change");
    };
    await changeFormField("Note", "first normal form edit");
    assert.match(source.value, /first normal form edit/);
    await changeFormField("Stack limit", "2");
    assert.match(source.value, /first normal form edit/);
    assert.match(source.value, /"stackLimit": 2/);
    assert.equal(document.querySelector("#save").disabled, false);
    await document.querySelector("#undo").click();
    assert.match(source.value, /first normal form edit/);
    assert.match(source.value, /"stackLimit": 1/);
    await document.querySelector("#undo").click();
    assert.match(source.value, /"note": "stored"/);
    await document.querySelector("#redo").click();
    await document.querySelector("#redo").click();
    assert.match(source.value, /first normal form edit/);
    assert.match(source.value, /"stackLimit": 2/);
    await document.querySelector("#save").click();
    const write = requests.find((request) => request.operation === "authoring.write");
    assert.equal(write.payload.document.note, "first normal form edit");
    assert.equal(write.payload.document.stackLimit, 2);
    assert.equal(write.payload.documentId, "item.raw_guard");

    const invalidSource = '{"schemaVersion":1,"id":"item.raw_guard","note":';
    source.value = invalidSource; await source.dispatch("input");
    assert.equal(document.querySelector("#editor-content").inert, true);
    const currentContent = document.querySelector("#editor-content");
    const currentStack = document.byLabel(currentContent, "Stack limit").children.find((node) => node.tagName === "INPUT");
    currentStack.value = "4"; await currentStack.dispatch("change");
    assert.equal(source.value, invalidSource);
    await discard.click();
    assert.equal(document.querySelector("#editor-content").inert, false);
    assert.match(source.value, /"note": "first normal form edit"/);
    assert.equal(document.byLabel(document.querySelector("#editor-content"), "Stack limit").children.find((node) => node.tagName === "INPUT").value, "2");
  } finally {
    Object.assign(globalThis, { document: previous.document, window: previous.window, fetch: previous.fetch, Option: previous.Option });
  }
});

test("actual reference UI keeps mixed source pins and ignores a late result from an older target/store snapshot", async () => {
  const previous = { document: globalThis.document, window: globalThis.window, fetch: globalThis.fetch, Option: globalThis.Option };
  const document = new FakeDocument();
  const window = { confirm: () => true, addEventListener() {}, setTimeout: () => 0, clearTimeout() {} };
  const selectors = ["#kind-list", "#reference-kind", "#reference-source-kind", "#load-references", "#reference-list",
    "#reference-selected-sources", "#resolve-references", "#reference-snapshot", "#diagnostics", "#validation-result",
    "#toast", "#connection", "#editor-content", "#preview", "#save", "#validate", "#undo", "#redo"];
  for (const selector of selectors) document.querySelector(selector);
  const requests = [];
  let storeRevision = 1;
  let failNextResolve = false;
  let delayNextScenarioPage = false;
  let releaseOldPage;
  let scenarioPage = [
    { documentId: "scenario.source.old", kind: "scenario_definition", revisionHash: "a".repeat(64) },
    { documentId: "scenario.source.new", kind: "scenario_definition", revisionHash: "b".repeat(64) }
  ];
  let releaseOld;
  const fetch = async (_url, options) => {
    const request = parseLossless(options.body); requests.push(request);
    let result = {};
    if (request.operation === "schema.describe") {
      const sourceKinds = {
        dialogue: ["dialogue_definition", "generic_dialogue"], scenario: ["scenario_definition"], topic: ["topic_definition"],
        furniture: ["furniture_definition"], item: ["item_definition"], action: ["action_definition"]
      }[request.payload.kindId];
      result = { descriptor: { sourceKinds } };
    } else if (request.operation === "authoring.info") result = { store: { storeId: "11111111-1111-4111-8111-111111111111", storeRevision } };
    else if (request.operation === "authoring.list") {
      if (request.payload.kind === "scenario_definition" && delayNextScenarioPage) {
        delayNextScenarioPage = false;
        return new Promise((resolve) => {
          releaseOldPage = () => resolve({ ok: true, status: 200, text: async () => stringifyLossless({
            protocolVersion: 1, requestId: request.requestId, status: "ok", diagnostics: [], result: {
              documents: [{ documentId: "scenario.late_page", kind: "scenario_definition", revisionHash: "9".repeat(64) }], nextCursor: ""
            }
          }) });
        });
      }
      const docs = request.payload.kind === "scenario_definition" ?
        (request.payload.cursor === "page-two" ? [{ documentId: "scenario.source.third", kind: "scenario_definition", revisionHash: "3".repeat(64) }] : scenarioPage) : request.payload.kind === "topic_definition" ? [
        { documentId: "topic.source.one", kind: "topic_definition", revisionHash: "c".repeat(64) }
      ] : [];
      result = { documents: docs, nextCursor: request.payload.kind === "scenario_definition" && request.payload.cursor === "" && storeRevision === 1 ? "page-two" : "" };
    } else if (request.operation === "references.list") {
      if (failNextResolve) {
        failNextResolve = false;
        return { ok: true, status: 200, text: async () => stringifyLossless({ protocolVersion: 1, requestId: request.requestId,
          status: "invalid", result: null, diagnostics: [{ code: "dependency_missing", message: "A required dependency is absent." }] }) };
      }
      if (request.payload.sourcePins.some((item) => item.documentId === "scenario.source.old")) {
        return new Promise((resolve) => {
          releaseOld = () => resolve({ ok: true, status: 200, text: async () => stringifyLossless({
            protocolVersion: 1, requestId: request.requestId, status: "ok", diagnostics: [], result: {
              storeId: "11111111-1111-4111-8111-111111111111", storeRevision: 1, snapshotHash: "d".repeat(64),
              sourcePins: request.payload.sourcePins, references: [{ targetKind: "scenario", refId: "scenario.old", ref: {
                store: "11111111-1111-4111-8111-111111111111", id: "scenario.old", revision: "e".repeat(64), expectedHash: "e".repeat(64)
              }, ownerSources: [] }]
            }
          }) });
        });
      }
      result = { storeId: request.payload.expectedStoreId, storeRevision: request.payload.expectedStoreRevision,
        snapshotHash: "f".repeat(64), sourcePins: request.payload.sourcePins, references: [{ targetKind: "scenario", refId: "scenario.new", ref: {
          store: request.payload.expectedStoreId, id: "scenario.new", revision: "1".repeat(64), expectedHash: "1".repeat(64)
        }, ownerSources: [] }] };
    }
    return { ok: true, status: 200, text: async () => stringifyLossless({ protocolVersion: 1, requestId: request.requestId, status: "ok", result, diagnostics: [] }) };
  };
  globalThis.document = document; globalThis.window = window; globalThis.fetch = fetch;
  globalThis.Option = class extends FakeElement { constructor(text, value) { super("option"); this.text = text; this.value = value; this.textContent = text; } };
  const pause = () => new Promise((resolve) => setImmediate(resolve));
  const visibleText = (node) => [node.textContent, ...node.children.map(visibleText)].join(" ");
  const listInputs = () => document.querySelector("#reference-list").querySelectorAll("input[type=checkbox]");
  const clickDocument = async (id) => {
    const input = listInputs().find((node) => node.dataset.documentId === id);
    assert.ok(input, `missing source checkbox ${id}; rows=${listInputs().map((node) => node.dataset.documentId).join(",")}; lists=${requests.filter((item) => item.operation === "authoring.list").map((item) => item.payload.kind).join(",")}`);
    input.checked = true; await input.dispatch("change");
  };
  try {
    await import(`../web/workbench.js?reference-race=${Date.now()}`);
    const target = document.querySelector("#reference-kind"); target.value = "scenario";
    await document.querySelector("#load-references").click();
    const sourceKind = document.querySelector("#reference-source-kind"); sourceKind.value = "scenario_definition";
    assert.ok(sourceKind.children.some((option) => option.value === "topic_definition"), "source kinds are independent of the result target");
    await sourceKind.dispatch("change");
    await clickDocument("scenario.source.old");
    sourceKind.value = "topic_definition"; await sourceKind.dispatch("change");
    await clickDocument("topic.source.one");
    sourceKind.value = "scenario_definition"; await sourceKind.dispatch("change");
    const nextPage = document.querySelector("#reference-list").children.find((node) => node.textContent === "Load next page (100 max)");
    assert.ok(nextPage, "the source chooser exposes the next bounded page"); await nextPage.click();
    await clickDocument("scenario.source.third");
    const firstResolution = document.querySelector("#resolve-references").dispatch("click");
    await pause();
    const firstPayload = requests.filter((item) => item.operation === "references.list")[0].payload;
    assert.deepEqual(firstPayload.sourcePins.map((item) => item.documentId).sort(), ["scenario.source.old", "scenario.source.third", "topic.source.one"]);
    target.value = "item"; await target.dispatch("change");
    target.value = "scenario"; await target.dispatch("change");
    assert.equal(document.querySelector("#reference-selected-sources").children.length, 3, "changing result target preserves the explicit mixed source set");
    sourceKind.value = "scenario_definition"; await sourceKind.dispatch("change");
    const oldInput = listInputs().find((node) => node.dataset.documentId === "scenario.source.old");
    oldInput.checked = false; await oldInput.dispatch("change");
    await clickDocument("scenario.source.new");
    storeRevision = 2; scenarioPage = [{ documentId: "scenario.source.new", kind: "scenario_definition", revisionHash: "2".repeat(64) }];
    await document.querySelector("#load-references").click();
    const refreshedSource = document.querySelector("#reference-source-kind"); refreshedSource.value = "scenario_definition";
    await refreshedSource.dispatch("change");
    await clickDocument("scenario.source.new");
    const secondResolution = document.querySelector("#resolve-references").dispatch("click");
    await secondResolution;
    assert.ok(visibleText(document.querySelector("#reference-list")).includes("scenario.new"),
      `no resolved new candidate; refs=${JSON.stringify(requests.filter((item) => item.operation === "references.list").map((item) => item.payload))}; status=${document.querySelector("#reference-snapshot").textContent}`);
    assert.ok(requests.filter((item) => item.operation === "references.list")[1].payload.sourcePins.some((item) =>
      item.documentId === "scenario.source.new" && item.revisionHash === "2".repeat(64)));
    releaseOld(); await firstResolution;
    assert.ok(visibleText(document.querySelector("#reference-list")).includes("scenario.new"));
    assert.ok(!visibleText(document.querySelector("#reference-list")).includes("scenario.old"));
    assert.match(document.querySelector("#reference-snapshot").textContent, /store rev 2/);
    failNextResolve = true;
    await document.querySelector("#resolve-references").click();
    assert.ok(!visibleText(document.querySelector("#reference-list")).includes("scenario.new"));
    assert.match(document.querySelector("#reference-snapshot").textContent, /Resolution unavailable/);
    scenarioPage = [{ documentId: "scenario.current_page", kind: "scenario_definition", revisionHash: "8".repeat(64) }];
    delayNextScenarioPage = true;
    target.value = "scenario"; const staleBrowse = target.dispatch("change"); await pause();
    assert.equal(typeof releaseOldPage, "function");
    target.value = "item"; await target.dispatch("change");
    releaseOldPage(); await staleBrowse;
    assert.ok(visibleText(document.querySelector("#reference-list")).includes("scenario.current_page"));
    assert.ok(!visibleText(document.querySelector("#reference-list")).includes("scenario.late_page"));
  } finally {
    Object.assign(globalThis, { document: previous.document, window: previous.window, fetch: previous.fetch, Option: previous.Option });
  }
});

test("actual Topic editor resolves, selects, and saves an importer-pinned Scenario reference", async () => {
  const previous = { document: globalThis.document, window: globalThis.window, fetch: globalThis.fetch, Option: globalThis.Option };
  const document = new FakeDocument();
  const window = { confirm: () => true, addEventListener() {}, setTimeout: () => 0, clearTimeout() {} };
  for (const selector of ["#kind-list", "#new-record", "#record-list", "#reference-kind", "#reference-source-kind",
    "#load-references", "#reference-list", "#reference-selected-sources", "#resolve-references", "#reference-snapshot",
    "#diagnostics", "#validation-result", "#toast", "#connection", "#editor", "#welcome", "#kind-filter", "#record-filter",
    "#kind-kicker", "#document-title", "#source-kind-wrap", "#source-json", "#editor-content", "#deferred-notice",
    "#conflict-notice", "#graph-panel", "#preview", "#save", "#validate", "#undo", "#redo", "#revision-meta", "#save"])
    document.querySelector(selector);
  const topic = { schemaVersion: 1, topicId: "topic.reference.fixture", scenarios: [{ entryId: "entry.one", scenarioRef: {
    store: "old-store", id: "scenario.old", revision: "a".repeat(64), expectedHash: "a".repeat(64), futureRefField: "preserve"
  } }] };
  const descriptor = { kindId: "topic", displayName: "Topic", defaultSourceKind: "topic_definition", sourceKinds: ["topic_definition"],
    sourceContracts: [{ kind: "topic_definition", identityField: "topicId" }], capabilities: { create: true, preview: true },
    sections: [{ id: "topic", title: "Topic", fields: [{ fieldId: "scenarios", path: "scenarios", label: "Scenarios", control: "array",
      fields: [{ fieldId: "entryId", path: "entryId", label: "Entry ID", control: "text" },
        { fieldId: "scenarioRef", path: "scenarioRef", label: "Reusable Scenario Flow", control: "reference", targetKind: "scenario" }] }] }],
    documentTemplate: topic, graph: { enabled: false }, deferredFields: [] };
  const requests = [];
  let failNextResolve = false;
  const fetch = async (_url, options) => {
    const request = parseLossless(options.body); requests.push(request);
    let result = {};
    if (request.operation === "schema.describe") {
      const sourceKinds = { dialogue: ["dialogue_definition", "generic_dialogue"], scenario: ["scenario_definition"], topic: ["topic_definition"],
        furniture: ["furniture_definition"], item: ["item_definition"], action: ["action_definition"] }[request.payload.kindId];
      result = { descriptor: { ...descriptor, sourceKinds,
        referenceSourceKinds: request.payload.kindId === "furniture" ? ["furniture_definition", "furniture_catalogue_settings"] : sourceKinds } };
    } else if (request.operation === "authoring.list") {
      result = { documents: request.payload.kind === "topic_definition" ? [{ documentId: "topic.reference.fixture", kind: "topic_definition", revisionHash: "b".repeat(64) }] :
        request.payload.kind === "scenario_definition" ? [{ documentId: "scenario.source", kind: "scenario_definition", revisionHash: "c".repeat(64) }] : [], nextCursor: "" };
    } else if (request.operation === "authoring.read") result = { documentId: "topic.reference.fixture", kind: "topic_definition",
      logicalPath: "topic/reference.fixture.json", contentHash: "d".repeat(64), document: structuredClone(topic) };
    else if (request.operation === "authoring.info") result = { store: { storeId: "11111111-1111-4111-8111-111111111111", storeRevision: 7 } };
    else if (request.operation === "references.list") {
      if (failNextResolve) {
        failNextResolve = false;
        return { ok: true, status: 200, text: async () => stringifyLossless({ protocolVersion: 1, requestId: request.requestId,
          status: "invalid", result: null, diagnostics: [{ code: "dependency_missing", message: "Resolver fixture failed." }] }) };
      }
      result = { storeId: request.payload.expectedStoreId, storeRevision: 7, snapshotHash: "e".repeat(64),
        sourcePins: request.payload.sourcePins, references: [{ targetKind: "scenario", refId: "scenario.reusable", ref: {
          store: request.payload.expectedStoreId, id: "scenario.reusable", revision: "f".repeat(64), expectedHash: "f".repeat(64)
        }, ownerSources: [{ sourceId: "scenario.source", sourceKind: "scenario_definition", logicalPath: "scenario/source.json" }] }] };
    }
    else if (request.operation === "authoring.write") result = { documentId: request.payload.documentId, contentHash: "saved" };
    else if (request.operation === "preview.run") result = { kindId: "topic", fixtureOnly: true };
    return { ok: true, status: 200, text: async () => stringifyLossless({ protocolVersion: 1, requestId: request.requestId, status: "ok", result, diagnostics: [] }) };
  };
  globalThis.document = document; globalThis.window = window; globalThis.fetch = fetch;
  globalThis.Option = class extends FakeElement { constructor(text, value) { super("option"); this.text = text; this.value = value; this.textContent = text; } };
  try {
    await import(`../web/workbench.js?resolved-reference-save=${Date.now()}`);
    await document.querySelector("#kind-list").children.find((node) => node.dataset.kind === "topic").click();
    await document.querySelector("#record-list").children.find((node) => node.dataset.id === "topic.reference.fixture").click();
    const target = document.querySelector("#reference-kind"); target.value = "scenario";
    await target.dispatch("change");
    const sourceKind = document.querySelector("#reference-source-kind"); sourceKind.value = "scenario_definition";
    await sourceKind.dispatch("change");
    const source = document.querySelector("#reference-list").querySelectorAll("input[type=checkbox]").find((node) => node.dataset.documentId === "scenario.source");
    assert.ok(source, "the exact Scenario source revision is available to pin");
    source.checked = true; await source.dispatch("change");
    await document.querySelector("#resolve-references").click();
    let root = document.querySelector("#editor-content");
    const staleSelect = document.byLabel(root, "Reusable Scenario Flow").children.find((node) => node.tagName === "SELECT");
    let referenceSelect = staleSelect;
    assert.equal(referenceSelect.disabled, false, "resolved candidates reach the mounted Topic selector");

    target.value = "action"; await target.dispatch("change");
    referenceSelect.value = "0"; await referenceSelect.dispatch("change");
    assert.equal(JSON.parse(document.querySelector("#source-json").value).scenarios[0].scenarioRef.id, "scenario.old",
      "a selector retained by an old render cannot apply candidates after context invalidation");
    root = document.querySelector("#editor-content");
    referenceSelect = document.byLabel(root, "Reusable Scenario Flow").children.find((node) => node.tagName === "SELECT");
    assert.equal(referenceSelect.disabled, true, "the mounted selector is revoked as soon as its resolver context changes");

    target.value = "scenario"; await target.dispatch("change");
    await document.querySelector("#resolve-references").click();
    root = document.querySelector("#editor-content");
    referenceSelect = document.byLabel(root, "Reusable Scenario Flow").children.find((node) => node.tagName === "SELECT");
    assert.equal(referenceSelect.disabled, false, "resolving the current context re-enables the selector");
    const staleAfterFailure = referenceSelect;
    failNextResolve = true; await document.querySelector("#resolve-references").click();
    staleAfterFailure.value = "0"; await staleAfterFailure.dispatch("change");
    assert.equal(JSON.parse(document.querySelector("#source-json").value).scenarios[0].scenarioRef.id, "scenario.old",
      "a previously mounted selector cannot write after resolution fails");
    root = document.querySelector("#editor-content");
    referenceSelect = document.byLabel(root, "Reusable Scenario Flow").children.find((node) => node.tagName === "SELECT");
    assert.equal(referenceSelect.disabled, true, "failed resolution leaves the current selector visibly unavailable");
    await document.querySelector("#resolve-references").click();
    root = document.querySelector("#editor-content");
    referenceSelect = document.byLabel(root, "Reusable Scenario Flow").children.find((node) => node.tagName === "SELECT");
    assert.equal(referenceSelect.disabled, false, "a fresh successful resolution restores current candidates");
    referenceSelect.value = "0"; await referenceSelect.dispatch("change");
    assert.deepEqual(JSON.parse(document.querySelector("#source-json").value).scenarios[0].scenarioRef, {
      store: "11111111-1111-4111-8111-111111111111", id: "scenario.reusable", revision: "f".repeat(64), expectedHash: "f".repeat(64), futureRefField: "preserve"
    });
    document.querySelector("#fixture-id").value = "sit-together/no-table";
    document.querySelector("#preview-draw").value = "0.25";
    document.querySelector("#preview-events").value = '[{"action":"continue","atUtc":"2026-10-24T08:00:01Z"}]';
    await document.querySelector("#preview").onclick();
    const preview = requests.find((request) => request.operation === "preview.run");
    assert.deepEqual(preview.payload.sourcePins, [{ documentId: "scenario.source", revisionHash: "c".repeat(64) }],
      "the Editor preview uses the exact source revisions selected in its reference picker");
    assert.equal(preview.payload.expectedStoreId, "11111111-1111-4111-8111-111111111111");
    assert.equal(preview.payload.expectedStoreRevision, 7);
    assert.equal(preview.payload.kindId, "topic");
    assert.deepEqual(preview.payload.events, [{ action: "continue", atUtc: "2026-10-24T08:00:01Z" }]);
    target.value = "furniture"; await target.dispatch("change");
    assert.ok(document.querySelector("#reference-source-kind").children.some((option) => option.value === "furniture_catalogue_settings"),
      "Furniture catalogue settings are browseable as an exact preview dependency without becoming an editable Furniture root kind");
    await document.querySelector("#save").click();
    const write = requests.find((request) => request.operation === "authoring.write");
    assert.equal(write.payload.document.scenarios[0].scenarioRef.id, "scenario.reusable");
    assert.equal(write.payload.document.scenarios[0].scenarioRef.futureRefField, "preserve");
  } finally {
    Object.assign(globalThis, { document: previous.document, window: previous.window, fetch: previous.fetch, Option: previous.Option });
  }
});

test("actual Action editor transitions variants recoverably and keeps host-effect fields function-specific", async () => {
  const previous = { document: globalThis.document, window: globalThis.window, fetch: globalThis.fetch, Option: globalThis.Option };
  const document = new FakeDocument();
  const confirmations = [false, true, true, true];
  const window = { confirm: () => confirmations.shift() ?? true, addEventListener() {}, setTimeout: () => 0, clearTimeout() {} };
  for (const selector of ["#kind-list", "#new-record", "#record-list", "#reference-kind", "#reference-source-kind", "#load-references",
    "#reference-list", "#reference-selected-sources", "#resolve-references", "#reference-snapshot", "#diagnostics", "#validation-result",
    "#toast", "#connection", "#editor", "#welcome", "#kind-filter", "#record-filter", "#kind-kicker", "#document-title",
    "#source-kind-wrap", "#source-json", "#editor-content", "#deferred-notice", "#conflict-notice", "#graph-panel", "#preview",
    "#save", "#validate", "#undo", "#redo", "#revision-meta"])
    document.querySelector(selector);
  const template = { schemaVersion: 1, id: "action.variant.fixture", capabilityId: "scenario.activity_sequence", implementationKind: "activity_sequence",
    data: { steps: [{ stepId: "step.custom", primitiveId: "activity.complete", arguments: [], futureStepField: "keep" }], futureSequenceField: "keep" } };
  const descriptor = { kindId: "action", displayName: "Action", defaultSourceKind: "action_definition", sourceKinds: ["action_definition"],
    sourceContracts: [{ kind: "action_definition", identityField: "id" }], capabilities: { create: true, preview: false },
    actionCatalog: { capabilities: ["scenario.activity_sequence", "player.item.grant", "power.set"] },
    hostEffectCatalog: [{ functionId: "player.item.grant" }, { functionId: "power.set" }],
    sections: [{ id: "identity", title: "Action", fields: [
      { fieldId: "id", path: "id", label: "Action ID", control: "text" },
      { fieldId: "implementationKind", path: "implementationKind", label: "Implementation", control: "enum", options: ["activity_sequence", "host_effect"] },
      { fieldId: "capabilityId", path: "capabilityId", label: "Registered capability", control: "registeredCapability" },
      { fieldId: "data", path: "data", label: "Activity sequence data", control: "actionData", visibleWhenPath: "implementationKind", visibleWhenValue: "activity_sequence" },
      { fieldId: "functionId", path: "data.functionId", label: "Host-effect function", control: "enum", options: ["player.item.grant", "power.set"], visibleWhenPath: "implementationKind", visibleWhenValue: "host_effect" },
      { fieldId: "itemRef", path: "data.arguments.itemRef", label: "Granted Item", control: "reference", targetKind: "item", visibleWhenPath: "data.functionId", visibleWhenValue: "player.item.grant" },
      { fieldId: "quantity", path: "data.arguments.quantity", label: "Grant quantity", control: "int64", minimum: 1, visibleWhenPath: "data.functionId", visibleWhenValue: "player.item.grant" },
      { fieldId: "desiredState", path: "data.arguments.desiredState", label: "Desired power state", control: "enum", options: ["Off", "On"], visibleWhenPath: "data.functionId", visibleWhenValue: "power.set" }
    ] }], documentTemplate: template, graph: { enabled: false }, deferredFields: [] };
  const requests = [];
  const fetch = async (_url, options) => {
    const request = parseLossless(options.body); requests.push(request);
    let result = {};
    if (request.operation === "schema.describe") {
      const sourceKinds = { dialogue: ["dialogue_definition", "generic_dialogue"], scenario: ["scenario_definition"], topic: ["topic_definition"],
        furniture: ["furniture_definition"], item: ["item_definition"], action: ["action_definition"] }[request.payload.kindId];
      result = { descriptor: { ...descriptor, sourceKinds } };
    } else if (request.operation === "authoring.list") result = { documents: request.payload.kind === "item_definition" ? [
      { documentId: "item.source", kind: "item_definition", revisionHash: "a".repeat(64) }
    ] : [], nextCursor: "" };
    else if (request.operation === "authoring.info") result = { store: { storeId: "22222222-2222-4222-8222-222222222222", storeRevision: 2 } };
    else if (request.operation === "references.list") result = { storeId: request.payload.expectedStoreId, storeRevision: 2, snapshotHash: "b".repeat(64),
      sourcePins: request.payload.sourcePins, references: [{ targetKind: "item", refId: "item.water", ref: {
        store: request.payload.expectedStoreId, id: "item.water", revision: "c".repeat(64), expectedHash: "c".repeat(64)
      }, ownerSources: [{ sourceId: "item.source", sourceKind: "item_definition", logicalPath: "item/water.json" }] }] };
    else if (request.operation === "content.validate") result = { valid: true, diagnostics: [] };
    return { ok: true, status: 200, text: async () => stringifyLossless({ protocolVersion: 1, requestId: request.requestId, status: "ok", result, diagnostics: [] }) };
  };
  globalThis.document = document; globalThis.window = window; globalThis.fetch = fetch;
  globalThis.Option = class extends FakeElement { constructor(text, value) { super("option"); this.text = text; this.value = value; this.textContent = text; } };
  try {
    await import(`../web/workbench.js?action-variant=${Date.now()}`);
    await document.querySelector("#kind-list").children.find((node) => node.dataset.kind === "action").click();
    await document.querySelector("#new-record").click();

    const target = document.querySelector("#reference-kind"); target.value = "item"; await target.dispatch("change");
    const sourceKind = document.querySelector("#reference-source-kind"); sourceKind.value = "item_definition"; await sourceKind.dispatch("change");
    const source = document.querySelector("#reference-list").querySelectorAll("input[type=checkbox]").find((node) => node.dataset.documentId === "item.source");
    assert.ok(source, "an exact Item source revision is available"); source.checked = true; await source.dispatch("change");
    await document.querySelector("#resolve-references").click();

    const changeSelect = async (labelText, value) => {
      const label = document.byLabel(document.querySelector("#editor-content"), labelText);
      const select = label.children.find((node) => node.tagName === "SELECT");
      assert.ok(select, `missing ${labelText} select`); select.value = value; await select.dispatch("change");
    };
    await changeSelect("Implementation", "host_effect");
    assert.equal(JSON.parse(document.querySelector("#source-json").value).implementationKind, "activity_sequence",
      "declining the destructive variant switch leaves authored data and visible mode unchanged");
    await changeSelect("Implementation", "host_effect");
    let saved = JSON.parse(document.querySelector("#source-json").value);
    assert.equal(saved.capabilityId, "power.set");
    assert.deepEqual(saved.data, { functionId: "power.set", arguments: { desiredState: "Off" } });
    assert.equal(saved.data.steps, undefined, "hidden sequence data does not leak into the host-effect schema");
    await document.querySelector("#undo").click();
    saved = JSON.parse(document.querySelector("#source-json").value);
    assert.equal(saved.implementationKind, "activity_sequence");
    assert.equal(saved.data.futureSequenceField, "keep", "Undo restores the original sequence, including extensions");
    await document.querySelector("#redo").click();

    await changeSelect("Host-effect function", "player.item.grant");
    const itemLabel = document.byLabel(document.querySelector("#editor-content"), "Granted Item");
    const itemSelect = itemLabel.children.find((node) => node.tagName === "SELECT");
    assert.equal(itemSelect.disabled, false, "the Action form receives resolved Item candidates");
    itemSelect.value = "0"; await itemSelect.dispatch("change");
    const quantity = document.byLabel(document.querySelector("#editor-content"), "Grant quantity").children.find((node) => node.tagName === "INPUT");
    quantity.value = "3"; await quantity.dispatch("change");
    await changeSelect("Host-effect function", "power.set");
    saved = JSON.parse(document.querySelector("#source-json").value);
    assert.equal(saved.capabilityId, "power.set");
    assert.deepEqual(saved.data, { functionId: "power.set", arguments: { desiredState: "Off" } },
      "switching registered functions removes fields not in the new argument contract");
    await document.querySelector("#undo").click();
    saved = JSON.parse(document.querySelector("#source-json").value);
    assert.equal(saved.data.functionId, "player.item.grant");
    assert.equal(saved.data.arguments.itemRef.id, "item.water");
    assert.equal(saved.data.arguments.quantity, 3);
    await document.querySelector("#validate").click();
    const validation = requests.filter((request) => request.operation === "content.validate").at(-1);
    assert.equal(validation.payload.document.data.functionId, "player.item.grant");
    assert.equal(validation.payload.document.data.arguments.itemRef.id, "item.water");
    assert.equal(validation.payload.document.data.arguments.quantity, 3);
  } finally {
    Object.assign(globalThis, { document: previous.document, window: previous.window, fetch: previous.fetch, Option: previous.Option });
  }
});

test("Action sequence form adds catalog primitives, binds produced entities, and exposes graph-addressable steps", async () => {
  const previousDocument = globalThis.document;
  const document = new FakeDocument();
  globalThis.document = document;
  try {
    const { mountActivitySequenceEditor } = await import(`../web/activity-sequence-editor.js?sequence-form=${Date.now()}`);
    const source = { data: { steps: [] } };
    const descriptor = { actionCatalog: { primitives: [
      { primitiveId: "reserve_nearest", operationKind: "Pure", arguments: [
        { name: "useId", valueType: "text", required: true },
        { name: "functionId", valueType: "enum", required: false, allowedValues: ["power.set"] }
      ], results: [{ name: "targetToken", valueType: "entity", entityKind: "activity_target" }] },
      { primitiveId: "move_to", operationKind: "Pure", arguments: [
        { name: "target", valueType: "entity", required: true, entityKind: "activity_target", requiredProducerPrimitiveId: "reserve_nearest" }
      ], results: [] },
      { primitiveId: "complete", operationKind: "Pure", arguments: [], results: [] }
    ] } };
    const container = document.createElement("div");
    let changeCount = 0;
    const render = () => container.replaceChildren(mountActivitySequenceEditor({ descriptor, document: source, path: "data",
      onChange: () => changeCount++, canEdit: () => true, onStructureChange: render }));
    render();
    const find = (root, predicate) => {
      if (predicate(root)) return root;
      for (const child of root.children || []) { const result = find(child, predicate); if (result) return result; }
      return null;
    };
    const addPrimitive = async (primitiveId) => {
      const root = container.children[0];
      const chooser = find(root, (node) => node.tagName === "SELECT" && node.attributes["aria-label"] === "Primitive to add");
      chooser.value = primitiveId;
      const add = find(root, (node) => node.tagName === "BUTTON" && node.textContent === "Add primitive");
      await add.click();
    };
    await addPrimitive("reserve_nearest");
    await addPrimitive("move_to");
    const targetLabel = document.byLabel(container, "target · required");
    const targetSelect = targetLabel.children.find((node) => node.tagName === "SELECT");
    assert.ok(targetSelect.children.some((option) => option.attributes.value === "reserve_nearest.1.targetToken"),
      "entity arguments list only the prior registered producer's result binding");
    targetSelect.value = "reserve_nearest.1.targetToken"; await targetSelect.dispatch("change");
    assert.equal(source.data.steps[1].arguments[0].value.bindingId, "reserve_nearest.1.targetToken");
    assert.equal(source.data.steps[0].successNextStepId, source.data.steps[1].stepId,
      "the palette wires a new step after an unconnected previous step");
    assert.equal(container.children[0].children.find((node) => node.attributes["data-object-path"] === "data.steps.0") === undefined, false);
    assert.ok(changeCount >= 3);

    const composerDocument = { data: { composerId: "sequence.wait_then_complete.v1",
      composerArguments: [{ name: "durationSeconds", value: { type: "double", literal: 0.25 } }] } };
    descriptor.actionCatalog.composers = [{ composerId: "sequence.wait_then_complete.v1", arguments: [
      { name: "durationSeconds", valueType: "double", required: true, minimum: 0, maximum: 30 }
    ] }];
    const composerContainer = document.createElement("div");
    composerContainer.append(mountActivitySequenceEditor({ descriptor, document: composerDocument, path: "data",
      onChange: () => {}, canEdit: () => true, onStructureChange() {} }));
    const duration = document.byLabel(composerContainer, "durationSeconds").children.find((node) => node.tagName === "INPUT");
    duration.value = "0.75"; await duration.dispatch("change");
    assert.equal(composerDocument.data.composerArguments[0].value.literal, 0.75,
      "registered composer arguments remain editable through their catalog descriptor");
  } finally {
    globalThis.document = previousDocument;
  }
});

test("Furniture catalogue settings use the Furniture kind through form, validate, fixed save, and reopen", async () => {
  const previous = { document: globalThis.document, window: globalThis.window, fetch: globalThis.fetch, Option: globalThis.Option };
  const document = new FakeDocument();
  const sourceKinds = ["furniture_definition", "furniture_catalogue_settings"];
  const sourceContracts = [
    { kind: "furniture_definition", identityField: "id" },
    { kind: "furniture_catalogue_settings", identityField: "catalogueVersion",
      fixedDocumentId: "furniture.catalogue", fixedLogicalPath: "Furniture/catalogue-settings.json" }
  ];
  const settingsTemplate = { schemaVersion: 1, catalogueVersion: "catalogue-v1", lengthUnit: "m", angleUnit: "deg",
    coordinateConvention: "unity_local_y_up_positive_z_forward", relationshipRules: [], capabilities: [], legacyAliases: [],
    futureMetadata: { keep: true } };
  const descriptor = (sourceKind) => ({ kindId: "furniture", displayName: "Furniture", defaultSourceKind: "furniture_definition",
    sourceKinds, sourceContracts, capabilities: { create: true, preview: sourceKind !== "furniture_catalogue_settings" },
    sections: [{ id: "catalogue", title: "Furniture catalogue conventions", fields: [
      { fieldId: "catalogueVersion", path: "catalogueVersion", label: "Catalogue version", control: "text", required: true }
    ] }], documentTemplate: sourceKind === "furniture_catalogue_settings" ? settingsTemplate : { schemaVersion: 1, id: "" },
    graph: { enabled: false }, deferredFields: [] });
  const selectors = ["#editor", "#welcome", "#kind-list", "#new-record", "#kind-filter", "#record-filter", "#record-list",
    "#kind-kicker", "#document-title", "#revision-meta", "#source-kind-wrap", "#source-kind", "#source-json", "#editor-content",
    "#deferred-notice", "#conflict-notice", "#graph-panel", "#preview", "#preview-result", "#preview-events", "#diagnostics",
    "#validation-result", "#undo", "#redo", "#save", "#validate", "#refresh", "#load-references", "#reference-kind",
    "#reference-source-kind", "#reference-list", "#fixture-id", "#preview-draw", "#apply-json", "#discard-json",
    "#raw-json-pending", "#toast", "#connection"];
  selectors.forEach((selector) => document.querySelector(selector));
  const window = { confirm: () => true, addEventListener() {}, setTimeout: () => 0, clearTimeout() {} };
  const requests = []; let savedSettings = null; let revision = 0;
  const fetch = async (_url, options) => {
    const request = parseLossless(options.body); requests.push(request); let result = {};
    if (request.operation === "schema.describe") result = { descriptor: descriptor(request.payload.sourceKind || "furniture_definition") };
    else if (request.operation === "authoring.info") result = { store: { storeId: "isolated-workbench", storeRevision: 0 } };
    else if (request.operation === "authoring.list") result = { documents: savedSettings && request.payload.kind === "furniture_catalogue_settings"
      ? [{ documentId: "furniture.catalogue", kind: "furniture_catalogue_settings", revisionHash: "saved-settings-hash" }] : [], nextCursor: "" };
    else if (request.operation === "content.validate") result = { valid: true, diagnostics: [] };
    else if (request.operation === "authoring.write") { savedSettings = request.payload.document; revision++; result = { contentHash: `settings-revision-${revision}` }; }
    else if (request.operation === "authoring.read") result = { documentId: "furniture.catalogue", kind: "furniture_catalogue_settings",
      logicalPath: "Furniture/catalogue-settings.json", contentHash: `settings-revision-${revision}`, document: savedSettings };
    return { ok: true, status: 200, text: async () => stringifyLossless({ protocolVersion: 1, requestId: request.requestId,
      status: "ok", result, diagnostics: [] }) };
  };
  globalThis.document = document; globalThis.window = window; globalThis.fetch = fetch;
  globalThis.Option = class { constructor(text, value) { this.text = text; this.value = value; } };
  try {
    await import(`../web/workbench.js?furniture-settings-form=${Date.now()}`);
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(document.querySelector("#kind-list").children.map((button) => button.dataset.kind),
      ["dialogue", "scenario", "topic", "furniture", "item", "action"], "settings remain a Furniture source contract, not a seventh kind");
    await document.querySelector("#kind-list").children.find((button) => button.dataset.kind === "furniture").click();
    await document.querySelector("#new-record").click();
    const sourceKind = document.querySelector("#source-kind");
    assert.equal(document.querySelector("#source-kind-wrap").hidden, false);
    sourceKind.value = "furniture_catalogue_settings"; await sourceKind.dispatch("change");
    const catalogueVersion = document.byLabel(document.querySelector("#editor-content"), "Catalogue version")
      .children.find((node) => node.tagName === "INPUT");
    catalogueVersion.value = "catalogue-workbench-v2"; await catalogueVersion.dispatch("change");
    await document.querySelector("#validate").click();
    const validate = requests.filter((request) => request.operation === "content.validate").at(-1);
    assert.equal(validate.payload.kindId, "furniture");
    assert.equal(validate.payload.sourceKind, "furniture_catalogue_settings");
    await document.querySelector("#save").click();
    const write = requests.filter((request) => request.operation === "authoring.write").at(-1).payload;
    assert.equal(write.kind, "furniture_catalogue_settings");
    assert.equal(write.documentId, "furniture.catalogue");
    assert.equal(write.logicalPath, "Furniture/catalogue-settings.json");
    assert.equal(write.document.catalogueVersion, "catalogue-workbench-v2");
    assert.deepEqual(write.document.futureMetadata, { keep: true }, "known-field edits preserve unrelated source metadata");
    const record = document.querySelector("#record-list").children[0];
    assert.ok(record, "saved singleton reappears in the Furniture catalogue");
    await record.click();
    assert.equal(requests.filter((request) => request.operation === "authoring.read").at(-1).payload.documentId, "furniture.catalogue");
    assert.match(document.querySelector("#source-json").value, /catalogue-workbench-v2/);
    assert.equal(document.querySelector("#document-title").textContent, "Furniture catalogue settings");
    const reopenedVersion = document.byLabel(document.querySelector("#editor-content"), "Catalogue version")
      .children.find((node) => node.tagName === "INPUT");
    reopenedVersion.value = "catalogue-workbench-v3"; await reopenedVersion.dispatch("change");
    await document.querySelector("#save").click();
    const edit = requests.filter((request) => request.operation === "authoring.write").at(-1).payload;
    assert.equal(edit.expectedHash, "settings-revision-1", "editing submits the exact reopened revision for CAS");
    assert.equal(savedSettings.catalogueVersion, "catalogue-workbench-v3");
  } finally {
    Object.assign(globalThis, { document: previous.document, window: previous.window, fetch: previous.fetch, Option: previous.Option });
  }
});

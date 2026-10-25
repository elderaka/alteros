import { cloneLossless, displayJsonValue, isRawNumber, parseLossless, RawNumber, stringifyLossless } from "./lossless-json.js";
import { mountActivitySequenceEditor } from "./activity-sequence-editor.js";

const el = (tag, attrs = {}, text = "") => {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (key === "className") node.className = value;
    else if (key.startsWith("on") && typeof value === "function") node.addEventListener(key.slice(2).toLowerCase(), value);
    else if (value !== undefined && value !== null) node.setAttribute(key, String(value));
  }
  if (text !== "") node.textContent = text;
  return node;
};
const get = (root, path) => path ? path.split(".").reduce((value, part) => value == null ? undefined : value[part], root) : root;
const set = (root, path, value) => {
  const parts = path.split("."); let target = root;
  for (const part of parts.slice(0, -1)) { if (!target[part] || typeof target[part] !== "object") target[part] = {}; target = target[part]; }
  if (value === undefined) delete target[parts.at(-1)]; else target[parts.at(-1)] = value;
};
const clone = cloneLossless;

function referenceSelect(field, value, candidates, onSelect, canEdit, isCandidateContextCurrent, preservedLabel = "") {
  const control = el("select");
  const current = value && typeof value === "object" ? value : null;
  const sameRef = (candidate) => current && candidate.ref?.store === current.store && candidate.ref?.id === current.id &&
    candidate.ref?.revision === current.revision && candidate.ref?.expectedHash === current.expectedHash;
  const selectedIndex = candidates.findIndex(sameRef);
  control.append(el("option", { value: "" }, candidates.length ? "Choose an importer-resolved reference…" : "Resolve source references in the inspector first"));
  candidates.forEach((candidate, index) => {
    const owner = (candidate.ownerSources || []).map((item) => item.sourceId || item.logicalPath).filter(Boolean).join(", ");
    control.append(el("option", { value: String(index) }, `${candidate.refId} · ${candidate.ref?.revision?.slice(0, 12) || "revision unknown"}${owner ? ` · ${owner}` : ""}`));
  });
  if (current && selectedIndex < 0) {
    control.append(el("option", { value: "existing" }, preservedLabel || `${current.id || "Existing reference"} · pinned source value preserved`));
    control.value = "existing";
  } else control.value = selectedIndex < 0 ? "" : String(selectedIndex);
  const canSelect = () => canEdit() && isCandidateContextCurrent();
  control.disabled = !canSelect() || candidates.length === 0;
  control.addEventListener("change", () => {
    if (!canSelect() || control.value === "" || control.value === "existing") return;
    const candidate = candidates[Number(control.value)]; if (candidate?.ref) onSelect({ ...(current || {}), ...candidate.ref });
  });
  return control;
}

function syncArrayBindings(document, sourcePath, targetPath) {
  const source = get(document, sourcePath);
  const previous = get(document, targetPath);
  const bindings = Array.isArray(previous) ? previous : [];
  const key = (item) => [item?.functionId, item?.pointId, item?.useId].join("\u0000");
  const byKey = new Map(bindings.map((item) => [key(item), item]));
  const next = (Array.isArray(source) ? source : []).filter((item) => item?.functionId && item?.pointId && item?.useId)
    .map((item) => ({ ...(byKey.get(key(item)) || {}), functionId: item.functionId, pointId: item.pointId, useId: item.useId }));
  set(document, targetPath, next);
}

export function mountDraftEditor({ descriptor, document, onChange, canEdit = () => true, referenceCandidates = {},
  referenceGeneration = 0, getReferenceGeneration = () => referenceGeneration }) {
  const root = el("div", { className: "form-sections" });
  const update = () => { if (canEdit()) onChange(document); };
  const isCandidateContextCurrent = () => referenceGeneration === getReferenceGeneration();
  const confirmVariantReplacement = () => typeof window === "undefined" || typeof window.confirm !== "function" ||
    window.confirm("This replaces the Action data for the selected variant. Use Undo to restore the previous mode and data. Continue?");
  const hostEffectArguments = (functionId) => functionId === "player.item.grant" ? { quantity: 1 } :
    functionId === "power.set" ? { desiredState: "Off" } : {};
  const defaultHostEffectFunction = () => descriptor.hostEffectCatalog?.find((item) => item.functionId === "power.set")?.functionId ||
    descriptor.hostEffectCatalog?.[0]?.functionId || descriptor.sections?.flatMap((section) => section.fields || [])
      .find((field) => field.fieldId === "functionId")?.options?.[0] || "";
  const replaceActionVariant = (implementationKind, functionId = "") => {
    const hostFunction = functionId || defaultHostEffectFunction();
    set(document, "implementationKind", implementationKind);
    if (implementationKind === "activity_sequence") {
      set(document, "capabilityId", descriptor.documentTemplate?.capabilityId || "scenario.activity_sequence");
      set(document, "data", clone(descriptor.documentTemplate?.data || { steps: [] }));
    } else {
      set(document, "capabilityId", hostFunction);
      set(document, "data", { functionId: hostFunction, arguments: hostEffectArguments(hostFunction) });
    }
  };
  const input = (field, value, write, id, scope = "") => {
    const wrap = el("label", { className: `field ${field.control === "textarea" || field.control === "json" ? "wide" : ""}` });
    wrap.append(el("span", { className: "field-label" }, field.label || field.fieldId));
    let control;
    if (field.control === "readOnlyArray" || field.readOnly) {
      control = el("output", { className: "read-only" }, value == null ? "—" : (typeof value === "object" && !isRawNumber(value) ? stringifyLossless(value, 2) : displayJsonValue(value)));
    } else if (field.control === "boolean") {
      control = el("input", { type: "checkbox" }); control.checked = Boolean(value);
      control.addEventListener("change", () => write(control.checked));
    } else if (field.control === "enum" || field.control === "registeredCapability") {
      control = el("select");
      const options = [...(field.control === "registeredCapability" ? (descriptor.actionCatalog?.capabilities || []) : (field.options || []))];
      if (value && !options.some((item) => item === value)) options.unshift(value);
      for (const option of options) { const item = el("option", { value: option }, option); control.append(item); }
      control.value = value ?? ""; control.addEventListener("change", () => write(control.value));
    } else if (field.control === "reference" && field.targetKind) {
      const candidates = referenceCandidates[field.targetKind] || [];
      const legacyNodes = field.fieldId === "scenarioRef" && scope.startsWith("scenarios.") ? get(document, `${scope}.nodes`) : null;
      const inLegacyEntry = Array.isArray(legacyNodes) && legacyNodes.length > 0;
      control = referenceSelect(field, value, candidates, write, canEdit, isCandidateContextCurrent);
      control.disabled = !canEdit() || inLegacyEntry || candidates.length === 0;
      if (inLegacyEntry) control.title = "This entry contains legacy inline nodes. They are preserved; create a separate reusable entry to use a pinned Scenario.";
    } else if (["array", "object", "json", "localizedText", "actionData", "reference", "registeredFunction", "registeredFunctionContract", "registeredPresentation"].includes(field.control)) {
      control = el("textarea", { rows: field.control === "array" ? 2 : 4, spellcheck: "false" });
      control.value = value == null ? "" : stringifyLossless(value, 2);
      control.addEventListener("change", () => { try { write(parseLossless(control.value)); control.classList.remove("invalid"); } catch (_) { control.classList.add("invalid"); } });
      control.dataset.json = "true";
    } else if (field.control === "int64") {
      control = el("input", { type: "text", inputmode: "numeric", autocomplete: "off" });
      control.value = displayJsonValue(value);
      if (field.minimum != null) control.min = displayJsonValue(field.minimum);
      if (field.maximum != null) control.max = displayJsonValue(field.maximum);
      control.addEventListener("change", () => {
        if (!/^-?\d+$/.test(control.value)) return control.classList.add("invalid");
        try {
          const next = BigInt(control.value);
          const min = field.minimum == null ? -9223372036854775808n : BigInt(displayJsonValue(field.minimum));
          const max = field.maximum == null ? 9223372036854775807n : BigInt(displayJsonValue(field.maximum));
          if (next < min || next > max) return control.classList.add("invalid");
        } catch (_) { return control.classList.add("invalid"); }
        control.classList.remove("invalid"); write(new RawNumber(control.value));
      });
    } else {
      const numeric = ["integer", "number"].includes(field.control);
      control = el("input", { type: numeric ? "number" : "text" });
      if (field.control === "integer") control.step = "1";
      if (field.control === "number") control.step = "any";
      if (field.minimum != null) control.min = displayJsonValue(field.minimum);
      if (field.maximum != null) control.max = displayJsonValue(field.maximum);
      control.value = displayJsonValue(value);
      control.addEventListener("change", () => {
        if (control.value === "") return write(undefined);
        if (numeric) {
          const number = Number(control.value); if (!Number.isFinite(number) || (field.control === "integer" && !Number.isSafeInteger(number))) return control.classList.add("invalid");
          control.classList.remove("invalid"); return write(number);
        }
        write(control.value);
      });
    }
    control.id = id; if (field.description) control.title = field.description;
    wrap.append(control);
    if (field.description) wrap.append(el("small", { className: "field-help" }, field.description));
    if (field.ownedPath && field.referencePath) wrap.append(el("small", { className: "field-help" }, `Owned value: ${field.ownedPath} · exact pinned reference: ${field.referencePath}`));
    return wrap;
  };

  function renderActionDialogueReferences(full, parent) {
    const reference = (descriptor.references || []).find((item) => item.targetKind === "dialogue" &&
      (item.paths || []).includes("data.steps[].arguments[].value"));
    if (!reference) return;
    const argumentName = reference.argumentName || "";
    if (!argumentName) return;
    const data = get(document, full);
    if (!Array.isArray(data?.steps)) return;
    const candidates = referenceCandidates.dialogue || [];
    const rows = [];
    data.steps.forEach((step, stepIndex) => {
      const primitive = (descriptor.actionCatalog?.primitives || []).find((item) => item.primitiveId === step?.primitiveId &&
        (item.arguments || []).some((argument) => argument.name === argumentName && argument.valueType === "text"));
      if (!primitive || !Array.isArray(step.arguments)) return;
      step.arguments.forEach((argument, argumentIndex) => {
        if (argument?.name !== argumentName) return;
        const literal = argument.value?.literal;
        const exactCandidate = candidates.find((candidate) => candidate.ref?.id === literal);
        const currentRef = exactCandidate?.ref || (literal ? { store: "", id: literal, revision: "", expectedHash: "" } : null);
        const field = { fieldId: "dialogueId", label: `${step.stepId || `Step ${stepIndex + 1}`} · Dialogue`, targetKind: "dialogue" };
        const select = referenceSelect(field, currentRef, candidates, (selected) => {
          const current = get(document, full);
          const currentArgument = current?.steps?.[stepIndex]?.arguments?.[argumentIndex];
          if (!currentArgument) return;
          currentArgument.value = { type: "text", literal: selected.id };
          update(); renderEditorArray();
        }, canEdit, isCandidateContextCurrent, `Existing Dialogue ID preserved: ${literal}`);
        const row = el("label", { className: "field" });
        row.append(el("span", { className: "field-label" }, field.label), select);
        rows.push(row);
      });
    });
    if (rows.length) {
      const group = el("fieldset", { className: "nested-field" }); group.append(el("legend", {}, "Pinned sequence Dialogue references"));
      group.append(...rows); parent.append(group);
    }
  }

  function renderActivitySequence(field, full, parent) {
    parent.append(mountActivitySequenceEditor({ descriptor, document, path: full,
      onChange: update, canEdit, onStructureChange: renderEditorArray }));
  }

  function renderField(field, scope, parent, keyPrefix, bindingSync = null) {
    if (field.visibleWhenPath && get(document, field.visibleWhenPath) !== field.visibleWhenValue) return;
    const path = field.path || field.fieldId;
    const full = path ? (scope ? `${scope}.${path}` : path) : scope;
    const current = get(document, full);
    if (field.control === "actionData") {
      renderActivitySequence(field, full, parent);
      renderActionDialogueReferences(full, parent);
      return;
    }
    if (field.control === "object" && field.fields && !field.readOnly) {
      const group = el("fieldset", { className: "nested-field" }); group.append(el("legend", {}, field.label));
      for (const child of field.fields) renderField(child, full, group, keyPrefix, bindingSync);
      parent.append(group); return;
    }
    if (field.control === "localizedText") {
      const group = el("div", { className: "localized-editor" });
      const ownedPath = scope ? `${scope}.${field.ownedPath}` : field.ownedPath;
      const referencePath = scope ? `${scope}.${field.referencePath}` : field.referencePath;
      const mirrorPath = field.mirrorPath || "";
      const owned = get(document, ownedPath); const reference = get(document, referencePath);
      const mode = el("select", { className: "localized-mode" });
      mode.append(el("option", { value: "owned" }, "Owned localized Text"), el("option", { value: "reference" }, "Pinned Text reference"));
      mode.value = reference && !owned ? "reference" : "owned";
      group.append(mode);
      const details = el("div", { className: "localized-details" });
      function renderLocalized() {
        details.replaceChildren();
        if (mode.value === "reference") {
          const ref = get(document, referencePath) || { store: "", id: "", revision: "", expectedHash: "" };
          const referenceField = { fieldId: field.fieldId + "Ref", label: "Pinned Text", targetKind: field.referenceKind };
          details.append(referenceSelect(referenceField, ref, referenceCandidates[field.referenceKind] || [], (next) => {
            set(document, referencePath, next); if (mirrorPath) set(document, mirrorPath, next.id); update();
          }, canEdit, isCandidateContextCurrent));
          if (ref.id) details.append(el("small", { className: "field-help" }, `Current pin: ${ref.store} · ${ref.id} · ${ref.revision} · ${ref.expectedHash}`));
        } else {
          const text = get(document, ownedPath) || { id: "", translations: [] };
          const idField = el("label", { className: "field" }); idField.append(el("span", { className: "field-label" }, "Text ID"));
          const idInput = el("input", { type: "text" }); idInput.value = text.id || "";
          idInput.addEventListener("change", () => { if (!canEdit()) return; const next = { ...(get(document, ownedPath) || text), id: idInput.value }; set(document, ownedPath, next); if (mirrorPath) set(document, mirrorPath, idInput.value); update(); });
          idField.append(idInput); details.append(idField);
          const translations = Array.isArray(text.translations) ? text.translations : [];
          translations.forEach((translation, index) => {
            const row = el("div", { className: "translation-row" });
            const language = el("input", { type: "text", placeholder: "Language (e.g. en)" }); language.value = translation.language || "";
            const phrase = el("textarea", { rows: 2, placeholder: "Localized text" }); phrase.value = translation.text || "";
            const saveTranslation = () => {
              if (!canEdit()) return;
              const current = get(document, ownedPath) || text;
              const currentTranslations = Array.isArray(current.translations) ? current.translations : [];
              const next = { ...current, translations: [...currentTranslations] };
              next.translations[index] = { ...(next.translations[index] || {}), language: language.value, text: phrase.value };
              set(document, ownedPath, next); update();
            };
            language.addEventListener("change", saveTranslation); phrase.addEventListener("change", saveTranslation);
            row.append(language, phrase); details.append(row);
          });
          const addTranslation = el("button", { type: "button", className: "button small" }, "Add translation");
          addTranslation.addEventListener("click", () => {
            if (!canEdit()) return;
            const current = get(document, ownedPath) || text;
            const next = { ...current, translations: [...(Array.isArray(current.translations) ? current.translations : []), { language: "en", text: "" }] };
            set(document, ownedPath, next); update(); renderLocalized();
          });
          details.append(addTranslation);
        }
      }
      mode.addEventListener("change", () => {
        if (!canEdit()) { mode.value = get(document, referencePath) && !get(document, ownedPath) ? "reference" : "owned"; renderLocalized(); return; }
        if (mode.value === "owned") {
          set(document, referencePath, undefined);
          if (!get(document, ownedPath)) set(document, ownedPath, { id: "", translations: [] });
          if (mirrorPath) set(document, mirrorPath, get(document, ownedPath)?.id || "");
        }
        else { set(document, ownedPath, undefined); if (mirrorPath) set(document, mirrorPath, ""); set(document, referencePath, { store: "", id: "", revision: "", expectedHash: "" }); }
        update(); renderLocalized();
      });
      renderLocalized(); group.append(details);
      const wrap = el("label", { className: "field wide" }); wrap.append(el("span", { className: "field-label" }, field.label || field.fieldId), group);
      if (field.description) wrap.append(el("small", { className: "field-help" }, field.description));
      parent.append(wrap); return;
    }
    if (field.control === "array") {
      const group = el("section", { className: "array-field" });
      const heading = el("div", { className: "array-heading" }); heading.append(el("h4", {}, field.label));
      const add = el("button", { type: "button", className: "button small" }, "Add item");
      add.disabled = field.readOnly || (field.maximum != null && (current || []).length >= field.maximum);
      add.addEventListener("click", () => {
        if (!canEdit()) return;
        const rows = Array.isArray(get(document, full)) ? get(document, full) : [];
        const item = {};
        if (field.fields) for (const child of field.fields) if (child.defaultValue != null) item[child.path || child.fieldId] = clone(child.defaultValue);
        if (field.itemControl === "text" || field.itemControl === "textarea") rows.push(""); else rows.push(item);
        set(document, full, rows); if (field.syncPath) syncArrayBindings(document, full, field.syncPath); update(); renderEditorArray();
      });
      heading.append(add); group.append(heading);
      const rows = Array.isArray(current) ? current : [];
      rows.forEach((row, index) => {
        const card = el("div", { className: "array-row", "data-object-path": `${full}.${index}` });
        const rowTitle = field.fields ? (row?.[field.fields[0]?.path || field.fields[0]?.fieldId] || `Item ${index + 1}`) : `Item ${index + 1}`;
        const rowHead = el("div", { className: "array-row-heading" }); rowHead.append(el("strong", {}, String(rowTitle)));
        const remove = el("button", { type: "button", className: "icon-button danger", title: "Remove item" }, "×");
        remove.disabled = field.readOnly || (field.minimum != null && rows.length <= field.minimum);
        remove.addEventListener("click", () => { if (!canEdit()) return; rows.splice(index, 1); set(document, full, rows); if (field.syncPath) syncArrayBindings(document, full, field.syncPath); update(); renderEditorArray(); });
        rowHead.append(remove); card.append(rowHead);
        if (field.itemControl === "text" || field.itemControl === "textarea") {
          const itemInput = field.itemControl === "textarea" ? el("textarea", { rows: 2 }) : el("input", { type: "text" });
          itemInput.value = row ?? "";
          itemInput.addEventListener("change", () => { if (!canEdit()) return; rows[index] = itemInput.value; set(document, full, rows); if (field.syncPath) syncArrayBindings(document, full, field.syncPath); update(); }); card.append(itemInput);
        } else if (field.fields) {
          let visibleFields = field.fields;
          if (full === "steps" && row?.kind) {
            const byKind = {
              dialogue: ["id", "kind", "beats", "dialogueRef", "nextStepId", "closeExchange"],
              ask: ["id", "kind", "questionId", "question", "options", "allowFreeText", "freeTextNextStepId", "skipNextStepId", "cancelNextStepId"],
              action: ["id", "kind", "actionRef", "capabilityId", "arguments", "successNextStepId", "failureNextStepId", "cancelNextStepId"],
              host_effect: ["id", "kind", "actionRef", "capabilityId", "arguments", "successNextStepId", "failureNextStepId", "cancelNextStepId", "data"],
              wait: ["id", "kind", "conditionId", "successNextStepId", "failureNextStepId", "cancelNextStepId", "data"],
              branch: ["id", "kind", "objective", "sourceId", "cases", "defaultNextStepId"],
              end: ["id", "kind", "outcome"]
            };
            const allowed = new Set(byKind[row.kind] || byKind.end);
            visibleFields = field.fields.filter((child) => allowed.has(child.fieldId));
          }
          const childSync = field.syncPath ? { sourcePath: full, targetPath: field.syncPath } : bindingSync;
          for (const child of visibleFields) renderField(child, `${full}.${index}`, card, keyPrefix, childSync);
        } else {
          const raw = el("textarea", { rows: 3, spellcheck: "false" }); raw.value = stringifyLossless(row, 2);
          raw.addEventListener("change", () => { if (!canEdit()) return; try { rows[index] = parseLossless(raw.value); set(document, full, rows); if (field.syncPath) syncArrayBindings(document, full, field.syncPath); update(); raw.classList.remove("invalid"); } catch (_) { raw.classList.add("invalid"); } }); card.append(raw);
        }
        group.append(card);
      });
      parent.append(group); return;
    }
    const uniquePath = full.replace(/[^a-zA-Z0-9_-]/g, "-");
    const control = input(field, current, (value) => {
      if (!canEdit()) return;
      if (field.fieldId === "implementationKind" && value !== get(document, full)) {
        if (!confirmVariantReplacement()) { renderEditorArray(); return; }
        replaceActionVariant(value);
        update(); renderEditorArray(); return;
      }
      if (field.fieldId === "functionId" && full === "data.functionId" && value !== current) {
        if (!confirmVariantReplacement()) { renderEditorArray(); return; }
        set(document, full, value);
        set(document, "capabilityId", value);
        set(document, "data.arguments", hostEffectArguments(value));
        update(); renderEditorArray(); return;
      }
      set(document, full, value);
      if (field.mirrorPath) set(document, field.mirrorPath, value);
      if (bindingSync) syncArrayBindings(document, bindingSync.sourcePath, bindingSync.targetPath);
      update();
      if ((field.fieldId === "kind" && scope.startsWith("steps.")) || field.fieldId === "implementationKind" || field.fieldId === "functionId") renderEditorArray();
    }, `${keyPrefix}-${uniquePath}-${field.fieldId}`, scope);
    parent.append(control);
  }
  function renderEditorArray() {
    if (!canEdit()) return;
    const fresh = mountDraftEditor({ descriptor, document, onChange, canEdit, referenceCandidates,
      referenceGeneration, getReferenceGeneration });
    root.replaceWith(fresh);
    onChange(document);
  }
  for (const section of descriptor.sections || []) {
    const panel = el("section", { className: "form-section", id: `section-${section.id}` });
    panel.append(el("div", { className: "section-heading" }, section.title));
    const fields = el("div", { className: "field-grid" });
    for (const field of section.fields || []) renderField(field, "", fields, section.id);
    panel.append(fields); root.append(panel);
  }
  return root;
}

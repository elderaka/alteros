import { displayJsonValue, RawNumber } from "./lossless-json.js";

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

export function mountActivitySequenceEditor({ descriptor, document: sourceDocument, path, onChange, canEdit, onStructureChange }) {
  const editor = el("section", { className: "sequence-editor" });
  editor.append(el("h4", {}, "Activity sequence"));
  const sequence = get(sourceDocument, path) || {};
  const steps = Array.isArray(sequence.steps) ? sequence.steps : [];
  const primitives = descriptor.actionCatalog?.primitives || [];
  const composers = descriptor.actionCatalog?.composers || [];
  const changed = () => { if (canEdit()) onChange(sourceDocument); };
  const structureChanged = () => { changed(); onStructureChange(); };
  const primitiveById = (id) => primitives.find((item) => item.primitiveId === id) || null;
  const defaultArgument = (argument) => {
    if (argument.valueType === "entity") return { name: argument.name, value: { bindingId: "" } };
    let literal = "";
    if (argument.valueType === "bool") literal = false;
    else if (argument.valueType === "enum") literal = argument.allowedValues?.[0] || "";
    else if (argument.valueType === "int64") literal = new RawNumber(String(argument.minimum ?? "0"));
    else if (argument.valueType === "double") literal = Number(argument.minimum ?? 0);
    return { name: argument.name, value: { type: argument.valueType, literal } };
  };
  const availableBindings = (stepIndex, argument) => {
    const result = [];
    for (let index = 0; index < stepIndex; index++) {
      const producer = steps[index];
      if (argument.requiredProducerPrimitiveId && producer?.primitiveId !== argument.requiredProducerPrimitiveId) continue;
      const contract = primitiveById(producer?.primitiveId);
      for (const output of contract?.results || []) {
        if (argument.entityKind && output.entityKind !== argument.entityKind) continue;
        const binding = (producer.resultBindings || []).find((item) => item.resultName === output.name);
        if (binding?.bindingId) result.push(binding.bindingId);
      }
    }
    return [...new Set(result)];
  };
  const setArgument = (stepIndex, descriptorArgument, value) => {
    if (!canEdit()) return;
    const current = get(sourceDocument, path) || {}; const nextSteps = [...(current.steps || [])];
    const nextStep = { ...(nextSteps[stepIndex] || {}) }; const args = [...(nextStep.arguments || [])];
    const index = args.findIndex((item) => item?.name === descriptorArgument.name);
    const previous = index >= 0 ? args[index] : { name: descriptorArgument.name };
    const nextArgument = { ...previous, value };
    if (index >= 0) args[index] = nextArgument; else args.push(nextArgument);
    nextStep.arguments = args; nextSteps[stepIndex] = nextStep;
    set(sourceDocument, path, { ...current, steps: nextSteps }); changed();
  };
  const textInput = (labelText, value, onWrite, attrs = {}) => {
    const label = el("label", { className: "field" }); label.append(el("span", { className: "field-label" }, labelText));
    const control = el("input", { type: "text", ...attrs }); control.value = value ?? "";
    control.addEventListener("change", () => onWrite(control.value)); label.append(control); return label;
  };

  if (sequence.composerId) {
    const composer = composers.find((item) => item.composerId === sequence.composerId);
    const section = el("fieldset", { className: "nested-field sequence-composer" });
    section.append(el("legend", {}, "Registered sequence composer"));
    const chooser = el("select", { "aria-label": "Sequence composer" });
    if (!composers.some((item) => item.composerId === sequence.composerId))
      chooser.append(el("option", { value: sequence.composerId }, `${sequence.composerId} · unsupported`));
    composers.forEach((item) => chooser.append(el("option", { value: item.composerId }, item.composerId)));
    chooser.value = sequence.composerId; chooser.disabled = !canEdit();
    chooser.addEventListener("change", () => {
      if (!canEdit()) return;
      const selected = composers.find((item) => item.composerId === chooser.value); if (!selected) return;
      const current = get(sourceDocument, path) || {};
      const composerArguments = (selected.arguments || []).map((argument) => defaultArgument(argument));
      set(sourceDocument, path, { ...current, composerId: selected.composerId, composerArguments }); structureChanged();
    });
    const composerLabel = el("label", { className: "field" }); composerLabel.append(el("span", { className: "field-label" }, "Composer"), chooser);
    section.append(composerLabel);
    if (composer) {
      (composer.arguments || []).forEach((argument) => {
        const current = (sequence.composerArguments || []).find((item) => item?.name === argument.name) || defaultArgument(argument);
        const value = current.value || {};
        const label = el("label", { className: "field" }); label.append(el("span", { className: "field-label" }, argument.name));
        let control;
        if (argument.valueType === "enum" && (argument.allowedValues || []).length) {
          control = el("select"); argument.allowedValues.forEach((item) => control.append(el("option", { value: item }, item)));
          control.value = value.literal ?? "";
          control.addEventListener("change", () => write({ type: "enum", literal: control.value }));
        } else if (argument.valueType === "bool") {
          control = el("input", { type: "checkbox" }); control.checked = Boolean(value.literal);
          control.addEventListener("change", () => write({ type: "bool", literal: control.checked }));
        } else {
          const integer = argument.valueType === "int64";
          control = el("input", { type: integer ? "text" : (argument.valueType === "double" ? "number" : "text") });
          if (!integer && argument.valueType === "double") control.step = "any";
          if (argument.minimum != null) control.min = displayJsonValue(argument.minimum);
          if (argument.maximum != null) control.max = displayJsonValue(argument.maximum);
          control.value = displayJsonValue(value.literal);
          control.addEventListener("change", () => {
            if (integer) {
              if (!/^-?\d+$/.test(control.value)) return control.classList.add("invalid");
              try { const next = BigInt(control.value);
                if (argument.minimum != null && next < BigInt(displayJsonValue(argument.minimum))) throw new Error();
                if (argument.maximum != null && next > BigInt(displayJsonValue(argument.maximum))) throw new Error();
              } catch (_) { return control.classList.add("invalid"); }
              control.classList.remove("invalid"); write({ type: "int64", literal: new RawNumber(control.value) });
            } else if (argument.valueType === "double") {
              const next = Number(control.value);
              if (!Number.isFinite(next) || (argument.minimum != null && next < argument.minimum) ||
                  (argument.maximum != null && next > argument.maximum)) return control.classList.add("invalid");
              control.classList.remove("invalid"); write({ type: "double", literal: next });
            } else write({ type: "text", literal: control.value });
          });
        }
        control.disabled = !canEdit(); label.append(control); section.append(label);
        function write(nextValue) {
          if (!canEdit()) return;
          const currentData = get(sourceDocument, path) || {}; const args = [...(currentData.composerArguments || [])];
          const index = args.findIndex((item) => item?.name === argument.name);
          const next = { ...(index >= 0 ? args[index] : {}), name: argument.name, value: nextValue };
          if (index >= 0) args[index] = next; else args.push(next);
          set(sourceDocument, path, { ...currentData, composerArguments: args }); changed();
        }
      });
    } else section.append(el("p", { className: "diagnostic" },
      "This composer is not present in the registered catalog. Its source is preserved; edit it through Source JSON only if you understand the exact schema."));
    const switchMode = el("button", { type: "button", className: "button small" }, "Switch to primitive sequence");
    switchMode.disabled = !canEdit();
    switchMode.addEventListener("click", () => {
      if (!canEdit() || (typeof window !== "undefined" && typeof window.confirm === "function" &&
          !window.confirm("This replaces the registered composer and its arguments with an explicit primitive sequence. Continue?"))) return;
      set(sourceDocument, path, { steps: [] }); structureChanged();
    });
    editor.append(section, switchMode);
    return editor;
  }

  const palette = el("fieldset", { className: "nested-field sequence-palette" });
  palette.append(el("legend", {}, "Activity primitive palette"));
  palette.append(el("small", { className: "field-help" },
    "Primitives and argument contracts come from the registered ActivitySequenceCatalog. Unsupported legacy nodes remain visible to validation."));
  const primitiveSelect = el("select", { "aria-label": "Primitive to add" });
  primitiveSelect.append(el("option", { value: "" }, "Choose a registered primitive…"));
  for (const primitive of primitives) primitiveSelect.append(el("option", { value: primitive.primitiveId },
    `${primitive.primitiveId} · ${primitive.operationKind}`));
  const add = el("button", { type: "button", className: "button small" }, "Add primitive");
  add.disabled = !canEdit() || primitives.length === 0;
  add.addEventListener("click", () => {
    if (!canEdit() || !primitiveSelect.value) return;
    const primitive = primitiveById(primitiveSelect.value); if (!primitive) return;
    const current = get(sourceDocument, path) || {}; const nextSteps = Array.isArray(current.steps) ? [...current.steps] : [];
    const slug = primitive.primitiveId.replace(/[^a-zA-Z0-9._:-]/g, "_");
    let stepId = `${slug}.${nextSteps.length + 1}`;
    while (nextSteps.some((item) => item?.stepId === stepId)) stepId += "_";
    const step = { stepId, primitiveId: primitive.primitiveId,
      arguments: (primitive.arguments || []).map(defaultArgument),
      resultBindings: (primitive.results || []).map((result) => ({ resultName: result.name, bindingId: `${stepId}.${result.name}` })),
      successNextStepId: "", failureNextStepId: "", cancelNextStepId: "", checkpointPolicy: "none" };
    const previous = nextSteps.at(-1);
    if (previous && !previous.successNextStepId) previous.successNextStepId = stepId;
    nextSteps.push(step); set(sourceDocument, path, { ...current, steps: nextSteps }); structureChanged();
  });
  palette.append(primitiveSelect, add); editor.append(palette);

  if (!Array.isArray(sequence.steps)) {
    const initialize = el("button", { type: "button", className: "button small" }, "Initialize empty sequence");
    initialize.addEventListener("click", () => { if (!canEdit()) return; set(sourceDocument, path, { ...sequence, steps: [] }); structureChanged(); });
    editor.append(initialize);
  }

  steps.forEach((step, index) => {
    const card = el("section", { className: "array-row sequence-step", "data-object-path": `${path}.steps.${index}` });
    const header = el("div", { className: "array-row-heading" });
    header.append(el("strong", {}, `${index + 1}. ${step?.stepId || "Unidentified step"}`));
    const remove = el("button", { type: "button", className: "icon-button danger", title: "Remove step" }, "×");
    remove.disabled = !canEdit();
    remove.addEventListener("click", () => {
      if (!canEdit()) return;
      const current = get(sourceDocument, path) || {}; const nextSteps = [...(current.steps || [])];
      nextSteps.splice(index, 1); set(sourceDocument, path, { ...current, steps: nextSteps }); structureChanged();
    });
    header.append(remove); card.append(header);

    const primitiveControl = el("select", { "aria-label": `Step ${index + 1} primitive` });
    if (!primitiveById(step?.primitiveId)) primitiveControl.append(el("option", { value: step?.primitiveId || "" }, `${step?.primitiveId || "Unknown"} · unsupported`));
    for (const primitive of primitives) primitiveControl.append(el("option", { value: primitive.primitiveId }, primitive.primitiveId));
    primitiveControl.value = step?.primitiveId || ""; primitiveControl.disabled = !canEdit();
    primitiveControl.addEventListener("change", () => {
      if (!canEdit()) return;
      const primitive = primitiveById(primitiveControl.value); if (!primitive) return;
      const current = get(sourceDocument, path) || {}; const nextSteps = [...(current.steps || [])];
      const nextStep = { ...(nextSteps[index] || {}), primitiveId: primitive.primitiveId };
      const args = new Map((nextStep.arguments || []).map((item) => [item?.name, item]));
      const knownArguments = new Set((primitive.arguments || []).map((argument) => argument.name));
      nextStep.arguments = (primitive.arguments || []).map((argument) => args.get(argument.name) || defaultArgument(argument))
        .concat((nextStep.arguments || []).filter((item) => !knownArguments.has(item?.name)));
      const results = new Map((nextStep.resultBindings || []).map((item) => [item?.resultName, item]));
      const knownResults = new Set((primitive.results || []).map((result) => result.name));
      nextStep.resultBindings = (primitive.results || []).map((result) => results.get(result.name) ||
        ({ resultName: result.name, bindingId: `${nextStep.stepId}.${result.name}` }))
        .concat((nextStep.resultBindings || []).filter((item) => !knownResults.has(item?.resultName)));
      nextSteps[index] = nextStep; set(sourceDocument, path, { ...current, steps: nextSteps }); structureChanged();
    });
    const primitiveLabel = el("label", { className: "field" });
    primitiveLabel.append(el("span", { className: "field-label" }, "Primitive"), primitiveControl); card.append(primitiveLabel);
    const idLabel = textInput("Step ID", step?.stepId || "", (value) => {
      const current = get(sourceDocument, path) || {}; const nextSteps = [...(current.steps || [])];
      nextSteps[index] = { ...(nextSteps[index] || {}), stepId: value };
      set(sourceDocument, path, { ...current, steps: nextSteps }); structureChanged();
    }, { maxlength: "128" });
    card.append(idLabel);

    const primitive = primitiveById(step?.primitiveId);
    if (primitive) {
      (primitive.arguments || []).forEach((argument) => {
        const currentArgument = (step.arguments || []).find((item) => item?.name === argument.name) || defaultArgument(argument);
        const value = currentArgument.value || {};
        const label = el("label", { className: "field" });
        label.append(el("span", { className: "field-label" }, `${argument.name}${argument.required ? " · required" : ""}`));
        let control;
        if (argument.valueType === "entity") {
          control = el("select"); control.append(el("option", { value: "" }, "Select a prior result binding…"));
          const bindings = availableBindings(index, argument);
          if (value.bindingId && !bindings.includes(value.bindingId)) bindings.unshift(value.bindingId);
          bindings.forEach((bindingId) => control.append(el("option", { value: bindingId }, bindingId)));
          control.value = value.bindingId || "";
          control.addEventListener("change", () => setArgument(index, argument, { bindingId: control.value }));
        } else if (argument.valueType === "enum" && (argument.allowedValues || []).length) {
          control = el("select");
          if (value.literal && !argument.allowedValues.includes(value.literal)) control.append(el("option", { value: value.literal }, `${value.literal} · unsupported`));
          argument.allowedValues.forEach((item) => control.append(el("option", { value: item }, item)));
          control.value = value.literal ?? "";
          control.addEventListener("change", () => setArgument(index, argument, { type: "enum", literal: control.value }));
        } else if (argument.valueType === "bool") {
          control = el("input", { type: "checkbox" }); control.checked = Boolean(value.literal);
          control.addEventListener("change", () => setArgument(index, argument, { type: "bool", literal: control.checked }));
        } else {
          const integer = argument.valueType === "int64";
          control = el("input", { type: integer ? "text" : (argument.valueType === "double" ? "number" : "text") });
          if (!integer && argument.valueType === "double") control.step = "any";
          if (argument.minimum != null) control.min = displayJsonValue(argument.minimum);
          if (argument.maximum != null) control.max = displayJsonValue(argument.maximum);
          if (integer) control.inputMode = "numeric";
          control.maxLength = argument.valueType === "text" ? 2048 : control.maxLength;
          control.value = displayJsonValue(value.literal);
          control.addEventListener("change", () => {
            if (integer) {
              if (!/^-?\d+$/.test(control.value)) return control.classList.add("invalid");
              try {
                const next = BigInt(control.value);
                if (argument.minimum != null && next < BigInt(displayJsonValue(argument.minimum))) throw new Error();
                if (argument.maximum != null && next > BigInt(displayJsonValue(argument.maximum))) throw new Error();
              } catch (_) { return control.classList.add("invalid"); }
              control.classList.remove("invalid"); setArgument(index, argument, { type: "int64", literal: new RawNumber(control.value) });
            } else if (argument.valueType === "double") {
              const next = Number(control.value);
              if (!Number.isFinite(next) || (argument.minimum != null && next < argument.minimum) ||
                  (argument.maximum != null && next > argument.maximum)) return control.classList.add("invalid");
              control.classList.remove("invalid"); setArgument(index, argument, { type: "double", literal: next });
            } else setArgument(index, argument, { type: "text", literal: control.value });
          });
        }
        control.disabled = !canEdit(); label.append(control);
        if (argument.requiredProducerPrimitiveId) label.append(el("small", { className: "field-help" },
          `Requires a result produced by ${argument.requiredProducerPrimitiveId}.`));
        card.append(label);
      });

      const knownArguments = new Set((primitive.arguments || []).map((argument) => argument.name));
      const unsupportedArguments = (step.arguments || []).filter((item) => !knownArguments.has(item?.name));
      if (unsupportedArguments.length) {
        const preserved = el("fieldset", { className: "nested-field" }); preserved.append(el("legend", {}, "Unsupported arguments preserved"));
        unsupportedArguments.forEach((item) => {
          const row = el("div", { className: "sequence-binding-row" });
          row.append(el("span", { className: "field-label" }, item?.name || "Unidentified argument"));
          const remove = el("button", { type: "button", className: "button small" }, "Remove"); remove.disabled = !canEdit();
          remove.addEventListener("click", () => {
            if (!canEdit()) return;
            const current = get(sourceDocument, path) || {}; const nextSteps = [...(current.steps || [])];
            const nextStep = { ...(nextSteps[index] || {}) };
            nextStep.arguments = (nextStep.arguments || []).filter((candidate) => candidate !== item);
            nextSteps[index] = nextStep; set(sourceDocument, path, { ...current, steps: nextSteps }); structureChanged();
          });
          row.append(remove); preserved.append(row);
        });
        card.append(preserved);
      }

      const outputs = el("fieldset", { className: "nested-field" }); outputs.append(el("legend", {}, "Result bindings"));
      (primitive.results || []).forEach((result) => {
        const binding = (step.resultBindings || []).find((item) => item?.resultName === result.name) || { resultName: result.name, bindingId: "" };
        const row = el("div", { className: "sequence-binding-row" });
        row.append(el("span", { className: "field-label" }, `${result.name} · ${result.valueType}`));
        const bindingId = el("input", { type: "text" }); bindingId.value = binding.bindingId || "";
        bindingId.addEventListener("change", () => {
          if (!canEdit()) return;
          const current = get(sourceDocument, path) || {}; const nextSteps = [...(current.steps || [])];
          const nextStep = { ...(nextSteps[index] || {}) }; const list = [...(nextStep.resultBindings || [])];
          const bindingIndex = list.findIndex((item) => item?.resultName === result.name);
          const next = { ...(bindingIndex >= 0 ? list[bindingIndex] : {}), resultName: result.name, bindingId: bindingId.value };
          if (bindingIndex >= 0) list[bindingIndex] = next; else list.push(next);
          nextStep.resultBindings = list; nextSteps[index] = nextStep; set(sourceDocument, path, { ...current, steps: nextSteps }); structureChanged();
        });
        row.append(bindingId); outputs.append(row);
      });
      if ((primitive.results || []).length) card.append(outputs);
      const knownResults = new Set((primitive.results || []).map((result) => result.name));
      const unsupportedResults = (step.resultBindings || []).filter((item) => !knownResults.has(item?.resultName));
      if (unsupportedResults.length) {
        const preserved = el("fieldset", { className: "nested-field" }); preserved.append(el("legend", {}, "Unsupported result bindings preserved"));
        unsupportedResults.forEach((item) => preserved.append(el("div", { className: "sequence-binding-row" },
          item?.resultName || "Unidentified result binding")));
        card.append(preserved);
      }
    } else card.append(el("p", { className: "diagnostic" },
      "This step uses a primitive that is not in the registered catalog. It is preserved, but cannot be edited or previewed safely."));

    const edges = el("fieldset", { className: "nested-field sequence-edges" }); edges.append(el("legend", {}, "Flow edges"));
    for (const [property, labelText] of [["successNextStepId", "Success"], ["failureNextStepId", "Failure"], ["cancelNextStepId", "Cancel"]]) {
      const label = el("label", { className: "field" }); label.append(el("span", { className: "field-label" }, labelText));
      const select = el("select"); select.append(el("option", { value: "" }, "End / unspecified"));
      if (step?.[property] && !steps.some((item, targetIndex) => targetIndex !== index && item?.stepId === step[property]))
        select.append(el("option", { value: step[property] }, `${step[property]} · unresolved`));
      steps.forEach((target, targetIndex) => { if (targetIndex !== index && target?.stepId) select.append(el("option", { value: target.stepId }, target.stepId)); });
      select.value = step?.[property] || ""; select.disabled = !canEdit();
      select.addEventListener("change", () => {
        if (!canEdit()) return;
        const current = get(sourceDocument, path) || {}; const nextSteps = [...(current.steps || [])];
        nextSteps[index] = { ...(nextSteps[index] || {}), [property]: select.value };
        set(sourceDocument, path, { ...current, steps: nextSteps }); structureChanged();
      });
      label.append(select); edges.append(label);
    }
    const checkpointLabel = el("label", { className: "field" }); checkpointLabel.append(el("span", { className: "field-label" }, "Checkpoint policy"));
    const checkpoint = el("select"); ["none", "before", "after"].forEach((value) => checkpoint.append(el("option", { value }, value)));
    checkpoint.value = step?.checkpointPolicy || "none"; checkpoint.disabled = !canEdit();
    checkpoint.addEventListener("change", () => {
      if (!canEdit()) return;
      const current = get(sourceDocument, path) || {}; const nextSteps = [...(current.steps || [])];
      nextSteps[index] = { ...(nextSteps[index] || {}), checkpointPolicy: checkpoint.value };
      set(sourceDocument, path, { ...current, steps: nextSteps }); changed();
    });
    checkpointLabel.append(checkpoint); edges.append(checkpointLabel); card.append(edges); editor.append(card);
  });
  return editor;
}

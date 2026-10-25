export async function renderPreview(kindId, sourceDocument, sourceContext = {}) {
  const fixtureId = sourceContext.fixtureId ?? globalThis.document.querySelector("#fixture-id").value;
  const draw = Number(sourceContext.draw ?? globalThis.document.querySelector("#preview-draw").value);
  if (!Number.isFinite(draw) || draw < 0 || draw >= 1) throw new Error("Draw must be finite and in [0, 1).");
  const rawEvents = sourceContext.events ?? globalThis.document.querySelector("#preview-events").value;
  let events = rawEvents;
  if (typeof rawEvents === "string") {
    try { events = JSON.parse(rawEvents); }
    catch { throw new Error("Synthetic events must be valid JSON."); }
  }
  if (!Array.isArray(events)) throw new Error("Synthetic events must be a JSON array.");
  if (kindId === "scenario") return { kindId, fixtureId, document: sourceDocument, ...sourceContext, events };
  if (kindId === "topic") {
    const nowUtc = new Date().toISOString();
    return { kindId, fixtureId, document: sourceDocument, ...sourceContext,
      instanceId: `preview-${sourceDocument.topicId || "topic"}`, source: "authored", draw, nowUtc, events };
  }
  return { kindId, fixtureId, document: sourceDocument, ...sourceContext, events };
}

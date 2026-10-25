const numberPattern = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/;

export class RawNumber {
  constructor(raw) {
    if (typeof raw !== "string" || !numberPattern.test(raw)) throw new TypeError("Raw JSON number token is invalid.");
    this.raw = raw;
  }
}

export const isRawNumber = (value) => value instanceof RawNumber;
export const displayJsonValue = (value) => isRawNumber(value) ? value.raw : value == null ? "" : String(value);
export const cloneLossless = (value) => {
  if (isRawNumber(value)) return new RawNumber(value.raw);
  if (Array.isArray(value)) return value.map(cloneLossless);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, cloneLossless(item)]));
  return value;
};

function decimalCanonical(value) {
  const match = /^(-?)(\d+)(?:\.(\d*))?(?:[eE]([+-]?\d+))?$/.exec(value);
  if (!match) return value;
  const sign = match[1]; let digits = match[2] + (match[3] || "");
  let exponent = Number(match[4] || 0) - (match[3] || "").length;
  digits = digits.replace(/^0+/, "");
  if (!digits) return "0";
  const trailing = /0+$/.exec(digits)?.[0].length || 0;
  if (trailing) { digits = digits.slice(0, -trailing); exponent += trailing; }
  return `${sign}${digits}e${exponent}`;
}

function preserveToken(raw) {
  const value = Number(raw);
  if (!Number.isFinite(value)) throw new SyntaxError("JSON numeric value is outside the finite browser range.");
  if (Number.isInteger(value) && !Number.isSafeInteger(value)) return true;
  return decimalCanonical(raw) !== decimalCanonical(String(value));
}

export function parseLossless(source) {
  if (typeof source !== "string") throw new TypeError("JSON input must be text.");
  const marker = `__alteros_raw_number_${globalThis.crypto.randomUUID()}__`;
  let output = ""; let index = 0; let inString = false; let escaped = false;
  while (index < source.length) {
    const current = source[index];
    if (inString) {
      output += current; index++;
      if (escaped) escaped = false;
      else if (current === "\\") escaped = true;
      else if (current === '"') inString = false;
      continue;
    }
    if (current === '"') { inString = true; output += current; index++; continue; }
    if (current === "-" || (current >= "0" && current <= "9")) {
      const match = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/.exec(source.slice(index));
      if (match) {
        const raw = match[0];
        output += preserveToken(raw) ? JSON.stringify({ [marker]: raw }) : raw;
        index += raw.length; continue;
      }
    }
    output += current; index++;
  }
  const parsed = JSON.parse(output);
  const revive = (value) => {
    if (Array.isArray(value)) return value.map(revive);
    if (value && typeof value === "object") {
      const keys = Object.keys(value);
      if (keys.length === 1 && keys[0] === marker) return new RawNumber(value[marker]);
      for (const key of keys) value[key] = revive(value[key]);
    }
    return value;
  };
  return revive(parsed);
}

export function stringifyLossless(value, spacing = 0) {
  const indent = typeof spacing === "number" ? " ".repeat(Math.max(0, Math.min(10, spacing))) : String(spacing).slice(0, 10);
  const encode = (item, depth) => {
    if (isRawNumber(item)) return item.raw;
    if (item === null) return "null";
    if (typeof item === "string" || typeof item === "boolean") return JSON.stringify(item);
    if (typeof item === "number") {
      if (!Number.isFinite(item)) throw new TypeError("Non-finite numbers cannot be serialized as JSON.");
      return JSON.stringify(item);
    }
    if (Array.isArray(item)) {
      const values = item.map((value) => value === undefined ? "null" : encode(value, depth + 1));
      if (!values.length) return "[]";
      return indent ? `[\n${indent.repeat(depth + 1)}${values.join(`,\n${indent.repeat(depth + 1)}`)}\n${indent.repeat(depth)}]` : `[${values.join(",")}]`;
    }
    if (item && typeof item === "object") {
      const entries = Object.entries(item).filter(([, value]) => value !== undefined);
      if (!entries.length) return "{}";
      const values = entries.map(([key, value]) => `${JSON.stringify(key)}${indent ? ": " : ":"}${encode(value, depth + 1)}`);
      return indent ? `{\n${indent.repeat(depth + 1)}${values.join(`,\n${indent.repeat(depth + 1)}`)}\n${indent.repeat(depth)}}` : `{${values.join(",")}}`;
    }
    throw new TypeError("Value is not JSON serializable.");
  };
  return encode(value, 0);
}

"""Explicit opt-in synthetic text smoke. No retries, audio, or player history.

Run with --live only after spend approval. Uses the existing provider/parser,
with a test-only transport enforcing <=2 requests and conservative price caps.
Never emits credentials or raw provider errors. Does not change game settings.
"""
import asyncio
import ctypes
import json
import os
from pathlib import Path
import sys

import httpx
from pydantic import ValidationError

sys.path.insert(0, str(Path(__file__).resolve().parent / "src"))

from alteros_companion.contracts import ConversationJudgmentV2, parse_envelope
from alteros_companion.graph import generate
from alteros_companion.provider import OpenRouterProvider


def safe_schema_errors(content):
    """Return field paths and error types only; never include rejected values."""
    try:
        value = json.loads(content)
    except (TypeError, json.JSONDecodeError):
        return [{"loc": [], "type": "json_invalid"}]
    try:
        ConversationJudgmentV2.model_validate(value, strict=True)
        return []
    except ValidationError as exc:
        return [{"loc": [str(part) for part in error["loc"]], "type": error["type"]}
                for error in exc.errors(include_input=False)[:12]]


def credential():
    override = os.environ.get("OPENROUTER_API_KEY", "").strip()
    if override:
        return override
    path = Path(os.environ["USERPROFILE"]) / "AppData/LocalLow/DefaultCompany/alteros/openrouter-api-key.bin"
    encrypted = path.read_bytes()

    class Blob(ctypes.Structure):
        _fields_ = [("size", ctypes.c_uint32), ("data", ctypes.c_void_p)]

    buffer = ctypes.create_string_buffer(encrypted)
    source = Blob(len(encrypted), ctypes.cast(buffer, ctypes.c_void_p))
    output = Blob()
    crypt = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    crypt.CryptUnprotectData.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.c_void_p,
                                        ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(Blob)]
    crypt.CryptUnprotectData.restype = ctypes.c_int
    kernel.LocalFree.argtypes = [ctypes.c_void_p]
    if not crypt.CryptUnprotectData(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(output)):
        raise RuntimeError("saved_credential_unavailable")
    try:
        return ctypes.string_at(output.data, output.size).decode("utf-8").strip()
    finally:
        ctypes.memset(output.data, 0, output.size)
        kernel.LocalFree(output.data)


class BudgetTransport(httpx.AsyncBaseTransport):
    def __init__(self, inner_transport: httpx.AsyncBaseTransport | None = None):
        self.inner = inner_transport or httpx.AsyncHTTPTransport(retries=0)
        self.calls = 0
        self.receipts = []

    async def handle_async_request(self, request):
        if self.calls >= 1 or str(request.url) != "https://openrouter.ai/api/v1/chat/completions":
            raise RuntimeError("smoke_budget_guard")
        body = json.loads(request.content)
        body["max_completion_tokens"] = 192
        body["provider"] = {**body["provider"],
                            "max_price": {"prompt": 0.30, "completion": 1.20, "request": 0}}
        raw = json.dumps(body, ensure_ascii=False).encode("utf-8")
        if len(raw) > 18000:
            raise RuntimeError("smoke_input_budget_guard")
        self.calls += 1
        bounded = httpx.Request("POST", request.url, headers={"Authorization": request.headers["Authorization"],
                               "Content-Type": "application/json"}, content=raw)
        response = await self.inner.handle_async_request(bounded)
        try:
            content = await response.aread()
            payload = json.loads(content)
            usage = payload.get("usage", {})
            choice = (payload.get("choices") or [{}])[0]
            message = choice.get("message") or {}
            self.receipts.append({"httpStatus": response.status_code, "requestBytes": len(raw),
                                  "generationId": payload.get("id"), "provider": payload.get("provider"),
                                  "reasoningTokens": usage.get("completion_tokens_details", {}).get("reasoning_tokens"),
                                  "usage": {k: usage.get(k) for k in ["prompt_tokens", "completion_tokens", "total_tokens", "cost"]},
                                  "finishReason": choice.get("finish_reason"),
                                  "schemaErrors": safe_schema_errors(message.get("content"))})
            return httpx.Response(response.status_code, content=content, headers=response.headers)
        finally:
            await response.aclose()

    async def aclose(self):
        await self.inner.aclose()


async def main():
    if sys.argv[1:] != ["--live"]:
        print("Not run: requires --live and explicit human spend approval.")
        return
    key = credential()
    fixture = Path(__file__).resolve().parents[1] / "Conversation/Fixtures/v2/turn-valid.json"
    base = json.loads(fixture.read_text(encoding="utf-8"))
    data = json.loads(json.dumps(base))
    data["payload"]["request"]["text"] = "Do you like cats? Keep it brief."
    data["payload"]["presentedHistory"] = []
    data["payload"]["candidates"]["cards"] = []
    data["payload"]["coverage"]["topics"] = []
    job = parse_envelope(json.dumps(data).encode("utf-8")).payload
    transport = BudgetTransport()
    persona_path = Path(__file__).resolve().parent / "src/alteros_companion/persona.json"
    result = await generate(job, key, provider=OpenRouterProvider(transport=transport),
                            persona_path=persona_path)
    judgment = result.judgment
    receipt = transport.receipts[0] if transport.receipts else None
    print(json.dumps({"case": "novel_topic_no_candidates",
                      "error": result.error.code if result.error else None,
                      "diagnosticCode": result.error.diagnosticCode if result.error else None,
                      "kind": judgment.kind if judgment else None,
                      "beatCount": len(judgment.reply.beats) if judgment and judgment.reply else 0,
                      "usage": result.usage.model_dump(), "timing": result.timing.model_dump(),
                      "receipt": receipt}, ensure_ascii=True), flush=True)
    cost = receipt["usage"]["cost"] if receipt else None
    if not isinstance(cost, (int, float)) or cost < 0 or cost > 0.01:
        print("Warning: missing or unexpectedly high reported cost. No additional request will be made.")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception as exc:
        print(json.dumps({"stopped": type(exc).__name__, "detail": "Diagnostic stopped; no credential or raw error emitted."}))
        sys.exit(1)

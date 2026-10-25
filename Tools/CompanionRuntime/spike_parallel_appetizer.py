"""Throwaway synthetic benchmark, not production conversation orchestration.

--self-test is keyless. --live requires explicit approval for two paid requests.
No player history, memory writes, TTS, tools, Unity or provider retries.
"""
import asyncio
import json
from pathlib import Path
import sys
import time

import httpx

sys.path.insert(0, str(Path(__file__).resolve().parent / "src"))

from alteros_companion.contracts import parse_envelope
from alteros_companion.graph import generate
from alteros_companion.prompts import load_persona
from alteros_companion.provider import OpenRouterProvider
from smoke_openrouter import credential

ROOT = Path(__file__).resolve().parents[2]
PROMPT = "Do you like cats?"
BRIEF = ("Both stages share this immutable brief: Alterra likes cats, as her persona states. "
         "Do not invent pet ownership or past experiences. No actions or tools. "
         "Appetizer only acknowledges liking cats; continuation explores their personality "
         "and asks what the player likes about them. Do not contradict this division.")
SHAPE = ('Return only JSON with exactly kind, topicId, referencedTopicIds, reply, suggestedReplies. '
         'Use kind="novel_candidate", topicId="", referencedTopicIds=[], suggestedReplies=[]. '
         'reply must be {"beats":[{"id":"b1","text":"日本語","displayText":"English",'
         '"language":"japanese","expression":"gentle"}]}. '
         'Expressions are gentle, thinking, or empty. No other fields. Text must be Japanese '
         'and displayText its natural English translation.')


class StageTransport(httpx.AsyncBaseTransport):
    def __init__(self, stage, inner=None):
        self.stage = stage
        self.inner = inner or httpx.AsyncHTTPTransport(retries=0)
        self.calls = 0
        self.receipt = None

    async def handle_async_request(self, request):
        if self.calls or str(request.url) != "https://openrouter.ai/api/v1/chat/completions":
            raise RuntimeError("request_budget_guard")
        body = json.loads(request.content)
        if body["provider"].get("only") != ["digitalocean"] or body["reasoning"] != {"enabled": False}:
            raise RuntimeError("provider_policy_guard")
        body["max_completion_tokens"] = 256 if self.stage == "appetizer" else 768
        body["provider"]["max_price"] = {"prompt": 0.30, "completion": 1.20, "request": 0}
        if self.stage == "appetizer":
            body["messages"] = [
                {"role": "system", "content": load_persona().characterRules + "\n" + BRIEF + "\n" + SHAPE +
                 "\nAPPETIZER: Give one or two very short beats, at most 15 English words total. "
                 "No question, no long explanation; this is only a warm opening."},
                {"role": "user", "content": json.dumps({"playerText": PROMPT})},
            ]
        else:
            body["messages"][0]["content"] += ("\n" + BRIEF +
                "\nCONTINUATION: Give two or three short beats. Do not repeat the opening "
                "acknowledgement or say 'as I said'. You do not know the other output. "
                "Follow the shared brief; end with one natural player-facing question.")
        raw = json.dumps(body, ensure_ascii=False).encode("utf-8")
        if len(raw) > 12000:
            raise RuntimeError("input_budget_guard")
        self.calls += 1
        forwarded = httpx.Request("POST", request.url, headers={
            "Authorization": request.headers["Authorization"], "Content-Type": "application/json"}, content=raw)
        response = await self.inner.handle_async_request(forwarded)
        try:
            content = await response.aread()
            if len(content) > 128 * 1024:
                raise RuntimeError("response_size_guard")
            payload = json.loads(content)
            usage = payload.get("usage") or {}
            choice = (payload.get("choices") or [{}])[0]
            self.receipt = {"status": response.status_code, "generationId": payload.get("id"),
                "provider": payload.get("provider"), "requestBytes": len(raw),
                "inputTokens": usage.get("prompt_tokens"), "outputTokens": usage.get("completion_tokens"),
                "reasoningTokens": (usage.get("completion_tokens_details") or {}).get("reasoning_tokens"),
                "costUsd": usage.get("cost"), "finishReason": choice.get("finish_reason")}
            return httpx.Response(response.status_code, headers=response.headers, content=content)
        finally:
            await response.aclose()

    async def aclose(self):
        await self.inner.aclose()


def synthetic_job():
    data = json.loads((ROOT / "Tools/Conversation/Fixtures/v2/turn-valid.json").read_text(encoding="utf-8"))
    data["payload"]["request"]["text"] = PROMPT
    data["payload"]["presentedHistory"] = []
    data["payload"]["candidates"]["cards"] = []
    data["payload"]["coverage"]["topics"] = []
    return parse_envelope(json.dumps(data).encode()).payload


async def run_pair(key, inner_factory=None):
    started = time.monotonic()

    async def stage(name):
        transport = StageTransport(name, inner_factory() if inner_factory else None)
        began = time.monotonic() - started
        result = await generate(synthetic_job(), key, provider=OpenRouterProvider(transport=transport))
        ended = time.monotonic() - started
        record = {"stage": name, "startedAtSeconds": round(began, 3), "readyAtSeconds": round(ended, 3),
            "valid": result.error is None, "error": result.error.code if result.error else None,
            "receipt": transport.receipt,
            "beats": result.judgment.reply.model_dump()["beats"] if result.judgment and result.judgment.reply else []}
        print(json.dumps(record, ensure_ascii=True), flush=True)
        return record

    return await asyncio.gather(stage("appetizer"), stage("continuation"))


async def self_test():
    seen = []

    def handle(request):
        body = json.loads(request.content)
        seen.append(body)
        assert body["provider"]["only"] == ["digitalocean"]
        assert body["provider"]["allow_fallbacks"] is False
        assert body["provider"]["max_price"] == {"prompt": 0.30, "completion": 1.20, "request": 0}
        assert body["reasoning"] == {"enabled": False}
        assert len(request.content) < 12000
        reply = {"kind": "novel_candidate", "topicId": "", "referencedTopicIds": [],
            "reply": {"beats": [{"id": "b1", "text": "猫が好きだよ。", "displayText": "I like cats.",
                "language": "japanese", "expression": "gentle"}]}, "suggestedReplies": []}
        return httpx.Response(200, json={"id": "synthetic", "provider": "DigitalOcean",
            "choices": [{"message": {"content": json.dumps(reply)}, "finish_reason": "stop"}],
            "usage": {"prompt_tokens": 30, "completion_tokens": 30, "total_tokens": 60, "cost": 0.00003}})

    pair = await run_pair("synthetic-key", lambda: httpx.MockTransport(handle))
    assert len(seen) == 2
    assert sorted(x["max_completion_tokens"] for x in seen) == [256, 768]
    assert all(x["valid"] for x in pair)
    assert all(x["startedAtSeconds"] < min(y["readyAtSeconds"] for y in pair) for x in pair)
    print("PASS: two overlapping stages, bounded requests, strict parser/host validation.")


if __name__ == "__main__":
    try:
        if sys.argv[1:] == ["--self-test"]:
            asyncio.run(self_test())
        elif sys.argv[1:] == ["--live"]:
            asyncio.run(run_pair(credential()))
        else:
            print("No requests made. Use --self-test; --live requires explicit spend approval.")
    except Exception as exc:
        print(json.dumps({"stopped": type(exc).__name__, "detail": "No secrets or raw exception emitted."}))
        sys.exit(1)

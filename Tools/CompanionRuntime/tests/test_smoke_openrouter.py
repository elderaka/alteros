from __future__ import annotations

import asyncio
import json
from pathlib import Path
import sys

import httpx
import pytest


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from smoke_openrouter import BudgetTransport, safe_schema_errors


def test_live_smoke_transport_caps_output_and_allows_only_one_request() -> None:
    seen: list[httpx.Request] = []

    async def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json={
            "id": "synthetic-generation",
            "provider": "synthetic",
            "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15, "cost": 0.0001},
            "choices": [{"finish_reason": "stop"}],
        })

    async def run() -> None:
        budget = BudgetTransport(inner_transport=httpx.MockTransport(handler))
        request = httpx.Request("POST", "https://openrouter.ai/api/v1/chat/completions",
            headers={"Authorization": "Bearer synthetic-key", "Content-Type": "application/json"},
            content=json.dumps({"max_completion_tokens": 4096, "provider": {"only": ["digitalocean"]}}))
        response = await budget.handle_async_request(request)
        await response.aclose()
        with pytest.raises(RuntimeError, match="smoke_budget_guard"):
            await budget.handle_async_request(request)
        await budget.aclose()

    asyncio.run(run())

    assert len(seen) == 1
    sent = json.loads(seen[0].content)
    assert sent["max_completion_tokens"] == 192
    assert sent["provider"]["max_price"] == {"prompt": 0.30, "completion": 1.20, "request": 0}


def test_smoke_schema_diagnostic_never_returns_rejected_values() -> None:
    summary = safe_schema_errors('{"kind":"novel_candidate","secret":"do not print"}')

    assert summary
    assert all(set(item) == {"loc", "type"} for item in summary)
    assert "do not print" not in json.dumps(summary)

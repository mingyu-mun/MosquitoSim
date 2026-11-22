"""Planner backends.

mock          no model; answers APPROACH to the nearest skin named in the
              instruction after an optional delay (pipeline tests, no GPU)
transformers  Qwen3-VL in-process with Hugging Face transformers (baseline
              BF16; FP8 / AWQ / GPTQ checkpoints load the same way)
openai        any OpenAI-compatible server, e.g. `vllm serve <model>` with
              prefix caching or quantized checkpoints

The latent-tensor variant C (precomputed vision tokens) plugs in as another
backend that reuses TransformersBackend's model; it is not implemented yet.
"""
from __future__ import annotations

import base64
import json
import re
import sys
import time
import urllib.request
from pathlib import Path
from typing import Sequence

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from mosquitosim.prompt import MAX_NEW_TOKENS, SYSTEM_PROMPT, chat_messages, user_text  # noqa: E402

_SKIN_RE = re.compile(r"(S\d) [^,\n]*? ([\d.]+)m")


class MockBackend:
    name = "mock"

    def __init__(self, delay: float = 0.0) -> None:
        self.model = "mock"
        self.delay = delay

    def generate(self, instruction: str, images: Sequence[str], use_images: bool) -> str:
        if self.delay:
            time.sleep(self.delay)
        skins = [(float(d), s) for s, d in _SKIN_RE.findall(instruction)]
        target = min(skins)[1] if skins else "S1"
        return json.dumps({"subgoal": "APPROACH", "target": target})


class TransformersBackend:
    name = "transformers"

    def __init__(self, model: str, dtype: str = "bfloat16") -> None:
        import torch
        from transformers import AutoModelForImageTextToText, AutoProcessor

        self.torch = torch
        self.model = model
        torch_dtype = "auto" if dtype == "auto" else getattr(torch, dtype)
        self.net = AutoModelForImageTextToText.from_pretrained(model, torch_dtype=torch_dtype, device_map="auto")
        self.processor = AutoProcessor.from_pretrained(model)
        self.net.eval()

    def generate(self, instruction: str, images: Sequence[str], use_images: bool) -> str:
        messages = chat_messages(instruction, images, use_images)
        inputs = self.processor.apply_chat_template(
            messages, tokenize=True, add_generation_prompt=True, return_dict=True, return_tensors="pt"
        ).to(self.net.device)
        with self.torch.inference_mode():
            out = self.net.generate(**inputs, max_new_tokens=MAX_NEW_TOKENS, do_sample=False)
        if self.torch.cuda.is_available():
            self.torch.cuda.synchronize()
        new_tokens = out[:, inputs["input_ids"].shape[1]:]
        return self.processor.batch_decode(new_tokens, skip_special_tokens=True)[0]


class OpenAIBackend:
    name = "openai"

    def __init__(self, url: str, model: str) -> None:
        self.url = url.rstrip("/") + "/chat/completions"
        self.model = model

    def generate(self, instruction: str, images: Sequence[str], use_images: bool) -> str:
        content = []
        if use_images:
            for p in images:
                b64 = base64.b64encode(Path(p).read_bytes()).decode("ascii")
                content.append({"type": "image_url", "image_url": {"url": f"data:image/png;base64,{b64}"}})
        content.append({"type": "text", "text": user_text(instruction, use_images)})
        body = {
            "model": self.model,
            "messages": [{"role": "system", "content": SYSTEM_PROMPT}, {"role": "user", "content": content}],
            "max_tokens": MAX_NEW_TOKENS,
            "temperature": 0,
        }
        req = urllib.request.Request(self.url, data=json.dumps(body).encode("utf-8"),
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=120) as resp:
            out = json.loads(resp.read().decode("utf-8"))
        return out["choices"][0]["message"]["content"]


def make_backend(args):
    if args.backend == "mock":
        return MockBackend(args.mock_delay)
    if args.backend == "transformers":
        return TransformersBackend(args.model, args.dtype)
    return OpenAIBackend(args.openai_url, args.model)

"""Prompt for the VLM planner. The image-free condition uses the same text."""
from __future__ import annotations

from typing import List, Sequence

SYSTEM_PROMPT = (
    "너는 모기 시뮬레이터의 상위 플래너다. 모기는 사람에게 잡히지 않고 피부에 착지해 "
    "흡혈하려 한다. 하위 정책이 실제 비행을 맡고, 너는 다음 지시 하나만 고른다.\n"
    "- APPROACH(피부 ID): 그 피부로 가서 착지하고 흡혈한다.\n"
    "- HIDE(후보 지점 ID): 사람 눈에 보이지 않는 곳으로 가서 멈춘다.\n"
    "- MOVE_TO(후보 지점 ID): 그 지점으로 이동한다. 장애물을 돌아갈 때 쓴다.\n"
    "- EVADE: 다가오는 손에서 멀어진다.\n"
    "이미지에는 피부 ID(S1~S5, 하늘색)와 후보 지점 ID(P1~P8, 노란색)가 표시되어 있다. "
    "이미지는 모기 시점 1장과 방 네 모서리에서 본 전경 4장이다.\n"
    "설명 없이 JSON 한 줄만 출력한다."
)

NOIMG_NOTE = "이미지는 주어지지 않는다. 글로 된 상황만 보고 고른다."

MAX_NEW_TOKENS = 32


def user_text(instruction: str, use_images: bool) -> str:
    return instruction if use_images else NOIMG_NOTE + "\n" + instruction


def chat_messages(instruction: str, image_paths: Sequence[str], use_images: bool) -> List[dict]:
    """Messages in the transformers / Qwen-VL chat format."""
    content: List[dict] = []
    if use_images:
        content.extend({"type": "image", "image": p} for p in image_paths)
    content.append({"type": "text", "text": user_text(instruction, use_images)})
    return [
        {"role": "system", "content": [{"type": "text", "text": SYSTEM_PROMPT}]},
        {"role": "user", "content": content},
    ]

"""Planner side channel (Python end). Unity end: unity/Assets/Scripts/Agent/PlannerChannel.cs.

Messages are JSON strings.

Unity -> Python
    {"type": "request", ...}       external planner decision (see docs/interface.md)
    {"type": "decision", ...}      log of an in-Unity planner decision
    {"type": "episode_end", ...}   one per finished episode

Python -> Unity
    {"type": "config", "image_dir": ..., "log": true, "capture_images": true, "image_size": 448}
    {"type": "command", "room": 3, "episode": "...", "decision": 2, "command": {"subgoal": "HIDE", "target": "P3"}}

Unity sends nothing until it has received a config, so training with
mlagents-learn (which does not register this channel) is unaffected.
"""
from __future__ import annotations

import json
import uuid
from collections import deque
from typing import Deque, Dict, List, Optional

CHANNEL_ID = uuid.UUID("6f1c2a5e-8d3b-4c7a-9e21-5b4d3c2a1f00")


def config_message(image_dir: str, log: bool = True, capture_images: bool = False,
                   image_size: int = 448) -> dict:
    return {"type": "config", "image_dir": image_dir, "log": log,
            "capture_images": capture_images, "image_size": image_size}


def command_message(room: int, episode: str, decision: int, command: dict) -> dict:
    return {"type": "command", "room": room, "episode": episode, "decision": decision, "command": command}


class MessageQueue:
    """Transport-independent part: parses incoming JSON and queues outgoing dicts."""

    def __init__(self) -> None:
        self.incoming: Deque[dict] = deque()
        self.outgoing: List[dict] = []
        self.bad_messages = 0

    def receive(self, text: str) -> None:
        try:
            msg = json.loads(text)
        except json.JSONDecodeError:
            self.bad_messages += 1
            return
        if isinstance(msg, dict) and "type" in msg:
            self.incoming.append(msg)
        else:
            self.bad_messages += 1

    def pop_all(self) -> List[dict]:
        out = list(self.incoming)
        self.incoming.clear()
        return out

    def send(self, msg: dict) -> None:
        self.outgoing.append(msg)


def make_side_channel(queue: Optional[MessageQueue] = None):
    """Build an mlagents_envs SideChannel bound to a MessageQueue (imports mlagents lazily)."""
    from mlagents_envs.side_channel.side_channel import IncomingMessage, OutgoingMessage, SideChannel

    q = queue or MessageQueue()

    class PlannerSideChannel(SideChannel):
        def __init__(self) -> None:
            super().__init__(CHANNEL_ID)
            self.queue = q

        def on_message_received(self, msg: IncomingMessage) -> None:
            self.queue.receive(msg.read_string())

        def send(self, payload: Dict) -> None:
            out = OutgoingMessage()
            # write_string encodes as ASCII, so keep json.dumps' default \uXXXX escapes
            # (a non-ASCII image_dir or text would raise UnicodeEncodeError otherwise).
            out.write_string(json.dumps(payload))
            super().queue_message_to_send(out)

    return PlannerSideChannel()

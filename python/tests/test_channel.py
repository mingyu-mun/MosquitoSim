"""The planner side channel must survive ML-Agents' ASCII-only string transport."""
import json

import pytest

from mosquitosim.channel import MessageQueue, make_side_channel


def test_side_channel_messages_survive_ascii_only_transport():
    pytest.importorskip("mlagents_envs")
    from mlagents_envs.side_channel.incoming_message import IncomingMessage
    from mlagents_envs.side_channel.outgoing_message import OutgoingMessage

    q = MessageQueue()
    ch = make_side_channel(q)

    # Python -> Unity: a non-ASCII path must not raise and must round-trip.
    ch.send({"type": "config", "image_dir": "C:/Users/문지원/images"})
    sent = IncomingMessage(ch.message_queue[-1]).read_string()
    assert sent.isascii()
    assert json.loads(sent)["image_dir"] == "C:/Users/문지원/images"

    # Unity -> Python: text escaped the way JsonBuilder writes it is restored.
    out = OutgoingMessage()
    out.write_string('{"type": "request", "instruction": "\\ubaa9 0.5m"}')
    ch.on_message_received(IncomingMessage(out.buffer))
    assert q.pop_all()[0]["instruction"] == "목 0.5m"

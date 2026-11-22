import re
import sys
import threading
from http.server import ThreadingHTTPServer
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "planner_server"))
import server  # noqa: E402
from backends import make_backend  # noqa: E402

from conftest import REPO_ROOT  # noqa: E402
from mosquitosim import channel, prompt  # noqa: E402
from mosquitosim.planner_client import PlannerClient  # noqa: E402
from mosquitosim.subgoals import Command, Subgoal, parse_planner_output  # noqa: E402


def test_mock_server_round_trip():
    args = SimpleNamespace(backend="mock", mock_delay=0.05, model="mock")
    server.PlannerHandler.backend = make_backend(args)
    httpd = ThreadingHTTPServer(("127.0.0.1", 0), server.PlannerHandler)
    t = threading.Thread(target=httpd.serve_forever, daemon=True)
    t.start()
    try:
        client = PlannerClient(f"http://127.0.0.1:{httpd.server_address[1]}", timeout=5)
        instr = "피부: S1 머리 2.0m, S2 왼팔 0.9m, S3 오른팔 3.0m\n직전 지시: APPROACH S1\n"
        res = client.plan(instr, [], use_images=False)
        assert res.error is None
        assert parse_planner_output(res.text) == Command(Subgoal.APPROACH, "S2")
        assert 0.05 <= res.service_time <= res.wall_time
    finally:
        httpd.shutdown()


def test_client_reports_unreachable_server_as_error():
    res = PlannerClient("http://127.0.0.1:9", timeout=1).plan("x", [], False)
    assert res.error and res.text == ""


def test_prompt_images_only_for_vlm():
    msgs = prompt.chat_messages("상황", ["a.png", "b.png"], use_images=True)
    assert [c["type"] for c in msgs[1]["content"]] == ["image", "image", "text"]
    msgs = prompt.chat_messages("상황", ["a.png"], use_images=False)
    assert [c["type"] for c in msgs[1]["content"]] == ["text"]
    assert prompt.NOIMG_NOTE in msgs[1]["content"][0]["text"]


def test_channel_guid_matches_unity():
    cs = (REPO_ROOT / "unity/Assets/Scripts/Agent/PlannerChannel.cs").read_text(encoding="utf-8")
    guid = re.search(r'new Guid\("([0-9a-f-]+)"\)', cs).group(1)
    assert guid == str(channel.CHANNEL_ID)


def test_message_queue():
    q = channel.MessageQueue()
    q.receive('{"type": "request", "room": 1}')
    q.receive("not json")
    q.receive("[1, 2]")
    assert [m["room"] for m in q.pop_all()] == [1]
    assert q.bad_messages == 2 and q.pop_all() == []
    m = channel.command_message(3, "e", 2, {"subgoal": "EVADE"})
    assert m == {"type": "command", "room": 3, "episode": "e", "decision": 2, "command": {"subgoal": "EVADE"}}

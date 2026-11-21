import pytest

from mosquitosim.subgoals import Subgoal, parse_planner_output


@pytest.mark.parametrize("text,expected", [
    ('{"subgoal": "HIDE"}', Subgoal.HIDE),
    ('```json\n{"subgoal": "evade"}\n```', Subgoal.EVADE),
    ('I choose {"subgoal": " HIDE "} because...', Subgoal.HIDE),
    ('{"subgoal": "APPROACH", "reason": "no threat"}', Subgoal.APPROACH),
])
def test_valid(text, expected):
    assert parse_planner_output(text) == expected


@pytest.mark.parametrize("text", [
    "HIDE", '{"subgoal": "RUN"}', '{"goal": "HIDE"}', "{bad json}", "", None,
    '{"subgoal": 3}', '{"subgoal": "MOVE_TO"}', '{"subgoal": "WAIT"}',
])
def test_invalid(text):
    assert parse_planner_output(text) is None


def test_move_to_only_when_adopted():
    assert parse_planner_output('{"subgoal": "MOVE_TO"}', allow_move_to=True) == Subgoal.MOVE_TO

import pytest

from mosquitosim.subgoals import Command, Subgoal, parse_planner_output


@pytest.mark.parametrize("text,expected", [
    ('{"subgoal": "HIDE", "target": "P3"}', Command(Subgoal.HIDE, "P3")),
    ('```json\n{"subgoal": "approach", "target": "s2"}\n```', Command(Subgoal.APPROACH, "S2")),
    ('I pick {"subgoal": "MOVE_TO", "target": " P8 "} because...', Command(Subgoal.MOVE_TO, "P8")),
    ('{"subgoal": "EVADE"}', Command(Subgoal.EVADE)),
    ('{"subgoal": "EVADE", "target": "P1"}', Command(Subgoal.EVADE)),
])
def test_valid(text, expected):
    assert parse_planner_output(text) == expected


@pytest.mark.parametrize("text", [
    "HIDE", '{"subgoal": "RUN"}', '{"goal": "HIDE"}', "{bad json}", "", None,
    '{"subgoal": 3}', '{"subgoal": "WAIT"}',
    '{"subgoal": "HIDE"}',                       # target missing
    '{"subgoal": "HIDE", "target": "P9"}',       # not offered
    '{"subgoal": "HIDE", "target": "S1"}',       # skin given for a point subgoal
    '{"subgoal": "APPROACH", "target": "P1"}',   # point given for a skin subgoal
    '{"subgoal": "APPROACH", "target": 2}',
])
def test_invalid(text):
    assert parse_planner_output(text) is None


def test_only_offered_points_accepted():
    offered = ("P1", "P2", "P3")
    assert parse_planner_output('{"subgoal": "MOVE_TO", "target": "P3"}', offered_points=offered)
    assert parse_planner_output('{"subgoal": "MOVE_TO", "target": "P4"}', offered_points=offered) is None


def test_command_round_trip():
    for cmd in (Command(Subgoal.HIDE, "P2"), Command(Subgoal.EVADE)):
        assert parse_planner_output(cmd.to_json()) == cmd

import numpy as np
import pytest

from mosquitosim import obs_spec


def test_size_is_59_and_contiguous():
    assert obs_spec.OBS_SIZE == 59
    end = 0
    for f in obs_spec.FIELDS:
        a, b = obs_spec.INDEX[f.name]
        assert a == end and b - a == f.size
        end = b
    assert end == 59


def test_ray_directions_unit_and_unique():
    d = obs_spec.ray_directions()
    assert d.shape == (26, 3)
    assert np.allclose(np.linalg.norm(d, axis=1), 1.0)
    assert len({tuple(np.round(v, 6)) for v in d}) == 26
    # first is (-1,-1,-1), last is (1,1,1): nested x, y, z order
    assert np.allclose(d[0], -np.ones(3) / np.sqrt(3))
    assert np.allclose(d[-1], np.ones(3) / np.sqrt(3))


def _valid_obs():
    o = np.zeros(59)
    a, _ = obs_spec.INDEX["rays"]
    o[a:a + 26] = 1.0
    o[obs_spec.INDEX["subgoal_onehot"][0]] = 1.0
    o[obs_spec.INDEX["remaining_time"][0]] = 0.5
    return o


def test_check_accepts_valid():
    assert obs_spec.check(_valid_obs()) == []


@pytest.mark.parametrize("mutate,expected", [
    (lambda o: o.__setitem__(53, 0.0), "one-hot"),
    (lambda o: o.__setitem__(58, 0.5), "landed"),
    (lambda o: o.__setitem__(0, np.nan), "non-finite"),
    (lambda o: o.__setitem__(27, 1.3), "ray"),
])
def test_check_rejects(mutate, expected):
    o = _valid_obs()
    mutate(o)
    assert any(expected in p for p in obs_spec.check(o))


def test_split_names_parts():
    parts = obs_spec.split(np.arange(59.0))
    assert parts["landed"].tolist() == [58.0]
    assert parts["subgoal_onehot"].tolist() == [53.0, 54.0, 55.0, 56.0]

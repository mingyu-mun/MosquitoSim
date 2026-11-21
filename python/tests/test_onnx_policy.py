import numpy as np
import pytest

onnx = pytest.importorskip("onnx")
pytest.importorskip("onnxruntime")
from onnx import TensorProto, helper  # noqa: E402

from mosquitosim.onnx_policy import OnnxPolicy  # noqa: E402


def _model(path, out_name="deterministic_continuous_actions", obs=59):
    """obs_0 [N, obs] -> tanh(obs_0 @ W) [N, 3], like an ML-Agents export."""
    w = np.zeros((obs, 3), np.float32)
    w[0, 0], w[1, 1], w[2, 2] = 10.0, -10.0, 0.5
    graph = helper.make_graph(
        [helper.make_node("MatMul", ["obs_0", "W"], ["h"]), helper.make_node("Tanh", ["h"], [out_name])],
        "policy",
        [helper.make_tensor_value_info("obs_0", TensorProto.FLOAT, ["batch", obs])],
        [helper.make_tensor_value_info(out_name, TensorProto.FLOAT, ["batch", 3])],
        [helper.make_tensor("W", TensorProto.FLOAT, w.shape, w.flatten())],
    )
    m = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)])
    m.ir_version = 8
    onnx.save(m, path)


def test_policy_runs_batch(tmp_path):
    p = tmp_path / "Mosquito.onnx"
    _model(str(p))
    pol = OnnxPolicy(p)
    obs = np.zeros((4, 59), np.float32)
    obs[:, 0], obs[:, 1], obs[:, 2] = 1.0, 1.0, 1.0
    act = pol(obs)
    assert act.shape == (4, 3)
    assert np.allclose(act[:, 0], np.tanh(10)) and np.allclose(act[:, 1], np.tanh(-10))
    assert pol(obs[0]).shape == (1, 3)


def test_policy_falls_back_and_checks_size(tmp_path):
    p = tmp_path / "old.onnx"
    _model(str(p), out_name="continuous_actions")
    assert OnnxPolicy(p).output == "continuous_actions"
    bad = tmp_path / "bad.onnx"
    _model(str(bad), obs=60)
    with pytest.raises(ValueError):
        OnnxPolicy(bad)

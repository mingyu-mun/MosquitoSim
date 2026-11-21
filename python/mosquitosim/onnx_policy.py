"""Run an ML-Agents ONNX policy in Python (deterministic actions).

mlagents-learn writes results/<run-id>/Mosquito.onnx and Mosquito-<step>.onnx
checkpoints. Input is "obs_0" [batch, 59]; the deterministic output is
"deterministic_continuous_actions" (older exports only have
"continuous_actions", which is then used).
"""
from __future__ import annotations

from pathlib import Path

import numpy as np

from . import obs_spec


class OnnxPolicy:
    def __init__(self, path: str | Path) -> None:
        import onnxruntime as ort

        self.path = str(path)
        self.session = ort.InferenceSession(self.path, providers=["CPUExecutionProvider"])
        inputs = {i.name: i for i in self.session.get_inputs()}
        if "obs_0" not in inputs:
            raise ValueError(f"{path}: no obs_0 input (inputs: {list(inputs)})")
        outputs = [o.name for o in self.session.get_outputs()]
        if "deterministic_continuous_actions" in outputs:
            self.output = "deterministic_continuous_actions"
        elif "continuous_actions" in outputs:
            self.output = "continuous_actions"
        else:
            raise ValueError(f"{path}: no continuous action output (outputs: {outputs})")
        shape = inputs["obs_0"].shape
        if len(shape) == 2 and isinstance(shape[1], int) and shape[1] != obs_spec.OBS_SIZE:
            raise ValueError(f"{path}: obs size {shape[1]} != {obs_spec.OBS_SIZE}")

    def __call__(self, obs: np.ndarray) -> np.ndarray:
        obs = np.asarray(obs, dtype=np.float32)
        if obs.ndim == 1:
            obs = obs[None]
        act = self.session.run([self.output], {"obs_0": obs})[0]
        return np.clip(act.reshape(obs.shape[0], -1), -1.0, 1.0).astype(np.float32)

"""Check that the C# observation layout matches obs_spec.py (Unity cannot be
compiled in CI, so the constants are compared as text)."""
import re
import sys

from conftest import REPO_ROOT
from mosquitosim import obs_spec
from mosquitosim.subgoals import Subgoal

CS_DIR = REPO_ROOT / "unity" / "Assets" / "Scripts"

CS_TO_PY = {
    "SelfVelocity": "self_velocity",
    "TargetSkinRelPos": "target_skin_rel_pos",
    "TargetSkinDist": "target_skin_dist",
    "SubgoalTargetRelPos": "subgoal_target_rel_pos",
    "SubgoalTargetDist": "subgoal_target_dist",
    "HumanVelocity": "human_velocity",
    "HumanAlert": "human_alert",
    "HandRelPos": "hand_rel_pos",
    "HandVelocity": "hand_velocity",
    "HandActive": "hand_active",
    "HazardRelPos": "hazard_rel_pos",
    "HazardActive": "hazard_active",
    "HazardExposure": "hazard_exposure",
    "Rays": "rays",
    "SubgoalOneHot": "subgoal_onehot",
    "RemainingTime": "remaining_time",
    "Landed": "landed",
}


def _consts(text, kind):
    return {m.group(1): m.group(2) for m in re.finditer(
        rf"public const {kind} (\w+) = ([-\d.]+)f?;", text)}


def test_field_starts_match():
    ints = _consts((CS_DIR / "Agent" / "ObsLayout.cs").read_text(encoding="utf-8"), "int")
    for cs, py in CS_TO_PY.items():
        assert int(ints[cs]) == obs_spec.INDEX[py][0], cs
    assert int(ints["ObsSize"]) == obs_spec.OBS_SIZE
    assert int(ints["NumSubgoalSlots"]) == obs_spec.NUM_SUBGOAL_SLOTS
    # every Python field is covered
    assert set(CS_TO_PY.values()) == set(obs_spec.INDEX)


def test_scales_match():
    floats = _consts((CS_DIR / "Agent" / "ObsLayout.cs").read_text(encoding="utf-8"), "float")
    pairs = {"MaxSpeed": obs_spec.MAX_SPEED, "MaxAccel": obs_spec.MAX_ACCEL,
             "RayLength": obs_spec.RAY_LENGTH, "PosScale": obs_spec.POS_SCALE,
             "HumanVelScale": obs_spec.HUMAN_VEL_SCALE, "HandVelScale": obs_spec.HAND_VEL_SCALE}
    for cs, py in pairs.items():
        assert float(floats[cs]) == py, cs


def test_subgoal_enum_matches():
    text = (CS_DIR / "Agent" / "Subgoal.cs").read_text(encoding="utf-8")
    body = re.search(r"enum Subgoal\s*\{(.*?)\}", text, re.S).group(1)
    cs = {m.group(1).upper(): int(m.group(2)) for m in re.finditer(r"(\w+) = (\d+)", body)}
    py = {sg.name.replace("_", ""): sg.value for sg in Subgoal}
    assert cs == py


def test_builder_writes_by_layout_constant():
    """ObsBuilder must index the buffer through ObsLayout, never by literals."""
    text = (CS_DIR / "Agent" / "ObsBuilder.cs").read_text(encoding="utf-8")
    assert not re.search(r"buf\[\d", text)
    for cs in CS_TO_PY:
        assert f"ObsLayout.{cs}" in text, f"ObsBuilder never writes {cs}"


def test_stage0_config_parameters_are_read():
    import yaml
    cs = "\n".join(f.read_text(encoding="utf-8") for f in CS_DIR.rglob("*.cs"))
    for stage in (0, 1, 2):
        cfg = yaml.safe_load((REPO_ROOT / "configs" / f"stage{stage}.yaml").read_text(encoding="utf-8"))
        for key in cfg["environment_parameters"]:
            assert f'"{key}"' in cs, f"stage{stage}: {key} set in config but never read in Unity"
        assert "Mosquito" in cfg["behaviors"]


def test_eval_params_are_read():
    sys.path.insert(0, str(REPO_ROOT / "python" / "evaluation"))
    import ast
    src = (REPO_ROOT / "python" / "evaluation" / "evaluate.py").read_text(encoding="utf-8")
    keys = re.findall(r'"(\w+)": ', src[src.index("params = {"):src.index("needs_images")])
    cs = "\n".join(f.read_text(encoding="utf-8") for f in CS_DIR.rglob("*.cs"))
    assert keys and all(f'"{k}"' in cs for k in keys), keys

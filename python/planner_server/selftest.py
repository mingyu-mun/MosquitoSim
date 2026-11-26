"""VLM planner self-test (11/25): load the model, answer a fixed planner request with and without images,
and report whether the answer parses, the latency and the GPU memory. Run it on the machine that serves
the planner before any evaluation.

    python planner_server/selftest.py --model models/Qwen3-VL-4B-Instruct --images <dir with 5 png> --out selftest.txt

--images: a folder whose PNG/JPG files (sorted, first five) stand in for the mosquito view and the four
overviews; without it the test makes five plain 448 px images. Exit code 0 only if every answer parses.
"""
from __future__ import annotations

import argparse
import statistics
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from backends import TransformersBackend  # noqa: E402
from mosquitosim.subgoals import parse_planner_output  # noqa: E402

INSTRUCTION = (
    "[과제] 사람에게 들키지 않고 피부에 착지해 흡혈하라.\n[상황] 3.2초 / 60초. 사람: 서 있음.\n모기: 비행 중.\n"
    "피부: S1 얼굴 1.92m, S2 왼팔 1.60m, S3 오른팔 1.71m, S4 왼다리 2.10m, S5 오른다리 2.05m(착지 실패)\n"
    "후보 지점(이미지에 번호 표시, 모기로부터 / 사람 머리로부터 / 높이): "
    "P1 0.80m / 1.50m / 1.20m, P2 1.10m / 2.40m / 1.60m, P3 1.40m / 0.90m / 0.50m\n"
    "직전 지시: APPROACH S5\n[출력] 다음 중 하나를 JSON으로:\n"
    "{\"subgoal\": \"APPROACH\", \"target\": \"S1~S5 중 하나\"}\n"
    "{\"subgoal\": \"HIDE\", \"target\": \"P1~P3 중 하나\"}\n"
    "{\"subgoal\": \"MOVE_TO\", \"target\": \"P1~P3 중 하나\"}\n"
)


def images_from(folder: str | None) -> list[str]:
    if folder:
        files = sorted(p for p in Path(folder).iterdir() if p.suffix.lower() in (".png", ".jpg", ".jpeg"))
        if len(files) < 5:
            raise SystemExit(f"--images needs at least 5 images, found {len(files)} in {folder}")
        return [str(p) for p in files[:5]]
    from PIL import Image
    tmp = Path(tempfile.mkdtemp(prefix="vlm_selftest_"))
    out = []
    for i, c in enumerate([(200, 180, 160), (120, 140, 170), (90, 120, 90), (170, 120, 110), (130, 130, 130)]):
        p = tmp / f"view{i}.png"
        Image.new("RGB", (448, 448), c).save(p)
        out.append(str(p))
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="Qwen/Qwen3-VL-4B-Instruct")
    ap.add_argument("--dtype", default="bfloat16")
    ap.add_argument("--images", default=None)
    ap.add_argument("--repeats", type=int, default=5)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    lines: list[str] = []

    def log(s: str) -> None:
        print(s, flush=True)
        lines.append(s)

    t = time.perf_counter()
    backend = TransformersBackend(args.model, args.dtype)
    log(f"model {args.model} ({args.dtype}) loaded in {time.perf_counter() - t:.1f} s; {backend.memory()}")
    images = images_from(args.images)
    log(f"images: {', '.join(Path(p).name for p in images)}")
    backend.generate(INSTRUCTION, [], False)  # warm-up, not counted
    ok = True
    for use in (False, True):
        times, outs = [], []
        for _ in range(args.repeats):
            t = time.perf_counter()
            out = backend.generate(INSTRUCTION, images, use)
            times.append(time.perf_counter() - t)
            outs.append(out)
        parsed = [parse_planner_output(o) for o in outs]
        valid = sum(p is not None for p in parsed)
        ok &= valid == len(outs)
        label = "vlm (5 images)" if use else "vlm_noimg"
        log(f"{label}: median {statistics.median(times):.2f} s, max {max(times):.2f} s, "
            f"parsed {valid}/{len(outs)}, answers {sorted(set(outs))}")
    log(f"after test: {backend.memory()}")
    log("RESULT " + ("OK" if ok else "FAILED: some answers did not parse"))
    if args.out:
        Path(args.out).write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())

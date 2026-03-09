#!/usr/bin/env python3

from __future__ import annotations

import os
import pathlib
import sys


def locate_build_rs() -> pathlib.Path:
    cargo_home = pathlib.Path(os.environ.get("CARGO_HOME", pathlib.Path.home() / ".cargo"))
    matches = sorted(cargo_home.glob("registry/src/*/ffmpeg-sys-next-*/build.rs"))
    if not matches:
        print("ffmpeg-sys-next build.rs not found", file=sys.stderr)
        raise SystemExit(1)
    return matches[-1]


def replace_once(text: str, old: str, new: str, *, path: pathlib.Path, description: str) -> str:
    if old not in text:
        print(f"expected {description} not found in {path}", file=sys.stderr)
        raise SystemExit(1)
    return text.replace(old, new, 1)


def write_if_changed(path: pathlib.Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")
    print(f"patched {path}")

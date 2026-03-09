#!/usr/bin/env python3

import pathlib
import sys


def main() -> int:
    path = pathlib.Path("Cargo.toml")
    text = path.read_text(encoding="utf-8")
    old = 'crate-type = ["cdylib", "rlib", "staticlib"]'
    new = 'crate-type = ["staticlib", "rlib"]'

    if old not in text:
        print("expected crate-type declaration not found", file=sys.stderr)
        return 1

    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print("patched Cargo.toml for iOS static library build")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

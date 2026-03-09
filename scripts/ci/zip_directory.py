#!/usr/bin/env python3

from __future__ import annotations

import argparse
import pathlib
import zipfile


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    source = pathlib.Path(args.source).resolve()
    output = pathlib.Path(args.output).resolve()

    if not source.exists():
        raise FileNotFoundError(f"目录不存在: {source}")

    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        output.unlink()

    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        if source.is_file():
            archive.write(source, source.name)
        else:
            for path in source.rglob("*"):
                archive.write(path, path.relative_to(source.parent))

    print(f"[zip] source={source}")
    print(f"[zip] output={output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

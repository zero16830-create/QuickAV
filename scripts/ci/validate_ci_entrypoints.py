#!/usr/bin/env python3

from __future__ import annotations

import pathlib
import subprocess
import sys

from common import run


def main() -> int:
    script_dir = pathlib.Path(__file__).resolve().parent
    project_root = script_dir.parent.parent

    entrypoints = [
        script_dir / "common.py",
        script_dir / "ffmpeg_sys_next_patch_common.py",
        script_dir / "build_windows_unity_plugin.py",
        script_dir / "build_android_unity_plugin.py",
        script_dir / "build_ios_unity_plugin.py",
        script_dir / "build_unity_plugins.py",
        script_dir / "assemble_unity_plugins_bundle.py",
        script_dir / "compute_release_version.py",
        script_dir / "sync_unity_plugins_to_project.py",
        script_dir / "validate_ci_entrypoints.py",
        script_dir / "zip_directory.py",
    ]

    run(
        [sys.executable, "-m", "py_compile", *[str(path) for path in entrypoints]],
        cwd=project_root,
        prefix="ci-validate",
        dry_run=False,
    )

    dry_run_commands = [
        [
            sys.executable,
            str(script_dir / "build_windows_unity_plugin.py"),
            "--project-root",
            str(project_root),
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "build_android_unity_plugin.py"),
            "--project-root",
            str(project_root),
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "build_ios_unity_plugin.py"),
            "--project-root",
            str(project_root),
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "assemble_unity_plugins_bundle.py"),
            "--project-root",
            str(project_root),
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "build_unity_plugins.py"),
            "--project-root",
            str(project_root),
            "--platform",
            "all",
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "compute_release_version.py"),
            "--project-root",
            str(project_root),
        ],
    ]

    for cmd in dry_run_commands:
        run(cmd, cwd=project_root, prefix="ci-validate", dry_run=False)

    print("[ci-validate] all CI entrypoint dry-run checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

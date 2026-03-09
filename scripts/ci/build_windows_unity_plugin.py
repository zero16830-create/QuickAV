#!/usr/bin/env python3

import argparse
import os
import pathlib
from common import copy_file, ensure_directory, resolve_path, run, write_lines


def locate_vcpkg_root(project_root: pathlib.Path) -> pathlib.Path | None:
    candidates: list[pathlib.Path] = []

    env_root = os.environ.get("VCPKG_ROOT")
    if env_root:
        candidates.append(pathlib.Path(env_root))

    candidates.append(project_root / ".vcpkg")
    candidates.append(pathlib.Path("C:/vcpkg"))

    for candidate in candidates:
        if (candidate / "installed" / "x64-windows" / "bin").exists():
            return candidate

    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-root", default=".")
    parser.add_argument("--output-root", default="target/unity-package/windows")
    parser.add_argument("--configuration", default="release")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    project_root = pathlib.Path(args.project_root).resolve()
    output_root = resolve_path(project_root, args.output_root)

    configuration = args.configuration
    package_root = output_root / "Assets" / "Plugins" / "x86_64"
    artifact_dll = project_root / "target" / configuration / "rustav_native.dll"

    run(
        ["cargo", "build", f"--{configuration}", "--lib", "--locked"],
        cwd=project_root,
        prefix="windows-build",
        dry_run=args.dry_run,
    )

    if args.dry_run:
        return 0

    ensure_directory(package_root)
    copy_file(artifact_dll, package_root / artifact_dll.name)

    runtime_dlls: list[pathlib.Path] = []
    vcpkg_root = locate_vcpkg_root(project_root)
    if vcpkg_root:
        runtime_dir = vcpkg_root / "installed" / "x64-windows" / "bin"
        if runtime_dir.exists():
            runtime_dlls = sorted(runtime_dir.glob("*.dll"))
            for dll in runtime_dlls:
                copy_file(dll, package_root / dll.name)

    dependency_file = package_root / "DEPENDENCIES.txt"
    lines = [
        "Windows Unity 插件目录：Assets/Plugins/x86_64",
        "",
        "运行时文件：",
        "  - rustav_native.dll",
    ]
    for dll in runtime_dlls:
        lines.append(f"  - {dll.name}")
    write_lines(dependency_file, lines)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

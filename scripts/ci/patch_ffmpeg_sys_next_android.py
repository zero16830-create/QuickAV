#!/usr/bin/env python3

from ffmpeg_sys_next_patch_common import locate_build_rs, replace_once, write_if_changed


def main() -> int:
    path = locate_build_rs()
    text = path.read_text(encoding="utf-8")
    old = (
        'android_cc_path\n'
        '                    .join("..")\n'
        '                    .join(format!("llvm-{tool}"))\n'
        '                    .canonicalize()'
    )
    new = (
        'android_cc_path\n'
        '                    .parent()\n'
        '                    .expect("android compiler path must have parent")\n'
        '                    .join(format!("llvm-{tool}"))\n'
        '                    .canonicalize()'
    )

    text = replace_once(
        text,
        old,
        new,
        path=path,
        description="ffmpeg-sys-next android tool lookup snippet",
    )
    write_if_changed(path, text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

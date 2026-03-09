#!/usr/bin/env python3

from ffmpeg_sys_next_patch_common import locate_build_rs, replace_once, write_if_changed


def main() -> int:
    path = locate_build_rs()
    text = path.read_text(encoding="utf-8")

    block_old = """fn get_ffmpeg_target_os() -> String {
            let cargo_target_os = env::var(\"CARGO_CFG_TARGET_OS\").unwrap();
            match cargo_target_os.as_str() {
                \"ios\" => \"darwin\".to_string(),
                _ => cargo_target_os,
            }
        }
"""
    helper_new = """fn get_ffmpeg_target_os() -> String {
            let cargo_target_os = env::var(\"CARGO_CFG_TARGET_OS\").unwrap();
            match cargo_target_os.as_str() {
                \"ios\" => \"darwin\".to_string(),
                _ => cargo_target_os,
            }
        }

        fn get_apple_sdk_name() -> &'static str {
            match env::var(\"TARGET\") {
                Ok(target) if target.contains(\"-ios-sim\") => \"iphonesimulator\",
                _ => \"iphoneos\",
            }
        }
"""
    text = replace_once(
        text,
        block_old,
        helper_new,
        path=path,
        description="get_ffmpeg_target_os block",
    )
    text = replace_once(
        text,
        '["--sdk", "iphoneos", "--show-sdk-path"]',
        '["--sdk", get_apple_sdk_name(), "--show-sdk-path"]',
        path=path,
        description="iphoneos sdk lookup snippet",
    )
    text = replace_once(
        text,
        '["--sdk", "iphoneos", "-f", "clang"]',
        '["--sdk", get_apple_sdk_name(), "-f", "clang"]',
        path=path,
        description="iphoneos clang snippet",
    )

    write_if_changed(path, text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

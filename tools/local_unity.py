#!/usr/bin/env python3
"""Run this project locally; credentials may be provided in environment only."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
METHODS = {
    "configure": "MyXonotic.EditorTools.LocalBuild.Configure",
    "scene": "MyXonotic.EditorTools.LocalBuild.CreateDevelopmentScene",
    "import": "MyXonotic.EditorTools.LocalBuild.ImportExternalBsp",
    "android": "MyXonotic.EditorTools.LocalBuild.BuildAndroid",
    "linux": "MyXonotic.EditorTools.LocalBuild.BuildLinux",
    "test": "MyXonotic.EditorTools.LocalTests.Run",
    "playtest": "MyXonotic.EditorTools.LocalPlaytest.Run",
    "original-playtest": "MyXonotic.EditorTools.OriginalMapPlaytest.Run",
}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("task", choices=["compile"] + list(METHODS))
    p.add_argument("--unity", default=os.environ.get("UNITY_EDITOR"),
                   help="Unity Editor executable (or local sandbox wrapper).")
    p.add_argument("--timeout", type=int, default=1800)
    p.add_argument("--graphics", action="store_true",
                   help="Use local display/Xvfb rather than -nographics (required for render test).")
    args = p.parse_args()
    if not args.unity or not Path(args.unity).is_file():
        p.error("Set UNITY_EDITOR or --unity to an existing local Unity executable.")
    if args.timeout <= 0:
        p.error("--timeout must be positive.")
    artifacts = ROOT / "Artifacts"
    artifacts.mkdir(exist_ok=True)
    lock = artifacts / "local-unity.lock"
    try:
        fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except FileExistsError:
        p.error("Another local invocation holds the lock. Check its PID before removing a stale lock.")
    os.write(fd, str(os.getpid()).encode())
    os.close(fd)
    log = artifacts / (args.task + ".log")
    command = [args.unity, "-batchmode", "-projectPath", str(ROOT), "-logFile", str(log)]
    command += ["-force-glcore"] if args.graphics else ["-nographics"]
    if args.task not in ("playtest", "original-playtest"):
        command += ["-quit"]
    if args.task == "android":
        command += ["-buildTarget", "Android"]
    if args.task == "linux":
        command += ["-buildTarget", "Linux64"]
    if args.task in METHODS:
        command += ["-executeMethod", METHODS[args.task]]
    # Never print command: Unity legacy licence activation passes credentials as argv.
    user, password = os.environ.get("UNITY_USER"), os.environ.get("UNITY_PASS")
    if bool(user) != bool(password):
        lock.unlink()
        p.error("Supply both UNITY_USER and UNITY_PASS, or neither (preferred: activate in Unity Hub).")
    if user:
        command += ["-username", user, "-password", password]
    started = time.monotonic()
    code = 1
    try:
        if args.task in ("playtest", "original-playtest"):
            # A previous successful run must not mask an early zero-exit failure.
            (artifacts / ("original-playtest.json" if args.task == "original-playtest" else "playtest-result.json")).unlink(missing_ok=True)
        # Account/licensing diagnostics must remain local and private.
        log_fd = os.open(log, os.O_CREAT | os.O_TRUNC | os.O_WRONLY |
                         getattr(os, "O_NOFOLLOW", 0), 0o600)
        if hasattr(os, "fchmod"):
            os.fchmod(log_fd, 0o600)
        os.close(log_fd)
        print(f"Running local {args.task}; log: Artifacts/{log.name}", flush=True)
        process = subprocess.Popen(command, cwd=ROOT, start_new_session=True)
        try:
            code = process.wait(timeout=args.timeout)
        except (subprocess.TimeoutExpired, KeyboardInterrupt):
            import signal
            if os.name == "posix":
                os.killpg(process.pid, signal.SIGTERM)
            else:
                process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                if os.name == "posix":
                    os.killpg(process.pid, signal.SIGKILL)
                else:
                    process.kill()
                process.wait()
            code = 124
        if code == 0 and args.task in ("playtest", "original-playtest"):
            result = artifacts / ("original-playtest.json" if args.task == "original-playtest" else "playtest-result.json")
            if not result.exists() or not json.loads(result.read_text()).get("passed"):
                code = 1
        print(f"Local {args.task}: exit {code}, {time.monotonic()-started:.1f}s", flush=True)
        return code
    finally:
        lock.unlink(missing_ok=True)


if __name__ == "__main__":
    sys.exit(main())

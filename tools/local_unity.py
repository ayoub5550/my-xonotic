#!/usr/bin/env python3
"""Run this project locally; credentials may be provided in environment only."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
METHODS = {
    "compile": "MyXonotic.EditorTools.LocalBuild.ValidateCompilation",
    "configure": "MyXonotic.EditorTools.LocalBuild.Configure",
    "scene": "MyXonotic.EditorTools.LocalBuild.CreateDevelopmentScene",
    "import": "MyXonotic.EditorTools.LocalBuild.ImportExternalBsp",
    "prepare-maps": "MyXonotic.EditorTools.FullGameBuild.PrepareFullGame",
    "android": "MyXonotic.EditorTools.LocalBuild.BuildAndroid",
    "linux": "MyXonotic.EditorTools.LocalBuild.BuildLinux",
    "test": "MyXonotic.EditorTools.LocalTests.Run",
    "sky-test": "MyXonotic.EditorTools.SkyImportRegressionTests.Run",
    "gameplay-test": "MyXonotic.EditorTools.GameplayIntegrationTests.Run",
    "gameplay-playtest": "MyXonotic.EditorTools.GameplayPlaytest.Run",
    "playtest": "MyXonotic.EditorTools.LocalPlaytest.Run",
    "original-playtest": "MyXonotic.EditorTools.OriginalMapPlaytest.Run",
    "all-maps-playtest": "MyXonotic.EditorTools.AllMapsPlaytest.Run",
    "content-test": "MyXonotic.EditorTools.ContentRegressionTests.Run",
}


def build_output(task, imported):
    if task == "android":
        if os.environ.get("XONOTIC_ALL_MAPS") == "1":
            return "my-xonotic-full.apk"
        return "my-xonotic-unity-boil.apk" if imported else "my-xonotic-development.apk"
    return "my-xonotic.x86_64"


def validate_build_receipt(root, task, invocation, imported):
    """Require this invocation's receipt and the exact artifact it describes."""
    receipt_path = root / "Builds/build-receipt.json"
    try:
        receipt = json.loads(receipt_path.read_text())
        target = "Android" if task == "android" else "StandaloneLinux64"
        artifact = root / "Builds" / build_output(task, imported)
        if not isinstance(receipt, dict):
            return False
        if (receipt.get("invocation") != invocation
                or receipt.get("target") != target
                or receipt.get("result") != "Succeeded"
                or receipt.get("errors") != 0
                or receipt.get("output") != artifact.name):
            return False
        if not artifact.is_file() or artifact.is_symlink() or artifact.stat().st_size <= 0:
            return False
        if receipt.get("artifactBytes") != artifact.stat().st_size:
            return False
        digest = hashlib.sha256()
        with artifact.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        return receipt.get("sha256") == digest.hexdigest()
    except (OSError, ValueError, TypeError):
        return False


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("task", choices=list(METHODS))
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
    if args.task not in ("playtest", "original-playtest", "gameplay-playtest", "all-maps-playtest"):
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
    invocation = uuid.uuid4().hex
    imported = os.environ.get("XONOTIC_INCLUDE_EXTERNAL") == "1"
    child_env = os.environ.copy()
    child_env["XONOTIC_BUILD_INVOCATION"] = invocation
    try:
        if args.task in ("android", "linux"):
            # Keep any previous artifact, but never allow its old receipt to
            # make an activation/early-exit failure look like a new build.
            (ROOT / "Builds/build-receipt.json").unlink(missing_ok=True)
        test_reports = {"test": "editor-tests.txt", "content-test": "content-regression-tests.txt", "sky-test": "sky-import-regression-tests.txt",
                        "gameplay-test": "gameplay-integration-tests.txt"}
        if args.task in test_reports:
            (artifacts / test_reports[args.task]).unlink(missing_ok=True)
        if args.task == "compile":
            (artifacts / "compile-result.json").unlink(missing_ok=True)
        if args.task in ("playtest", "original-playtest", "gameplay-playtest", "all-maps-playtest"):
            # A previous successful run must not mask an early zero-exit failure.
            report_name = {"playtest": "playtest-result.json",
                           "original-playtest": "original-playtest.json",
                           "gameplay-playtest": "gameplay-playtest.json",
                           "all-maps-playtest": "all-maps-playtest.json"}[args.task]
            (artifacts / report_name).unlink(missing_ok=True)
        # Account/licensing diagnostics must remain local and private.
        log_fd = os.open(log, os.O_CREAT | os.O_TRUNC | os.O_WRONLY |
                         getattr(os, "O_NOFOLLOW", 0), 0o600)
        if hasattr(os, "fchmod"):
            os.fchmod(log_fd, 0o600)
        os.close(log_fd)
        print(f"Running local {args.task}; log: Artifacts/{log.name}", flush=True)
        process = subprocess.Popen(command, cwd=ROOT, env=child_env, start_new_session=True)
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
        if code == 0 and args.task in ("playtest", "original-playtest", "gameplay-playtest", "all-maps-playtest"):
            result = artifacts / report_name
            try:
                passed = json.loads(result.read_text()).get("passed") is True
            except (OSError, ValueError, AttributeError):
                passed = False
            if not passed:
                code = 1
        if code == 0 and args.task in test_reports:
            result = artifacts / test_reports[args.task]
            if not result.is_file() or not result.read_text().strip():
                code = 1
        if code == 0 and args.task == "compile":
            try:
                result = json.loads((artifacts / "compile-result.json").read_text())
                passed = (result.get("passed") is True
                          and result.get("invocation") == invocation)
            except (OSError, ValueError, AttributeError):
                passed = False
            if not passed:
                print("Compilation not verified: Editor did not write this run's completion marker.", flush=True)
                code = 1
        if code == 0 and args.task in ("android", "linux"):
            if not validate_build_receipt(ROOT, args.task, invocation, imported):
                print("Build not verified: fresh matching receipt/artifact/hash required.", flush=True)
                code = 1
        print(f"Local {args.task}: exit {code}, {time.monotonic()-started:.1f}s", flush=True)
        return code
    finally:
        lock.unlink(missing_ok=True)


if __name__ == "__main__":
    sys.exit(main())

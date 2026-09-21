#!/usr/bin/env python3
"""Host integration probe: real Android fs/listing branches + SDL RWops.

Requires a previously built Linux engine object directory and its build log.
Does NOT run an Android APK, GLES rendering, JNI, or Android SDL asset access.
Usage: python3 tools/test_engine_android_fs.py /path/to/hostbuild /path/to/hostbuild.log
"""
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[1]
host = Path(sys.argv[1]).resolve()
lines = Path(sys.argv[2]).read_text().splitlines()
objdir = host / "build-obj/release/darkplaces-sdl"
compile_cmd = shlex.split(next(l for l in lines if l.startswith("gcc ") and " -c " in l))
link_cmd = shlex.split(next(l for l in lines if l.startswith("gcc ") and " -o " in l and " -c " not in l))
with tempfile.TemporaryDirectory(prefix="xonotic-fs-probe-") as td:
    tmp = Path(td)
    for name in ("filematch", "fs"):
        cmd = compile_cmd[:]
        cmd[cmd.index("-c")+1] = str(root / f"darkplaces/{name}.c")
        cmd[cmd.index("-o")+1] = str(tmp / f"{name}.o")
        cmd += ["-D__ANDROID__", "-I"+str(root/"darkplaces")]
        subprocess.run(cmd, cwd=objdir, check=True)
    cmd = link_cmd[:]
    cmd[cmd.index("-o")+1] = str(tmp/"engine")
    cmd = [str(tmp / x) if x in ("fs.o","filematch.o") else x for x in cmd]
    subprocess.run(cmd, cwd=objdir, check=True)
    (tmp/"base/data").mkdir(parents=True)
    (tmp/"user").mkdir()
    for p in (root/"android/app/src/main/assets/game").glob("*.pk3"):
        (tmp/"base/data"/p.name).symlink_to(p)
    # Run only long enough for FS_Init's actual read-through guard; no gameplay claim.
    with (tmp/"run.log").open("w") as log:
        proc = subprocess.Popen([str(tmp/"engine"), "-dedicated", "-xonotic",
            "-basedir", str(tmp/"base"), "-userdir", str(tmp/"user"),
            "-sessionid", "", "+quit"], cwd=tmp, stdout=log, stderr=subprocess.STDOUT)
        try:
            proc.wait(timeout=15)
        except subprocess.TimeoutExpired:
            proc.terminate()
            proc.wait(timeout=5)
    report = (tmp/"base/startup-resources.txt").read_text()
    print(report)
    checks = [l for l in report.splitlines() if l.startswith("PASS ")]
    assert len(checks) == 16 and "FAIL " not in report
    print("PASS: 16 actual FS_LoadFile reads through Android fs/listing branches + host SDL RWops.")

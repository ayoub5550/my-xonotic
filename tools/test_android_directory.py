#!/usr/bin/env python3
"""Compile actual filematch.c with its Android branch and tiny engine API stubs.

Host-side regression, NOT an Android emulator/device test.
Usage: python3 tools/test_android_directory.py [alternative-filematch.c]
"""
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1]) if len(sys.argv) > 1 else root / "darkplaces/filematch.c"
header = r"""
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <stdbool.h>
#include <stdarg.h>
typedef int qbool;
typedef void qfile_t;
typedef struct { int numstrings, maxstrings; char **strings; } stringlist_t;
#define MAX_OSPATH 4096
#define Z_Malloc(n) calloc(1,n)
#define Z_Free(p) free(p)
#define Mem_Free(p) free(p)
#define dpsnprintf snprintf
extern void *tempmempool;
extern int asset_reads;
void *FS_SysLoadFile(const char*, void*, qbool, void*);
int matchpattern_with_separator(const char*,const char*,int,const char*,qbool);
void listdirectory(stringlist_t*,const char*,const char*);
void stringlistinit(stringlist_t*);
void stringlistfreecontents(stringlist_t*);
"""
test = r"""
#include "darkplaces.h"
void *tempmempool = NULL;
int asset_reads = 0;
void *FS_SysLoadFile(const char *name, void *pool, qbool quiet, void *size) {
    asset_reads++;
    if (!strcmp(name, "assets/data/ls.txt")) return strdup("asset.pk3\n");
    return NULL;
}
static int check(const char *base, const char *path, int n, int reads) {
    stringlist_t list;
    stringlistinit(&list); asset_reads=0;
    listdirectory(&list, base, path);
    int pass = list.numstrings==n && asset_reads==reads;
    printf("%s: base='%s' path='%s': entries=%d asset_reads=%d\n",
      pass?"PASS":"FAIL",base,path,list.numstrings,asset_reads);
    stringlistfreecontents(&list);
    return !pass;
}
int main(int argc, char **argv) {
    int fail=0; char dir[4096];
    snprintf(dir,sizeof(dir),"%s/data/",argv[1]);
    fail+=check("",dir,2,0); /* FS_AddGameDirectory's exact argument shape */
    snprintf(dir,sizeof(dir),"%s/",argv[1]);
    fail+=check(dir,"data/",2,0); /* absolute base + relative suffix */
    fail+=check("assets/","data/",1,1); /* relative APK asset listing */
    fail+=check("","assets/data/",1,1);
    return fail ? 1 : 0;
}
"""
with tempfile.TemporaryDirectory() as td:
    tmp = Path(td)
    (tmp / "darkplaces.h").write_text(header)
    (tmp / "filematch.c").write_text(source.read_text())
    (tmp / "test.c").write_text(test)
    (tmp / "data").mkdir()
    (tmp / "data/a.pk3").touch()
    (tmp / "data/b.pk3").touch()
    subprocess.run(["cc", "-D__ANDROID__", "-std=gnu99", "-Wall", "-Wextra",
                    "-Wno-unused-parameter", "-Wno-unused-variable", str(tmp/"filematch.c"),
                    str(tmp/"test.c"), "-o", str(tmp/"test")], check=True)
    subprocess.run([str(tmp/"test"), str(tmp)], check=True)

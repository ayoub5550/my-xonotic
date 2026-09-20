#!/bin/sh
# Build libmain.so (DarkPlaces + deps) for arm64-v8a and copy into the Gradle project.
# Requires: Android NDK r23b, cmake >= 3.22, ninja. See env.sh for the sandbox layout.
set -e
cd "$(dirname "$0")"
. ./env.sh
: "${ANDROID_NDK:?set ANDROID_NDK}"
TOOLCHAIN="$ANDROID_NDK/build/cmake/android.toolchain.cmake"
COMMON="-G Ninja -DCMAKE_TOOLCHAIN_FILE=$TOOLCHAIN -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-26 -DCMAKE_BUILD_TYPE=Release"
[ -n "$CMAKE_MAKE_PROGRAM" ] && COMMON="$COMMON -DCMAKE_MAKE_PROGRAM=$CMAKE_MAKE_PROGRAM"

# 1. libjpeg-turbo refuses add_subdirectory(); build + install it separately.
mkdir -p build/jpeg build/prefix
( cd build/jpeg && cmake $COMMON ../../deps/libjpeg-turbo-3.0.4 -DENABLE_SHARED=OFF -DENABLE_STATIC=ON \
    -DWITH_TURBOJPEG=OFF -DCMAKE_INSTALL_PREFIX="$PWD/../prefix" && ninja && ninja install )

# 2. Everything else (SDL2, libpng, ogg, vorbis, freetype, DarkPlaces).
mkdir -p build/arm64
( cd build/arm64 && cmake $COMMON ../../native && ninja )

# 3. Stage into the Gradle project.
JNI=android/app/src/main/jniLibs/arm64-v8a
mkdir -p "$JNI"
cp build/arm64/libmain.so build/arm64/sdl2/libSDL2.so "$JNI/"
cp build/arm64/png/libpng16.so "$JNI/libpng.so"   # engine dlopen()s "libpng.so"
python3 tools/make_touch_pk3.py
echo "native libs staged in $JNI"

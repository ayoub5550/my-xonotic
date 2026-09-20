#!/bin/sh
# Download and verify the third-party library sources used by native/CMakeLists.txt.
set -e
cd "$(dirname "$0")/../deps"
fetch() { # name url sha256
  [ -f "$1" ] || curl -sSL -o "$1" "$2"
  echo "$3  $1" | sha256sum -c -
  tar xzf "$1"
}
fetch SDL2.tar.gz     https://github.com/libsdl-org/SDL/releases/download/release-2.30.11/SDL2-2.30.11.tar.gz 8b8d4aef2038533da814965220f88f77d60dfa0f32685f80ead65e501337da7f
fetch jpeg.tar.gz     https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/3.0.4/libjpeg-turbo-3.0.4.tar.gz 99130559e7d62e8d695f2c0eaeef912c5828d5b84a0537dcb24c9678c9d5b76b
fetch png.tar.gz      https://github.com/pnggroup/libpng/archive/refs/tags/v1.6.44.tar.gz 0ef5b633d0c65f780c4fced27ff832998e71478c13b45dfb6e94f23a82f64f7c
fetch ogg.tar.gz      https://github.com/xiph/ogg/releases/download/v1.3.5/libogg-1.3.5.tar.gz 0eb4b4b9420a0f51db142ba3f9c64b333f826532dc0f48c6410ae51f4799b664
fetch vorbis.tar.gz   https://github.com/xiph/vorbis/releases/download/v1.3.7/libvorbis-1.3.7.tar.gz 0e982409a9c3fc82ee06e08205b1355e5c6aa4c36bca58146ef399621b0ce5ab
fetch freetype.tar.gz https://download.savannah.gnu.org/releases/freetype/freetype-2.13.3.tar.gz 5c3a8e78f7b24c20b25b54ee575d6daa40007a5f4eea2845861c3409b3021747
echo "deps ready"

cmake_minimum_required(VERSION 3.22)
project(xonotic_android C CXX ASM)

# Layout: this file lives in <root>/native; sources are siblings.
set(ROOT ${CMAKE_CURRENT_SOURCE_DIR}/..)
set(DEPS ${ROOT}/deps)
set(DP   ${ROOT}/darkplaces)

set(CMAKE_C_STANDARD 11)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# ---------------------------------------------------------------- SDL2 (shared)
set(SDL_SHARED ON  CACHE BOOL "" FORCE)
set(SDL_STATIC OFF CACHE BOOL "" FORCE)
set(SDL_TEST   OFF CACHE BOOL "" FORCE)
add_subdirectory(${DEPS}/SDL2-2.30.11 sdl2)

# ---------------------------------------------------------------- libjpeg-turbo (static, prebuilt by build_jpeg step)
set(JPEG_PREFIX ${ROOT}/build/prefix)
add_library(jpeg-static STATIC IMPORTED)
set_target_properties(jpeg-static PROPERTIES IMPORTED_LOCATION ${JPEG_PREFIX}/lib/libjpeg.a)

# ---------------------------------------------------------------- libpng (shared, dlopen'ed as libpng.so)
set(PNG_SHARED ON  CACHE BOOL "" FORCE)
set(PNG_STATIC OFF CACHE BOOL "" FORCE)
set(PNG_TESTS  OFF CACHE BOOL "" FORCE)
set(PNG_TOOLS  OFF CACHE BOOL "" FORCE)
set(PNG_FRAMEWORK OFF CACHE BOOL "" FORCE)
set(PNG_HARDWARE_OPTIMIZATIONS ON CACHE BOOL "" FORCE)
set(SKIP_INSTALL_ALL ON)
add_subdirectory(${DEPS}/libpng-1.6.44 png)

# ---------------------------------------------------------------- ogg / vorbis (static)
set(BUILD_SHARED_LIBS OFF)
set(INSTALL_DOCS OFF CACHE BOOL "" FORCE)
set(INSTALL_PKG_CONFIG_MODULE OFF CACHE BOOL "" FORCE)
set(INSTALL_CMAKE_PACKAGE_MODULE OFF CACHE BOOL "" FORCE)
add_subdirectory(${DEPS}/libogg-1.3.5 ogg)
set(OGG_INCLUDE_DIR ${DEPS}/libogg-1.3.5/include ${CMAKE_CURRENT_BINARY_DIR}/ogg/include)
set(OGG_LIBRARY ogg)
add_subdirectory(${DEPS}/libvorbis-1.3.7 vorbis)

# ---------------------------------------------------------------- freetype (static, minimal)
set(FT_DISABLE_ZLIB OFF CACHE BOOL "" FORCE)
set(FT_DISABLE_BZIP2 ON CACHE BOOL "" FORCE)
set(FT_DISABLE_PNG ON CACHE BOOL "" FORCE)
set(FT_DISABLE_HARFBUZZ ON CACHE BOOL "" FORCE)
set(FT_DISABLE_BROTLI ON CACHE BOOL "" FORCE)
add_subdirectory(${DEPS}/freetype-2.13.3 freetype)

# ---------------------------------------------------------------- DarkPlaces -> libmain.so
file(STRINGS ${CMAKE_CURRENT_SOURCE_DIR}/dp_sources.txt DP_SOURCES_REL)
set(DP_SOURCES "")
foreach(f ${DP_SOURCES_REL})
  list(APPEND DP_SOURCES ${DP}/${f})
endforeach()

add_library(main SHARED ${DP_SOURCES})
target_include_directories(main PRIVATE
  ${DP}
  ${DEPS}/SDL2-2.30.11/include
  ${JPEG_PREFIX}/include
  ${DEPS}/libogg-1.3.5/include ${CMAKE_CURRENT_BINARY_DIR}/ogg/include
  ${DEPS}/libvorbis-1.3.7/include
  ${DEPS}/freetype-2.13.3/include)
target_compile_definitions(main PRIVATE
  LINK_TO_LIBJPEG=1 CONFIG_MENU=1 CONFIG_VIDEO_CAPTURE=1 _FILE_OFFSET_BITS=64 __KERNEL_STRICT_NAMES=1
  SVNREVISION="xonotic-android"
  BUILDTYPE="release")
target_compile_options(main PRIVATE -O2 -fno-strict-aliasing -ffast-math
  -Wall -Wno-unused-but-set-variable -Wno-unused-variable -Wno-unused-function
  -Wno-deprecated-declarations -Wno-unknown-warning-option)
target_link_libraries(main PRIVATE SDL2 jpeg-static vorbisfile vorbis ogg freetype z m dl log android GLESv3 EGL)

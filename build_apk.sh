#!/bin/sh
# Package the APK with the Gradle launcher bundled in Unity's Android tools (no Gradle download needed).
set -e
cd "$(dirname "$0")"
. ./env.sh
export HOME="${HOME:-/work/unity/home}"
export GRADLE_USER_HOME="${GRADLE_USER_HOME:-$HOME/.gradle}"
# Never silently produce another downloader-sized APK without its game resources.
[ -f android/app/src/main/assets/game-manifest.tsv ] || {
    echo "Run tools/bundle_data.py <official-xonotic-0.8.6.zip> first" >&2
    exit 1
}
cd android
java -Xmx2g -classpath "$GRADLE_JAR" org.gradle.launcher.GradleMain --no-daemon --console=plain "${1:-assembleRelease}"
find app/build/outputs/apk -name '*.apk' -ls 2>/dev/null || true

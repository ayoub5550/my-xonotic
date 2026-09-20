#!/bin/sh
# Package the APK with the Gradle launcher bundled in Unity's Android tools (no Gradle download needed).
set -e
cd "$(dirname "$0")"
. ./env.sh
export HOME="${HOME:-/work/unity/home}"
export GRADLE_USER_HOME="${GRADLE_USER_HOME:-$HOME/.gradle}"
cd android
java -Xmx2g -classpath "$GRADLE_JAR" org.gradle.launcher.GradleMain --no-daemon --console=plain "${1:-assembleRelease}"
ls -la app/build/outputs/apk/*/*.apk

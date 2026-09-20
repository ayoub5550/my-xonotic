export AP=/work/unity/editor/Editor/Data/PlaybackEngines/AndroidPlayer
export ANDROID_NDK=$AP/NDK
export ANDROID_SDK_ROOT=$AP/SDK
export ANDROID_HOME=$AP/SDK
export JAVA_HOME=$AP/OpenJDK
export LD_LIBRARY_PATH=/work/unity/sysroot/usr/lib/x86_64-linux-gnu:${LD_LIBRARY_PATH:-}
export PATH=/work/unity/sysroot/usr/bin:$JAVA_HOME/bin:$PATH
export GRADLE_JAR=$AP/Tools/gradle/lib/gradle-launcher-7.5.1.jar

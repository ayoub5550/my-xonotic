# Xonotic Android

منفذ **اللعبة الأصلية Xonotic 0.8.6** إلى أندرويد (ARM64): نفس محرك DarkPlaces وبيانات اللعبة
الرسمية، مغلّفان في تطبيق أندرويد عبر SDL2. كل الخرائط والأسلحة والبوتات وأوضاع اللعب
واللعب الشبكي — لأنه المحرك نفسه.

## التثبيت
1. ثبّت `app-release.apk` (Android 8.0+، معالج 64-bit، OpenGL ES 3.0).
2. عند أول تشغيل يطلب التطبيق تنزيل بيانات اللعبة (~1.1 GB) من `dl.xonotic.org` — استعمل Wi-Fi.
   أو انسخ ملفات `.pk3` من نسخة سطح المكتب يدويًا إلى:
   `Android/data/com.ayoub.xonotic/files/data/`
3. اضغط Play.

## البناء محليًا
راجع `AGENTS.md` (بالإنجليزية): `./build_native.sh` ثم `./build_apk.sh`.

## الترخيص
المحرك والبيانات: GPLv2+ (انظر `darkplaces/COPYING`). كود التطبيق في `android/app/src/main/java/com/ayoub/xonotic`
تحت GPLv2+ أيضًا. المكتبات في `deps/` بتراخيصها الأصلية.

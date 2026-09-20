# سجل التغييرات

## 0.1.0 — 2026-09-20 — أول APK للمحرك الأصلي

- ترجمة محرك DarkPlaces (gitlab.com/xonotic/darkplaces، commit d93f9c4) لأندرويد ARM64
  مع SDL2 2.30.11، libjpeg-turbo، libpng، ogg/vorbis، freetype. 4 تعديلات صغيرة على المحرك
  (`native/darkplaces-android.patch`): ترويسات GLES3، سياق ES 3.0، إصلاح const في وضع اللمس،
  تعطيل كود Steelstorm/KTX.
- تطبيق أندرويد: شاشة تنزيل البيانات (تستخرج pk3 من الأرشيف الرسمي 0.8.6 مباشرة)، ثم
  تشغيل المحرك بضبط لمس افتراضي (`android.cfg` + أيقونات لمس مولّدة).
- APK ≈ 3.3 MB (بدون بيانات). **غير مجرَّب على جهاز حقيقي بعد.**

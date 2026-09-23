# اختبار الجهاز الحقيقي — Firebase Test Lab (إلزامي لكل إصدار)

منذ dev.15، **كل APK يُختبر على هواتف حقيقية في Firebase Test Lab قبل أن يُعلَن الإصدار**.
لا يُكتب «جُرّب على جهاز» في ملاحظات الإصدار أو `docs/UNITY-DEV<N>.md` إلا بنتيجة من هنا
(أو من تجربة المالك اليدوية). هذه هي الطريقة الدائمة؛ لا بدائل ارتجالية.

## لماذا
- بيئة البناء (حاوية بلا KVM ولا GPU، والـAPK arm64 فقط) لا تستطيع تشغيل محاكي Android.
- Test Lab يشغّل الـAPK على أجهزة فعلية ويعطي: فيديو الجلسة، `logcat` كامل، لقطات شاشة،
  تقرير الأعطال (crash/ANR)، واستخدام CPU/الذاكرة.
- الحصة المجانية (خطة Spark): **5 اختبارات على أجهزة حقيقية + 10 افتراضية يومياً**. لا تُبذَّر.

## الإعداد (مرة واحدة لكل بيئة)
1. مشروع Firebase: `ayoub-261d7` (رقم المشروع 265104405016) على حساب المالك.
2. حساب خدمي: `firebase-adminsdk-fbsvc@ayoub-261d7.iam.gserviceaccount.com` بدور **Editor** على المشروع.
   مفتاحه JSON **خارج المستودع** (مثلاً `~/secrets/firebase-sa.json`، صلاحيات 600). لا يُرفع أبداً إلى git أو الإصدارات.
3. واجهات مفعّلة: `testing.googleapis.com`، `toolresults.googleapis.com`، `storage.googleapis.com`.
4. gcloud CLI:
   ```bash
   gcloud auth activate-service-account --key-file=$FIREBASE_SA_JSON
   gcloud config set project ayoub-261d7
   ```
   ملاحظة: إنشاء مفاتيح الحسابات الخدمية محجوب افتراضياً بسياسة المنظمة
   `iam.disableServiceAccountKeyCreation`؛ أُوقفت للمشروع في 2026-09-23 بعد إعطاء المالك دور Organization Policy Administrator.

## التشغيل لكل إصدار
```bash
export FIREBASE_SA_JSON=~/secrets/firebase-sa.json
tools/ftl_robo.sh Builds/my-xonotic-full.apk dev<N>
```
السكربت يشغّل **Robo test** (يستكشف الواجهة تلقائياً) على جهازين حقيقيين افتراضياً:
- `a15x` Galaxy A15 5G / Android 14 — يمثّل الفئة المتوسطة (الجمهور المستهدف).
- `SC-51E` Galaxy S24 / Android 16 — يمثّل أحدث نظام.
اختر أجهزة أخرى بـ `gcloud firebase test android models list --filter="form=PHYSICAL"`.

النتائج تُنسخ إلى `Artifacts/ftl/dev<N>/` (فيديو `video.mp4`، `logcat`، لقطات، `test_result_1.xml`).
**لا تُرفع إلى git** (المجلد `Artifacts/` مُهمَل أصلاً)؛ يُذكر في `docs/UNITY-DEV<N>.md` فقط:
الأجهزة، النتيجة (Passed/Failed)، رابط النتيجة في console.firebase.google.com، وملخص أخطاء `logcat`
(ابحث عن `E Unity`, `FATAL`, `Crash`, `GLES`, `Vulkan`, `OutOfMemory`).

## ما الذي يُعدّ نجاحاً
- الحالة `Passed` على الجهازين، بلا crash/ANR.
- الفيديو يُظهر: القائمة الرئيسية ترسم صحيحاً (لا شاشة فارغة/سوداء)، الدخول للمباراة، الـHUD وأزرار اللمس.
- `logcat` بلا `E Unity` متكررة. أي خطأ يُسجَّل في `Known gaps` ويُصلَح **أولاً** في الإصدار التالي (قاعدة 6 في `docs/ROADMAP.md`).

## Game Loop (من dev.16)
Robo يضغط عشوائياً ولا يلعب. من dev.16 يدعم التطبيق intent
`com.google.intent.action.TEST_LOOP`: يدخل مباراة تلقائياً، يجرّب كل سلاح، يلتقط لقطات، ويكتب
النتيجة إلى الملف الذي يمرّره Test Lab. يُشغَّل بـ `tools/ftl_robo.sh <apk> dev<N> game-loop`.

## فحص الـAPK قبل الرفع
`unzip -tq`, `aapt dump badging` (versionCode = N+1), `apksigner verify --print-certs`, `sha256sum`.
الرفع 405 MB ويستغرق مع التشغيل 10–15 دقيقة.

## الأثر الخام
فيديوهات وتقارير كل تشغيل محفوظة في `docs/testlab/dev<N>/` — انظر `docs/testlab/README.md`.

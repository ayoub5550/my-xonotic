# البناء المحلي

## المتطلبات

- Unity **2022.3.62f3** مع رخصة صالحة مفعّلة محليًا.
- Android Build Support وAndroid SDK/NDK وOpenJDK المتوافقة معه.
- Python **3.10+**، وPillow عند تحويل DDS، وMono لاختبارات C# المستقلة.
- الإعداد السابق استخدم NDK r23b وOpenJDK 11 وAndroid platform 36.
  لا تفترض وجودها على جهاز جديد، ولا تعتبر تنزيلها إثباتًا لنجاح البناء.

المشروع يبنى محليًا فقط، دون Unity Cloud Build أو GitHub Actions.
لا تنقل ملفات الرخصة أو كلمات المرور أو سجلات الحساب أو مفاتيح التوقيع إلى Git.
تسجيل الدخول إلى موقع Unity لا يثبت تفعيل المحرر على جهاز البناء.
استخدم Unity Hub للتفعيل المعتاد؛ لا تتجاوز الترخيص أو إقرار أهلية الحساب.

## استعادة الحالة الحالية

الفرع `main` تعريفي فقط. مصدر نقطة البداية `unity-v0.1.0-dev.2` موجود
على `feat/unity-original-map`؛ متابعة الإصلاحات على فرع منفصل
`feat/unity-android-continuation`. اقرأ `AGENTS.md` قبل التعديل.
النتائج القديمة ليست نتائج اختبارات أُعيد تشغيلها على جهازك.

## فحوص لا تحتاج Unity

```sh
python3 tools/content/pk3_tool.py fixture --out-dir tests/fixtures/generated
python3 -m unittest discover -s tests/python -p 'test_*.py' -v
python3 tools/asset_meta.py
python3 tools/content/verify_resources.py
bash tests/run_all.sh mono mcs \
  ThirdParty/Xonotic/maps-pk3/maps/_hudsetup.bsp \
  ThirdParty/Xonotic/maps-pk3/maps/boil.bsp
```

فحوص Python وقراءة الملفات لا تثبت عرض الخامات أو لعب Android.
اختبارات تحويل DDS تتطلب Pillow؛ راقب الاختبارات المتخطاة.

## إعداد موارد الخريطة الأصلية

الملفات الموجودة في `ThirdParty/Xonotic` مجموعة محدودة وليست كل اللعبة.
يمكن تجهيز بيانات إصدار Xonotic الرسمي **0.8.6** محليًا، مع التحقق من
checksum المنشور وحماية استخراج ZIP/PK3 من تجاوز المسارات والتضخم.
لا تنفّذ برامج أو شيفرة مضمنة في حزم الموارد.
المصادر الأصلية والتراخيص تبقى مستقلة عن مشتقات Unity.

```sh
# <extracted-data> هو جذر محتوى حزمة data، وليس جذر ملف ZIP الخارجي.
python3 tools/content/prepare_unity_textures.py <extracted-data> \
  --texture models/weapons/laser
export XONOTIC_CONTENT_ROOTS="<decoded>:<extracted-maps>:<extracted-data>:ThirdParty/Xonotic/maps-pk3"
export XONOTIC_BSP="<extracted-maps>/maps/boil.bsp"
export XONOTIC_INCLUDE_EXTERNAL=1
```

`--texture` قابل للتكرار لتحويل صور DDS المطلوبة فقط؛ تحفظ الأداة أصل كل
صورة وSHA256 للمدخل والمشتق في سجل التحويل. لا يُعاد ترخيص الأصل.
وجود جميع الملفات محليًا لا يعني أن المستورد يدعم كل خامة أو نموذج أو
أن APK يضم كل الخرائط. أوضاع اللعب والشبكة وبقية أنظمة اللعبة عمل مستقل.

## تشغيل Unity والبناء

```sh
export UNITY_EDITOR="<local-editor-executable>"
export XONOTIC_REVISION="<exact-source-commit>"
python3 tools/local_unity.py compile
python3 tools/local_unity.py configure
python3 tools/local_unity.py test
python3 tools/local_unity.py original-playtest
python3 tools/local_unity.py android --timeout 3600
```

بدون `XONOTIC_INCLUDE_EXTERNAL=1` يبني أمر Android ساحة التطوير المصطنعة.
عند تفعيله يستورد مسار `XONOTIC_BSP` ويولّد موارد الخريطة الأصلية قبل
البناء؛ المخرج الحالي اسمه `my-xonotic-unity-boil.apk`، وليس لعبة كاملة.

- `test` اختبار Editor؛ `playtest` اختبار الساحة المصطنعة؛
  `original-playtest` اختبار الخريطة الأصلية.
- الاختبارات بدون `--graphics` لا تثبت صحة العرض. الاختبار الرسومي يحتاج
  شاشة أو Xvfb ودعم OpenGL مناسبًا.
- أداة التشغيل تمنع تشغيل Unity مرتين على المشروع وتقيد المهلة.
- `android` و`linux` لا يعتبران الخروج برمز صفر نجاحًا وحده: يلزم إيصال
  جديد يطابق معرّف التشغيل والهدف واسم الملف وحجمه وSHA256.
- تحتفظ الأداة بملف البناء السابق إن فشل تشغيل جديد، لكنها تزيل الإيصال
  القديم قبل البدء؛ لا تشارك الملف القديم على أنه بناء جديد.
- إيصال SHA256 يثبت تطابق الملف، لا صحة اللعب ولا توقيع APK. تحقق من
  التوقيع بأداة `apksigner` ثم اختبر التثبيت والعرض واللمس على هاتف.

## إعداد Android الحالي وحدود الإصدار

`com.ayoub.myxonotic`، ARM64/IL2CPP، اتجاه أفقي، OpenGLES3،
min API 26 وtarget API 36. التوقيع تطويري وليس إصدار متجر جاهزًا.
أي تغيير في versionCode أو keystore يجب توثيقه.

الخريطة تستعمل قواعد قتال وحركة تقريبية. ليست كل أسلحة Xonotic أو
شخصياتها أو أنيميشنها أو ذكائها الاصطناعي أو أوضاعها أو شبكتها مكتملة.
مراجعة تطابق المصادر والتراخيص وتوافق التوزيع مع Unity ما زالت منفصلة.

## قيود البيئة

افحص `nproc` وRAM والمساحة وتوافق النظام على **الجهاز الحالي**. لا تنقل
أرقام الجهاز السابق إلى تقرير جديد.
وثّق `my-librequake` حلولًا خاصة لـgVisor: تشغيل ShaderCompiler عبر
qemu عند ثبوت خطأ FS/GS، وshim لجدولة FMOD عند ثبوت خطئه.
ليست متطلبات عادية ولا وسائل لتجاوز ترخيص Unity. لا تطبقها قبل التحقق
من الخطأ الحالي. شغّل نسخة Unity واحدة فقط.

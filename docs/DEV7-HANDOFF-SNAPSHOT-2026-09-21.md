# نقطة استئناف dev.7 — لقطة 2026-09-21، الساعة 20:34 UTC

**الحالة: تنفيذ جارٍ، لا APK dev.7 مُثبت.** هذا الملف يحفظ نقطة توقف رُوجعت
مباشرة في ملفات المشروع وتقارير التحقق، وليس إعلان اكتمال أو إعادة تشغيل الاختبارات.
قد تصل إصلاحات ونتائج لاحقة؛ افحص أحدث commit/evidence قبل اعتماد هذه اللقطة.

## ماذا وُجد؟

- أحدث نجاح Android في `Builds/build-receipt.json`: **dev.6** عند
  `2026-09-21T17:58:22Z`. APK الفعلي يحمل
  `versionName=0.1.0-dev.6` و`versionCode=7`؛ 400,459,348 بايت،
  SHA256 `ef140281d7639840c9b3d75b9631c1b28fad5784bdd73e32f56ccd6905ee7be5`.
  أعيد فحص manifest وhash أثناء هذا التسليم، وليس اسم الملف فقط.
- ملفات تطوير جديدة موجودة في شجرة العمل المحلية، و`VERSION` يشير إلى dev.7.
  ليست هذه الملفات ضمن baseline `7a6ec966` الذي يستند إليه PR التوثيق.
  لا تُسقطها أو تستبدلها بالـbaseline أثناء الدمج.
- لم يوجد dev.7 في قائمة GitHub Releases التي فُحصت، ولم يُعثر على
  إيصال نجاح أو APK له في مجلدات Builds التي فُحصت. هذه نتيجة نطاق الفحص،
  لا ادعاء استحالة وجود نسخة أخرى في مكان لم يُفحص.

## خريطة العمل الموجود — افحصه قبل البدء من الصفر

المسارات التالية تحت `Assets/MyXonotic/` إلا الأدوات المذكورة صراحة،
وتصف **شجرة dev.7 الجارية** لا ملفات مضمونة في checkout هذا الدليل:

| المجال | ملفات بارزة | حالة قبولها في هذه اللقطة |
|---|---|---|
| تحريك الشخصيات | `Runtime/Gameplay/CharacterAnimation.cs`, `CharacterAnimationData.cs`، تغييرات IQM importer، `Editor/CharacterAnimationTests.cs` | فشل اختبار Unity BakeMesh الفعلي؛ لا يُكتفى بتغيّر joint matrices |
| الأبواب والمنصات/الدوران | `Runtime/Gameplay/MapMover.cs`, `MapMoverActivator.cs`, `MapMoverRider.cs`, `Editor/Import/BspMoverImporter.cs`, `Editor/MoverRegressionTests.cs` | مجموعة component/data نجحت، لا إثبات مرور كل خريطة أو هاتف |
| الترسانة والذخيرة والقوى | تعديلات أنظمة الأسلحة/الالتقاط، `Runtime/Gameplay/PowerupState.cs`, `Editor/ArsenalRegressionTests.cs` | مجموعة component/data نجحت، لا ادعاء مطابقة قواعد upstream |
| نماذج OBJ | `Editor/Import/ObjMapModelReader.cs`, `Editor/ObjMapModelTests.cs` | مجموعة parser/props نجحت، لا مراجعة مرئية |
| الواجهة | تعديلات HUD/القائمة/اللمس، `Editor/UiRegressionTests.cs` | geometry/runtime construction نجحت؛ لمس Android منفصل |
| الاختبارات المجمعة | `Editor/Dev7RegressionTests.cs`, `Editor/Dev7Playtest.cs`، إضافات `tools/local_unity.py` | `systems-test` فشل؛ لا نجاح `dev7-playtest` أو APK مُثبت بهذه اللقطة |
| الموارد | `tools/content/dev7_resource_audit.py`, `prepare_full_game_textures.py`, `full_game_textures.json` | ملفات جديدة موجودة؛ لا تُنقل أوامرها إلى dev.6 دون نقل source/tests ومراجعتها |

## آخر سلسلة تحقق قرئت

المصدر: `Artifacts/dev7-retry-run.log` و`Artifacts/dev7-system-tests.json`
في شجرة العمل؛ حُفظ التقرير بعد تنقيح المسارات الداخلية في
[لقطة النتيجة](dev7-system-tests-snapshot-2026-09-21.json).

1. **20:32:35 UTC:** بدء compile.
2. **20:34:08 UTC:** compile خرج `0` بعد **92.4 ثانية**.
3. **20:34:08 UTC:** بدء systems-test.
4. نتيجة **20:34:48 UTC**: `passed=false`، أربع مجموعات ناجحة من خمس:
   - UI geometry and runtime construction — نجحت.
   - Original OBJ props and parser guards — نجحت.
   - Core arsenal, ammo and pickup behavior — نجحت.
   - Movers and rider mechanics — نجحت.
   - Original character skeletal data and animation — **فشلت**.
5. **20:34:56 UTC:** systems-test خرج `1` بعد **48.7 ثانية**؛ توقفت السلسلة.

الخطأ المحدد:
`erebus: SkinnedMeshRenderer.BakeMesh output matches the predicted per-frame skin within tolerance (worst=42.74235)`.
المسار في stack: `CheckBakeMeshMatchesPrediction` ← `RunImportAndAttachChecks`
← `CharacterAnimationTests.Run` ← `Dev7RegressionTests.RunModule`.
كما سجل الاختبار:
`Destroy may not be called from edit mode! Use DestroyImmediate instead.`

**لا تشخّص السبب من الرسالة وحدها:** قد يكون تحويل/scale/root/bind pose/توقع
اختبار أو تنفيذ التحريك؛ قارن بيانات IQM وSkinFrame ونتيجة Unity وأصل القياس.
لا توسّع tolerance ولا تتجاوز الاختبار لمجرد إنتاج APK.
`DestroyImmediate` حل يخص مسار Editor بعد مراجعة الملكية، لا استبدالًا عامًا
لـ`Destroy` في runtime.

## تعليمات الاستئناف الآمن

1. حدّد من يملك Unity والتغييرات الجارية؛ لا تشغّل Editor ثانيًا أو تمسح قفلًا حيًا.
2. اجمع أحدث source/diff ونتائج التحقق من المطوّر الحالي؛ احفظ العمل في commit
   فرعي أو WIP واضح دون أسرار، بعد توقف التغييرات المتزامنة.
3. أصلح فرق BakeMesh وخطأ دورة حياة Editor مع اختبارات تحفظ المعنى.
   النجاح القديم في ترجمة C# أو Python لا يغطي هذه المشكلة.
4. في الشجرة التي **تحتوي أوامر dev.7 فعلًا**: compile ثم systems-test
   وdev7-playtest ثم content/all-maps، بتقارير حديثة. افحص `--help` وMETHODS أولًا.
5. بعد اجتياز البوابات، ابنِ Android محليًا برقم جديد وحدد المصدر والإيصال
   وmanifest/hash/signature. APK dev.6 القديم قد يبقى مكانه بعد محاولة فاشلة.
6. حدّث `AGENTS.md` وCHANGELOG ووثيقة dev.7 بنتائجها الحقيقية؛ انقل هذه
   اللقطة إلى السياق التاريخي، لا تمحُ دليل الفشل أو تقدّمه كالحالة الأحدث.
7. اختبار هاتف/لمس/صورة/أداء شرط منفصل؛ لم يتم لهذه اللقطة.

راجع [دليل التسليم](../AGENTS.md) لإعداد الأدوات والموارد والبناء
والنشر وحماية أسرار الترخيص والتوقيع.

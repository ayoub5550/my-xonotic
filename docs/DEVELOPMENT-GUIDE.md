# ورقة التطوير — كيف نطوّر my-xonotic (منهجية dev.N)

هذه الورقة تشرح **الطريقة** التي بُنيت بها إصدارات dev.4 → dev.11، حتى يتبع
أي مطوّر (إنسان أو وكيل) النسق نفسه. اقرأها مع `AGENTS.md` (نقاط التوقف)
و`docs/LOCAL_BUILD.md` (إعداد البيئة) و`docs/RELEASING.md` (سياسة الإصدار).

## 1. المبدأ

- **المصدر الأصلي هو المرجع.** كل شيء يُشتق من محتوى Xonotic 0.8.6 الأصلي
  (`ExternalContent/`): الخرائط BSP، النماذج IQM/MD3/DPM، الأصوات، الخامات،
  ملفات `.framegroups` و`.shader`. لا نرسم أصولًا بديلة ولا نخمّن أرقامًا؛
  عند غياب ملف نُسقط الميزة بأمان (fallback) ونكتب ذلك في الوثيقة.
- **لا GPU في بيئة البناء.** كل التحقق هندسي/منطقي (اختبارات Editor وPlay Mode
  headless). لا نزعم تحققًا مرئيًا؛ نطلب لقطة شاشة من جهاز حقيقي.
- **كل إصدار = فرع + وثيقة + إيصال + APK.** لا كود بدون هذه الأربعة.

## 2. دورة إصدار dev.N (من البداية للنهاية)

1. **اقرأ** آخر `docs/UNITY-DEV{N-1}.md` وقسم checkpoint في `AGENTS.md`؛
   حدّد «ما لا يزال غائبًا» — هذا هو نطاق dev.N.
2. **افرع** من فرع الإصدار السابق:
   `feat/unity-dev{N}-<topic>` فوق `feat/unity-dev{N-1}-…` (سلسلة فروع مكدّسة؛
   الـPR يستهدف الفرع السابق، لا `main`).
3. **حلّل المحتوى الأصلي أولًا** بسكربت Python صغير (بايتات الرأس، عدد
   المفاصل/الإطارات، أسماء الـshaders) قبل كتابة C#. مثال: dev.11 اكتشف بهذه
   الطريقة أن `h_*.iqm` بعضها IQM v2، وواحد IQM v1، وخمسة DPM تحت اسم `.iqm`.
4. **اكتب المستورد في Editor** (`Assets/MyXonotic/Editor/Import/`) الذي يولّد
   أصولًا إلى `Assets/MyXonotic/Resources/` أو `Generated/` (مُتجاهلة في git).
   المستورد يجب أن: يكون حتميًا (نفس المسار → نفس GUID)، يكتب manifest/notes،
   ويحذف الأصل القديم عند الفشل حتى يعمل fallback وقت التشغيل.
5. **اكتب منطق التشغيل** في `Assets/MyXonotic/Runtime/Gameplay/` بدون اعتماد
   على المحرر؛ يقرأ الأصول عبر `Resources.Load` ويتحمّل غيابها.
6. **أضف اختبارًا لكل ادّعاء**:
   - `LocalTests.cs` (Editor، حتمي، بلا مشهد لعب) لهندسة الأصول: أعداد،
     أسماء، صناديق حدود معقولة.
   - `LocalPlaytest.cs` / `GameplayPlaytest` (Play Mode) لسلوك اللعب.
   - عدّاد الاختبارات المتوقع يُذكر في الوثيقة (مثل «Editor checks 237 → …»).
7. **شغّل البوابات بالترتيب**، كلٌّ منها يوقف السلسلة عند الفشل:
   `compile → weapons/prepare-maps (إن لزم) → test → playtest → gameplay-playtest → android`.
   أداة التشغيل: `python3 tools/local_unity.py <task>` (أو غلاف
   `pipeline.sh` مع `TASKS=… DO_ANDROID=1 VC=<versionCode>`). السجلات في
   `Artifacts/<task>.log`؛ ابحث عن `error CS` و`TEST FAIL`.
   **بوابة هندسة المنظور الأول** (منذ dev.12): اختبار `WeaponPlacement` يكتب
   `Artifacts/weapons/<Type>.obj`، ثم `python3 tools/weapon_snapshot.py
   Artifacts/weapons out.png` يرسم إطارًا سلكيًا بنفس FOV/near/نسبة الجهاز
   (2400×1080). هذا يُعيد إنتاج ما يراه الجهاز هندسيًا بلا GPU — قارنه بلقطة
   الجهاز قبل «إصلاح» أي موضع سلاح.
8. **وثّق**: `docs/UNITY-DEV{N}.md` (عربي: الهدف، الأسباب/الاكتشافات، ما تغيّر
   ملفًا بملف، التحقق بجدول، APK بجدول، الدروس)، سطر في `CHANGELOG.md`،
   قسم checkpoint جديد **أعلى** `AGENTS.md`، و`VERSION`.
9. **الإصدار**: commit مستهدف (المصدر + الوثائق فقط)، push، PR إلى الفرع
   السابق، GitHub prerelease `unity-v0.1.0-dev.{N}` مع APK + إيصال JSON
   (`docs/unity-dev{N}-build-<date>.json` الذي يكتبه البناء). versionCode
   يزيد بواحد كل إصدار (dev.10 = 11، dev.11 = 12).
10. **أبلغ** بالحقائق فقط: الحجم، SHA256، ما نجح من بوابات، وما لم يُتحقق
    منه (المرئي/الجهاز). اطلب لقطات شاشة.

## 3. قواعد الكود

- **الإحداثيات:** Quake (x يمين، y أمام، z أعلى، وحدة = 1/32 م) → Unity:
  الموضع `(x, z, y)/32`، الدوران `(x,y,z,w) → (−x,−z,−y,w)` (مُثبت إلى 0 مم
  في dev.9). نماذج العرض `h_/v_` تحتاج دورانًا إضافيًا Euler(0,−90,0) في
  `WeaponView` لأنها تنظر نحو +X.
- **الهياكل:** `CharacterRig` (ScriptableObject: أسماء المفاصل، الآباء، bind
  محلي، مقاطع، Poses مسطّحة `frame*joints*10`) يُحرَّك بـ `CharacterAnimator`.
  نفس الآلية تُستخدم للشخصيات (dev.9) والأسلحة (dev.11) — لا تكتب مُحرّكًا
  ثانيًا.
- **كل واجهة تُبنى بالكود تمرّ باختبار هندسة** (مستطيلات موجبة، لا `Mask`
  stencil فوق صورة شفافة) — درس dev.10.
- **لا تلتزم (commit)** `ProjectSettings/`، `Assets/**/Generated/`،
  `Resources/Weapons/`، `Builds/`، `ExternalContent/`، أي APK، أو أي بيانات
  اعتماد. `.meta` لملفات المصدر الجديدة تُلتزم.
- **الأسماء:** ملف واحد لكل مفهوم (`DpmDocument`, `WeaponRigImporter`,
  `WeaponRigInfo`)، تعليق رأس يشرح تنسيق الملف الأصلي وأرقام offsets.

## 4. تنسيقات المحتوى الأصلي المعروفة

| تنسيق | أين | القارئ | ملاحظات |
|---|---|---|---|
| BSP (Q3, IBSP 46) | `maps/*.bsp` | `Content/Bsp/*` | lightmaps، patches، كيانات |
| MD3 | `v_*.md3`, items | `Md3WeaponModelBuilder` | ثابت، خامات من `.shader` |
| IQM v2 | شخصيات، أغلب `h_*` | `IqmSkinnedDocument` | مقاطع من `.framegroups` |
| IQM v1 | `h_fireball.iqm` | نفسه (`v1` flag) | quaternion 3 مكونات، 9 قنوات، joint 44 B، pose 80 B |
| DPM («DARKPLACESMODEL», big-endian) | `h_electro/crylink/gl/hagar/rl` | `DpmDocument` | تحت امتداد `.iqm`! تحقق من الـmagic لا الامتداد |

## 5. البيئة (ملخص؛ التفاصيل في LOCAL_BUILD.md)

- Unity 2022.3.62f3 headless: `UNITY_EDITOR=<wrapper>`، متغيرات
  `XONOTIC_CONTENT_ROOTS` و`XONOTIC_MAPS_ROOT`.
- مثيل Unity واحد فقط في كل مرة؛ عند التوقّف احذف `Temp/UnityLockfile`
  و`Artifacts/local-unity.lock` ثم أعد التشغيل. أول ترجمة بعد تغيير كبير قد
  تأخذ 6–12 دقيقة (ILPP).
- بناء APK كامل (29 خريطة، ~445 MB) يأخذ 40–60 دقيقة على 17 نواة.

## 6. ما لا يزال غائبًا بعد dev.12 (مرشّحات dev.13+)

شبكة/لعب جماعي، waypoints للبوتات، سلاسل trigger→target، اختبار أداء على
جهاز، HUD مطابق للأصل أكثر، توقيع إنتاج للمتجر، وصفحة إعدادات لمس (حجم
الأزرار/الحساسية)، والتحقق من ظهور البوتات على الجهاز (لم تظهر في فيديوهات
dev.11).

## 7. قاعدة الخامات (منذ dev.12)

كل خامة مولَّدة تمرّ عبر `ImportedTexturePolicy.Finalize` (mipmaps + ETC2)
قبل `PersistAsset`. خامة 2048² RGBA32 بلا mips = 16 MB VRAM؛ 14 سلاحًا ×
عدة خامات + نماذج الالتقاط استهلكت ذاكرة GPU الهاتف فظهر Vortex أبيض
وأشكال داكنة في dev.11. لا تُكتب خامة خامًا مطلقًا.

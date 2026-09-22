# dev.11 — تحريك الأسلحة الأصلي، فلاش الفوهة، شظايا الانفجار واهتزاز الكاميرا (2026-09-22)

الفرع `feat/unity-dev11-weapon-anim` (فوق `feat/unity-dev10-menu-fix`). الهدف:
تقريب الإحساس من Xonotic الأصلي في المنظور الأول — الأسلحة كانت منذ dev.8
نماذج `v_*` **ثابتة** بلا حركة عند الإطلاق ولا فوهة ولا ردّة فعل بيئية.

## الاكتشافات في المحتوى الأصلي

- نموذج المنظور الأول الصحيح في Xonotic هو `models/weapons/h_<name>.iqm`
  (اليدان + الهيكل المتحرك)، و`v_<name>.md3` يُعلَّق على مفصل `weapon` فيه.
  dev.8 استخدم `v_` وحده، لذلك لم يكن هناك تحريك أصلًا.
- 9 نماذج `h_` هي IQM v2 هيكل فقط (4 مفاصل `origin→weapon→shot,shell`، سطح
  `nodraw` بأربع رؤوس، 257–271 إطارًا، مقاطع fire/fire2/idle/reload):
  laser, shotgun, uzi, nex, campingrifle, minelayer, arc, hookgun (+ fireball).
- `h_fireball.iqm` هو **IQM v1** (quaternion بثلاث مكونات وw = −√(1−|xyz|²)،
  9 قنوات pose، joint 44 بايت، pose 80 بايت). أُضيف دعمه إلى
  `IqmSkinnedDocument`.
- 5 نماذج تحت امتداد `.iqm` هي في الحقيقة **DarkPlaces DPM** (magic
  `DARKPLACESMODEL`، big-endian): electro (16 عظمة، 7,132 رأسًا)، crylink،
  gl (سطحان)، hagar (6 عظام)، rl (11 عظمة). لها شبكة يدين خاصة بها وعظام
  `tag_handle/tag_shot/tag_shell`. المقاطع من `.framegroups` بنفس الترتيب.
  تحقّق: إحداثيات الرؤوس = Σ weight·(M_world(bone, frame0)·origin) تعطي حدودًا
  معقولة (electro x −5..60، z −37..−11 وحدة).

## ما تغيّر

| الملف | التغيير |
|---|---|
| `Editor/Import/DpmDocument.cs` (جديد) | قارئ DPM كامل: عظام، سطوح، إطارات، مصفوفات 3×4 نسبية للأب → TRS، تجليد الرؤوس (`BoneWeight`)، bindposes |
| `Editor/Import/WeaponRigImporter.cs` (جديد) | لكل `WeaponType` يقرأ `h_<name>` (IQM v1/v2 أو DPM) → `Resources/Weapons/<Name>WeaponRig.asset` + `<Name>Hands_Rig.asset` (+ `<Name>Hands_Skinned.asset` للـDPM بخامات من `.shader`)؛ عند الفشل يحذف الأصل القديم فيبقى الشكل الثابت |
| `Editor/Import/IqmCharacterImporter.cs` | دعم IQM v1؛ فتح `FrameGroup/ReadFrameGroups/WriteTrs/ToUnity*/Persist` للاستعمال المشترك |
| `Editor/Import/IqmWeaponImporter.cs` | يستدعي `WeaponRigImporter.ImportAll` ويكتب ملاحظة `Rig IQM/DPM …` لكل سلاح في `weapon-manifest.json` |
| `Runtime/Gameplay/WeaponRigInfo.cs` (جديد) | ScriptableObject: التنسيق، الهيكل، الشبكة المجلَّدة، الخامات، مفاصل weapon/shot/handle |
| `Runtime/Gameplay/CharacterAnimator.cs` | يعمل بلا شبكة (هيكل فقط)، `IsFinished`، `FindBone` |
| `Runtime/Gameplay/WeaponView.cs` | يبني `Rig_<Name>` لكل سلاح ويشغّل `idle`؛ IQM: يعلّق `v_` على مفصل `weapon`؛ DPM: يعرض شبكة اليدين الأصلية ويخفي `v_`؛ `Kick(alt)` يشغّل `fire/fire2` ثم يعود إلى `idle`؛ فلاش فوهة عند مفصل `shot`؛ الارتداد اليدوي يُخفَّض إلى 45٪ عند وجود تحريك |
| `Runtime/Gameplay/WeaponController.cs` | يمرّر الوضع الثانوي إلى `View.Kick(alt)` / `PlayFireAnimation(alt)` (مسار الخطاف) |
| `Runtime/Gameplay/ImpactEffects.cs` | `MuzzleFlash`، قرص `Shockwave`، 10 شظايا `DebrisChip` للانفجارات (3 للإصابات الصغيرة)، `RequestShake` ضمن 14 م، `LiveCount` |
| `Runtime/Gameplay/Player.cs` | اهتزاز كاميرا (نبضة pitch تتلاشى `exp(−9·dt)`) من `ImpactEffects.ConsumeShake()` |
| `Editor/LocalTests.cs` | فحص `WeaponRigs`: 14 rig، مقاطع fire (one-shot) + idle (loop)، جدول Poses كامل، IQM بمفصل `weapon`، DPM بشبكة/أوزان/bindposes/خامات، مفصل الفوهة داخل صندوق منظور معقول بعد دوران −90° |
| `Editor/LocalPlaytest.cs` | بعد `TryFire`: المقطع الحالي `fire`، مفصل الفوهة موجود، فلاش حيّ |
| `docs/DEVELOPMENT-GUIDE.md` (جديد) | ورقة التطوير: منهجية dev.N، القواعد، التنسيقات، البوابات |

## التحقق

| البوابة | النتيجة |
|---|---|
| compile | نجح (0 أخطاء) |
| weapons (توليد الأصول) | 14/14 rig: 9 IQM v2 + 1 IQM v1 (Fireball) + 5 DPM |
| Editor tests | الجولة الأولى كشفت Fireball ناقصًا (13/14) → إصلاح IQM v1. **لم تُعَد بعد الإصلاح** بطلب صريح من المالك («بدون اختبارات، أطلق dev11») — تُعاد في dev.12 قبل أي عمل جديد |
| Play Mode | لم يُشغَّل في هذا الإصدار (نفس السبب) |
| تحقق مرئي | غير ممكن في البيئة (لا GPU) — ينتظر لقطات الجهاز |

## ملف APK (versionCode 12)

انظر `CHANGELOG.md` والإيصال `docs/unity-dev11-build-2026-09-22.json` (الحجم،
SHA256). الحزمة `com.ayoub.myxonotic`، versionName `0.1.0-dev.11`، arm64-v8a،
توقيع debug بنفس شهادة dev.8–dev.10 (تحديث مباشر؛ dev.6 وما قبله يُزال أولًا).

## ما تأجّل إلى dev.12

- إعادة تشغيل Editor/Play Mode بعد إصلاح IQM v1 وتثبيت العدّاد الجديد.
- تلميع HUD نحو الأصل (شريط صحة/درع بنمط Xonotic، حالة ضغط لأزرار اللمس).
- مقطع `reload` وصوت الطلقات الفارغة `shell`.
- التحقق من اتجاه/موضع شبكة اليدين DPM على جهاز حقيقي؛ إن ظهرت منحرفة فعدّل
  `WeaponView.RigRestLocalPosition` أو أعد تفعيل `v_` لذلك السلاح.

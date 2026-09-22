# dev.12 — إصلاحات من أول فيديوهات الجهاز: الخامات، الفراغ، HUD، سجل الأخطاء (2026-09-22)

الفرع `feat/unity-dev12-device-fixes` (فوق `feat/unity-dev11-weapon-anim`).
الهدف: معالجة ما ظهر في أول تسجيلَي شاشة من جهاز حقيقي (dev.11، هاتف
2400×1080) وإعادة تشغيل كل البوابات التي تخطّاها dev.11.

## ما ظهر في الفيديوهات

| الملاحظة | التشخيص |
|---|---|
| Vortex يُرسم **أبيض** بلا خامة؛ Mortar/Devastator أشكال داكنة كبيرة أسفل الشاشة | خامات الأسلحة كانت تُحفَظ خامًا `RGBA32` 2048² بلا mipmaps (33.5 MB لكل ملف، ~16 MB VRAM لكل خامة) و14 سلاحًا تُنشأ معًا + 246 خامة نماذج التقاط بنفس الطريقة (603 MB). ذاكرة GPU الهاتف تنفد فيسقط الرسم إلى أبيض/أسود |
| الأسلحة DPM (Mortar/Devastator) تبدو «قريبة جدًا» | **ليست علة.** أداة `tools/weapon_snapshot.py` تُعيد إنتاج شكل الجهاز هندسيًا من OBJ المُصدَّر من Editor؛ الموضع مطابق لـXonotic الأصلي (`CL_WeaponEntity_SetModel` يضع `h_` في أصل المنظور، `cl_gunoffset` 0، FOV عمودي ≈84° مقابل 85° عندنا). لا تُعدَّل |
| اللاعب سقط من حافة الخريطة وبقي يهبط ثوانيًا طويلة وصحته تنقص 10 كل 0.5 ث | حد الفراغ ثابت `y < −200` بعيد جدًا عن أرض الخرائط؛ و`trigger_hurt` بلا `dmg` كان يستعمل 10 بينما الافتراضي في Xonotic 1000 (قاتل) |
| صف HUD السفلي (FRAGS/DEATHS، اسم السلاح) مقطوع تحت الشاشة؛ شريط الأسلحة يغطي بانر النسخة أعلى | `Hud.CreateText` استعمل pivot مركزيًا مع anchors على الحواف فيتزحزح نصف الارتفاع خارج الشاشة؛ البانر كان في المنتصف أعلى |
| «آلاف الأخطاء» بلا وسيلة لقراءتها من الجهاز | لا يوجد سجل أخطاء على الجهاز |

## ما تغيّر

| الملف | التغيير |
|---|---|
| `Editor/Import/Md3WeaponModelBuilder.cs` | خامات الأسلحة تمرّ عبر `ImportedTexturePolicy.Finalize` (mipmaps + ETC2) قبل الحفظ؛ نفس المسارات → نفس GUIDs. 33.5 MB → 11.2 MB لكل ملف أصل |
| `Editor/Import/BspMapModelImporter.cs` | نفس السياسة لخامات نماذج الالتقاط: `Generated/MapModels` 603 MB → 199 MB |
| `Runtime/Gameplay/ArenaBootstrap.cs` | `ComputeVoidKillHeight()` قبل `IsReady`: `VoidKillY` = أدنى مصادم غير trigger − 12 م (احتياط −200) |
| `Runtime/Gameplay/Player.cs`, `Bot.cs` | تحت `VoidKillY` → `TakeDamage(10000)` (موت فوري بدل الهبوط اللانهائي) |
| `Runtime/Gameplay/MapTrigger.cs` | `DefaultHurtDamagePerTick = 1000` (كما في Xonotic)؛ قيمة قديمة ≤10 تُعامل كقاتلة |
| `Runtime/Gameplay/Hud.cs` | `CreateText`: pivot = anchorMin (لا قطع عند الحواف)؛ بانر النسخة أعلى يسار صغير شبه شفاف؛ شاشة PAUSED تعرض ملخص `RuntimeErrorLog` (عدد الأخطاء، آخر خطأ، مسار الملف) |
| `Runtime/Debugging/RuntimeErrorLog.cs` (جديد) | يلتقط `Application.logMessageReceived` ويكتب التحذيرات/الأخطاء (+6 أسطر stack) إلى `{persistentDataPath}/my-xonotic-errors.log` مع رأس (النسخة، الجهاز، GPU، الدقة)؛ يقصّ فوق 2 MB |
| `Editor/LocalTests.cs` | `WeaponPlacement` يكتب `Artifacts/weapons/<Type>.obj` (شبكة + عظام في إطار WeaponView)؛ اختبار جديد `HudGeometry`: كل نص داخل الـcanvas وبمستطيل موجب |
| `tools/weapon_snapshot.py` (جديد) | راسم numpy/PIL بنفس FOV 85° عمودي، near 0.05، نسبة 2400/1080، قصّ near-plane؛ لمقارنة الهندسة بلقطة الجهاز بلا GPU |
| `docs/DEVELOPMENT-GUIDE.md` | بوابة هندسة المنظور الأول + قاعدة الخامات |

## التحقق

| البوابة | النتيجة |
|---|---|
| compile | نجح (0 أخطاء) |
| weapons | 14/14 rig، خامات مضغوطة |
| prepare-maps | نجح (459 ث، نماذج الالتقاط مضغوطة) |
| Editor tests | **PASS 455** (كان 237 قبل dev.11؛ يشمل `WeaponRigs` 14/14 التي لم تُعَد في dev.11، `WeaponPlacement` 14/14، `HudGeometry`) |
| gameplay-playtest | نجح (exit 0) |
| هندسة الأسلحة | `weapon_snapshot.py` مقابل إطارات الفيديو: مطابقة لكل الأسلحة الـ14 |
| تحقق مرئي للخامات | غير ممكن في البيئة — ينتظر فيديو/لقطات جهاز dev.12 |

## ملف APK (versionCode 13)

انظر `CHANGELOG.md` والإيصال `docs/unity-dev12-build-2026-09-22.json`. الحزمة
`com.ayoub.myxonotic`، versionName `0.1.0-dev.12`، arm64-v8a، نفس شهادة
dev.8–dev.11 (تحديث مباشر). سجل الأخطاء على الجهاز:
`/storage/emulated/0/Android/data/com.ayoub.myxonotic/files/my-xonotic-errors.log`.

## ما تأجّل إلى dev.13

- البوتات لم تظهر في فيديوهات dev.11 — يحتاج تأكيد إعداد BOTS ولقطة.
- HUD بنمط Xonotic، مقطع `reload`، إعدادات اللمس، waypoints، الشبكة.
- إن استمر البياض/السواد في الخامات بعد dev.12: افحص `my-xonotic-errors.log`
  وتحقق من دعم ETC2 على الجهاز (كل أجهزة GLES3 تدعمه).

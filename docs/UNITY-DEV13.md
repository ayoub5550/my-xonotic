# dev.13 — البوتات، DevCapture، قائمة دخول جديدة، HUD بأيقونات الأصل، إعدادات اللمس (2026-09-23)

الفرع `feat/unity-dev13-bots-hud-menu` (فوق `feat/unity-dev12-device-fixes`).
versionCode **14**، versionName `0.1.0-dev.13`.

## الهدف

ورقة `docs/UNITY-DEV13-PLAN.md` بنودها 1 و3 و4 (+ جزء من 5) وطلب المالك
الصريح: «حسّن واجهة الدخول»، «ثبّت أداة تلتقط الأخطاء في اللعبة»، «اطّلع على
Xonotic الأصلية». البند 0 (ملف الأخطاء ولقطات dev.12 من الجهاز) **لم يصل**
قبل هذا الإصدار، لذا بُنيت أولًا الأداة التي تجعل إرساله تلقائيًا (DevCapture)
ثم عولجت فرضيات البوتات الثلاث معًا.

## البيئة (تحقق فعلي في هذه الجلسة، لا نقل من إصدار سابق)

- Unity 2022.3.62f3 (تنزيل رسمي `download.unity3d.com`، وحدة Android من
  بيان Unity Release API مع OpenJDK 11.0.14.1 وNDK r23b وSDK build-tools 34
  وplatforms 34/35/36) في `/work/unity/editor`. الرخصة Personal مفعّلة عبر
  `-username/-password` من `tools/local_unity.py` (لا تُكتب في أي ملف).
- gVisor (`4.19.0-gvisor`، 17 نواة، بلا GPU): طُبّقت وصفة `my-librequake`
  §9 — `UnityShaderCompiler` يعمل تحت `qemu-x86_64-static`، و`libschedfix.so`
  عبر `LD_PRELOAD` لتشغيل FMOD، ومكتبات GTK مستخرجة من حزم Debian بلا root.
- الموارد: `python3 tools/content/publish_resources.py restore` أنشأ
  `ExternalContent/{data,maps,music,decoded,worlddecoded,characters}` من
  `ThirdParty/Xonotic-0.8.6/` (12,999 ملفًا، 0 mismatches). لا تنزيل.

## ما تغيّر — ملفًا بملف

| الملف | التغيير |
|---|---|
| `Runtime/Debugging/DevCapture.cs` (جديد) | كائن دائم يعاين كل 5 ث: FPS وأسوأ إطار، ذاكرة، عدد البوتات الحية/المرئية (`Renderer.isVisible`) وأقربها، موضع اللاعب، `VoidKillY`، عدد spawns المصححة؛ يكتب العيّنة كـ `NOTE` في السجل. `BuildReport()` = رأس الجهاز + عدّادات + جدول الرسائل المتكررة + آخر 60 سطرًا (+ ذيل الملف 48 KB). `Share()` يفتح Android share sheet (`ACTION_SEND` نص) — يُرسل التقرير إلى Slack مباشرة؛ `Copy()` إلى الحافظة |
| `Runtime/Debugging/RuntimeErrorLog.cs` | رأس أغنى (GPU/RAM/ETC2/compute skinning)، حلقة `Recent` (60 سطرًا)، `Note()`، جدول تكرار الرسائل (`Repeats`) — بعد 20 تكرارًا يُعدّ فقط ولا يُكتب (كان «آلاف الأخطاء» يملأ الملف)، `ReadTail()`، التقاط `Application.lowMemory` |
| `Runtime/Gameplay/ArenaBootstrap.cs` | **`ValidateSpawns()`** (فرضية 3): كل spawn مستورد بلا أرض ضمن 2 م يُسحب إلى أول أرض تحته (≤ 40 م) أو يُحذف؛ `SnappedSpawns/DroppedSpawns`؛ `SpawnHasFloor()` مشترك مع اختبار Editor. سطر NOTE عند بداية كل ساحة (الخريطة، الوضع، البوتات المطلوبة فعلًا). أزرار SHARE LOG / COPY LOG في شاشة PAUSE |
| `Runtime/Gameplay/Bot.cs` | **الصيد** (فرضية «لا لقاء»): بعد 4 ث بلا رؤية عدو يتجه البوت نحو أقرب عدو حي بدل دوائر تجوّل عشوائية قطرها 12 م (كانت البوتات تُوضع في أبعد spawn عن اللاعب ثم تتجوّل عشوائيًا — على خريطة كاملة لا لقاء خلال دقيقتين). التعثّر > 1.5 ث → انحراف عشوائي 2.5 ث ثم صيد من جديد. `IsHunting` للتشخيص |
| `Runtime/Gameplay/Hud.cs` | لوحة سفلية بنمط luma (`hud_luma.cfg`: healtharmor 0.30–0.70 × 0.925، ammo فوقها): أيقونات `health/armor/ammo_*` الأصلية + أرقام كبيرة؛ سطر النتيجة/المؤقّت تحت البانر أعلى يسار؛ شريط الأسلحة يرسم أيقونات `weapon*` الأصلية (باهتة لغير المملوك، محمرّة عند نفاد الذخيرة) مع الرقم والذخيرة؛ سطر DevCapture (FPS/BOTS) تحت النتيجة؛ شاشة PAUSE تعرض آخر 7 أسطر من السجل + زرّي SHARE/COPY. يبقى الشريط أعلى الشاشة لأن أسفلها مشغول بالجويستيك وعنقود FIRE في الوضع الأفقي (الأصل يضعه على الحافة اليمنى — مشغولة عندنا أيضًا) |
| `Runtime/Gameplay/HudArt.cs` (جديد) | وصول وقت التشغيل إلى `Resources/Hud/<name>` مع cache وخرائط أسماء (سلاح → `weaponlaser`…، ذخيرة → `ammo_*`، وضع → `gametype_*`)؛ null دائمًا عند غياب الأصل → الرجوع للنص |
| `Runtime/Gameplay/TouchSettings.cs` (جديد) | PlayerPrefs: `ButtonScale` 0.8–1.4، `Sensitivity` 0.5–2.0، `InvertY`، `LeftHanded`؛ `OverrideForTest` |
| `Runtime/Gameplay/TouchLayout.cs` | كل دائرة وقطر الجويستيك × `Scale`؛ المراكز تتحرك للخارج مع الحجم وتُقصّ داخل safe area؛ مرآة يسارية (`MoveZone` يمينًا، العنقود يسارًا)؛ مستطيلا `ShareLog/CopyLog` |
| `Runtime/Gameplay/Player.cs` | حساسية اللمس × `TouchSettings.Sensitivity`، عكس Y |
| `Runtime/Menu/MainMenu.cs` | إعادة كتابة: ثلاث شاشات. **HOME**: خلفية `gfx/menu/luminos/background` الأصلية (مصغّرة 1280) + عنوان + PLAY/SETTINGS/QUIT بحجم إبهام (≥ 60 px مرجعي) + كارت خريطة مميّزة قابل للنقر + سطر الترخيص. **PLAY**: DM/TDM/CTF بأيقونات `gametype_*` الأصلية، ALL WEAPONS، BOTS − n +، والشبكة **مفلترة** حسب `gametype` في `.mapinfo` (19 dm، 18 tdm، 9 ctf من 29) مع زر ALL MAPS وعدّاد. **SETTINGS**: حجم الأزرار، الحساسية، عكس Y، يسار/يمين، RESET، ولوحة DevCapture (ملخص + SHARE LOG + COPY LOG) |
| `Editor/Import/HudArtImporter.cs` (جديد) | يستورد 30 صورة: أيقونات luma (health, armor, ammo×4, strength, shield, notify_death, flag×2, weapon×14) وluminos (gametype×3, background) إلى `Generated/Resources/Hud/*.asset`؛ يدمج `<name>_alpha.jpg` (اتفاقية DarkPlaces) في قناة alpha؛ `ImportedTexturePolicy.Finalize` (mips + ETC2) — قاعدة dev.12؛ بيان `hud-art-manifest.json` مع SHA256 المصدر. يُستدعى من `PrepareFullGame` (بوابة prepare-maps وبناء Android) |
| `Editor/FullGameBuild.cs` | استدعاء `HudArtImporter.Generate` + حقول التقرير |
| `Editor/LocalTests.cs` | `MainMenuGeometry` يبني الشاشات الثلاث ويفحص كل Text/Graphic (+ ارتفاع كل زر ≥ 44) والفلترة؛ `HudGeometry` يفحص الصور أيضًا وأرقام health/armor/ammo؛ اختبارات جديدة **`TouchLayoutSettings`** (كل عنصر داخل safe area عند 0.8/1.0/1.4 × يمين/يسار، لا تداخل FIRE/JUMP، المرآة تعمل فعلًا)، **`DevCaptureReport`**، **`HudArtAssets`** (30 صورة، alpha، mips، ضغط، أحجام)، **`BotSpawnGeometry`** (يفتح كل مشهد خريطة ويفحص أرضية كل spawn بنفس قاعدة وقت التشغيل؛ تقرير `Artifacts/bot-spawn-geometry.json`) |

## التحقق

| البوابة | النتيجة |
|---|---|
| compile | نجح (0 أخطاء) — بعد كل تعديل |
| weapons | 14/14 rig (41 ث) |
| prepare-maps | نجح (197 ث) — 29 خريطة، `[HudArtImporter] imported 29, missing 0`، 11 شخصية مستوردة **بخاماتها** (0 ملاحظات `untextured`) بعد فك الخامات المفقودة (انظر «انتكاسة الخامات» أدناه) |
| Editor tests | `EDITOR TESTS PASS 671` (32 ث) |
| BotSpawnGeometry | 29 خريطة، 583 spawn: 569 على الأرض، 14 قابلة للتثبيت (courtfun 12، darkzone 1، leave_em_behind 1)، **0 طافية** — `Artifacts/bot-spawn-geometry.json` |
| playtest | `PLAYTEST PASS` (MachineGun غير مُهيكل في playtest؛ فحوص الحركة تُتجاوز — كما في dev.12) |
| gameplay-playtest | `passed: true` — كل فحوص `Artifacts/gameplay-playtest.json` (pickups، إيقاف، حدود frag/time، إعادة التشغيل) |
| تحقق مرئي | `visual-probe` (جديد، `Editor/VisualProbe.cs`، `python3 tools/local_unity.py visual-probe --graphics` تحت xvfb/llvmpipe): 5 لقطات في `Artifacts/visual/` — HOME/PLAY/SETTINGS + HUD + pause على map_boil؛ `passed: true`. القوائم الثلاث تظهر كاملة (خلفية luminos، أيقونات gametype، شبكة الخرائط 18/29 dm، لوحة DevCapture). **حدود الفحص**: llvmpipe وليس جهازًا؛ عناصر IMGUI (أزرار اللمس وقائمة الإيقاف) لا تُلتقط؛ الكانفس يُحوَّل مؤقتًا إلى ScreenSpaceCamera فيظهر سلاح المنظور فوق لوحة الصحة في اللقطة — على الجهاز الكانفس Overlay ويُرسم فوق كل شيء |

## ملف APK (versionCode 14)

| البند | القيمة |
|---|---|
| الملف | `my-xonotic-full.apk` (إصدار `unity-v0.1.0-dev.13`) |
| الحجم | 405,422,879 بايت (≈ 405 MB؛ dev.12 كان 411 MB) |
| SHA256 | `cda3a010db6868a12e334955b30234fab4b40cac6ac6234d178f6d14411e72ef` |
| versionCode / versionName | 14 / `0.1.0-dev.13` |
| الحزمة / ABI / SDK | `com.ayoub.myxonotic` / arm64-v8a / minSdk 26، compileSdk 36 |
| التوقيع | debug (نفس شهادة dev.8–12: `bbf3ca26…ee24b5`) — تثبيت فوق dev.12 بلا إزالة |
| البناء | Unity 2022.3.62f3، IL2CPP، 357 ث، 0 أخطاء؛ الإيصال `docs/unity-dev13-build-2026-09-23.json` |
| الجهاز | **غير مُجرَّب على جهاز** — dev build |

### انتكاسة الخامات (اكتُشفت وأُصلحت قبل الإطلاق)
أول بناء dev.13 خرج بحجم 380 MB فقط: 11 خامة شخصيات (erebus, gak, gakarmor, ignis, ignishead, nyx, pyria, pyriahair, seraphina, shadowhead, umbra) وخامات العناصر لم تكن مفكوكة في `ExternalContent/decoded/`، فاستوردها `IqmCharacterImporter` بلا صور (`full-game-maps.json` → ملاحظات `untextured`). **الدرس**: قبل `prepare-maps` نفّذ أوامر فك الخامات في `docs/UNITY-DEV8.md` §المتطلبات، ثم تأكد أن `Artifacts/full-game-maps.json` بلا `no image resolved`، وأن `Generated/Resources/Characters/*_Texture_*.asset` موجودة (42 ملفًا). المقارنة مع حجم APK السابق كاشف سريع لأي أصول مفقودة.

## ما يجب أن يرسله المالك بعد تجربة dev.13 (يُغني عن أي وصف)

1. PAUSE → **SHARE LOG** → Slack (أو SETTINGS → SHARE LOG من القائمة). التقرير
   يحوي الجهاز/GPU، FPS، حالة البوتات (حيّة/مرئية/أقرب مسافة)، spawns المصححة،
   وكل خطأ متكرر مع عدده.
2. لقطة واحدة للقائمة HOME ولقطة لـPLAY ولقطة داخل اللعب (HUD السفلي).

## الدروس

- لا تعدّل مصادر C# بينما Unity يعمل على نفس المشروع في batchmode
  (`AssetDatabase.Refresh` داخل prepare-maps سيترجم تعديلًا نصفيًا)؛ حرّر في
  مجلد staging ثم انسخ بين البوابات.
- `HudArt` يجب أن يبقى اختياريًا: اختبار `MainMenuGeometry` يعمل قبل
  `prepare-maps` أيضًا (لا أصول) — كل استدعاء يتحمّل null.
- أيقونات DarkPlaces: `foo.jpg` + `foo_alpha.jpg`؛ بدون الدمج تظهر مربعات
  سوداء. `gfx/hud/luma/weapon*.jpg` بالأسماء القديمة (laser/uzi/nex/rocketlauncher/
  grenadelauncher) لا الحديثة (blaster/machinegun/vortex/devastator/mortar).

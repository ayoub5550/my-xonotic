# Unity dev.16 — بوتات NavMesh حقيقية، إصلاح CapsuleCollider، دقة عرض دينامية، Game Loop على Firebase

**الفرع:** `feat/unity-dev16-navmesh-bots` (مبني على `feat/unity-dev15-weapon-mechanics`) — **التاريخ:** 2026-09-23

## الهدف

dev.15 ترك ثلاث مشكلات من Test Lab: خطأ `CapsuleCollider` على الأجهزة، بوتات لا تقترب من اللاعب، وأداء Galaxy A15 دون 60 FPS.
dev.16 يعالجها الثلاث ويضيف مباراة آلية (Game Loop) كي تُختبر اللعبة نفسها — وليس القائمة فقط — على هواتف Firebase.

## ما تغيّر — ملفًا بملف

| الملف | التغيير |
|---|---|
| `Assets/link.xml` **(جديد)** | يحفظ `UnityEngine.PhysicsModule` و`UnityEngine.AIModule` من strip engine code. يُصلح `Can't add component because class 'CapsuleCollider' doesn't exist!` الذي ظهر على S24 وA15 في dev.15. |
| `Editor/NavMeshBake.cs` **(جديد)** | يبني NavMesh لكل خريطة أثناء `prepare-maps` (`FullGameBuild.ImportMapScene`) ويسجّل عدد المضلعات/المساحة في MapReport. تخطٍ بـ `XONOTIC_SKIP_NAVMESH=1`. |
| `Runtime/Gameplay/MapNavMesh.cs` **(جديد)** | استعلامات NavMesh وقت التشغيل (أقرب نقطة، عيّنة عشوائية، مسار). |
| `Runtime/Gameplay/BotNavigator.cs` **(جديد)** | تتبّع أركان `NavMeshPath`، `FlatDistance`، كشف الفجوات للقفز. |
| `Runtime/Gameplay/Bot.cs` **(أُعيدت كتابته)** | نقل `qcsrc/server/bot/`: دورة استراتيجية كل 7 s (5.5 s مع هدف متحرك)، كشف الأعداء كل 2 s (4 s أثناء الالتصاق)، اختيار السلاح كل 0.5 s حسب المسافة (300/850 qu)، أهداف: تجوّل → جمع عناصر حسب الحاجة → قتال → هروب عند صحة منخفضة، قائمة سوداء للأهداف المستحيلة (3 s → 10 s). مهارة 1–10 تضبط الدقة (`bot_ai_aimskill_offset`)، زمن التفكير، سرعة الدوران، وbunny-hop من مهارة 7. |
| `Runtime/Gameplay/MatchSettings.cs` | `BotSkillFor(index)` = 8/6/4 للبوتات الثلاثة. |
| `Runtime/Gameplay/AdaptiveResolution.cs` **(جديد)** | دقة عرض دينامية: نافذة 3 s، ينزل درجة إن تجاوز متوسط الإطار 22 ms ويصعد تحت 14 ms؛ درجات 1 / 0.85 / 0.7 / 0.6. |
| `Runtime/Debugging/GameLoop.cs` **(جديد)** | يلتقط intent `com.google.intent.action.TEST_LOOP`، يبدأ مباراة من القائمة (`LaunchFromMenu`)، يشغّل `ArenaBootstrap.AutoPilot` 120 s (+60 s لكل سيناريو)، ويكتب تقريرًا (FPS، أسوأ إطار، أخطاء، frags، navmesh) في `results_scenario_N.json`. |
| `Editor/GameLoopManifest.cs` **(جديد)** | `IPostGenerateGradleAndroidProject` يضيف intent-filter الخاص بـ Game Loop و`com.google.test.loops=2` إلى AndroidManifest المولَّد ويكتبه UTF-8 صراحةً. |
| `Runtime/Gameplay/ArenaBootstrap.cs`, `Menu/MainMenu.cs` | `SpawnPosition`, `AutoPilot`, `SetSkill`, تركيب `AdaptiveResolution` + `GameLoop`. |
| `Packages/manifest.json` (+lock) | إضافة `com.unity.modules.ai` (كانت غائبة → خطأ تجميع). |
| `Editor/Tests/Dev16BotTests.cs` **(جديد)** | 45 فحصًا: `FlatDistance`، تتبّع الأركان، جداول الأولوية، المهارة، `AdaptiveResolution.Decide`، `GameLoopManifest.Patch` (idempotent + utf-8 + إصلاح رأس utf-16 قديم). |

## التحقق

| البوابة | النتيجة |
|---|---|
| compile | 0 أخطاء (بعد إضافة AI module) |
| weapons / prepare-maps | PASS — 29 خريطة، NavMesh لكل واحدة (0.0–2.2 s لكل خريطة، مثل xoylent 11,757 مضلعًا) |
| test | `EDITOR TESTS PASS 787` (كان 742، +45 `Dev16BotTests`) |
| playtest / gameplay-playtest | PASS (`passed: true`) |
| android (vc17) | 2026-09-23 14:44 UTC (382.7 ث) — `my-xonotic-full.apk` 407,230,072 بايت، SHA256 `ec9d9da9078f7707f0da7e3cfee2e76d50a5de648749734ea65956506c7195a2`، توقيع debug، `TEST_LOOP` في المانيفست |

### ثلاثة أخطاء التقطتها البوابات قبل الجهاز

1. `Flat(a-b).magnitude` كان يُعيد 1 دائمًا (متجه مُطبَّع) → البوت «يصل» لكل ركن فورًا. أُضيف `BotNavigator.FlatDistance` واختبار «الركن البعيد لا يُتجاوَز».
2. `NavMeshPath`/`BotNavigator` لا يُنشآن في مُهيّئ حقل MonoBehaviour → خصائص lazy.
3. `GameLoopManifest` كتب `encoding="utf-16"` في ملف UTF-8 → `StackOverflowError` في Gradle manifest merger (بناءان فاشلان، 408 s + 362 s). الحل: كتابة عبر `MemoryStream` + `UTF8Encoding(false)` وإزالة أي إعلان قديم؛ ومسح `Library/Bee/Android/Prj` قبل إعادة البناء لأن المانيفست المعطوب يبقى فيه.

## Firebase Test Lab — 2026-09-23 14:47 UTC (الإجراء في `docs/DEVICE-TESTING.md`)

| التشغيل | الجهاز | Android | النتيجة | ملاحظات |
|---|---|---|---|---|
| Robo `7853666412667916610` | Galaxy S24 (`SC-51E`) | 16 | **Passed** (6.2 د فيديو) | 0 `E Unity` / FATAL / ANR في logcat (372,596 سطرًا). **خطأ CapsuleCollider اختفى.** |
| Game Loop `8242130421136989713` | Galaxy A15 5G (`a15x`) | 14 | **Passed** (129 ث) | `results_scenario_1.json`: 120 s، `avg_fps=36.2`، `worst_frame_ms=5622` (تحميل الخريطة)، `errors=0`، `navmesh=1`، `player_frags=0`, `player_deaths=0`, `bot_frags=-10` |

ما رأيناه في فيديو Game Loop (A15): الخريطة تُرسَم، HUD كامل، 3/3 بوتات حيّة وبوتان مرئيان في أول ثانية، صحة اللاعب تنزل إلى 50 ثم تتجدّد (أُصيب). لكن:

1. **الطيار الآلي يعلق أمام جدار** من 0:25 إلى نهاية الجولة (نفس الإطار 70 s) — `AutoPilot` لا يستخدم NavMesh؛ يجب أن يسير على مسار مثل البوت.
2. **البوتات لا تصل إلى اللاعب** (`nearest 19 m`, `visible 0` طوال المباراة) و`bot_frags=-10` = عشر وفيات بيئية/انتحار خلال دقيقتين (سقوط خارج NavMesh أو ضرر ذاتي بالصواريخ من قرب). البوتات تتحرك (2/3 حيّة في لحظات) لكنها تموت بدل أن تقاتل.
3. A15: 35–42 FPS داخل اللعب مع `AdaptiveResolution` — أفضل من dev.15 (30–43 بلا لعب) لكن ليس 60.

هذه الثلاث هي أول بنود dev.17 (قاعدة 6/8 في ROADMAP). معيار dev.16 في ROADMAP («كل بوت يجمع ≥1 عنصر ويسجّل ≥1 قتل») **لم يتحقق على الجهاز**؛ تحقق في اختبارات المحرر فقط.

## لم يُنقل بعد (مؤجَّل)

- jump pads/teleporters في تخطيط مسار البوت (OffMeshLink).
- منع الضرر الذاتي للبوت عند اختيار سلاح انفجاري من قرب (`bot_ai_*` يفعلها بأولوية السلاح).
- حراسة حواف NavMesh (kill-Y) كي لا يسقط البوت.
- Game Loop: سيناريو 2 (مباراة أطول مع تبديل خرائط).

## دروس

- `link.xml` إلزامي مع `stripEngineCode` لكل نوع يُنشأ عبر `CreatePrimitive`/`AddComponent` بالاسم؛ المحرر لا يكشف ذلك أبدًا.
- أي وحدة Unity (AI, Physics…) يجب أن تكون في `Packages/manifest.json` — غيابها خطأ تجميع صامت في `local_unity.py compile` حتى ترى `Artifacts/compile.log`.
- Game Loop على Test Lab يُقيَّم بـ `results_scenario_N.json` **والفيديو معًا**؛ `errors=0` لا يعني أن اللعب صحيح (الطيار الآلي العالق مرّ كـ Passed).
- ميزانية Test Lab المجانية: 5 أجهزة حقيقية/يوم — Robo على جهاز + Game Loop على آخر = تشغيلان في اليوم بعد تشغيل dev.15.

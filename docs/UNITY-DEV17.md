# Unity dev.17 — نماذج الطلقات الأصلية، دخان وكرات نار، بوتات لا تنتحر

**الفرع:** `feat/unity-dev17-projectile-visuals` (مبني على `feat/unity-dev16-navmesh-bots`) — **التاريخ:** 2026-09-23

## الهدف

في dev.16 كانت الطلقات كرات ملوّنة إجرائية (قرار dev.8) فبدت «كخامات»، والبوتات ماتت عشر مرات بيئيًا في دقيقتين (`bot_frags=-10`) دون أن تصل للاعب.
dev.17 يسحب بند «المؤثرات البصرية للطلقات» من dev.18 إلى الأمام (قاعدة 6/8 في ROADMAP: ما يكشفه Test Lab يُصلَح أولًا) ويعالج انتحار البوتات وتعليق الطيار الآلي.

## ما تغيّر — ملفًا بملف

| الملف | التغيير |
|---|---|
| `Editor/Import/ProjectileModelImporter.cs` **(جديد)** | يستورد نماذج الطلقات من حزمة Xonotic 0.8.6 إلى prefabs في `Resources/Weapons/*Projectile.prefab` أثناء `weapons` (مربوط في `IqmWeaponImporter.GenerateWeaponAssets`). يدعم **MD3 وIQM** معًا لأن `models/rocket.md3` و`grenademodel.md3` في الحزمة هي في الحقيقة ملفات IQM (`INTERQUAKEMODEL`). Devastator → `rocket` (773 رأسًا، 2/2 نسيج)، Mortar → `grenademodel` (646)، Minelayer → `mine` (1158)، Hagar → `tagrocket` **كبديل** (162) لأن `hagarmissile` بصيغة MDL غير مدعومة. الأنسجة DDS تُفكّ إلى PNG في `ExternalContent/decoded/` (خارج git). |
| `Runtime/Gameplay/ProjectileVisuals.cs` **(جديد)** | `ProjectileVisuals`: `ModelResource`، `TryAttachModel` (يُلحق النموذج بالطلقة ويوجّهه مع السرعة)، `AddSmokeTrail`/`ReleaseSmoke` (ذيل دخان للصواريخ والقنابل يُفصل عند الانفجار ليتلاشى). `ParticleFx`: مادة `VertexColor` خاصة بـ `_VertexWeight 1` (مادة الساحة تضبطه 0 فتتجاهل ألوان الجسيمات)، عرض Mesh بكرة `ArenaPrimitives.SphereMesh` (بلا اعتماد على shader billboard)، `Fireball(point, tint, radius)` للانفجارات. |
| `Runtime/Gameplay/Projectile.cs` | يحاول إلحاق النموذج؛ إن غاب يبقى على الكرة الملوّنة (Blaster/Electro/Crylink). توجيه النموذج مع اتجاه السرعة كل إطار. ذيل دخان لـ Devastator/Mortar/Hagar/Minelayer، `ReleaseSmoke` عند الانفجار أو التلاشي. |
| `Runtime/Gameplay/ImpactEffects.cs` | كرة نار جسيمية عند كل انفجار splash (أثناء `Application.isPlaying` فقط). |
| `Runtime/Gameplay/Bot.cs` | ثلاث حراسات: (1) **كشف التعليق موضعيًا** — إن لم يتحرك البوت ≥0.6 m خلال 2.5 s وهو يحمل هدفًا (`ProgressWindow`/`ProgressMinDistance`) يُدرَج الهدف في القائمة السوداء ويقفز؛ (2) **`SplashWouldHitSelf`** — raycast قبل `TryFire` بسلاح انفجاري؛ إن كان الجدار/الهدف داخل نصف قطر الانفجار لا يُطلق ويبدّل السلاح؛ (3) **`NearNavMeshEdge`** — أهداف التجوّل الأقرب من 0.9 m (`EdgeGuard`) لحافة NavMesh تُرفض كي لا يسقط. |
| `Runtime/Gameplay/Actor.cs` | عدّاد `Suicides` (موت بضرر ذاتي/بيئي). |
| `Runtime/Debugging/GameLoop.cs` | تقرير Game Loop يضيف `bot_deaths`، `bot_suicides`، `player_suicides` لتمييز الانتحار عن القتل. |
| `Packages/manifest.json` (+lock), `Assets/link.xml` | إضافة `com.unity.modules.particlesystem` وحفظ `UnityEngine.ParticleSystemModule` من strip engine code (درس `CapsuleCollider` في dev.15). |
| `Editor/Tests/Dev17VisualBotTests.cs` **(جديد)** | 22 فحصًا (809−787): جدول النماذج/الدخان، إلحاق النموذج وتوجيهه، تهيئة ParticleSystem جديد بلا خطأ، مادة الجسيمات، حراسات البوت، عدّادات الانتحار في التقرير. |

## التحقق

| البوابة | النتيجة |
|---|---|
| compile | 0 أخطاء |
| weapons | PASS — 4 prefabs طلقات (rocket/grenademodel IQM، mine/tagrocket MD3)، كل الأنسجة مُحلَّة |
| test | `EDITOR TESTS PASS 809` (كان 787، +22 `Dev17VisualBotTests`) |
| playtest / gameplay-playtest | PASS (`passed: true`) |
| android (vc18) | 2026-09-23 16:10 UTC (357.7 ث) — `my-xonotic-full.apk` 411,475,516 بايت، SHA256 `4751fe25efaeb8418ad00b03208a56a3b3ad800f1e2bceed9dcc0342dfc22994`، توقيع debug، `TEST_LOOP` في المانيفست |

### خطأ التقطته البوابات قبل الجهاز

- `Setting the duration while system is still playing is not supported` — `ParticleSystem` يبدأ التشغيل تلقائيًا عند `AddComponent`، فضبط `main.duration` بعده يرمي استثناءً (ظهر في playtest فقط). الحل: `ps.Stop(true, StopEmittingAndClear)` قبل الضبط ثم `ps.Play()`، مع فحص محرر «fireball configures a fresh ParticleSystem».

## Firebase Test Lab — 2026-09-23 (الإجراء في `docs/DEVICE-TESTING.md`)

| التشغيل | الجهاز | Android | النتيجة | ملاحظات |
|---|---|---|---|---|
| Game Loop `7881849412129240083` | Galaxy A15 5G (`a15x`) | 14 | **Passed** (120 ث) | `results_scenario_1.json`: `avg_fps=35.7`، `worst_frame_ms=4620` (تحميل الخريطة)، `errors=0`، `navmesh=1`، `player_frags=-1`، `player_deaths=4`، `player_suicides=4`، `bot_frags=-5`، `bot_deaths=16`، `bot_suicides=9`. logcat 64,807 سطرًا: 0 `E Unity` / FATAL / ANR. |

مقارنة بـ dev.16 على الجهاز نفسه:

| المؤشر | dev.16 | dev.17 |
|---|---|---|
| الطيار الآلي | علق أمام جدار من 0:25 حتى النهاية | **يتحرك طوال المباراة** (إطارات مختلفة كل 12 ث، صعود درج، تغيير أسلحة) |
| بوتات مرئية | 0 طوال المباراة، أقرب 19 m | **3/3 مرئية** في 0:23 (أقرب 61 m)، بوت أمام الكاميرا في 0:49، البوتات تصيب اللاعب (100 → 26 → 43 → 49) |
| موت البوتات | 10 كلّها بيئية | 16 موتًا منها **9 انتحار** و**7 قتل** (بوت↔بوت/لاعب) |
| اللاعب | لم يمت ولم يُصَب بجدّية | 4 وفيات كلّها `player_suicides` — سقوط/ضرر ذاتي أثناء الطيار الآلي |
| FPS | 36.2 | 35.7 (نماذج + جسيمات بلا كلفة ملموسة) |

**غير مؤكَّد على الجهاز:** ظهور نماذج الصواريخ/القنابل وذيل الدخان وكرة النار لم يُلتقط بوضوح في إطارات الفيديو المفحوصة (كل 12 ث) — مؤكَّد في المحرر فقط. Robo لم يُشغَّل (حصة اليوم 5 أجهزة حقيقية نُفدت).

**يبقى لـ dev.18 (قاعدة 6/8):** `bot_suicides=9` ما زال مرتفعًا (السبب المرجّح: السقوط من الحواف أثناء القتال لا التجوّل — `NearNavMeshEdge` يحرس التجوّل فقط)، و`player_suicides=4` للطيار الآلي؛ معيار ROADMAP «`bot_frags ≥ 3` و`player_deaths ≥ 1`» تحقق نصفه (player_deaths=4، bot_frags=-5).

## لم يُنقل بعد (مؤجَّل)

- طلقات بصيغة MDL (`bullet`, `elaser`, `hagarmissile`) — تبقى كرات ملوّنة؛ Hagar يستعمل `tagrocket` كبديل.
- decals، gibs، حركة السلاح في اليد (bob/kick)، lightmaps/skybox → dev.18 كما هو.
- بنود dev.17 الأصلية في ROADMAP (العناصر الكاملة، المعلن، الأصوات، scoreboard) **لم تُنجز** في هذا الإصدار وتنتقل إلى dev.18.
- jump pads/teleporters للبوت (OffMeshLink)، سيناريو Game Loop 2.

## دروس

- `ParticleSystem` جديد يعمل فورًا؛ أوقفه قبل تعديل `main.*`.
- لا تثق بامتداد الملف في حزمة Xonotic: افحص الرأس (`IDP3` = MD3، `INTERQUAKEMODEL` = IQM).
- أي وحدة Unity جديدة = سطر في `Packages/manifest.json` **و**سطر في `Assets/link.xml`.
- ميزانية Test Lab المجانية (5 أجهزة حقيقية/يوم) تُستهلك بسرعة مع إصدارات متعددة في يوم واحد؛ خطّط تشغيلًا واحدًا لكل dev واستعمل الأجهزة الافتراضية للـ Robo.

# Unity dev.18 — الجرافيك الأصلي (lightmaps/glow/شفافية/bloom)، صفحة إعدادات بتبويبات، بوتات 3/2/1 مع منزلق، طلقات MDL

**الفرع:** `feat/unity-dev18-visuals-settings-bots` (مبني على `feat/unity-dev17-projectile-visuals`) — **التاريخ:** 2026-09-23

## الهدف

البنود الأربعة من اختبار المالك على Poco F3 (ROADMAP §dev.18): (1) الطلقات كما في الأصل مع التحقق على الجهاز أن prefabs الطلقات تُحمَّل فعلًا، (2) صفحة إعدادات بتبويبات على نمط Xonotic، (3) جرافيك أقرب للأصل: lightmaps + skybox + shaders الأصلية (glow/أنسجة متحركة/شفافية) + bloom خفيف مع وضع «منخفض»، (4) صعوبة البوت الافتراضية 3/2/1 مع منزلق وجداول `aim.qc`. ثم بند Test Lab من dev.17: `bot_suicides ≤ 2` و`player_suicides = 0` في 120 ث على A15.

## ما تغيّر — ملفًا بملف

| الملف | التغيير |
|---|---|
| `Runtime/Content/Mdl/MdlReader.cs` **(جديد)** | قارئ Quake MDL (`IDPO` v6): رؤوس/مثلثات/إطار 0 + skin 8-bit عبر لوحة Quake؛ يُخرج Mesh + Texture2D. |
| `Editor/Import/ProjectileModelImporter.cs` | يدعم MDL إلى جانب MD3/IQM. جدول الطلقات صار **8/8** نماذج أصلية: Devastator `rocket`، Mortar `grenademodel`، Minelayer `mine`، Hagar `hagarmissile.mdl` (1214 رأسًا، كان `tagrocket` كبديل)، Blaster `laser.mdl`، Electro `elaser.mdl` (+ `ebomb.mdl` للقنبلة الثانوية)، Crylink `plasmatrail.mdl`. أنسجة `elecbeam/eleccore/elecglass` و`mine*`/`minelayer*` مفكوكة إلى PNG في `derived/decoded/`. |
| `Runtime/Gameplay/ProjectileVisuals.cs`, `Projectile.cs`, `WeaponController.cs` | `Projectile.Spawn(..., alt)` يختار نموذج الطلقة الثانوية؛ حجم وتوجيه النموذج كما في `cl_projectile`؛ تقرير Game Loop يحصي النماذج المُحمَّلة فعلًا على الجهاز (`projectile_models=8/8`). |
| `Runtime/Menu/SettingsPage.cs` **(جديد)**, `Runtime/Menu/MainMenu.cs` | صفحة SETTINGS بتبويبات **VIDEO / AUDIO / CONTROLS / GAME** بأزرار لمس كبيرة، حفظ فوري لكل تغيير، معاينة الحساسية، منزلق «BOT DIFFICULTY» (1–10، افتراضي EASY 3 → 3/2/1)، عدد البوتات/النمط، أزرار Device Report. `MainMenu` يحتفظ بالألوان العامة ويُفوّض الصفحة. |
| `Runtime/Gameplay/GameSettings.cs` **(جديد)** | `Effects` (LOW/MEDIUM/HIGH)، `Bloom`، `TargetFps`، `ShowFps`، أحجام Master/Music/SFX — مفاتيح PlayerPrefs `mx_*`، `Apply()` عند بدء الساحة. |
| `Runtime/Gameplay/MobileBloom.cs` **(جديد)**, `Resources/Bloom.shader` **(جديد)** | bloom بربع الدقة (threshold 0.85، intensity 0.45) يُضاف على كاميرا الساحة؛ يُعطَّل بـ Effects=LOW أو Bloom=off. |
| `Editor/Import/BspImportPipeline.cs`, `XonoticContentResolver.cs` | تصنيف كل سطح BSP إلى `BlendKind {Opaque, Cutout, Blend, Additive}` من `blendFunc/alphaFunc`؛ `tcMod scroll` → `_Scroll`؛ `*_glow` → `_GlowTex` + `_HasGlow`؛ `dp_water/dp_refract` → شفافية 0.75. النتائج على 29 خريطة: 2259 Lightmapped، 118 LightmappedAdd، 60 LightmappedBlend، 29 Sky6Sided، 20 VertexColor؛ 261 مادة بتوهج؛ 0 fallback في lightmaps. |
| `Resources/Lightmapped.shader`, `LightmappedBlend.shader` **(جديد)**, `LightmappedAdd.shader` **(جديد)** | نسيج × lightmap × 2 (كما DarkPlaces)، تمرير الأنسجة المتحركة، التوهج **بمزج screen** `1-(1-c)(1-g)` لا بالجمع (الجمع أحرق شرائط الإضاءة إلى ألواح بيضاء على الجهاز في التشغيل الأول). |
| `Editor/LocalBuild.cs` | يثبّت Shaders الجديدة (`LightmappedBlend/Add/Bloom/Sky6Sided`) في `GraphicsSettings` وقت البناء كي لا تُقتطع. |
| `Runtime/Gameplay/BotAim.cs` **(جديد)**, `MatchSettings.cs`, `Bot.cs` | نقل ثوابت `bot_ai_aimskill_*` من `qcsrc/server/bot/default/aim.qc` وتدرّجها بالمهارة (`BotAim`)، و`bot_ai_thinkinterval` يتدرّج بالمهارة (`Bot.ThinkIntervalFor`)؛ `MatchSettings.BotSkill` (1–10، افتراضي **3**) و`BotSkillFor(i) = clamp(skill − i%3)` → 3/2/1؛ `Bot.cs`: `CombatFallback` — إن رُفضت خطوتا الالتفاف المحروستان يتبع اتجاه الملاحة، بعد 1 ث يتراجع محروسًا، وبعد `CombatHoldGiveUp=3` ث يقفز ويترك الهدف ويعيد التخطيط؛ `SafeStep` لا يرفض الحركة إن كان البوت نفسه خارج NavMesh. |
| `Runtime/Gameplay/Actor.cs`, `Player.cs`, `MapTrigger.cs`, `Runtime/Debugging/GameLoop.cs` | سبب كل موت بلا قاتل: `void` / `hurt` (trigger_hurt/slime) / `self` / `other`؛ التقرير يضيف `bot_suicide_*`، `player_suicide_*`، `bot_skill`، `projectile_models`، `effects`، `bloom`. |
| `Hud.cs`, `MapMusic.cs`, `WeaponAudio.cs`, `ImpactEffects.cs`, `ArenaBootstrap.cs` | يقرؤون GameSettings (FPS في HUD، أحجام الصوت، كرة النار تُلغى في LOW، bloom على الكاميرا). |
| `Editor/VisualProbe.cs` | مراحل 30–32 تصوّر تبويبات الإعدادات؛ `XONOTIC_PROBE_MAP` لاختيار الخريطة (افتراضي boil — atelier تُسقط llvmpipe محليًا). |
| `Editor/Tests/Dev18Tests.cs` **(جديد)**, `LocalTests.cs`, `Dev16BotTests.cs`, `Dev17VisualBotTests.cs` | `EDITOR TESTS PASS 1074` (كان 809): MDL reader، جدول 8 نماذج، BlendKind/scroll/glow من scripts، GameSettings round-trip، تبويبات الصفحة، جداول aim.qc، 3/2/1، `CombatFallback`، عدّادات الأسباب، bloom الهادئ، مزج screen في الshader. |

## التحقق

| البوابة | النتيجة |
|---|---|
| compile | 0 أخطاء (27 ث) |
| weapons | PASS — 8/8 prefabs طلقات (MD3/IQM/MDL)، كل الأنسجة مُحلَّة |
| prepare-maps | PASS (337 ث) — 29 خريطة، 0 lightmap fallback، 11 شخصية **بخاماتها** بعد فك DDS (انظر «انتكاسة» أدناه) |
| test | `EDITOR TESTS PASS 1074` (كان 809) |
| playtest / gameplay-playtest | PASS (32 ث / 57 ث) |
| visual-probe (boil) | PASS قبل إصلاح glow — تبويبات الإعدادات الأربعة مرسومة (`Artifacts/visual-probe/`) |
| android (vc19) | exit 0 (397 ث) — `my-xonotic-full.apk` 413,413,477 بايت، sha256 `bc05de144bacf8ab265777ffa39ecdd452c7be52018c1bc447809c9fef63be25`، `versionCode=19` / `0.1.0-dev.18`، توقيع debug (`CN=Android Debug`، `9a37e028…9d30`)، 0 أخطاء / 30 تحذيرًا، `no image resolved` = 0، 42 خامة شخصيات مولَّدة — `docs/unity-dev18-build-2026-09-23.json` |

### انتكاسة التقطتها بالفيديو لا بالبوابات

- **البوتات بيضاء بلا خامات** في تشغيل Test Lab الثاني: خامات الشخصيات (erebus…umbra) لم تكن مفكوكة في `ExternalContent/decoded/` على بيئة جديدة — نفس درس dev.13 (`docs/UNITY-DEV13.md` §انتكاسة الخامات). البوابات كلها PASS لأن `IqmCharacterImporter` يسجّل `untextured` كملاحظة لا كخطأ. الكاشفان: `grep -c "no image resolved" Artifacts/full-game-maps.json` (كان 21) وحجم APK (392 MB بدل 411). الحل: أوامر `prepare_unity_textures.py` في `docs/UNITY-DEV8.md` §المتطلبات ثم `prepare-maps` وبناء جديد.

## Firebase Test Lab — 2026-09-23 (الإجراء في `docs/DEVICE-TESTING.md`)

`tools/ftl_robo.sh <apk> dev18 game-loop` يشغّل **السيناريوين** (1 = 120 ث afterslime، 2 = 180 ث atelier) لأنه لا يمرّر `--scenario-numbers`؛ زمن الاختبار ≈ 317 ث.

| التشغيل | APK | الجهاز | النتيجة | سيناريو 1 (afterslime، 120 ث) | سيناريو 2 (atelier، 180 ث) | ما رأيته في الفيديو |
|---|---|---|---|---|---|---|
| 1 — `5087549620686682598` | قبل إصلاحات Bot/glow | Galaxy A15 5G (`a15x`) / Android 14 | Passed (317 ث) | `avg_fps=36.2`، `worst=2610`، `bot_suicides=2` (hurt)، `player_suicides=0`، `player_frags=0`، `bot_deaths=6`، `projectile_models=8/8` | `avg_fps=41.3`، `bot_suicides=0`، `player_frags=1` | ❌ الطيار الآلي متجمّد في زاوية من 0:26 إلى النهاية (كلتا خطوتَي الالتفاف مرفوضتان → وقوف)؛ ❌ ألواح بيضاء محروقة (شرائط إضاءة: نسيج×lightmap×2 + glow جمعي + bloom) في atelier |
| 2 — `7105520146693537650` | بعد `CombatFallback` + glow screen (392,128,211 بايت، `86911f7e…`) | نفسه | Passed (317 ث) | `avg_fps=34.9`، `worst=3288`، `errors=0`، `player_frags=1`، `player_deaths=3`، `bot_deaths=8`، **`bot_suicides=5` كلها `hurt` (slime)**، **`player_suicides=2` (hurt)**، `bot_skill=3/2/1`، `projectile_models=8/8`، `effects=MEDIUM`، `bloom=1` | `avg_fps=41.3`، `worst=122`، `bot_suicides=0`، `player_suicides=0` | ✅ الطيار يتحرك طوال المباراتين ويقتل بوتًا («You fragged Bot_2» 0:30)؛ ✅ لا ألواح بيضاء في atelier؛ ✅ نماذج الطلقات وHUD وlightmaps ظاهرة؛ ❌ **البوتات صور ظلّية بيضاء** (خامات غير مفكوكة — انظر أعلاه) |
| 3 — `4868700399579265516` | بعد فك خامات الشخصيات (413,413,477 بايت، `bc05de14…`) | نفسه | Passed (≈320 ث) | `avg_fps=36.2`، `worst=3132`، `errors=0`، `player_frags=1`، `player_deaths=0`، `bot_deaths=9`، **`bot_suicides=8` كلها `hurt` (slime)**، `player_suicides=0`، `bot_skill=3/2/1`، `projectile_models=8/8`، `effects=MEDIUM`، `bloom=1` | `avg_fps=41.1`، `worst=89`، `player_frags=1`، `bot_frags=2`، `bot_deaths=3`، `bot_suicides=0`، `player_suicides=0` | ✅ **البوتات بخاماتها الأصلية** (شخصية داكنة واضحة 0:25–0:26 و1:53)؛ ✅ الطيار يتحرك ويقاتل في afterslime طوال 120 ث («You fragged Bot_2» 1:05) وفي atelier حتى «You fragged Bot_0» 2:19؛ ✅ لا ألواح بيضاء؛ ✅ نماذج الطلقات (mortar/shotgun/devastator/blaster/machine gun) وlightmaps وglow ظاهرة؛ ⚠️ في آخر ≈35 ث من atelier (2:19→2:55) الطيار يقف في ممرّ ويلتفت قليلًا فقط (بلا بوتات مرئية، صحة 100) — ليس تجمّدًا كاملًا كالتشغيل 1 لكنه سلوك يستحق المتابعة في dev.19؛ ⚠️ كتلة داكنة كبيرة بلا تفاصيل عند 1:37–1:38 في atelier (سطح معتم قريب من الكاميرا) — لم أحدّد سببها |

logcat (التشغيل 2: 74,741 سطرًا؛ التشغيل 3: 73,191 سطرًا): 0 `E Unity`، 0 FATAL؛ سطور «crashed service» كلها لخدمات Samsung/Play لا للتطبيق.

مقارنة بـ dev.17 على الجهاز نفسه (سيناريو 1، 120 ث):

| المؤشر | dev.17 | dev.18 تشغيل 2 | dev.18 تشغيل 3 |
|---|---|---|---|
| avg_fps | 35.7 | 34.9 (lightmaps + glow + bloom + 8 نماذج) | 36.2 |
| worst_frame_ms | 4620 | 3288 | 3132 |
| bot_suicides | 9 (بلا سبب) | 5 (كلها slime/`hurt`) | **8** (كلها slime/`hurt`) — الهدف ≤ 2 **لم يتحقّق** |
| player_suicides | 4 | 2 (`hurt`) | **0** ✅ |
| player_frags / player_deaths | −1 / 4 | 1 / 3 | 1 / 0 |
| bot_deaths | 16 | 8 | 9 |
| مهارة البوتات | 8/6/4 | 3/2/1 | 3/2/1 |
| نماذج الطلقات على الجهاز | غير مؤكَّد | 8/8 (عدّاد التقرير + مرئية في الفيديو) | 8/8 (عدّاد + مرئية) |

**غير مؤكَّد على الجهاز:**
- معيار المالك «لاعب لمس متوسط يحقّق `player_frags ≥ 3` قبل 3 وفيات على 3/2/1» يحتاج إنسانًا؛ الطيار الآلي حقّق 1 frag / 3 وفيات (تشغيل 2) — رقم للمقارنة فقط.
- هدف ROADMAP «`bot_suicides ≤ 2` و`player_suicides = 0`» تحقق في التشغيل الأول (2/0) **مع طيار متجمّد**، ولم يتحقق في التشغيل الثاني (5/2) حين تحرك الجميع: كل الوفيات من slime في afterslime (`*_suicide_hurt`)، لا سقوط ولا ضرر ذاتي. الحراسة التالية المطلوبة: استبعاد أحجام `trigger_hurt` من NavMesh/أهداف الحركة — **مفتوح لـ dev.19**.
- الأداء على Poco F3 (جهاز المالك) غير مقاس؛ A15 فقط.

## لم يُنقل بعد (مؤجَّل)

- بنود dev.17 الأصلية (العناصر الكاملة، المعلن، الأصوات، scoreboard) — لم تُنجز في dev.18 أيضًا.
- decals، gibs، حركة السلاح في اليد (bob/kick)، شعاع Arc المتعرّج، توهج Strength/Shield.
- jump pads/teleporters للبوت، حراسة `trigger_hurt` أثناء القتال.
- ميزانية draw calls (≤150 إضافية) غير مقاسة.

## دروس

- **افحص الفيديو لا Passed فقط** — التشغيل الأول Passed بأرقام «جيدة» (2/0) لأن الطيار كان متجمّدًا؛ الثاني Passed وكشف بوتات بيضاء لم تلتقطها أي بوابة.
- بيئة جديدة = نفّذ فك الخامات (`docs/UNITY-DEV8.md` §المتطلبات) قبل `prepare-maps`، وتحقق: `grep -c "no image resolved" Artifacts/full-game-maps.json` = 0 و`Generated/Resources/Characters/*_Texture_*.asset` = 42، وحجم APK قريب من الإصدار السابق.
- توهج DarkPlaces (`*_glow`) يُمزج screen لا جمعًا فوق نسيج×lightmap×2، وإلا احترقت شرائط الإضاءة مع bloom.
- `ftl_robo.sh game-loop` يشغّل كل السيناريوهات؛ 317 ث لكل جهاز — خطّط الحصة (5 أجهزة/يوم) وفقًا لذلك.
- Unity 2022.3 على Linux يحتاج `cmdline-tools` 6.0 (Java 11) في SDK Android؛ الإصدار 12.0 (Java 17) يُفشل «Failed to update Android SDK package list».

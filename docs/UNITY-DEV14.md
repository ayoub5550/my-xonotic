# Unity dev.14 — فيزياء Xonotic، توازن الأسلحة الأصلي، تجدّد الصحة، أزرار لمس بنمط Warzone Mobile

**الفرع:** `feat/unity-dev14-xonotic-physics` (مبني على `feat/unity-dev13-bots-hud-menu`) — **التاريخ:** 2026-09-23

## الهدف

بعد dev.13 سأل المالك: «هل صارت اللعبة مثل الأصلية؟» (فيديو *Xonotic: An hour of
Deathmatch*). الجواب كان لا، وأكبر فرقين هما **الحركة** و**سلوك الأسلحة**. dev.14
يعالجهما مباشرة من ملفات اللعبة الأصلية المرفقة في المستودع (GPL) بدل تقدير الأرقام:

1. نقل نموذج حركة Xonotic (`physicsX.cfg` + `qcsrc/common/physics/player.qc` +
   `qcsrc/ecs/systems/physics.qc`) إلى C# خالص قابل للاختبار.
2. مزامنة كل أرقام الأسلحة مع `bal-wep-xonotic.cfg` (ضرر/ضرر الحافة/معدل الإطلاق/
   سرعة القذيفة/نصف قطر الانفجار/قوة الدفع/الذخيرة/التشتت).
3. تجدّد الصحة وتعفّنها (health regen/rot) كما في `balance-xonotic.cfg`.
4. طلب المالك (لقطة Warzone Mobile): أزرار لمس دائرية داكنة شفافة بإطار أبيض ورموز بيضاء.

## ما تغيّر — ملفًا بملف

| الملف | التغيير |
|---|---|
| `Runtime/Gameplay/XonoticPhysics.cs` **(جديد)** | نقل `PM_Accelerate` (QW clamp مع `sv_airaccel_qw -0.8`، `stretchfactor 2`، `speedlimit_nonqw 900`)، `CPM_PM_Aircontrol` (`sv_aircontrol 100`، power 2)، مزج الـstrafe (`airstrafeaccelerate 18` / `maxairstrafespeed 100` / `airstrafeaccel_qw -0.95`)، `airstopaccelerate 3`، احتكاك الأرض بنسخة k9er المستقلة عن الإطار (`sv_friction 6`، `stopspeed 100`)، `GeomLerp`، `IsMoveInDirection`. كل الثوابت من `physicsX.cfg` بوحدة 32 qu = 1 m. |
| `Runtime/Gameplay/Player.cs` | الحركة تستدعي `XonoticPhysics.GroundMove/AirMove`. القفز يحدث قبل خطوة الحركة ويُلغي onground (كما في `PlayerJump`) فلا احتكاك في إطار القفز → **bunny-hop**. **auto-hop**: إبقاء JUMP مضغوطًا يعيد القفز عند الهبوط (`sv_track_canjump 0`) — مناسب لزرّ لمس. نافذة coyote 60 ms لأن `CharacterController.isGrounded` يرفّ على المنحدرات. **الدفع من الأسلحة يُضاف مباشرة إلى السرعة** (`velocity += force` كما في `Damage()`) بدل نبضة متلاشية منفصلة، فقفزة الصاروخ/الليزر تُحمَل إلى الجو. قصّ السرعة الأفقية عند الاصطدام بالجدران. |
| `Runtime/Gameplay/Bot.cs` | نفس تحويل الدفع (إلى السرعة مباشرة)؛ حُذف `_externalImpulse`. |
| `Runtime/Gameplay/WeaponController.cs` | جدول `WeaponDef` أُعيد من `bal-wep-xonotic.cfg` (انظر الجدول أدناه). حقل جديد `FireDef.EdgeDamage`. `Knockback` = force × Q ويُضاف للسرعة؛ القيم السالبة تجذب (Crylink، قنبلة الجاذبية). التشتت = atan(نسبة cfg). ضرر الذات 0.65 (`g_balance_selfdamagepercent`). |
| `Runtime/Gameplay/Projectile.cs`, `ArenaMath.cs` | `SplashDamage(dist, radius, damage, edgeDamage)`: من الضرر الكامل في المركز إلى ضرر الحافة عند نصف القطر (RadiusDamage الأصلي). |
| `Runtime/Gameplay/Actor.cs` | `TickRegen(dt)`: الصحة تحت 100 تتجدد بعد 5 ث من آخر ضرر (`0.08·(100−h)+0.5`/ث)، فوق 100 تتعفّن (`0.02·(h−100)+1`/ث) بعد 5 ث من الظهور، والدرع فوق 100 يتعفّن بالمثل. |
| `Runtime/Gameplay/TouchGlyphs.cs` **(جديد)** | رموز الأزرار مرسومة برمجيًا (مسافة إلى كبسولات/حلقات → قناع 96×96): رصاصة (FIRE)، مُصوِّب (ALT)، سهم لأعلى (JUMP)، شيفرون ▲/▼ (WPN). ألوان: تعبئة `rgba(10,12,16,0.47)`، تعبئة أثناء الضغط بيضاء 27%، إطار أبيض 75%، رمز أبيض 92% مع ظل. لا أصول صور جديدة. |
| `Runtime/Gameplay/Hud.cs` | `DrawGlyphButton`: قرص + إطار رفيع + رمز + تسمية صغيرة أسفل الرمز؛ أُزيلت الألوان القديمة (أحمر/أزرق). حلقة عصا التحكم أرفع. المواضع والأحجام لم تتغيّر (`TouchLayout`). |
| `Editor/Tests/Dev14PhysicsTests.cs` **(جديد)** | 27 فحصًا رقميًا (انظر التحقق). |
| `Editor/TouchSkinPreview.cs` **(جديد)**، `tools/local_unity.py` | مهمة `touch-skin` تُركّب معاينة الأزرار CPU في `Artifacts/visual/touch-skin.png` (OnGUI لا يُلتقط بـVisualProbe). |

### الأسلحة بعد المزامنة (cfg → اللعبة)

| السلاح | أساسي | ثانوي |
|---|---|---|
| Blaster | 20 (حافة 10)، 0.7 ث، **6000** qu/s (كان 3000)، r60، force 375 | 25/12، 6000، r70، force 360 |
| Shotgun | **12 × 4** (كان 14 × 3)، تشتت 0.12، force 15 | melee 70، مدى 120 qu، force 200 |
| Machine Gun | 10، 0.1 ث، تشتت 0.03، force 3 | 3 × 14، 0.45 ث |
| Mortar | 55/25، 0.8 ث، **1900**، r120، force 250، ذخيرة 2 | 55/30، 0.7 ث، 1400، مرتدّة |
| Electro | 40/20، **0.6 ث** (كان 0.25)، 2500، r100، force 200، **ذخيرة 4** | 30/15، **1.2 ث**، 1000، r150، force 50 |
| Crylink | **6 × 10**/5، تشتت 0.08، 2000، r80، **force −50 (يجذب)** | 5 × 8/4، 3000، r100، force −200 |
| Vortex | 80، 1.5 ث، force 200، ذخيرة 6 | zoom |
| Hagar | 25/12، 0.1667 ث، 2200، r65، force 100 | 4 × 35/17، 2000، r80 |
| Devastator | 80/40، **1.1 ث** (كان 0.9)، 1300، r110، **force 400**، **ذخيرة 4** | تفجير عن بعد |
| Rifle | 80، 1.2 ث، force 100، ذخيرة 10 | **4 × 20**، 0.9 ث، تشتت 0.04 |
| Mine Layer | 40/20، 1.5 ث، **1000**، **r175**، force 250، ذخيرة 4 | تفجير |
| Arc | شعاع **100 dps** (20 كل 0.2 ث)، force 600/ث، مدى 1000 qu | bolt 25/12، 0.1667 ث، 2300، r65 |
| Fireball | 200/50، 2 ث، 1200، r200، force 600 | 3 ألغام نارية 40، 900، 7 ث |
| Hook | خطاف 2000 | قنبلة جاذبية 25، r500، force −2000، 3 ث |

غير منقول عمدًا (خارج نطاق dev.14): تسارع صاروخ Devastator (`speedstart/speedaccel`)
وتوجيهه، `speed_up` للمورتر/الإلكترو، التحميل التدريجي لـHagar الثانوي، حرارة Arc،
`solidpenetration` للرشاش، اختراق Vortex للضرر بالمسافة.

## التحقق

- `compile`: 0 أخطاء (تحذيران قديمان CS0414 غير مرتبطين).
- `test`: **EDITOR TESTS PASS 698** (كان 671؛ +27 فحصًا في `Dev14PhysicsTests`): ثوابت
  physicsX (maxspeed 11.25 m/s، jump 8.125 m/s، gravity 25 m/s²، ارتفاع القفزة 42.25 qu
  كما في تعليق cfg)، الركض يصل إلى maxspeed ولا يتجاوزه، الاحتكاك يوقف في ~1 ث ومستقل
  عن معدل الإطارات، في الجو: الأمام وحده يكسب ~144 qu/s² (20% بسبب `airaccel_qw −0.8`)،
  الـstrafe بزاوية 45° يتجاوز maxspeed ويبقى تحت 900، air control يدوّر السرعة بثبات
  المقدار ويُعطَّل مع مفاتيح الجنب، airstop يكبح، توازن Devastator/Blaster/Shotgun/
  Crylink/Electro/Arc، falloff ضرر الحافة، regen بعد المهلة ورot فوق 100.
- `playtest`: **PLAYTEST PASS**؛ `gameplay-playtest`: `passed: true` (37 pickups، الفيزياء
  الفعلية تلتقط العناصر، الإيقاف يجمّد الضرر/الساعة/الالتقاط).
- `touch-skin`: `Artifacts/visual/touch-skin.png` رُوجعت بصريًا (رصاصة مائلة، مصوِّب، سهم،
  شيفرونات، عصا تحكم).
- لم يُعاد `prepare-maps` (لا تغيير في المحتوى)؛ `Assets/Imported` من dev.13 كما هو.

## ملف APK (versionCode 15)

| | |
|---|---|
| الملف | `my-xonotic-full.apk` |
| الحجم | 404,785,588 بايت (dev.13: 405,422,879) |
| SHA256 | `9d9a70954d6cc1229d1b807fba65d1a5fad242bca93b4e9790ba1f3effec4bdd` |
| versionName / versionCode | `0.1.0-dev.14` / 15 |
| الحزمة | `com.ayoub.myxonotic`، arm64-v8a، minSdk 26 |
| التوقيع | debug (نفس شهادة dev.8–13، `bbf3ca26…24b5`) |
| زمن البناء | 2026-09-23 12:04 UTC ث، 0 أخطاء، IL2CPP |
| الإيصال | `docs/unity-dev14-build-2026-09-23.json` |

**غير مُتحقَّق على جهاز.** ما نحتاجه من المالك بعد التجربة: (1) هل الحركة أصبحت
«تنزلق» كـXonotic؟ هل يعمل الـbunny-hop بإبقاء JUMP مضغوطًا؟ (2) قفزة الليزر (Blaster
نحو الأرض) ترفع اللاعب؟ (3) لقطة للأزرار الجديدة؛ (4) PAUSE → SETTINGS → SHARE LOG.

## الدروس

- الاختبار الرقمي الأول افترض أن الأمام وحده في الجو لا يكسب سرعة؛ الكود الأصلي
  (تعليق `PM_Accelerate`: «dv/dt = accel·maxspeed·(1−accelqw) when fast») يكسب 20%.
  اقرأ التعليقات في player.qc قبل كتابة التوقعات.
- ثابت `32` داخل `CPM_PM_Aircontrol` بوحدة qu؛ عند التحويل إلى المتر يجب ضربه في Q
  وإلا يدور اللاعب 32× أسرع.
- OnGUI لا يظهر في VisualProbe؛ معاينة CPU (`touch-skin`) كافية لمراجعة الشكل.

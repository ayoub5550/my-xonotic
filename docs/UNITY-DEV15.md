# Unity dev.15 — سلوك الأسلحة كما في Xonotic (توجيه، شحن، حرارة، تحميل، ارتداد، combo)

**الفرع:** `feat/unity-dev15-weapon-mechanics` (مبني على `feat/unity-dev14-xonotic-physics`) — **التاريخ:** 2026-09-23

## الهدف

dev.14 نقل *أرقام* الأسلحة. dev.15 ينقل *سلوكها* من `qcsrc/common/weapons/weapon/*.qc` و`bal-wep-xonotic.cfg`
بحيث يعمل كل سلاح كما في اللعبة الأصلية وليس كمجرد «قذيفة بضرر». هذا أول إصدار يتبع `docs/ROADMAP.md`.

## ما تغيّر — ملفًا بملف

| الملف | التغيير |
|---|---|
| `Runtime/Gameplay/WeaponController.cs` | حقول جديدة في `FireDef`: `SpeedUp`، `SpeedStart/SpeedAccel`، `Guided`، `BounceFactor/BounceStop/LifetimeAfterBounce`، `Remote*`، `Delay`، `BurstInterval`، `LoadMax/LoadTime/LoadHold`. وضع إطلاق جديد `FireMode.Load`. مجدول طلقات (`_pending`) للدفعات والتأخير. حالة لكل آلية: شحن Vortex، عداد Machinegun، حرارة Arc، تحميل Hagar. `UpdateAim(origin, dir)` يُستدعى كل إطار من اللاعب والبوت. `TickMechanics(dt)` عام للاختبارات. |
| `Runtime/Gameplay/Projectile.cs` | تسارع Devastator من 1000 إلى 1300 qu/s وتوجيه بالنظر (`SteerTowards`, 90°/s بعد 0.2 s) أثناء ضغط الزناد. `speed_up` عند الانطلاق. ارتداد بقيم cfg (`bouncefactor`, `bouncestop × gravity`) وانفجار بعد `lifetime_bounce`. تفجير عن بعد بقيم `remote_*`. combo الـ Electro: انفجار البولت (أو كرة مُفعَّلة) قرب كرات حيّة ضمن 300 qu يفجّرها بعد 0.1 s بقيم combo (تسلسل). ألغام: نصف قطر 150 qu، عد تنازلي 0.5 s، عمر 10 s. |
| `Runtime/Gameplay/Player.cs`, `Bot.cs` | `Weapons.UpdateAim` قبل الإطلاق؛ البوت لا يستخدم `Load`. |
| `Runtime/Gameplay/Hud.cs` | قراءات بجانب اسم السلاح: `CHG %` (Vortex)، `HEAT % / OVERHEAT` (Arc)، `LOAD n/4` (Hagar). |
| `Editor/Tests/Dev15WeaponTests.cs` **(جديد)** | 44 فحصًا (بيانات cfg، `SteerTowards`، شحن Vortex، انتشار Machinegun، تحميل Hagar وإطلاقه وauto-fire، فرط تسخين Arc وتبريده واستهلاك الخلايا، توجيه Devastator وremote، `speed_up` Mortar، تصنيف كرات Electro). |
| `docs/ROADMAP.md` **(جديد)** | خارطة الطريق dev.15 → dev.20 وقواعد كل إصدار. |

## الآليات المنقولة (القيم من `bal-wep-xonotic.cfg`)

| السلاح | السلوك |
|---|---|
| Devastator | `speedstart 1000` → `speedaccel 1300` حتى `speed 1300`؛ توجيه `guiderate 90°/s` بعد `guidedelay 0.2` نحو نقطة النظر (`guidegoal 512`) ما دام الزر الأساسي مضغوطًا؛ تفجير عن بعد `70/35`, force 300, radius 110 (الاصطدام: 80/40/400/110). |
| Mortar | أساسي `speed_up 225`؛ ثانوي `speed_up 150`, `bouncefactor 0.5`, `bouncestop 0.075`, ينفجر 0.5 s بعد أول ارتداد (`lifetime_bounce`), عمر 20 s. |
| Electro | ثانوي: 3 كرات بفاصل 0.2 s (`count 3 / refire2 0.2`), `speed_up 200`, `bouncefactor 0.3`, `bouncestop 0.05`, فتيل 4 s؛ combo: 50/25, force 120, radius 150, `comboradius 300`. |
| Hagar | ثانوي: ضغط = صاروخ واحد فورًا، كل 0.5 s صاروخ إضافي حتى 4 (`load_max`), ذخيرة 1 لكل صاروخ, الإفلات يطلقها معًا بانتشار 0.075, الاحتفاظ بحمولة كاملة 4 s يطلقها تلقائيًا (`load_hold`), refire 0.5. |
| Machinegun | أول طلقة بعد توقف: spread 0.03 / refire 0.125؛ مستمر: spread من 0.02 + 0.012 لكل طلقة حتى 0.05, refire 0.1؛ دفعة ثانوية 3 × 14 بفاصل 0.06 s ثم 0.45 s. |
| Vortex | شحن يبدأ 0.5 بعد الطلقة ويرتفع 0.6/s حتى 1 (فقط عندما يكون السلاح الحالي)؛ الضرر 40 → 80 حسب الشحن، والدفع بالنسبة نفسها. |
| Arc | 100 dps، 600 force/s، 1500 qu مدى، tick 0.25 s، 6 خلايا/s (1.5 لكل tick مع كسر محمول)؛ فرط تسخين بعد 5 s متصلة ثم تبريد إجباري 2.5 s. |
| Minelayer | `proximityradius 150`, `lifetime 10`, `lifetime_countdown 0.5`, remote 45/40, force 300, radius 200. |
| Shotgun melee | `melee_delay 0.25` قبل الضربة (تنفَّذ على اتجاه النظر وقت الضربة). |

## التحقق

| البوابة | النتيجة |
|---|---|
| compile | 0 أخطاء |
| test | `EDITOR TESTS PASS 742` (كان 698، +44 `Dev15WeaponTests`) |
| playtest / gameplay-playtest | PASS (نجحا، صفر أخطاء؛ الضوضاء الوحيدة ALSA/FMOD من بيئة الحاوية) |
| android (vc16) | 2026-09-23 12:35 UTC (412.7 ث) — `my-xonotic-full.apk` 404,824,528 بايت، SHA256 `868dd71739963fb38be680534c00e1d8d78dbd970d7dde374c5dffc6af0400d0`، توقيع debug |

## اختبار على أجهزة حقيقية — Firebase Test Lab (Robo، 2026-09-23 13:19 UTC)

أول تشغيل مُوثَّق للعبة على هواتف حقيقية (الإجراء في `docs/DEVICE-TESTING.md`). المصفوفة: `6501329712695047192` في مشروع `ayoub-261d7`.

| الجهاز | Android | النتيجة | ملاحظات |
|---|---|---|---|
| Samsung Galaxy S24 (`SC-51E`) | 16 | **Passed** (5.8 د) | 60 FPS ثابت (17 ms)، بدون Crash/ANR |
| Samsung Galaxy A15 5G (`a15x`) | 14 | **Passed** (6.3 د) | 30–43 FPS، أسوأ إطار 89 ms، أول إطار 2.1 ث |

أخطاء logcat (تحدّد نطاق dev.16 حسب قاعدة 6/8 في ROADMAP):
1. `E Unity: Can't add component because class 'CapsuleCollider' doesn't exist!` في `ArenaPrimitives.Grab` ← `ArenaBootstrap.BuildPlayer` على الجهازين. السبب: `stripEngineCode: 1` يحذف `CapsuleCollider` لأن لا كود يشير إليه مباشرة، و`GameObject.CreatePrimitive(Capsule)` يحتاجه. لا يظهر في المحرر إطلاقًا. الحل: `Assets/link.xml` يحفظ `UnityEngine.PhysicsModule` + مرجع صريح للنوع.
2. أداء A15 (هاتف متوسط) دون 60 FPS — يحتاج تحسينًا (ظلال/دقة عرض دينامية/LOD).
3. البوتات غالبًا `visible 0` أثناء 6 دقائق — لا تقترب من اللاعب؛ يؤكد أولوية NavMesh bots.
4. Robo لا يستخدم عصا الحركة؛ dev.16 يضيف Game Loop (`com.google.intent.action.TEST_LOOP`) لتشغيل مباراة آلية.

غير مُتحقَّق منه بعد: إحساس التوجيه على اللمس (يحتاج يدًا بشرية)، وضوح HUD على شاشة صغيرة.

## لم يُنقل بعد (مؤجَّل)

- صحة المقذوفات (إسقاط صاروخ برصاصة — `*_health`).
- اختراق Machinegun للجدران (`solidpenetration 13.1`).
- اندماج قذائف Crylink (`joinspread/joindelay`) وانفجار الاندماج.
- تحريك السلاح أثناء التحميل/الشحن (رسوم) — dev.18.
- `remote_jump` للـ Devastator (معطّل في cfg الافتراضي).

## دروس

- في اختبارات المحرر لا يُستدعى `OnEnable/Awake` عند `AddComponent`؛ استخدم `ResetLoadout()` و`FindObjectsOfType<Projectile>()` بدل `LiveCount`.
- أي feedback (صوت/مؤثرات/`View`) يجب أن يُحرس بـ `Application.isPlaying` كي تعمل آليات الأسلحة في وضع التحرير.
- الطلقات المجدولة تُطلق على اتجاه النظر **وقت التنفيذ** (`AimOrigin/AimDirection`) وليس وقت الضغط — كما في الأصل.

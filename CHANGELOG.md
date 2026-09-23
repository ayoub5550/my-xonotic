# سجل التغييرات

## 0.1.0-dev.16 — 2026-09-23 — بوتات NavMesh حقيقية، إصلاح CapsuleCollider على الأجهزة، دقة عرض دينامية، Game Loop على Firebase Test Lab

- **البوتات** (`Bot.cs` أُعيدت كتابته + `BotNavigator.cs`/`MapNavMesh.cs`/`Editor/NavMeshBake.cs` جديدة): NavMesh يُبنى لكل خريطة (29) في `prepare-maps`؛ دورة استراتيجية/كشف أعداء/اختيار سلاح بفواصل `bot_ai_*` من `xonotic-server.cfg`؛ مهارة 1–10 (البوتات 8/6/4)؛ تجوّل → جمع → قتال → هروب.
- **إصلاح الأجهزة**: `Assets/link.xml` يحفظ PhysicsModule وAIModule من strip engine code → اختفى `Can't add component because class 'CapsuleCollider' doesn't exist!` (مؤكَّد على Galaxy S24 في Test Lab).
- **الأداء**: `AdaptiveResolution` (1 / 0.85 / 0.7 / 0.6 حسب متوسط الإطار في 3 s).
- **Game Loop**: `GameLoop.cs` + `GameLoopManifest.cs` — intent `TEST_LOOP`، مباراة آلية 120 s، تقرير `results_scenario_1.json` (FPS/أخطاء/frags/navmesh). أُضيف `com.unity.modules.ai` إلى `Packages/manifest.json`.
- اختبارات Editor: PASS **787** (كان 742) — `Dev16BotTests`. playtest وgameplay-playtest PASS.
- APK versionCode 17: `my-xonotic-full.apk` 407,230,072 بايت، SHA256 `ec9d9da9078f7707f0da7e3cfee2e76d50a5de648749734ea65956506c7195a2`، توقيع debug. **Firebase Test Lab**: Robo Passed على S24/Android 16 (0 أخطاء Unity)، Game Loop Passed على A15/Android 14 (36 FPS متوسطًا، 0 أخطاء). **مفتوح لـ dev.17**: الطيار الآلي يعلق أمام جدار، البوتات تموت بيئيًا (`bot_frags=-10`) ولا تصل للاعب. التفاصيل في `docs/UNITY-DEV16.md`.

## 0.1.0-dev.15 — 2026-09-23 — سلوك الأسلحة كما في Xonotic: توجيه Devastator، شحن Vortex، حرارة Arc، تحميل Hagar، ارتداد Mortar، combo Electro

- **Devastator**: انطلاق 1000 → تسارع 1300 qu/s، توجيه بالنظر أثناء ضغط الزناد (90°/s بعد 0.2 s)، تفجير عن بعد بقيم `remote_*` (70/35/300/110).
- **Mortar/Electro**: `speed_up` عند الانطلاق، ارتداد بقيم cfg، انفجار 0.5 s بعد أول ارتداد (Mortar)، 3 كرات متتابعة (Electro) وcombo عند انفجار البولت قربها (50/25، radius 150، comboradius 300، تسلسل 0.1 s).
- **Hagar**: الثانوي تحميل حتى 4 صواريخ (0.5 s لكل واحد) وإطلاقها عند الإفلات أو بعد 4 s.
- **Machinegun**: أول طلقة 0.03/0.125 s ثم انتشار متزايد 0.02→0.05؛ دفعة 3×14 بفاصل 0.06 s.
- **Vortex**: شحن 0.5→1 بمعدل 0.6/s؛ ضرر 40→80. **Arc**: 6 خلايا/s، مدى 1500، فرط تسخين 5 s وتبريد 2.5 s. **Minelayer**: تفعيل 150 qu، عد تنازلي 0.5 s، عمر 10 s، remote 45/40/300/200. **Shotgun melee**: تأخير 0.25 s.
- HUD: `CHG %` / `HEAT %` / `LOAD n/4` بجانب اسم السلاح. `docs/ROADMAP.md` جديد (dev.15 → dev.20).
- اختبارات Editor: PASS **742** (كان 698) — `Dev15WeaponTests`. playtest وgameplay-playtest PASS (نجحا، صفر أخطاء؛ الضوضاء الوحيدة ALSA/FMOD من بيئة الحاوية).
- APK versionCode 16: `my-xonotic-full.apk` 404,824,528 بايت، SHA256 `868dd71739963fb38be680534c00e1d8d78dbd970d7dde374c5dffc6af0400d0`، توقيع debug. **Firebase Test Lab (Robo)**: Passed على Galaxy S24/Android 16 (60 FPS) وGalaxy A15/Android 14 (30–43 FPS)؛ خطأ واحد في logcat (`CapsuleCollider` محذوف بسبب strip engine code) → يُصلَح في dev.16. التفاصيل في `docs/UNITY-DEV15.md`.

## 0.1.0-dev.14 — 2026-09-23 — فيزياء Xonotic الأصلية، توازن الأسلحة من bal-wep-xonotic.cfg، تجدّد الصحة، أزرار لمس بنمط Warzone Mobile

- **الحركة** (`Runtime/Gameplay/XonoticPhysics.cs` جديد): نقل `PM_Accelerate`
  (QW clamp، `sv_airaccel_qw -0.8`)، `CPM_PM_Aircontrol` (100/power 2)، مزج الـstrafe
  (18 / 100 qu/s)، `airstopaccelerate`، واحتكاك الأرض المستقل عن الإطار من
  `physicsX.cfg` — bunny-hop وair control وقفزات الصواريخ/الليزر تعمل كالأصل.
  auto-hop بإبقاء JUMP مضغوطًا. الدفع من الأسلحة يُضاف للسرعة مباشرة (لاعب وبوت).
- **الأسلحة**: كل `WeaponDef` مزامَن مع `bal-wep-xonotic.cfg` (Blaster 6000 qu/s،
  Shotgun 12×4، Electro 0.6 ث/4 خلايا، Crylink 6 قذائف تجذب، Devastator 1.1 ث/force 400،
  Rifle ثانوي 4×20، Arc 100 dps…) + ضرر الحافة (`EdgeDamage`) في falloff الانفجار،
  ضرر الذات 0.65.
- **الصحة**: regen تحت 100 بعد 5 ث من آخر ضرر، rot فوق 100 للصحة والدرع
  (`Actor.TickRegen`).
- **أزرار اللمس** (`TouchGlyphs.cs` جديد + `Hud.DrawGlyphButton`): أقراص داكنة شفافة
  بإطار أبيض ورموز بيضاء مرسومة برمجيًا (رصاصة/مصوِّب/سهم/شيفرون) تضيء أثناء الضغط،
  حسب لقطة المالك من Warzone Mobile. المواضع لم تتغيّر. مهمة `touch-skin` للمعاينة.
- اختبارات Editor: PASS **698** (كان 671) — `Dev14PhysicsTests`. playtest وgameplay-playtest نجحا.
- APK versionCode 15: `my-xonotic-full.apk` 404,785,588 بايت، SHA256 `9d9a70954d6cc1229d1b807fba65d1a5fad242bca93b4e9790ba1f3effec4bdd`، توقيع debug، غير مُجرَّب على جهاز. التفاصيل في `docs/UNITY-DEV14.md`.

## 0.1.0-dev.13 — 2026-09-23 — DevCapture، صيد البوتات وتصحيح spawns، قائمة دخول جديدة، HUD بأيقونات الأصل، إعدادات اللمس

- **DevCapture** (`Runtime/Debugging/DevCapture.cs`): عيّنة كل 5 ث (FPS/أسوأ إطار/ذاكرة/
  بوتات حيّة ومرئية وأقرب مسافة/موضع اللاعب/VoidKillY) تُكتب في السجل؛ تقرير
  كامل مع جدول الرسائل المتكررة؛ **SHARE LOG** (Android share sheet) و**COPY LOG**
  في شاشة PAUSE وصفحة SETTINGS. `RuntimeErrorLog` يعدّ التكرارات بعد 20 نسخة
  بدل إغراق الملف، ويلتقط `lowMemory`.
- البوتات: `ArenaBootstrap.ValidateSpawns()` يسحب أي spawn بلا أرض ضمن 2 م إلى
  الأرض تحته أو يحذفه (اختبار `BotSpawnGeometry`: 583 spawn في 29 خريطة —
  569 على الأرض، 14 تُسحب، 0 عائم)؛ `Bot` يصطاد أقرب عدو بعد 4 ث بلا رؤية بدل
  التجوّل العشوائي، مع انحراف عند التعثّر. سطر NOTE عند بداية كل ساحة بعدد
  البوتات الفعلي.
- القائمة الرئيسية أُعيدت كتابتها: HOME (خلفية luminos الأصلية، PLAY/SETTINGS/QUIT
  بحجم إبهام، كارت خريطة مميّزة)، PLAY (DM/TDM/CTF بأيقونات gametype الأصلية،
  الشبكة مفلترة حسب `gametype` في mapinfo + ALL MAPS)، SETTINGS (حجم الأزرار
  0.8–1.4×، حساسية 0.5–2×، عكس Y، تخطيط يساري، RESET، لوحة DevCapture).
- HUD بنمط luma: لوحة سفلية بأيقونات health/armor/ammo الأصلية وأرقام كبيرة،
  أيقونات الأسلحة الأصلية في الشريط، النتيجة/المؤقّت تحت البانر، سطر FPS/BOTS.
- `HudArtImporter` جديد: 29 صورة من `gfx/hud/luma` و`gfx/menu/luminos` مع دمج
  `_alpha.jpg` وضغط ETC2 + mips إلى `Generated/Resources/Hud`.
- `TouchLayout` يكبّر/يصغّر ويعكس التخطيط حسب `TouchSettings`؛ `Player` يطبّق
  الحساسية وعكس Y.
- اختبارات Editor: PASS **671** (كان 455): `MainMenuGeometry` للشاشات الثلاث،
  `HudGeometry` مع الصور، `TouchLayoutSettings`، `DevCaptureReport`، `HudArtAssets`،
  `BotSpawnGeometry`. playtest وgameplay-playtest نجحا.
- البيئة: Unity 2022.3.62f3 + Android مثبّتان من جديد في sandbox gVisor مع وصفة
  my-librequake (qemu shader compiler، FMOD shim). الرندرة عبر llvmpipe تعمل
  في المحرر (OpenGL 4.5) — أول مرة لا تتجمّد.
- APK versionCode 14: `my-xonotic-full.apk` 405,422,879 بايت، SHA256 `cda3a010db6868a12e334955b30234fab4b40cac6ac6234d178f6d14411e72ef`، توقيع debug، غير مُجرَّب على جهاز. التفاصيل في `docs/UNITY-DEV13.md`.

## 0.1.0-dev.12 — 2026-09-22 — إصلاحات أول فيديوهات الجهاز: خامات مضغوطة، موت الفراغ، HUD، سجل أخطاء

- خامات الأسلحة ونماذج الالتقاط تمرّ الآن عبر `ImportedTexturePolicy.Finalize`
  (mipmaps + ETC2) بدل RGBA32 خام بلا mips (سبب Vortex الأبيض والأشكال
  الداكنة على الجهاز — نفاد ذاكرة GPU). أصول الأسلحة 33.5 → 11.2 MB لكل خامة،
  `Generated/MapModels` 603 → 199 MB، APK أصغر بـ ~49 MB.
- موضع أسلحة DPM (Mortar/Devastator) تحقّق أنه مطابق لـXonotic الأصلي عبر
  أداة `tools/weapon_snapshot.py` الجديدة (رسم هندسي بلا GPU) — لم يُغيَّر.
- السقوط خارج الخريطة يقتل فورًا (`ArenaBootstrap.VoidKillY` = أدنى مصادم −12 م)
  و`trigger_hurt` بلا `dmg` قاتل (1000) كما في الأصل.
- HUD: pivot الحواف أُصلح (صف FRAGS/DEATHS واسم السلاح لم يعودا مقطوعين)؛ بانر
  النسخة صغير أعلى يسار؛ شاشة PAUSED تعرض ملخص الأخطاء.
- `RuntimeErrorLog` جديد يكتب التحذيرات/الأخطاء إلى
  `Android/data/com.ayoub.myxonotic/files/my-xonotic-errors.log`.
- البوابات المتخطّاة في dev.11 أُعيدت: Editor tests PASS 455 (منها `WeaponRigs`
  14/14، `WeaponPlacement` 14/14، `HudGeometry` الجديد)، gameplay-playtest نجح.
- APK versionCode 13: 411,439,725 بايت، SHA256 `6aeed8f0e50f87e71db3efdd2119e12baf297039d99288db13944422585fa198`. التفاصيل في `docs/UNITY-DEV12.md`.

## 0.1.0-dev.11 — 2026-09-22 — تحريك الأسلحة الأصلي وفلاش الفوهة وشظايا الانفجار

- المنظور الأول يستخدم الآن نماذج `h_*` الأصلية المتحركة (idle/fire/fire2)
  لكل الأسلحة الـ14: 9 هياكل IQM v2 يُعلَّق عليها `v_`، `h_fireball` بصيغة
  IQM v1 (دعم جديد)، و5 نماذج DarkPlaces DPM (electro, crylink, gl, hagar, rl)
  بشبكة اليدين وخاماتها الأصلية عبر قارئ `DpmDocument` جديد.
- فلاش فوهة عند مفصل `shot`، قرص موجة صدمة + 10 شظايا للانفجارات، اهتزاز
  كاميرا يتلاشى ضمن 14 م، ارتداد يدوي مخفَّض عند وجود تحريك.
- اختبارات جديدة: `WeaponRigs` في Editor وفحص تشغيل مقطع `fire` في Play Mode.
  الجولة الأولى كشفت Fireball ناقصًا (سبب دعم v1)؛ لم تُعَد الاختبارات بعد
  الإصلاح بطلب المالك — تُعاد في dev.12.
- `docs/DEVELOPMENT-GUIDE.md`: ورقة منهجية التطوير للمطوّرين التالين.
- APK versionCode 12: 460,433,781 بايت، SHA256 `cd63b41ff24170aadb3305f98e8dbf7dad7bcf20454ddea697fb4be2beff5141`. التفاصيل في `docs/UNITY-DEV11.md`.

## 0.1.0-dev.10 — 2026-09-22 — إصلاح القائمة الرئيسية الفارغة على الجهاز

- أول لقطة من جهاز حقيقي (dev.9) أظهرت قائمة بلا عنوان/QUIT/بطاقات خرائط.
  السبب: مستطيلات بارتفاع سالب للعنوان والعنوان الفرعي وQUIT ومعاينة البطاقة
  (منذ dev.5)، و`Mask` فوق صورة ألفاها 0.001 يُخفي كل بطاقات الخرائط. أُصلحت
  المستطيلات واستُبدل القناع بـ `RectMask2D`؛ تسميات −/+ للبوتات تُعيَّن الآن.
- اختبار Editor جديد `MainMenuGeometry` يبني القائمة ويتحقق هندسيًا من كل
  عنصر (Editor checks 22 → 237).
- محاولة رسم القائمة في البيئة (بناء Linux تحت Xvfb/llvmpipe) لم تُنتج إطارًا؛
  التحقق المرئي ينتظر الجهاز.
- APK versionCode 11: 445,214,315 بايت، SHA256 `f224f8a7db03c68b694b0252b1fe4f75a5de55478bb1414905278b1a06bf2b21`. التفاصيل في `docs/UNITY-DEV10.md`.

## 0.1.0-dev.9 — 2026-09-22 — التحريك الهيكلي والمحركات والتعزيزات و14 سلاحًا وTDM/CTF

- تحريك هيكلي للشخصيات الـ11 من IQM (idle/run/strafe/jump/die) عبر
  `SkinnedMeshRenderer` مع توقيت `.framegroups` الأصلي؛ الجثث تبقى حتى الظهور.
- محركات: `func_door` (اقتراب، lip/speed/wait/START_OPEN)، `func_rotating`،
  `func_bobbing`، `func_plat`؛ اللاعب والبوتات يُحملون على المنصات.
- تعزيزات Strength (×3) وShield (÷3) 30 ث من `item_strength/item_invincible`.
- أسلحة 10–14: Rifle, Mine Layer (ألغام لاصقة + تفجير), Arc (شعاع), Fireball,
  Grappling Hook؛ نماذج عرض أصلية بخاماتها (14/14)؛ شريط أسلحة يتمدد للمملوك.
- أنماط DM/TDM/CTF مع فرق ولا ضرر بين الزملاء وتلوين فريق؛ أعلام CTF من
  `item_flag_team1/2` (18 قاعدة) بنموذج flags.md3 وأصوات ctf؛ حد 10 تسجيلات.
  قائمة: الوضع، ALL WEAPONS، عدد البوتات 1–7. البوتات تستهدف أي عدو وتلعب CTF.
- الاختبارات: Editor 22، تكامل لعب 234 (55 جديدة)، محتوى 194 — كلها ناجحة.
- APK versionCode 10: 445,214,075 بايت، SHA256 `74be4c09d3caaa56eaeb4480b1afa9b0f571645278a9aff8d8fc6d0db828e9d9`. لا اختبار جهاز؛ لا شبكة.
  التفاصيل في `docs/UNITY-DEV9.md`.

## 0.1.0-dev.8 — 2026-09-21 — الترسانة الكاملة وأزرار الأسلحة وبوتات أذكى

- تسعة أسلحة Xonotic (Blaster, Shotgun, Machine Gun, Mortar, Electro, Crylink,
  Vortex, Hagar, Devastator) بإطلاق أولي/ثانوي، أربع مخازن ذخيرة مشتركة بحدودها،
  تبديل تلقائي، ونماذج العرض الأصلية `v_*` بخاماتها وأصواتها (ثابتة بلا تحريك).
- كيانات `item_shells/bullets/rockets/cells` و`weapon_*` تُستورد كعناصر قابلة
  للالتقاط في كل الخرائط (Boil: 36/37 عنصرًا، 7 أسلحة؛ إجمالي 2,111 عنصرًا
  حيًا بدل 1,562، و28 زينة فقط).
- شريط أسلحة لمسي أعلى الشاشة، مفاتيح 1–9 وعجلة الفأرة، قراءة ذخيرة كبيرة،
  إشعارات التقاط/قتل، وميض ضرر، علامة إصابة، تقريب Vortex.
- بوتات: خط رؤية، طلب العناصر، اختيار السلاح حسب المسافة، استباق الهدف، خطأ
  تصويب متدرج، حركة جانبية وقفز.
- الاختبارات: Editor 22، محتوى 158، تكامل لعب 151 — كلها ناجحة.
- APK versionCode 9: 421,732,418 بايت، SHA256 `ea07334992740af610eccb2458b61fbbddbf2f6c869c71e90f9b8c2449a070a3`. لا اختبار جهاز.
  التفاصيل في `docs/UNITY-DEV8.md`.

## 0.1.0-dev.6 — 2026-09-21 — ملء الخرائط بالأجزاء والنماذج الأصلية

- استيراد النماذج الداخلية المرئية (`func_wall/func_door/func_rotating/...`) كهندسة
  ثابتة بدل تجاهلها (كانت سبب "المناطق الفارغة")، ونماذج الزينة MD3
  (`misc_gamemodel/misc_breakablemodel`) في مواضعها.
- إصلاح قصّ ألفا كان يُفعَّل على كل سطح مُضاء بخريطة إضاءة (`blendfunc filter`).
- عناصر الالتقاط بنماذج Xonotic الأصلية؛ الأسلحة/التعزيزات غير المدعومة تُعرض
  كزينة غير قابلة للالتقاط.
- 11 نموذج لاعب رسمي (IQM) بوضع idle ثابت وخاماتهم الأصلية للبوتات بدل الكبسولات.
- إصلاح حفظ المشاهد الذي كان يعيد نماذج العناصر الأصلية إلى كرات التطوير؛
  اختبار `content-test` يعيد فتح المشاهد ويفحص مراجع الأصول. تجنب ضغط ETC2
  للخامات ذات mipmaps غير الملائمة؛ حفظ GUID عند إعادة استيراد النماذج.
- تصحيح مواضع الأجزاء الداخلية ذات محور أصل (`origin`): تُطبق إزاحة
  الكيان على الشبكة المحلية، مع فحص جميع المراجع المحفوظة مقابل BSP.
- APK versionCode 7: 400,459,348 بايت، SHA256
  `ef140281d7639840c9b3d75b9631c1b28fad5784bdd73e32f56ccd6905ee7be5`.
  اجتازت القائمة +29 خريطة اختبار التشغيل بلا رسوميات (30/30، صفر أخطاء)؛
  157 فحص محتوى محفوظ و22 Editor و154 Python ناجحة.
- لا حركة هيكلية ولا محركات متحركة بعد؛ لا اختبار جهاز. التفاصيل في `docs/UNITY-DEV6.md`.

## 0.1.0-dev.5 — 2026-09-21 — أول APK يجمع كل الخرائط الرسمية

- versionCode 6: إصلاح استثناء Input Submit في القائمة الرئيسية، قبول نقاط
  ظهور team/race/attacker/defender (10 خرائط كانت بلا ظهور)، وأداة
  `all-maps-playtest` تشغّل القائمة وكل الخرائط في Play Mode وتسجّل الأخطاء.

- استيراد 29 خريطة رسمية من Xonotic 0.8.6 في مشاهد مستقلة مع قائمة رئيسية
  (كروت معاينة) وموسيقى الخريطة الأصلية وزر رجوع للقائمة من شاشة الإيقاف.
- ضغط الخامات المستوردة (ETC2 + mipmaps) ومشاركتها بين الخرائط؛ الحجم
  322,882,281 بايت بدل >1 GB. تنظيف UV تالفة في BSP بدل فشل الاستيراد.
- APK محلي متحقق `my-xonotic-full.apk`، versionCode 5، بصمة
  `0e34d259539458ff091f100fb78648530f0ccdf72f693126bb73613db54c887f`؛
  الإيصال في `docs/unity-dev5-build-2026-09-21.json`. لا اختبار جهاز.
- ليست اللعبة الكاملة: كل الخرائط Deathmatch أوفلاين تقريبي، 3 أسلحة
  تقريبية، لا شخصيات/حركات/أنماط/شبكة. التفاصيل في `docs/UNITY-DEV5.md`.

## 0.1.0-dev.4 — 2026-09-21

- عناصر صحة/درع/صواريخ في مواضع Boil الأصلية: 25 عنصرًا، بأشكال تطوير
  مؤقتة؛ لا استبدال الذخيرة غير المدعومة بأسلحة تجريبية.
- جلسة Deathmatch أوفلاين بحد قتل/وقت ونتيجة وتجميد وإعادة، واختبار
  فيزيائي فعلي داخل Unity للالتقاط والعودة والتوقف ونهاية المباراة.
- جرد آمن لكل 31 ملف BSP رسمي (يشمل ملف تهيئة داخليًا)، مع بوابات
  اكتمال موثقة؛ لا يعني الجرد أن جميع الخرائط قابلة للعب.
- APK محلي متحقق، 72,377,724 بايت؛ إيصال البناء وبصمته في
  `docs/unity-dev4-build-2026-09-21.json`. لا اختبار جهاز جديد.
- ليست اللعبة الكاملة. الاختبار المرئي وعلى Android والتراخيص منفصلة.

## 0.1.0-dev.3 — 2026-09-21 — APK تطوير Boil مبني محليًا

- فصل مواد السماء عن صور معاينة المحرّر، واستعمال الصور الأصلية الست مع
  اختبار استيراد مستقل. اتجاه الصور والحواف لم يُثبتا بفحص بصري.
- دعم استيراد MD3 ثابت للإطار الأول، مع إبقاء IQM حسب توقيع الملف لا امتداده.
- تعميم تحويل خامات DDS المختارة إلى PNG مع بصمات المصدر والنتيجة.
- تشديد مشغّل البناء: إيصال حديث مرتبط بالمحاولة وبصمة APK وحجمه؛ لا قبول
  لنتيجة قديمة أو خروج مبكر من Unity كنجاح. تقارير الاختبارات القديمة تُحذف.
- استرجاع أرشيف Xonotic 0.8.6 الكامل والتحقق من SHA512 الرسمي؛ استخراج
  مجموعة مختارة فقط، وليس تحويل كامل اللعبة أو تضمين الأرشيف في التطبيق.
- متابعة حالة البناء والاختبارات وحدودها في `docs/UNITY-CONTINUATION.md`.
- APK متحقق التوقيع والبصمة، 72,332,393 بايت؛ لم يُختبر على الهاتف.
  شهادة التطوير مختلفة عن dev.2، لذا لا يدعم تحديثها فوق التثبيت السابق.

## 0.1.0-dev.2 — 2026-09-21 — نسخة Boil التجريبية السابقة

- خريطة Boil الأصلية وخاماتها وإضاءتها مع منطق لعب تقريبي ونموذج Blaster ثابت.
- APK محلي ARM64/IL2CPP/GLES3، versionCode 2، حجمه 51,114,182 بايت.
- البصمة: `3f82420161a2b23388b46eb8a4d283e721ff16321681830cd11d30bf98f1d753`.
- أبلغ المستخدم أنه اختبر هذه النسخة وأرسل تسجيلًا؛ ليس ذلك تحققًا شاملًا من
  كل وظائفها، ولا اختبار جهاز للنسخة الجديدة.

## 0.1.0-dev.1 — 2026-09-20 — أول APK مبني محلياً (ساحة تطوير فقط)

- تفعيل Unity 2022.3.62f3 محلياً ونجاح استيراد المشروع وترجمته (0 أخطاء C#).
- اختبارات المحرر: `LocalTests.Run` → 14 PASS (تتطلب توليد fixtures أولاً:
  `python3 tools/content/pk3_tool.py fixture --out-dir tests/fixtures/generated`).
- إصلاح بناء بطيء: كان shader `Standard` مثبّتاً في Always Included Shaders فأنتج
  24,576 نسخة. أُزيل التثبيت (يبقى احتياطياً فقط)، وأُضيف `ShaderVariantStripper`
  (IPreprocessShaders) وإعدادات stripping للضباب/lightmap/instancing.
- أول APK: `com.ayoub.myxonotic` 0.1.0-dev.1، ARM64/IL2CPP/GLES3، 18.5 MB،
  بناء كامل في ~5 دقائق. المحتوى: ساحة التطوير الأصلية فقط — ليس Xonotic الكاملة.
- التزام ملفات ProjectSettings والمشهد `DevelopmentArena.unity` المولّدة.

**لم يُتحقق بعد:** التشغيل على جهاز Android حقيقي.

## 0.1.0-dev.1 — 2026-09-20 — مصدر تطوير، بلا APK

- تأسيس مشروع Unity 2022.3.62f3 وأدوات بناء Android محلية فقط.
- كود ساحة اختبار مستقل للحركة واللمس والقتال والخصوم والصحة والدرع والـHUD.
- قارئ IBSP v46 وتوليد هندسة worldspawn وpatches ونقاط ظهور مع حدود أمان.
- إدخال 207 ملفات Xonotic أصلية (203,918,053 بايت): خرائط ومصادر وخامات
  ونماذج وصوت وBlender/PSD وتراخيص وprovenance، منفصلة عن كود MIT؛ الدفعة
  الأولى ليست كامل موارد اللعبة، وبعض مطابقة المصادر المصدّرة غير مؤكدة.
- اختبارات C#/Python وفحص host API وGUID، ومشغّل Editor/Play Mode مكتوب.
- توثيق بنية المشروع، البناء، الاختبارات، الخطة وتسليم الوكلاء.

**التحقق:** C# host compilation و53 assertion و57 اختبار Python وبصمات الموارد
نجحت؛ Unity Editor لم
يتجاوز تفعيل الرخصة (401). لم يُبنَ APK ولم تُختبر اللعبة على Android.

**الفجوات:** منطق Xonotic الكامل، مواد/إضاءة/نماذج مستوردة في Unity، تطابق الحركة
والأسلحة، modes والشبكة، الأداء والجهاز، ومراجعة تضمين الموارد في المنتج النهائي.

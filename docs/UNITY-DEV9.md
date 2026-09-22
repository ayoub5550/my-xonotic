# 0.1.0-dev.9 — التحريك الهيكلي، المحركات، التعزيزات، 14 سلاحًا، TDM/CTF

التاريخ: 2026-09-22. الفرع: `feat/unity-dev9-modes` (فوق `feat/unity-dev8-weapons`).
يُنجز هذا الإصدار ما ورد في «ما لم يُنجز» في `UNITY-DEV8.md` عدا الشبكة
واختبار الجهاز (خارج إمكانيات بيئة البناء الحالية). ليس إصدارًا للاعبين ولا
ادعاء اكتمال Xonotic؛ كل الأدلة headless ولا اختبار على هاتف.

## ما الجديد

### التحريك الهيكلي للشخصيات (`Editor/Import/IqmCharacterImporter.cs`, `Runtime/Gameplay/CharacterRig.cs`, `CharacterAnimator.cs`)

- يقرأ المستورد المفاصل (60) والوضعيات لكل إطار (740 إطارًا في erebus) من IQM
  ويحوّلها إلى فضاء Unity: الإزاحة `(x, z, y)/32`، الرباعي `(x,y,z,w) → (−x,−z,−y,w)`
  (انعكاس المحورين يعكس اتجاه الدوران). يكتب `<name>_Skinned.asset` (شبكة
  bind-pose بأوزان العظام وbindposes) و`<name>_Rig.asset` (المفاصل والمقاطع
  والوضعيات؛ 740×60×10 عددًا). الشبكة الثابتة `<name>_Mesh` تبقى كاحتياط.
- توقيت المقاطع (fps وتكرار) من ملفات `models/player/<name>.iqm.framegroups`
  الأصلية (idle 5fps، run 29fps، dieone 30fps ...)؛ IQM نفسه يحمل rate=0.
- `CharacterAnimator` يبني تسلسل العظام تحت `SkinnedMeshRenderer` (تجليد GPU)
  ويختار المقطع من حركة البوت: idle / run / runbackwards / strafeleft /
  straferight / jump، و`dieone`/`dietwo` ثم `deadone`/`deadtwo` عند الموت (الجثة
  تبقى حتى الظهور)، و`shoot` قصير عند الإطلاق البطيء. استيفاء خطي/Slerp بين
  الإطارين. لا مزج علوي/سفلي كما في Xonotic.
- اختبار: التجليد بمصفوفات العظام في فضاء Unity يطابق التجليد المخبوز على
  CPU لإطار run بخطأ أقصى 0.00000 م (`Dev9Tests.Rig_ConversionMatchesBakedSkinning`)،
  واختبار المحتوى يتحقق من rig/skinned لكل الشخصيات الـ 11 مع مقاطع idle/run/dieone.

### المحركات (`Runtime/Gameplay/Mover.cs`, `Runtime/Content/ImportedSubmodel.cs`)

- المستورد يسجل في `ImportedSubmodel` مفاتيح الكيان (angle/angles/speed/lip/wait/
  height/phase/spawnflags/dmg/target) وحدود الجزء المحلية بوحدات Unity.
- `Mover.Attach` عند بداية الساحة يُلحق سلوكًا بكل:
  `func_door`/`func_door_secret` (22+1 في الخرائط): تنزلق باتجاه `angle`
  (−1 أعلى/−2 أسفل/ياو) مسافة الامتداد − lip (افتراضي 8 وحدات)، سرعة 100 و/ث،
  تفتح عند اقتراب لاعب/بوت ضمن حقل 60 وحدة حول الحدود، تغلق بعد `wait` (3 ث،
  −1 تبقى مفتوحة)، تعود للفتح إذا دخل أحد أثناء الإغلاق، `START_OPEN` مدعوم.
  `func_rotating` (35): دوران مستمر حول Z (Unity Y) أو X/Y حسب spawnflags 4/8،
  100°/ث افتراضيًا، الإشارة معكوسة (يمين↔يسار اليد).
  `func_bobbing` (15): تذبذب جيبي بارتفاع 32 وحدة ودورة 4 ث وphase.
  `func_plat` (0 في الخرائط الرسمية): ترتفع عند الاقتراب ثم تنزل.
- الأجسام تحمل `Rigidbody` حركيًا؛ اللاعب والبوتات يقرؤون `LastDelta` لمن
  يقفون عليه (`OnControllerColliderHit`) فيتحركون مع المنصة.
- تقريب: الأبواب كلها ذات `targetname` في الخرائط الرسمية (تفتح أصلاً عبر
  trigger/button)؛ هنا تفتح بالاقتراب لأن سلاسل trigger→target غير منفذة.
  `func_train` (2) و`func_button` (1) ما زالا ثابتين.

### التعزيزات (`Actor.cs`, `Pickup.cs`)

- `item_strength` (20 في الخرائط) → Strength 30 ث: الضرر ×3.
  `item_invincible/shield` (0 في الخرائط الرسمية، مدعوم) → Shield 30 ث: الضرر ÷3.
  عودة 120 ث. تُلغى عند الموت. أصوات `powerup/powerup_shield/poweroff` ومؤقت في HUD.

### الأسلحة 10–14 (`WeaponController.cs`, `Projectile.cs`, `GrapplingHook.cs`)

| السلاح | الذخيرة | أولي | ثانوي |
|---|---|---|---|
| Rifle | Bullets (40) | hitscan 80 ضرر كل 1.2 ث، 10 رصاصات | 20 ضرر كل 0.15 ث، انتشار 1.2° |
| Mine Layer | Rockets (20) | لغم يلتصق بالجدار/الأرض، يتسلح بعد 1 ث، ينفجر (40، 4.7 م) قرب عدو ضمن 1.9 م؛ 3 ألغام كحد | تفجير الألغام |
| Arc | Cells (25) | شعاع مستمر 15 ضرر/0.2 ث حتى 25 م، خلية لكل دفعة | قذيفة 30 ضرر |
| Fireball | بلا | كرة 200 ضرر انفجار 6.25 م كل 2 ث | 3 كرات نار ترتد 40 ضرر |
| Grappling Hook | بلا | خطاف 62 م/ث يشد اللاعب 25 م/ث ما دام الزر مضغوطًا (8 ث كحد) | قنبلة ترتد 50 ضرر |

- نماذج العرض الأصلية `v_campingrifle/v_minelayer/v_arc/v_fireball/v_hookgun.md3`
  بخاماتها (`textures/sniperrifle|minelayer|arc|fireball|hookgun`)، مع أسماء
  بديلة لسلاحين يسميان سطحهما `shotgun`/`mesh`. `weapon-manifest.json`: 14/14 و16/16 سطحًا.
- شريط الأسلحة: 9 خانات ثابتة + خانة لكل سلاح إضافي مملوك (مفتاح 0 يدور
  بينها). التقاط سلاح إضافي لا يبدل تلقائيًا. البوتات لا تستخدم الخطاف ولا الألغام.
- الخرائط الرسمية تحوي `weapon_minelayer` واحدًا فقط ولا تحوي Rifle/Arc/Fireball/
  Hook؛ لذلك أُضيف خيار «ALL WEAPONS» (كل الأسلحة والذخيرة عند الظهور) في القائمة.

### الأنماط والفرق (`MatchSettings.cs`, `CtfFlag.cs`, `ArenaBootstrap.cs`, `MatchSession.cs`, `Menu/MainMenu.cs`)

- القائمة الرئيسية: DM / TDM / CTF، ALL WEAPONS، عدد البوتات 1–7 (PlayerPrefs).
- الفرق: اللاعب أحمر؛ البوتات تتوزع أزرق/أحمر بالتناوب؛ لا ضرر بين الزملاء؛
  تلوين الشخصيات بلون الفريق (`_Tint`)؛ البوتات تستهدف أقرب عدو مرئي (بوتًا أو لاعبًا).
- TDM: مجموع قتل الفريق هو النتيجة؛ حد القتل 20 على الفريق (`MatchSession.ScoreOf`).
- CTF: `item_flag_team1/2` تُستورد كقواعد أعلام (18 في 9 خرائط) بنموذج
  `models/ctf/flags.md3` الأصلي. الأخذ/الإسقاط عند الموت/الإرجاع باللمس أو بعد
  30 ث/التسجيل بلمس علم الفريق وهو في قاعدته؛ حد 10 تسجيلات؛ أصوات `sound/ctf`
  الأصلية وإشعارات HUD. البوتات: تسعى للعلم المعادي، تعود بقاعدتها إذا حملته،
  ترجع علمها الساقط. في خريطة بلا أعلام يُظهر HUD تحذيرًا.
- ملاحة البوتات ما زالت خطًا مستقيمًا بلا waypoints؛ ستتعلق في الخرائط المعقدة.

## الاختبارات

- Editor 22/22؛ تكامل اللعب 234/234 (منها 55 اختبار dev.9: فرق/تعزيزات/
  أسلحة/محركات/CTF/rig)؛ Boil: 37/37 عنصرًا حيًا (item_strength أصبح حيًا).
- اختبار المحتوى 194/194: rig كل شخصية، 20 Strength، 18 قاعدة علم، 58 علامة
  محرك (door/rotating/bobbing ذات مثلثات مرئية)، 2138 عنصرًا حيًا + زينة واحدة
  (`weapon_tuba`) = 2139 ثابت. 366 مرجع خامة غير محلول كما في dev.6 (مُبلَّغ).

## المتطلبات قبل `prepare-maps` على نسخة جديدة

بالإضافة إلى أوامر `UNITY-DEV8.md`:

```
python3 tools/content/prepare_unity_textures.py ExternalContent/data \
  --output ExternalContent/decoded \
  --texture textures/arc --texture textures/fireball --texture textures/hookgun \
  --texture models/ctf/flag --texture models/ctf/banner --texture models/ctf/glow
python3 tools/local_unity.py weapons     # يولّد نماذج الأسلحة الـ14 وأصوات ctf/plats
```

## ملف APK (versionCode 10)

| البند | القيمة |
|---|---|
| الملف | `my-xonotic-full.apk`، 445,214,075 بايت |
| SHA256 | `74be4c09d3caaa56eaeb4480b1afa9b0f571645278a9aff8d8fc6d0db828e9d9` |
| الحزمة | `com.ayoub.myxonotic`، versionCode 10، versionName `0.1.0-dev.9`، arm64-v8a |
| Unity | 2022.3.62f3، النتيجة Succeeded، 0 أخطاء / 31 تحذيرًا |
| التوقيع | APK v2 debug؛ شهادة SHA256 `3f009959f60813ff4853200ead16fc03e491c89255c0f83d65856e47e0dc2a0c` (نفس dev.8) |
| ملاحظة التوقيع | تُحدَّث فوق dev.8 مباشرة؛ أمّا dev.6 وما قبله (شهادة `2ef0784b…`) فيجب إزالته قبل التثبيت |
| نماذج الأسلحة | `weapon-manifest.json`: 14/14 سلاحًا، 16/16 سطحًا وُجدت لها خامات (`resolved: true`) |
| اختبار جهاز | لا شيء؛ CRC (`unzip -t`) وapksigner وaapt فقط؛ لا اختبار GPU |

## ما لم يُنجز

- الشبكة (multiplayer) غير منفذة.
- لم يُجرَّب على جهاز Android ولا رُسم على GPU؛ الأدلة كلها headless.
- تحريك الأسلحة (نماذج العرض ثابتة)، مزج التحريك العلوي/السفلي، حركة اللاعب
  من منظور ثالث.
- `func_train`/`func_button`/سلاسل trigger→target؛ الأبواب تفتح بالاقتراب فقط.
- أنماط أخرى (Domination/Onslaught/Nexball)، Seeker/HLAC/Porto/Tuba/Vaporizer، jetpack.
- ملاحة waypoints للبوتات.

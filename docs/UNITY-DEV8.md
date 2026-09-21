# 0.1.0-dev.8 — الترسانة الكاملة (9 أسلحة) وأزرار الأسلحة وبوتات أذكى

التاريخ: 2026-09-21. الفرع: `feat/unity-dev8-weapons` (فوق `feat/unity-dev5-full-game`).
يكمل هذا الإصدار بوابة «الأسلحة» في `FULL-GAME-GATES.md`: الترسانة الأصلية
التسعة بنماذج العرض الأصلية، إطلاق أولي/ثانوي، التقاط الأسلحة والذخيرة من
مواضع الخرائط الأصلية، شريط أسلحة لمس، وبوتات تختار السلاح وتلتقط العناصر.
ليس إصدارًا للاعبين ولا ادعاء اكتمال Xonotic؛ لا اختبار جهاز لهذه الحزمة.

## ما الجديد

### الأسلحة (`Runtime/Gameplay/WeaponController.cs`, `Projectile.cs`)

| السلاح | الذخيرة | أولي | ثانوي |
|---|---|---|---|
| Blaster | بلا | قذيفة 20 ضرر، دفع ذاتي (laser-jump) | نفس القذيفة بدفع أقوى |
| Shotgun | Shells (15/التقاط) | 14 حبة × 3 ضرر، انتشار 4.5° | ضربة قريبة 80 ضرر |
| Machine Gun | Bullets (60) | 10 ضرر كل 0.1 ث | رشقة 3 طلقات دقيقة |
| Mortar | Rockets (15) | قنبلة بالستية 55 ضرر، انفجار 110 وحدة | قنبلة ترتد، فتيل 2.5 ث |
| Electro | Cells (25) | قذيفة 40 ضرر سريعة | كرة ترتد وتنفجر بعد 4 ث |
| Crylink | Cells (25) | 4 قذائف × 12 ضرر | قذيفة واحدة 30 ضرر |
| Vortex | Cells (25) | hitscan 80 ضرر، 6 خلايا | تقريب (FOV 30) |
| Hagar | Rockets (25) | صاروخ صغير 25 ضرر كل 0.16 ث | رشقة 4 صواريخ |
| Devastator | Rockets (15) | صاروخ 80 ضرر، انفجار 110 وحدة | تفجير الصاروخ عن بعد |

- الحدود: Shells 60، Bullets 320، Rockets 160، Cells 180. يبدأ اللاعب بـ Blaster +
  Shotgun و15 shell ويظهر حاملًا Shotgun. التقاط سلاح مملوك يمنح ذخيرته فقط.
- التبديل التلقائي عند التقاط سلاح أقوى، والرجوع لأفضل سلاح متاح عند نفاد
  الذخيرة. مفاتيح 1–9 وعجلة الفأرة على الحاسوب؛ شريط أسلحة لمس في أعلى الشاشة.
- الأرقام تقريب من قيم Xonotic 0.8.6 (balance-xonotic.cfg) بعد التحويل إلى وحدات
  Unity؛ ليست مطابقة رقمية موثقة.

### النماذج والأصوات (`Editor/Import/IqmWeaponImporter.cs`)

- نماذج العرض الأصلية التسعة (`v_laser.iqm`, `v_gl.iqm`, `v_electro.iqm`,
  `v_crylink.iqm`, `v_shotgun.md3`, `v_uzi.md3`, `v_nex.md3`, `v_hagar.md3`,
  `v_rl.md3`) مع خاماتها المفكوكة إلى `ExternalContent/decoded/textures`.
  ملفات IQM متعددة الشبكات تُبنى عبر `Md3WeaponModelBuilder` مع أسماء بديلة
  للأسطح غير المسماة. الناتج في `Assets/MyXonotic/Generated/Weapons/` مع
  `weapon-manifest.json` الذي يسجل كل سطح وخامته أو سبب عدم الحل.
- أصوات إطلاق/اصطدام أصلية لكل سلاح (`sound/weapons/*`) وأصوات الالتقاط
  والإصابة والقتل (`sound/misc/*`) عبر `WeaponAudio.cs`؛ تأثيرات اصطدام بسيطة
  في `ImpactEffects.cs`. النماذج ثابتة (بلا حركة هيكلية).

### العناصر (`Editor/Import/BspPickupImporter.cs`, `Pickup.cs`)

- كيانات `item_shells/bullets/rockets/cells` و`weapon_*` التسعة تُستورد
  كعناصر قابلة للالتقاط (كانت زينة في dev.6). Boil: 36 من 37 عنصرًا (يبقى
  `item_strength` زينة)، منها 7 أسلحة. العودة: ذخيرة 15 ث، سلاح 20 ث، صحة كبرى 30 ث.

### الواجهة واللمس (`Hud.cs`, `TouchLayout.cs`, `Player.cs`)

- شريط أسلحة يعرض الأسلحة المملوكة والذخيرة، قراءة ذخيرة كبيرة، صحة/درع
  ملونان، إشعارات التقاط/قتل، وميض ضرر، علامة إصابة، شبكة تصويب للتقريب.

### البوتات (`Bot.cs`)

- فحص خط الرؤية، طلب العناصر القريبة، اختيار السلاح حسب المسافة، استباق
  الهدف، خطأ تصويب متدرج لكل بوت، حركة جانبية وقفز، سلاح إضافي عشوائي عند
  الظهور (معطل في وضع الاختبار). لا توجد ملاحة waypoints بعد.

## الاختبارات

- Editor: `PickupTests` (قواعد الترسانة: مرة واحدة ثم ذخيرة فقط)،
  `GameplayIntegrationTests` (36 عنصرًا/7 أسلحة في Boil)،
  `ContentRegressionTests` (مجموع 2139 عنصر+زينة ثابت؛ العناصر > 1562).
- النتائج الفعلية للبناء مسجلة في `CHANGELOG.md` وإيصال البناء
  `docs/unity-dev8-build-2026-09-21.json`.

## ملف APK (versionCode 9)

| البند | القيمة |
|---|---|
| الملف | `my-xonotic-full.apk`، 421,732,418 بايت |
| SHA256 | `ea07334992740af610eccb2458b61fbbddbf2f6c869c71e90f9b8c2449a070a3` |
| الحزمة | `com.ayoub.myxonotic`، versionCode 9، versionName `0.1.0-dev.8`، arm64-v8a |
| Unity | 2022.3.62f3، النتيجة Succeeded، 0 أخطاء / 31 تحذيرًا |
| التوقيع | APK v2 debug؛ شهادة SHA256 `3f009959f60813ff4853200ead16fc03e491c89255c0f83d65856e47e0dc2a0c` |
| ملاحظة التوقيع | شهادة debug مختلفة عن dev.6 (`2ef0784b…`) لأن البناء جرى في بيئة أخرى؛ **يجب إزالة dev.6 قبل التثبيت** وإلا رفض Android التحديث |
| نماذج الأسلحة | `weapon-manifest.json`: 9/9 أسلحة، كل السطوح (11) وُجدت لها خامات (`resolved: true`) |
| اختبار جهاز | لا شيء؛ CRC (`unzip -t`) وapksigner وaapt فقط |

## المتطلبات قبل `prepare-maps` على نسخة جديدة

```
python3 tools/content/pk3_tool.py fixture --out-dir tests/fixtures/generated
python3 tools/content/prepare_unity_textures.py ExternalContent/data \
  --output ExternalContent/decoded \
  --texture erebus --texture shadowhead --texture gak --texture gakarmor \
  --texture ignis --texture ignishead --texture nyx --texture pyria \
  --texture pyriahair --texture seraphina --texture umbra
python3 tools/content/prepare_unity_textures.py ExternalContent/data \
  --output ExternalContent/decoded \
  --texture textures/items/a_bullets --texture textures/items/cellammo \
  --texture textures/items/explosiveammo --texture textures/items/explosiveammo_icon_01 \
  --texture shellsammo --texture models/items/red --texture invincible --texture strength \
  --texture textures/sniperrifle --texture textures/minelayer --texture textures/tuba \
  --texture textures/crylink_new --texture textures/rl_new \
  --texture textures/shotgun2 --texture textures/uzi --texture textures/grenadelauncher \
  --texture textures/electronew --texture textures/nexgun --texture textures/hagar \
  --texture textures/glsight01 --texture textures/shotgun_sight --texture textures/electro_plasma \
  --texture models/weapons/laser
```

## كيفية إعادة الإنتاج

```
export UNITY_EDITOR=/path/to/Unity
export XONOTIC_CONTENT_ROOTS=ExternalContent/decoded:ExternalContent/worlddecoded:ExternalContent/maps:ExternalContent/data:ThirdParty/Xonotic/maps-pk3
export XONOTIC_MAPS_ROOT=ExternalContent/maps
python3 tools/local_unity.py compile
python3 tools/local_unity.py prepare-maps
python3 tools/local_unity.py content-test
python3 tools/local_unity.py test
python3 tools/local_unity.py gameplay-test
XONOTIC_ALL_MAPS=1 XONOTIC_VERSION_CODE=9 python3 tools/local_unity.py android
```

## ما لم يُنجز

- حركة هيكلية للشخصيات والأسلحة؛ المحركات (`func_door/plat`) ما زالت ثابتة.
- لا أوضاع لعب سوى deathmatch مع بوتات؛ لا شبكة.
- Rifle/Minelayer/Arc/Fireball/Hook والتعزيزات (strength/shield) غير مدعومة.
- لم يُجرَّب على جهاز Android ولا رُسم على GPU؛ الأدلة كلها headless.

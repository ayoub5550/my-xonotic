# dev.6 — ملء الخرائط: أجزاء البناء، نماذج الزينة، العناصر والشخصيات الأصلية

**التاريخ:** 2026-09-21 · **الفرع:** `feat/unity-dev5-full-game` · **الإصدار:** `0.1.0-dev.6`

## APK الإصلاحات

| البند | النتيجة المتحققة |
|---|---|
| الملف | `my-xonotic-full.apk`؛ خارج Git |
| versionCode | 7 |
| الحجم | 400,459,348 بايت (400.5 MB) |
| SHA256 | `ef140281d7639840c9b3d75b9631c1b28fad5784bdd73e32f56ccd6905ee7be5` |
| الحزمة | `com.ayoub.myxonotic` |
| الهدف | ARM64 / IL2CPP / GLES3؛ Android API 26–36 |
| مدة أمر البناء | 332.3 ثانية، بما فيه إعادة استيراد الخرائط |
| نتيجة Unity | نجح؛ 0 أخطاء / 30 تحذير بناء |
| سلامة ZIP | فُحص CRC لكل المداخل؛ لا مدخل تالف |
| التوقيع | APK v2 debug؛ شهادة SHA256 `2ef0784b3aad4046b2add35f9da40f424ba42607d95897807bdc0b6763bd408a` |
| مقارنة التوقيع | نفس شهادة dev.3/dev.4/vc5/vc6، مختلفة عن dev.2؛ لم يُختبر التحديث على هاتف |

تحذيرات البناء الثلاثون تخص عدم تحديث Ambient/Reflection Probes لغياب
جهاز الرسم في وضع `-nographics`. لا يعني ذلك أن الإضاءة اختُبرت بصريًا.
لم تتكرر رسائل فشل ضغط ETC في البناء النهائي.

البناء من شجرة عمل معدلة فوق `deea0ed6`؛ `revision` في إيصال البناء يشير
إلى **الأساس السابق**، لا commit نظيف يحتوي الإصلاحات. بصمات ملفات المصدر
المستخدمة في `unity-dev6-source-2026-09-21.json`.
الأبنية الداخلية السابقة لـ vc7 رُفضت قبل التسليم بسبب حفظ كرات الالتقاط
وإغفال إزاحة محاور الأجزاء الداخلية؛
هذه الوثيقة تخص البصمة أعلاه فقط.

## لماذا هذه الموجة

التحقيق في المناطق الفارغة والشخصيات المفقودة في dev.5 كشف مسارات استيراد
كانت تتجاهل أجزاء من المحتوى، إضافة إلى خطأ في الخامات. الإصلاحات التالية
مثبتة بالكود وتقارير الاستيراد، **وليست تأكيدًا بصريًا أن جميع الفراغات اختفت**.
لا يوجد اختبار هاتف موثق لهذه النسخة.

| النقص المحتمل | السبب في الكود | الإصلاح |
|---|---|---|
| جدران/أبواب/منصات/هياكل غائبة (مناطق فارغة) | `BspImportPipeline` كان يبني **model 0 (worldspawn) فقط** ويتجاهل كل النماذج الداخلية `*N` التي تشير إليها `func_wall/func_door/func_rotating/func_bobbing/func_illusionary/...` (~140 قطعة عبر الخرائط) | `BuildVisibleSubmodels`: كل كيان في `VisibleSubmodelClasses` يُبنى كهندسة ثابتة في موضعه الأصلي (الأبواب مغلقة، المحركات ساكنة) مع `MeshCollider` للأجزاء الصلبة؛ العلامة `ImportedSubmodel` تحفظ classname/targetname لنظام محركات لاحق. الـ trigger_* تبقى غير مرئية كما في الأصل |
| أنابيب/مصابيح/صناديق/أشجار غائبة | كيانات `misc_gamemodel` و`misc_breakablemodel` (~304 نموذج MD3، 17 ملفًا مختلفًا) لم تُستورد أصلًا | `BspMapModelImporter` جديد: يقرأ MD3 (وIQM الثابت متعدد الشبكات) عبر `Md3WeaponModelBuilder`/`IqmReader.ReadStaticAll`، يضعه بـ origin/angles/modelscale، ويشارك الشبكة بين كل التكرارات. ملفات `.obj` (12) تُسجَّل كغير مدعومة ولا تُخمَّن |
| أسطح قد تختفي أو تظهر مثقوبة | شرط "قصّ ألفا" كان يُفعَّل عند **أي** `blendfunc`، حتى `$lightmap / blendfunc filter` وأطوار الوهج الإضافية؛ هذا قد يقص خامات تحمل بيانات غير الشفافية في قناة ألفا | `StageCarvesAlpha`: القصّ فقط عند `alphaFunc` أو مزج بـ `GL_SRC_ALPHA`/`blend` في طور **غير** خريطة الإضاءة. لا تزال الشفافية تقريبية؛ لم تُتحقق النتيجة بصريًا |
| العناصر كرات ملونة | `BspPickupImporter` كان يضع كرة تطوير | نماذج العناصر الأصلية (`models/items/g_h*.md3`, `item_armor_*.md3`, `a_*.md3`) عبر `OriginalModelByClass`؛ الأسلحة/الخلايا/التعزيزات التي لا لعب لها بعد تُعرض **كزينة فقط** (`PickupDecoration`، غير قابلة للالتقاط، مذكورة في التحذيرات) بدل الغياب |
| البوتات كبسولات | لا مستورد لنماذج اللاعبين | `IqmCharacterImporter` + `IqmSkinnedDocument`: قارئ IQM نظيف (مفاصل/أوضاع/إطارات) يقيّم الإطار الأول من حركة `idle` على المعالج ويخبز الوضع في شبكة ثابتة؛ 11 نموذجًا رسميًا (erebus, gak, gakmasked, ignis, ignismasked, megaerebus, nyx, pyria, seraphina, seraphinamasked, umbra) بخاماتها الأصلية (DDS مفكوكة إلى PNG بـ `tools/content/prepare_unity_textures.py`، ثم ETC2). `CharacterModels` في وقت التشغيل يلحق النموذج بكل بوت (يتناوب على النماذج) ويحذف الكبسولة |

## ما ليس في هذه الموجة (بصراحة)

- الشخصيات **ساكنة** (وضع idle واحد): لا مشي/قفز/موت متحرك. الحركة الهيكلية
  في وقت التشغيل بوابة لاحقة.
- الأبواب/المصاعد/الدوّارات **لا تتحرك**: تُرسم في وضعها الأصلي في BSP فقط.
- نماذج `.obj` (12 كيانًا) ومفاعيل الجسيمات/الليزر/الوهج غير مستوردة.
- 23 نموذجًا داخليًا لم ينتج وجوهًا قابلة للرسم، منها سلالم وأحجام مساعدة؛
  تخطّيها لا يثبت اكتمال الاصطدام أو حركة الأبواب في تلك المواضع.
- جسم اللاعب نفسه (منظور أول) لا يزال كبسولة ظلال فقط.
- الشفافية الحقيقية غير مدعومة (قصّ ألفا 0.5 حيث يلزم فقط).
- لا اختبار جهاز لهذه البصمة؛ الأدلة أدناه من Play Mode على المضيف بلا رسوميات.
- 13 أصل خامة لنماذج منصات القفز لم يُحل (366 مرجعًا للخامات عبر المشاهد)؛
  وجود النموذج لا يعني صحة كل خاماته.
- نماذج الشخصيات الإحدى عشرة مضمنة؛ البوتات الثلاثة الحالية تختار أول ثلاثة
  نماذج من القائمة، وليست قائمة اختيار شخصيات للمستخدم.

## إصلاحات إضافية كشفها فحص ما بعد البناء

- `PersistPickupVisuals` كان يستبدل حتى الشبكات/الخامات الأصلية المحفوظة
  بأصول الكرات القديمة. صار يحفظ **الأصول المؤقتة فقط**؛ اختبار
  `content-test` يعيد فتح المشاهد المحفوظة ويفحص مسارات الشبكات وكل الخامات.
  فشل الاختبار على المشاهد القديمة قبل إعادة الاستيراد، فلا يكفي عدد
  العناصر في تقرير الاستيراد لإثبات نماذجها.
- ضغط ETC2 كان يحاول ضغط خامة 600×600 لأن حجمها الأساسي يقبل القسمة على 4،
  لكن mip 150×150 يفشل داخل Unity دون استثناء C#. الخامات غير ذات الأبعاد
  قوةً للعدد 2 تُترك RGBA32 مع mipmaps بدل ناتج ضغط مشكوك فيه.
- استيراد نماذج IQM يزيل امتداد اسم الخامة عند البحث عن shader أصلي
  (`RL.tga` → `RL`)، مع فك صور DDS المطلوبة. حفظ الأصول الجديدة يحافظ على
  GUID عند إعادة الاستيراد حتى لا تنكسر مراجع المشاهد.
- إحداثيات الأجزاء ذات `origin` ليست عالمية دائمًا: قارنت بيانات BSP
  الفعلية (مثل `afterslime *7`) فوجدت الشبكة قرب الصفر وموضع الكيان بعيدًا
  عنه. يطبق المستورد الآن إزاحة `origin` على الجزء، والاختبار يقارن موضع كل
  جزء محفوظ بالكيان الأصلي بدل الاكتفاء بعدّه.

قارئ الشخصيات في هذه المرحلة موجّه لحزمة الموارد الرسمية المتحققة، وليس
واجهة لاستيراد ملفات IQM مجهولة أو غير موثوقة.

## الأدلة

- الاستيراد: 29/29 خريطة، 0 فشل؛ 583 نقطة ظهور، 1,562 عنصر التقاط،
  304 نماذج ديكور، 117 جزءًا داخليًا ثابتًا و577 عنصر زينة؛ 11/11 شخصية.
  يوجد 2,856 تحذير استيراد، منها قيود وكيانات غير منفذة وتشخيصات معلوماتية؛
  ليست هي 30 تحذير البناء ولا أخطاء تشغيل.
- اختبارات Python: 154 ناجحة، واختبارات Editor الأساسية: 22 ناجحة.
- جولة Play Mode النهائية: **30/30 مشهدًا ناجحًا** (القائمة +29 خريطة)،
  0 أخطاء و0 تحذيرات أثناء التشغيل؛ مدة الأمر 982.4 ثانية وانتهى 18:16 UTC.
  فحص تسوية نقاط الظهور والحركة والإطلاق والتوقف، وليس الرسم أو اجتياز
  كل ممر أو اكتمال حركة الأبواب. إعادة الاستيراد للاختبار تطابق أعداد
  محتوى بناء APK، وملفات المصدر مطابقة لبصماتها.
- `unity-dev6-content-tests-2026-09-21.txt`: 157 تحققًا ناجحًا من الأصول
  المحفوظة، بينها 1,562 نموذج عنصر أصلي دون كرات بديلة، وموارد الشخصيات.
  رُصدت الخامات غير المحلولة كمعلومة صريحة، لا نجاح تطابق الخامات.
- `unity-dev6-maps-2026-09-21.json`: تقرير الاستيراد (لكل خريطة `submodels`،
  `mapModels`، `decorations`، `pickups`؛ ولكل شخصية `vertices/joints/poseFrame/materials`).
- `unity-dev6-all-maps-playtest-2026-09-21.json`: Play Mode على القائمة + 29 خريطة.
- `unity-dev6-build-2026-09-21.json`: إيصال APK versionCode 7.
- `unity-dev6-texture-provenance-2026-09-21.json`: بصمات DDS الأصلية وPNG
  المشتقة لـ24 صورة أضيفت في هذه المرحلة (التراخيص الأصلية بلا تغيير).
- تحقق عددي مستقل بـ Python من وضعية erebus: وضع الربط T (x ±34 وحدة) → إطار idle
  143 (x −13..22، z −23..44) أي الذراعان إلى الأسفل والأصل 23 وحدة فوق القدمين
  (يطابق `CharacterModels.OriginAboveFeet = 24/32`).

## كيفية إعادة الإنتاج

```
export UNITY_EDITOR=/path/to/Unity
export XONOTIC_CONTENT_ROOTS=ExternalContent/decoded:ExternalContent/worlddecoded:ExternalContent/maps:ExternalContent/data:ThirdParty/Xonotic/maps-pk3
export XONOTIC_MAPS_ROOT=ExternalContent/maps
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
  --texture textures/crylink_new --texture textures/rl_new
python3 tools/local_unity.py prepare-maps
python3 tools/local_unity.py content-test
python3 tools/local_unity.py test
python3 tools/local_unity.py all-maps-playtest
XONOTIC_ALL_MAPS=1 XONOTIC_VERSION_CODE=7 python3 tools/local_unity.py android
```

## الخطوة التالية المقترحة

1. تجربة الجهاز لـ versionCode 7 وتسجيل ما بقي فارغًا (مع لقطات شاشة).
2. حركة هيكلية للشخصيات (idle/run/jump) من نفس ملفات IQM.
3. محركات: func_door/func_plat/func_rotating بحركة فعلية.
4. الأسلحة الأصلية التسعة بنماذج العرض، ثم التقاط الأسلحة.

## محاولة الصور والفيديو

شُغلت نسخة Linux السابقة للإصلاحات تحت Xvfb وllvmpipe مع حل FMOD وOpenGL.
انتهت مهلة 1,500 ثانية دون أي PNG صالح (رمز خروج 124). لا صور ولا فيديو
لهذه النسخة، ولا يجوز عرض صور الأصل أو صور مصطنعة على أنها من APK.
الاختبار البصري واللمس والأداء على Android ما زالت مطلوبة.

# dev.5 — أول APK يجمع كل الخرائط الرسمية (2026-09-21)

هذه أول نسخة تُجمّع فيها **كل الخرائط الرسمية القابلة للاستيراد من Xonotic 0.8.6**
مع قائمة رئيسية وموسيقى في APK واحد. ليست Xonotic الكاملة: الأسلحة والشخصيات
والحركات وأنماط اللعب والشبكة ما تزال بوابات قبول منفصلة (`FULL-GAME-GATES.md`).

> ملاحظة: الشيفرة الموصوفة في `UNITY-DEV5-WIP.md` (تسعة أسلحة، Erebus،
> أبواب/منصات) **لم تُرفع إلى المستودع أبدًا**؛ الموارد والوثائق فقط. هذا الفرع
> يبدأ dev.5 الحقيقي من شيفرة dev.4 ويعيد بناء الجزء متعدد الخرائط من الصفر.

## نتيجة البناء

| البند | القيمة |
|---|---|
| الملف | `Builds/my-xonotic-full.apk` (غير ملتزم في Git) |
| الحجم | 322,882,281 بايت |
| SHA256 | `0e34d259539458ff091f100fb78648530f0ccdf72f693126bb73613db54c887f` |
| versionCode / الإصدار | 5 / `0.1.0-dev.5` |
| المعمارية | ARM64 / IL2CPP / GLES3 |
| مصدر الشيفرة | شجرة العمل فوق `147fc832` (هذا الفرع) |
| مدة الأمر الكامل | 726.8 ثانية (استيراد 29 خريطة + البناء) |
| الأخطاء / التحذيرات | 0 / 30 (Unity build) |
| التوقيع | debug، نفس شهادة dev.3/dev.4 (`2ef0784b…`) |

الإيصال: `unity-dev5-build-2026-09-21.json`. تقرير الخرائط:
`unity-dev5-maps-2026-09-21.json` (29 مطلوبة / 29 مستوردة / 0 فاشلة؛
258 نقطة ظهور، 1,562 عنصر التقاط، 4,664 تحذير استيراد أغلبها تقريب
alpha-cutout وملاحظة deluxemap). خريطة `_init.bsp` الداخلية تُتجاوز عمدًا.
`leave_em_behind` و`nexballarena` بلا عناصر التقاط لأن كياناتها خاصة
بأنماط Onslaught/Nexball غير المدعومة.

**لم يُجرَّب على جهاز Android.** أي تجربة على هاتف يجب أن تُسجَّل بهذه البصمة.

## ما أُضيف في هذا الفرع

- `Editor/FullGameBuild.cs`: `PrepareFullGame()` يستورد كل
  `ExternalContent/maps/maps/*.bsp` (يتجاوز `_*`)، مشهد لكل خريطة في
  `Assets/MyXonotic/Generated/Maps/map_<name>.unity` مع `ArenaBootstrap` +
  `MapMusic`، صور المعاينة والموسيقى (Vorbis streaming)، وفهرس
  `Generated/Resources/MapCatalog.asset`، ومشهد `Generated/MainMenu.unity`.
  فشل خريطة واحدة لا يوقف البقية؛ التقرير في `Artifacts/full-game-maps.json`.
  متغيرات: `XONOTIC_MAP_FILTER=boil,afterslime`، `XONOTIC_MUSIC_ROOT`.
- `Editor/LocalBuild.cs`: نمط `FullGame` عند `XONOTIC_ALL_MAPS=1` → ناتج
  `my-xonotic-full.apk`، `XONOTIC_VERSION_CODE` (افتراضي 5)، اسم المنتج
  `my-xonotic`.
- `Editor/Import/ImportedTexturePolicy.cs`: mipmaps + ضغط GPU للخامات
  المستوردة (`XONOTIC_TEXTURE_FORMAT=etc2|astc|none`، `XONOTIC_TEXTURE_REBUILD=1`)،
  والخامات مشتركة بين الخرائط في `Generated/Textures` حسب بصمة المحتوى.
  هذا سبب بقاء الحجم ~323 MB بدل >1 GB.
- `Runtime/Content/MapCatalog.cs`، `Runtime/Menu/{SceneFlow,MainMenu}.cs`،
  `Runtime/Gameplay/MapMusic.cs`: قائمة كروت قابلة للتمرير (معاينة/اسم/مؤلف/
  أنماط) + QUIT، وتشغيل `cdtrack` الأصلي في حلقة.
- شاشة الإيقاف: زر **MAIN MENU** (مفتاح M على سطح المكتب).
- `BspReader.SanitizeUv`: إحداثيات UV التالفة/الضخمة تُصفَّر مع تحذير واحد
  بدل رمي استثناء (كانت `afterslime` تفشل بـ `UV magnitude 4.95E+31`).
- `tools/local_unity.py`: مهمة `prepare-maps`؛ `build_output` يتبع
  `XONOTIC_ALL_MAPS`.

## إعادة الإنتاج

```bash
python3 tools/content/publish_resources.py restore
export XONOTIC_CONTENT_ROOTS=ExternalContent/decoded:ExternalContent/worlddecoded:ExternalContent/maps:ExternalContent/data:ThirdParty/Xonotic/maps-pk3
export XONOTIC_MAPS_ROOT=ExternalContent/maps
export UNITY_EDITOR=/path/to/Unity
python3 tools/local_unity.py compile           # 0 أخطاء
python3 tools/local_unity.py test              # 22/22 PASS
XONOTIC_MAP_FILTER=boil,afterslime python3 tools/local_unity.py prepare-maps
XONOTIC_ALL_MAPS=1 XONOTIC_VERSION_CODE=5 python3 tools/local_unity.py android --timeout 5400
```

## حدود معروفة

- كل الخرائط تعمل كـ Deathmatch أوفلاين تقريبي فقط؛ لا CTF/Onslaught/Nexball/Race.
- ثلاثة أسلحة تقريبية فقط، عناصر التقاط بأشكال تطوير، لا شخصيات ولا حركات.
- لا فحص بصري داخل الساندبوكس (Camera.Render يتجمد في llvmpipe)؛ اتجاه
  السماء والإضاءة وأداء الخرائط الكبيرة (geoplanetary، techassault) غير مؤكد.
- مسارات موسيقى بعض الخرائط قد لا تُحل إذا لم يطابق `cdtrack` ملفًا في
  `ExternalContent/music`؛ راجع حقل `music` في التقرير.

## الخطوة التالية المقترحة

1. تجربة الجهاز لهذه البصمة وتسجيل النتائج (الخرائط التي تتعطل/تتباطأ).
2. الأسلحة التسعة الأصلية مع نماذج العرض، ثم عناصر الالتقاط بالنماذج الأصلية.
3. الشخصيات والحركات، ثم أنماط اللعب، ثم الشبكة.

## تشغيل تجريبي لكل الخرائط وإصلاحات (versionCode 6، 2026-09-21)

بعد بناء versionCode 5 شُغّلت اللعبة في Unity Play Mode (بلا رسوميات) على
القائمة الرئيسية + كل الخرائط الـ29 عبر `tools/local_unity.py all-maps-playtest`
(`Editor/AllMapsPlaytest.cs`). الجولة الأولى: 18/30 مشهدًا ناجحًا. الأخطاء
الحقيقية التي وُجدت وأُصلحت:

| الخطأ | السبب | الإصلاح |
|---|---|---|
| القائمة الرئيسية ترمي `ArgumentException: Input Button Submit is not setup` كل إطار | `InputManager.asset` كان يحوي محورَي الفأرة فقط بينما `StandaloneInputModule` يستطلع Submit/Cancel/Horizontal/Vertical | أُضيفت المحاور الافتراضية إلى `ProjectSettings/InputManager.asset` وأُطفئ `sendNavigationEvents` في القائمة (لمس فقط) |
| 10 خرائط (catharsis, dance, geoplanetary, go, implosion, leave_em_behind, nexballarena, space-elevator, techassault, vorix) بلا نقاط ظهور → ظهور احتياطي عند الأصل داخل الجدران | المستورد كان يقبل `info_player_deathmatch/start` فقط؛ خرائط CTF/Nexball/Race تستخدم `info_player_team1..4` و`info_player_race` | `BspImportPipeline.SpawnClasses` يقبل الآن team1..4 / race / attacker / defender (كما تفعل Xonotic في DM)؛ نقاط الظهور الكلية 258 → 583 |
| `courtfun` انتهت مهلتها في الاختبار | 45 نقطة ظهور × ~1 ث تسوية > مهلة 45 ث | مهلة المشهد تتدرج مع عدد نقاط الظهور (خلل اختبار لا لعبة) |
| `darkzone` فشل فحص "player jumps" مرة واحدة | الفحص كان يقيس إطارًا واحدًا بعد المشي وقد يكون اللاعب في الهواء | الفحص يتابع ذروة الارتفاع خلال 0.5 ث ويتجاوز إن لم يكن اللاعب على الأرض (خلل اختبار) |

الجولة الأخيرة: `unity-dev5-all-maps-playtest-2026-09-21.json`.
Python 154/154، اختبارات المحرر 22/22.

### APK versionCode 6

| البند | القيمة |
|---|---|
| الملف | `Builds/my-xonotic-full.apk` |
| الحجم | 322,902,649 بايت |
| SHA256 | `e9e1d8ee84e84610f1847f1039a7110d725f38dd4cfbb926f53520c735add07d` |
| versionCode / الإصدار | 6 / `0.1.0-dev.5` |
| مدة البناء | 367.7 ثانية (الخرائط مستوردة مسبقًا) |
| الأخطاء / التحذيرات | 0 / 30 |
| التوقيع | debug، شهادة `2ef0784b…` (نفس dev.3/dev.4/vc5) |

الإيصال: `unity-dev5-build-2026-09-21-vc6.json`، تقرير الخرائط
`unity-dev5-maps-2026-09-21-vc6.json` (583 نقطة ظهور، 1,562 عنصر التقاط، 0 خرائط بلا ظهور).
**لم يُجرَّب على جهاز Android.** نطاق الاختبار: Play Mode على المضيف بلا رسوميات؛
لا يثبت اللمس ولا الرسم ولا الأداء على الهاتف.

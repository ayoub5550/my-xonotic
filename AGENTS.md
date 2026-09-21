# my-xonotic — دليل التسليم الكامل للمطوّر والـagent التالي

**مرجع هذا الدليل:** نقطة المصدر المنشورة `7a6ec966e802a0039ba8f079de8716ec3980c3d8`
وAPK `0.1.0-dev.6 / versionCode 7`، بتاريخ **2026-09-21**.
هذا تحديث توثيق، **ليس بناء APK جديدًا ولا إعلان نجاح dev.7**.

## 0. اقرأ هذا أولًا

الهدف إعادة تنفيذ Xonotic على **Unity/C# وAndroid**، لا إعادة تسمية LibreQuake
ولا العودة إلى محرك DarkPlaces. الموجود حزمة خرائط ومحتوى أصلي مع لعب
مستقل تقريبي؛ **ليست Xonotic مكتملة**. افصل دائمًا بين:
وجود الملف ← نجاح الاستيراد ← ظهور الرسم ← صحة الاصطدام ← صحة اللعب ← اختبار الهاتف.

1. اقرأ هذا الملف ثم [تفاصيل dev.6](docs/UNITY-DEV6.md).
2. افحص الفرع وcommit وحالة الملفات قبل أي تعديل. التنفيذ على
   `feat/unity-dev5-full-game` ([PR #3](https://github.com/ayoub5550/my-xonotic/pull/3))،
   وليس `main` التعريفي. لا تعتبر checkout لـ`main` فقدانًا للمشروع.
3. **لا تفترض أن شجرة العمل = APK المنشور.** عند إعداد هذا الدليل وُجدت
   تعديلات dev.7 محلية قيد التطوير/التحقق، و`VERSION` محليًا قد يقول dev.7.
   وجود `systems-test` أو `dev7-playtest` أو كود تحريك/محركات جديد ليس إثبات نجاح.
   تلك الأوامر غير موجودة في baseline dev.6 لهذا الدليل؛ راجع أحدث commit
   وإيصال واختبارات قبل دمجها أو البناء منها.
4. نسّق مع أي agent نشط. لا تبدّل فرعه، ولا تسترجع ملفاته، ولا تشغّل Unity
   أثناء تغيّر المصدر. مطوّر واحد يملك البناء والدمج في كل موجة.
5. الدليل يوثّق الأدلة السابقة ولا يحوّلها إلى نتائج أُعيد تشغيلها عندك.
   لا يوجد اختبار هاتف/رسم موثّق لبصمة APK أدناه.

**لمن يستأنف dev.7:** اقرأ [نقطة توقف العمل الجاري](docs/DEV7-HANDOFF-SNAPSHOT-2026-09-21.md)
قبل إعادة تنفيذ الأنظمة. توجد ملفات وتعديلات فعلية، لكن فحص 20:34 UTC
لم يجتز تحريك الشخصيات؛ هذا snapshot تاريخي، لا بديل عن أحدث نتيجة.
`versionCode=7` في الـAPK المنشور **لا يعني** `versionName=dev.7`.

### قواعد لا تُكسر

- البناء **محلي فقط**: لا Unity Cloud Build ولا GitHub Actions للبناء ولا
  خدمات مدفوعة أو شراء موارد. الـAPK يحمل موارده المطلوبة دون تنزيل وقت اللعب.
- فروع معزولة وPR؛ لا push مباشر إلى `main`، لا force-push للعمل المشترك.
  استند إلى فرع التنفيذ، لا إلى `main` الفارغ من التنفيذ. افحص upstream قبل push.
- لا كلمات مرور أو tokens أو رخصة Unity أو logs حساب أو keystores في Git.
  **مفاتيح التطوير هنا تعني الإعدادات ونقاط الربط، لا الأسرار.**
- APK/AAB مرفقات **GitHub Release** أو تسليم خاص، لا ملفات داخل تاريخ Git.
- احتفظ بالموارد الأصلية الفعلية ونَسبها ومصادرها في `ThirdParty/`؛
  لا تنسخ تنفيذ DarkPlaces/QuakeC GPL إلى تنفيذ C# الأصلي.
- وثائق المالك بالعربية؛ أسماء الكود والتعليقات التقنية وcommit messages بالإنجليزية.
- لا ادعاء «كاملة»، «بلا أخطاء»، «اختُبرت على الهاتف»، أو تطابق بصري بلا دليل.
  حجم APK وعدد الاختبارات ليسا مقياس اكتمال؛ لا تحشُ ملفات غير مستخدمة لتكبيره.
- في Viktor، عمليات Git وGitHub عبر أدوات SDK المعتمدة لا shell git/gh المباشر.
  عند رفض صلاحية توقف؛ لا تغيّر الملكية أو تبحث عن نسخة تتجاوز المنع.

## 1. آخر APK منشور ومصدره

| البند | القيمة المثبتة |
|---|---|
| الإصدار | `0.1.0-dev.6`، `versionCode 7` |
| Release | [unity-v0.1.0-dev.6](https://github.com/ayoub5550/my-xonotic/releases/tag/unity-v0.1.0-dev.6)، prerelease وليس latest |
| مرفق التنزيل | `xonotic-unity-0.1.0-dev.6-vc7.apk` |
| اسم مخرج البناء | `Builds/my-xonotic-full.apk`؛ إعادة تسمية المرفق لا تغيّر البايتات |
| الحجم | **400,459,348 بايت**، نحو 400.5 MB |
| SHA-256 | `ef140281d7639840c9b3d75b9631c1b28fad5784bdd73e32f56ccd6905ee7be5` |
| الحزمة | `com.ayoub.myxonotic`؛ مختلفة عن native القديم `com.ayoub.xonotic` |
| Android | ARM64 فقط، IL2CPP، OpenGLES3، أفقي، minimum API 26 / target API 36 |
| Unity | `2022.3.62f3` |
| التوقيع | Android Debug، APK v2؛ ليس مفتاح متجر/إنتاج |
| بصمة شهادة SHA-256 العامة | `2ef0784b3aad4046b2add35f9da40f424ba42607d95897807bdc0b6763bd408a` |
| بناء dev.6 المسجّل | 332.3 ثانية، 0 أخطاء و30 تحذير Ambient/Reflection Probes في `-nographics` |
| تحقق النشر | الحجم وSHA-256 وZIP CRC وmanifest وABI والتوقيع محليًا؛ digest مرفق GitHub مطابق |
| اختبار Android/اللمس/الأداء | **غير منفذ لهذه البصمة** |

**تتبّع المصدر مهم:** بُني APK من شجرة معدّلة فوق `deea0ed6`.
`revision` في [إيصال البناء](docs/unity-dev6-build-2026-09-21.json) هو الأساس
السابق، وليس commit نظيفًا يحتوي كل الإصلاحات. نقطة `7a6ec966` تحفظ المصدر
والتوثيق؛ عند تدقيق هذا التسليم طابقت ملفات المصدر الـ81 كلها بصمات
[سجل المصدر](docs/unity-dev6-source-2026-09-21.json).
هذا لا يثبت إعادة إنتاج APK مطابق بايتًا ببايت، ولا يغطي ProjectSettings المولّدة.

الشهادة تطابق dev.3/dev.4/vc5/vc6 وتختلف عن dev.2. لا تفترض إمكان تحديث
dev.2 فوق تثبيته الحالي؛ **لا تحذف التطبيق أو بياناته تلقائيًا** لحل تعارض التوقيع.
أي جهاز/بيئة جديدة قد تولّد debug keystore مختلفًا؛ قارن الشهادة أولًا.

## 2. ما يعمل وما لا يعمل في baseline dev.6

المصدر: [تقرير الخرائط](docs/unity-dev6-maps-2026-09-21.json)
و[شرح الإصلاحات](docs/UNITY-DEV6.md)، وليس اختبارًا بصريًا.

| النظام | الموجود | الحد الذي لا تتجاوزه في وصف النتيجة |
|---|---|---|
| الخرائط والقائمة | 29 خريطة مستوردة، قائمة اختيار وموسيقى ومعاينات | بيانات gametype لا تعني تنفيذ CTF/Nexball/Race؛ اللعب تقريبي أوفلاين |
| نقاط الظهور | 583؛ تشمل فئات team/race/attacker/defender | فحص الظهور ليس اجتياز كل ممر |
| عناصر الالتقاط | 1,562 بنماذج أصلية، صحة/درع/ذخيرة مدعومة حسب المستورد | 577 عنصرًا إضافيًا زينة غير قابلة للالتقاط |
| زينة العالم | 304 نماذج أصلية | 12 كيان OBJ متخطى؛ وجود النموذج لا يثبت خاماته |
| الأجزاء الداخلية | 117 جزءًا مرئيًا مع إزاحة origin | ثابتة؛ لا سلوك أبواب/مصاعد/دوّارات. 23 submodel بلا مثلثات قابلة للرسم |
| الشخصيات | 11 مورد IQM مخبوز في وضع idle ثابت | 3 بوتات تختار أول 3 موارد؛ لا قائمة اختيار ولا skeletal animation |
| الأسلحة | Blaster وRocket بمرئيات أصلية ثابتة، Rifle prototype؛ قواعد مستقلة | ليست الترسانة الأصلية ولا توازن/إطلاق ثانوي مطابق |
| المباراة | Deathmatch أوفلاين، حد قتل/وقت، فوز/تعادل/تجميد/إعادة | لا بقية الأنماط ولا شبكة ولا AI أصلي |
| المحفزات | push/teleport/hurt عبر AABB تقريبي | لا brush volumes الأصلية ولا اختبار عبور شامل |
| الخامات | صور/lightmaps/سماء وضغط ومشاركة أصول | 13 خامة jump-pad بلا صورة، 366 مرجعًا؛ شفافية مقصوصة تقريبية لا شفافية كاملة |

أسماء الشخصيات: `erebus`, `gak`, `gakmasked`, `ignis`, `ignismasked`,
`megaerebus`, `nyx`, `pyria`, `seraphina`, `seraphinamasked`, `umbra`.
الجسم المرئي للاعب منظور أول ما زال غير مكتمل، والبوتات خصوم اختبار لا نظام تنقل أصلي.

## 3. خريطة المستودع ونقاط التطوير

كل المسارات أدناه **نسبية لجذر المشروع**. لا تعتمد على مسار جهاز سابق.

| المسار | مسؤوليته |
|---|---|
| `Assets/MyXonotic/Runtime/Content/Bsp/` | قراءة IBSP v46 بحدود وفحص offsets، سجلات وهندسة وتحويل إحداثيات |
| `Runtime/Content/Md3/` تحت `Assets/MyXonotic/` | قارئ MD3 مستقل |
| `Assets/MyXonotic/Editor/Import/BspImportPipeline.cs` | worldspawn، الأسطح والخامات والسماء، submodels وحفظ الأصول |
| `BspGameplayImporter.cs` في نفس مجلد Import | محفزات الخريطة وارتباطاتها |
| `BspPickupImporter.cs` | فئات الالتقاط، الأصل المرئي، حفظ المراجع، Unsupported → decoration |
| `BspMapModelImporter.cs` | MD3/IQM لزينة الخريطة، التحويلات ومشاركة النماذج |
| `XonoticContentResolver.cs` | أولوية جذور الملفات وقراءة shader scripts كبيانات لا كود |
| `BspTextureLoader.cs`, `ImportedTexturePolicy.cs` | فك الصور/الخامات، lightmaps، mipmaps والضغط وإعادة استخدام الأصول |
| `IqmWeaponImporter.cs`, `Md3WeaponModelBuilder.cs` | توليد مرئيات وأصوات الأسلحة وmanifest |
| `IqmCharacterImporter.cs`، وفيه الصنف الداخلي `IqmSkinnedDocument` | قراءة IQM وتقييم pose ثم mesh ثابت في dev.6؛ القارئ ليس ملفًا منفصلًا |
| `Assets/MyXonotic/Editor/FullGameBuild.cs` | استيراد الخرائط، MapCatalog، المشاهد، القائمة والموسيقى والتقرير |
| `Assets/MyXonotic/Editor/LocalBuild.cs` | إعداد Android، اختيار fixture/Boil/all-maps، notices، BuildPlayer والإيصال |
| `Assets/MyXonotic/Runtime/Gameplay/Player.cs` | الحركة، CharacterController، لوحة المفاتيح واللمس متعدد الأصابع |
| `TouchLayout.cs`, `Hud.cs` في Gameplay | مناطق لمس وsafe area مشتركة بين القراءة والرسم |
| `Actor.cs`, `Bot.cs`, `ArenaBootstrap.cs`, `ContentBridge.cs` | الضرر/البوتات/إنشاء الجولة/الربط مع الخريطة ونقاط الظهور |
| `WeaponController.cs`, `WeaponView.cs`, `Projectile.cs` | منطق الأسلحة ومرئياتها والمقذوفات |
| `MatchRules.cs`, `MatchSession.cs`, `Pickup.cs`, `MapTrigger.cs` | قواعد المباراة والالتقاط والمحفزات |
| `Assets/MyXonotic/Runtime/Menu/MainMenu.cs` | اختيار الخريطة والقائمة |
| `tools/local_unity.py` | تشغيل Editor بمهلة وقفل، فحص marker/receipt ومنع اعتماد النتائج القديمة |
| `tools/host_compile.py` | ترجمة C# ضد مكتبات Unity؛ لا يُعد اختبار Editor أو IL2CPP |
| `tools/content/` | أرشيفات وprovenance واستعادة وتحويل صور وجرد |
| `tests/csharp/`, `tests/python/`, `Assets/MyXonotic/Editor/Tests/` | اختبارات مستقلة وتكامل؛ توجد اختبارات أخرى مباشرة تحت Editor |

### الأصل مقابل الناتج المولّد

- **مصدر قابل للتعديل:** C#، shaders الأصلية، ProjectSettings، Packages، VERSION، tests، docs.
- **مواد upstream لا تعدّلها بصمت:** `ThirdParty/Xonotic/` و`ThirdParty/Xonotic-0.8.6/`.
- **مشتقات تُعاد توليدها:** `Assets/MyXonotic/Generated/`، موارد Weapons المولدة،
  `Assets/StreamingAssets/Xonotic/`، و`ExternalContent/`.
- `Library/`, `Artifacts/`, `Builds/`, `Logs/`, `UserSettings/` ليست مصادر ولا دليل نجاح بمجرّد وجودها.
- حافظ على `.meta` وGUID؛ لا تعالج كسر المراجع بحذف الأصول وإعادة إنشائها.
  `tools/asset_meta.py --write-missing` للأصول المصدرية الجديدة فقط؛ لا يعيد كتابة الموجود.

## 4. الأدوات والترخيص والبيئة

- Unity **2022.3.62f3** مع رخصة محلية صالحة، Android Build Support،
  OpenJDK 11، NDK r23b، SDK platform 36؛ build-tools 34.0.0 استُخدمت للفحص.
- Python **3.10+** وPillow لتحويل DDS؛ Mono/mcs للاختبارات المستقلة.
  افحص نسخ الأدوات والمساحة والذاكرة على جهازك. الحزمة الأصلية المفكوكة
  وحدها كبيرة، ثم تحتاج نسخ العمل وLibrary ومخرجات البناء؛ لا تعتمد على مساحة APK فقط.
- Unity Hub هو مسار التثبيت/التفعيل المعتاد. تسجيل الدخول إلى موقع Unity
  ليس تفعيلًا للمحرر. لا تتجاوز الترخيص أو شروط أهلية الحساب.
- نقطة إصدار المحرر: `96770f904ca7`. يمكن مراجعة metadata الرسمية:
  `https://services.api.unity.com/unity/editor/release/v1/releases?version=2022.3.62f3&platform=LINUX&architecture=X86_64`.
  لا تفترض أن اسم installer يعني توافقه؛ تحقق من module/platform/version.
- على Linux المعزول السابق استُخرج Android support من حزمة `.pkg` التي
  أشارت إليها metadata إلى `Editor/Data/PlaybackEngines/AndroidPlayer/`.
  استخدم Hub إن أمكن بدل وصفة استخراج غير موثقة لجهاز مختلف.
- الإعداد المعتاد:

```bash
export UNITY_EDITOR="/absolute/path/to/Unity-or-approved-local-wrapper"
export UNITY_EDITOR_DATA="/absolute/path/to/Editor/Data"
export JAVA_HOME="$UNITY_EDITOR_DATA/PlaybackEngines/AndroidPlayer/OpenJDK"
export PATH="$JAVA_HOME/bin:$PATH"
```

### حلول gVisor الخاصة — ليست متطلبات لكل جهاز

1. عند ثبوت `Shader compiler initialization error 0x80000004`، استُخدم
   `qemu-x86_64-static` لتشغيل نسخة `UnityShaderCompiler.real` عبر wrapper
   اسمه `UnityShaderCompiler`. احفظ الأصل ولا تجعل wrapper يشغّل نفسه.
2. تعذّر جدولة FMOD بزمن حقيقي عولج بـshim محلي `libschedfix.so` محمّل
   عبر `LD_PRELOAD` لتعطيل طلبات أولوية الجدولة فقط؛ لا علاقة له بتجاوز الرخصة.
3. launcher يحدد HOME الثابت الذي يحمل تفعيل Unity، ومكتبات Linux
   عبر `LD_LIBRARY_PATH`، والـshim. هذه أدوات **خارج المستودع**، لا يفترض
   agent جديد أنها موجودة أو مثبتة على جهاز آخر.
4. رسالة FMOD output device قد تبقى في headless؛ افصلها عن فشل التهيئة القاتل.
5. لا تثبّت shader `Standard` في Always Included Shaders: توسّع سابقًا إلى
   24,576 variant ووقت طويل مع qemu. `LocalBuild.Configure` يزيله،
   و`ShaderVariantStripper.cs` يقلل variants.

### إدارة الأسرار

الـrunner يقبل `UNITY_USER` و`UNITY_PASS` **معًا أو لا شيء** للتفعيل القديم،
لكن التفعيل المحلي المسبق دون تمريرها هو المفضّل. لا تسجل قيمهما أو argv
لـUnity؛ قد تحمل الوسيطات كلمة المرور. افحص PID/comm فقط عند إدارة العمليات.
لا ترفع logs الخام قبل التنقيح. سر التوقيع يبقى لدى المالك في مخزن أسرار؛
بصمة الشهادة العامة ليست المفتاح الخاص ولا يمكن استخدامها لتوقيع إصدار.
احتفظ بنسخة احتياطية آمنة للمفتاح خارج Git ولا تستبدله بلا خطة ترقية.

## 5. الموارد: من أين جاءت وكيف تُستعاد

### مصدران مختلفان داخل Git

1. `ThirdParty/Xonotic/`: المجموعة الأولى، **207 ملفًا / 203,918,053 بايت**؛
   فهرسها `resource-index.json` وأداة فحصها `tools/content/verify_resources.py`.
2. `ThirdParty/Xonotic-0.8.6/`: الحزم السبعة المفكوكة والمشتقات والإشعارات.
   يذكر `publish-manifest.json` **17,345 ملفًا / 4,474,463,781 بايت منطقيًا**،
   و2,845 نسخة hardlink متطابقة و1,908 رابط archive حُلّ إلى ملف عادي؛
   لا symlink نظام حقيقي مطلوب. هذه أرقام سجل النشر، لا قياس جديد لمساحة القرص.
   verifier المجموعة الأولى **لا يفحص الحزمة الكبيرة**.

الأرشيف الأصلي: `https://dl.xonotic.org/xonotic-0.8.6.zip`،
**1,238,439,495 بايت**؛ SHA-512 الذي يوثّق `FULL-GAME-GATES` تحققه سابقًا
(لم يُعَد تنزيل/فحص الأرشيف أثناء كتابة هذا الدليل؛ manifest يقول `rehashed_this_run:false`):

```text
cb39879e96f19abb2877588c2d50c5d3e64dd68153bec3dd1bebedf4d765e506afa419c28381d7005aed664cb1a042571c132b5b319e4308cab67745d996c2a6
```

بصمات الحزم السبع مثبتة في `KNOWN_PACK_SHA256` داخل
`tools/content/publish_resources.py`. الخرائط من `xonotic-20230620-maps.pk3`،
البيانات من `xonotic-20230620-data.pk3`؛ البقية موسيقى وخطان وحزم توافق.
لا تشغّل أي `.cfg/.qc/.shader` أو executable منها؛ تعامل معها كبيانات مرجعية.
مرجع المصدر والتراخيص: [بيان الحزمة الكبيرة](ThirdParty/Xonotic-0.8.6/README.md)،
[المجموعة الأولى](ThirdParty/Xonotic/README.md)، [الإشعارات](THIRD_PARTY_NOTICES.md).
وصف «لم يتحقق SHA512 كاملًا» في بعض وثائق المجموعة الأولى تاريخي، لا يلغي
التحقق اللاحق في [FULL-GAME-GATES](docs/FULL-GAME-GATES.md).

### المسار المفضّل لـcheckout كامل — بلا تنزيل

من جذر المشروع، بعد التأكد من وجود `ThirdParty/Xonotic-0.8.6/publish-manifest.json`:

```bash
python3 tools/content/verify_resources.py
python3 tools/content/publish_resources.py restore
```

`restore` يتحقق من SHA-256 للمصدر قبل نسخه، ويعيد `ExternalContent/data`,
`maps`, `decoded`, `worlddecoded`, `characters`, `music`, `notices`.
الموجود المطابق يُترك؛ المختلف **لا يُكتب فوقه** ويظهر mismatch؛ والـsymlink
مرفوض. لا تحذف تعديلات محلية لحل mismatch: اعزلها وراجع سببها أولًا.
`restore --with-pk3-entries` يضيف المحتوى المفكوك لحزم الخطوط/الموسيقى/التوافق
في staging؛ **لا يعيد أرشيفات PK3 المضغوطة الأصلية**.

لقطة `derived/characters` تحتوي bundle Erebus فقط، لا bundles للشخصيات الـ11.
مصدر الشخصيات العام هو `models/player/*.iqm` داخل `ExternalContent/data`
بعد restore؛ `IqmCharacterImporter.ImportAll` يستورده أثناء prepare-maps.
وجود ذلك المصدر لا يعفي من تحويل صور dev.6 أو فحص التقرير والمشاهد.
لا يوجد ادعاء بأن checkout نظيفًا ثم restore/build كاملًا أُعيد اختباره هنا.

لا تبدأ بـ`publish`: ذلك ينقل موارد محلية إلى Git، ليس تنزيلًا ولا استعادة.
`publish --overwrite --verify-full-zip` يحتاج الأرشيف الأصلي في
`ExternalContent/downloads/xonotic-0.8.6.zip` والحزم السبع في
`ExternalContent/staging_pk3/Xonotic/data/`، فضلًا عن المشتقات/الإشعارات.
إذا كانت هذه المدخلات غير موجودة فتوقف؛ لا تقل إن restore أعادها كلها.
الاستثناء من فحص نسبة الضغط مرتبط ببصمات الحزم الرسمية المثبتة فقط،
ولا يجوز تعميمه على ZIP مجهول أو تعطيل فحص CRC/path traversal.

### تحويلات dev.6 الإضافية — لا يكفي restore وحده

تدقيق manifest المنشور وجد **22 من 24 PNG إضافية** في سجل dev.6 غير موجودة
ضمن لقطة المشتقات المنشورة؛ جميع DDS الأصلية الـ24 موجودة وتطابق بصماتها.
بعد الاستعادة، أعد تحويل القائمة الموثقة بدل افتراض اكتمال الصور:

```bash
python3 - <<'PY'
import json, subprocess, sys
from pathlib import Path
records = json.loads(Path("docs/unity-dev6-texture-provenance-2026-09-21.json").read_text())
cmd = [sys.executable, "tools/content/prepare_unity_textures.py",
       "ExternalContent/data", "--output", "ExternalContent/decoded"]
for item in records:
    name = item["source"].removeprefix("dds/").removesuffix(".dds")
    cmd += ["--texture", name]
subprocess.run(cmd, check=True)
PY
```

الأداة تستعمل Pillow: DDS → RGBA → PNG بلا إعادة ترخيص، مع
`ExternalContent/decoded/conversion-manifest.json`. تتحقق من المسارات والحجم
والصيغة وتجهّز الدفعة قبل استبدال النتائج. PNG قد تختلف بايتاتها مع نسخة Pillow؛
سجّل نسختك وبصمات المشتقات الجديدة ولا تُعدّل البصمات القديمة لتبدو مطابقة.
للإضافة المحددة: `--texture models/weapons/laser`، بلا `dds/` وبلا امتداد.
`worlddecoded/world-texture-manifest.json` و`music/music-manifest.json` جزء
من خط الموارد؛ وجود raw OGG وحده لا يبني ربط cdtrack.

## 6. متغيرات التطوير والإعدادات

| المفتاح | الوظيفة والتحذير |
|---|---|
| `UNITY_EDITOR` / `--unity` | executable محلي أو wrapper مُراجع |
| `XONOTIC_REVISION` | commit المصدر الذي تم البناء منه؛ إن كانت الشجرة معدلة صرّح بذلك وسجل بصماتها |
| `XONOTIC_ALL_MAPS=1` | حزمة الخرائط والقائمة؛ بدونه قد تحصل على fixture صغيرة |
| `XONOTIC_VERSION_CODE` | رقم Android صريح؛ default في dev.6 هو 5، فلا تعتمد عليه للإصدار التالي |
| `XONOTIC_CONTENT_ROOTS` | جذور مرتبة، `:` على Linux و`;` على Windows؛ تُبحث أولًا وتُلحق بعدها الجذور الافتراضية |
| `XONOTIC_MAPS_ROOT` | default `ExternalContent/maps`، ويتوقع داخله `maps/*.bsp` |
| `XONOTIC_MUSIC_ROOT` | default `ExternalContent/music` مع music-manifest |
| `XONOTIC_MAP_FILTER` | أسماء خرائط بفاصلة/مسافة للتشخيص؛ **أزله** قبل قبول all-maps |
| `XONOTIC_TEXTURE_FORMAT` | `etc2` افتراضي، `astc` أو `none`؛ ASTC لا يُفترض دعمه بكل جهاز |
| `XONOTIC_TEXTURE_REBUILD=1` | لا تعِد استخدام texture asset موجود؛ مهم عند تغيير سياسة الضغط |
| `XONOTIC_INCLUDE_EXTERNAL=1` | مسار خريطة منفردة بدل fixture، وليس بديلًا لـALL_MAPS |
| `XONOTIC_BSP` | BSP للخريطة المفردة؛ اختره صراحة |
| `XONOTIC_BUILD_INVOCATION` | يولّده runner؛ لا تعِد استعماله لتزوير إيصال سابق |
| `--timeout` | مهلة موجبة؛ default 1800 ثانية، ارفعها بقرار واضح للأعمال الثقيلة |
| `--graphics` | استخدام display/OpenGL بدل `-nographics`؛ ليس ضمان التقاط صورة |

إعداد Android في `LocalBuild.Configure`: Linear color، shadows وMSAA
معطّلان، fixedDeltaTime=1/60، stripping Low، Development + StrictMode.
`VERSION` يحدد versionName. لا تغيّر نسخة Unity خلال إصلاح محتوى بلا اختبار ترحيل منفصل.

## 7. وصفة البناء المحلي خطوة بخطوة

الأوامر من **جذر checkout المقصود**، بعد الاستعادة وتحويل DDS أعلاه
وتفعيل Unity. ليست إعلانًا أن هذه الوثيقة أعادت البناء.

```bash
set -eu
export XONOTIC_CONTENT_ROOTS="ExternalContent/decoded:ExternalContent/worlddecoded:ExternalContent/maps:ExternalContent/data:ThirdParty/Xonotic/maps-pk3"
export XONOTIC_MAPS_ROOT="ExternalContent/maps"
export XONOTIC_MUSIC_ROOT="ExternalContent/music"
export XONOTIC_ALL_MAPS=1
export XONOTIC_TEXTURE_FORMAT=etc2
unset XONOTIC_MAP_FILTER XONOTIC_INCLUDE_EXTERNAL XONOTIC_BSP
# عيّن commit المصدر الحقيقي عبر أداة Git المعتمدة قبل البناء.
export XONOTIC_REVISION="<exact-source-commit>"
# 7 لإعادة baseline dev.6 فقط؛ لأي APK جديد اختر رقمًا أعلى بعد فحص آخر إصدار.
export XONOTIC_VERSION_CODE=7

python3 tools/content/pk3_tool.py fixture --out-dir tests/fixtures/generated
python3 -m unittest discover -s tests/python -p 'test_*.py' -v
python3 tools/asset_meta.py
python3 tools/local_unity.py compile --timeout 3600
python3 tools/local_unity.py configure
python3 tools/local_unity.py test
python3 tools/local_unity.py sky-test
python3 tools/local_unity.py gameplay-test
python3 tools/local_unity.py gameplay-playtest
python3 tools/local_unity.py prepare-maps --timeout 3600
python3 - <<'PY'
import json
from pathlib import Path
r = json.loads(Path("Artifacts/full-game-maps.json").read_text())
assert (r["requested"], r["imported"], r["failed"]) == (29, 29, 0), r
PY
python3 tools/local_unity.py content-test
python3 tools/local_unity.py all-maps-playtest --timeout 3600
python3 tools/local_unity.py android --timeout 3600
python3 - <<'PY'
import json
from pathlib import Path
r = json.loads(Path("Artifacts/full-game-maps.json").read_text())
assert (r["requested"], r["imported"], r["failed"]) == (29, 29, 0), r
PY
```

- أمر Android يعيد Configure ثم تجهيز الحزمة؛ اختبارات Editor قد تغيّر
  اسم المنتج/رقم النسخة ومشهد DevelopmentArena، لذلك افحص APK النهائي نفسه.
- `prepare-maps` يولّد مشاهد `Assets/MyXonotic/Generated/Maps/map_*.unity`،
  `Generated/MainMenu.unity` و`Generated/Resources/MapCatalog.asset`.
- `FullGameBuild` يقرأ BSP غير البادئة بـ`_`، ينشئ لكل خريطة المشهد والبيانات،
  يربط cdtrack بموسيقى streaming Vorbis، ويبني القائمة أول مشهد.
  **تحذير baseline:** فشل خريطة يُسجل وتُتخطى، ولا يرمي المستورد فشلًا نهائيًا
  إلا إذا فشلت جميع الخرائط. لذا راجع `requested=imported=29` و`failed=0`
  بنفسك؛ نجاح الأمر أو كلمة “all” لا يمنع APK ناقصة الخرائط.
- `LocalBuild.PrepareFullGamePackage` يجهّز أسلحة وnotices مع الحزمة في baseline.
  ثم BuildPlayer ينشئ APK ويكتب `Builds/build-receipt.json`.
- ترتيب جذور decoded أولًا يمنع استخدام DDS الخام بدل PNG المُعدّة.
- `scene` يبني الساحة المصطنعة، فلا تشغّله بعد prepare-maps وتتوقع بقاء القائمة.
- كل shell جديد يحتاج متغيراته من جديد. لا تشغّل أمرًا يعتمد على بيئة shell انتهى.

### فحوص مستقلة واختبار محدود

```bash
bash tests/run_all.sh mono mcs \
  ThirdParty/Xonotic/maps-pk3/maps/_hudsetup.bsp \
  ThirdParty/Xonotic/maps-pk3/maps/boil.bsp
python3 tools/host_compile.py --editor-data "$UNITY_EDITOR_DATA" \
  --ui-dll "<actual-path-to-UnityEngine.UI.dll>"
```

الـhost compiler يستخدم Mono و`csc.exe` داخل Unity، ومراجع modular؛ لا تخلط
UnityEngine monolithic وmodular. UGUI assembly اسمه `UnityEngine.UI`.
لا تفترض أن UI DLL مولّدة موجودة في checkout جديد.
لتسريع تحقيق محدد يمكن `XONOTIC_MAP_FILTER=boil` مع prepare/playtest؛
النتيجة عندئذ ليست all-maps. لمسار Boil المنفرد عطّل ALL_MAPS، فعّل
INCLUDE_EXTERNAL وحدد BSP؛ المخرج `my-xonotic-unity-boil.apk`.
بدون الاثنين المخرج `my-xonotic-development.apk` لساحة صناعية.

## 8. أين تجد دليل النجاح؟

| الأمر/الدليل | المخرج | ما لا يثبته |
|---|---|---|
| `compile` | `Artifacts/compile-result.json` مع passed وinvocation الحالي | shader/render/Android |
| `test` | `Artifacts/editor-tests.txt` | لمس أو أداء هاتف |
| `sky-test` | `Artifacts/sky-import-regression-tests.txt` | تطابق السماء بصريًا |
| `gameplay-test` | `Artifacts/gameplay-integration-tests.txt` | كل قواعد upstream |
| `gameplay-playtest` | `Artifacts/gameplay-playtest.json` | اللعب على جهاز |
| `playtest` | `Artifacts/playtest-result.json` | خرائط أصلية؛ هذا fixture |
| `original-playtest` | `Artifacts/original-playtest.json` | جميع الخرائط |
| `prepare-maps` | `Artifacts/full-game-maps.json` | صحة الرسم بمجرد نجاح الاستيراد |
| `content-test` | `Artifacts/content-regression-tests.txt` | أن كل خامة مرئية صحيحة |
| `all-maps-playtest` | `Artifacts/all-maps-playtest.json` | رسم/لمس/اجتياز كامل |
| `android` / `linux` | `Builds/build-receipt.json` وartifact مطابق | صحة اللعب أو توقيع APK |

الـrunner يحذف التقارير القديمة التي يتحقق منها، ويشترط marker للترجمة،
وpassed لتقارير Play Mode، وإيصال بناء حديث مرتبط بـinvocation/target/output/
artifactBytes/SHA-256. يحتفظ بالـAPK القديم عند فشل محاولة جديدة؛
**لا تشاركه على أنه نتيجة المحاولة الفاشلة**. خروج Unity بصفر وحده لا يكفي.

### أدلة dev.6 التاريخية المحفوظة

- 154 Python و22 Editor في [شرح dev.6](docs/UNITY-DEV6.md).
- 157 تحقق محتوى في [تقرير المحتوى](docs/unity-dev6-content-tests-2026-09-21.txt):
  يفتح المشاهد المحفوظة ويفحص mesh/material أصلية، لا مجرد أعداد.
- 30/30 مشهدًا (قائمة +29 خريطة)، 0 runtime errors/warnings، 982.4 ثانية:
  [Play Mode](docs/unity-dev6-all-maps-playtest-2026-09-21.json).
- 2,856 تحذير استيراد في تقرير الخرائط، ليست هي 30 تحذير البناء ولا أخطاء التشغيل.
- نجاح جلسة سابقة لا يغطي كود dev.7 ولا أي APK ببصمة مختلفة.

## 9. فحص APK وإطلاقه دون تسريب أسرار

```bash
export ANDROID_BT="$UNITY_EDITOR_DATA/PlaybackEngines/AndroidPlayer/SDK/build-tools/34.0.0"
export JAVA_HOME="$UNITY_EDITOR_DATA/PlaybackEngines/AndroidPlayer/OpenJDK"
export PATH="$JAVA_HOME/bin:$PATH"
sha256sum Builds/my-xonotic-full.apk
python3 -m zipfile -t Builds/my-xonotic-full.apk
"$ANDROID_BT/aapt" dump badging Builds/my-xonotic-full.apk
"$ANDROID_BT/apksigner" verify --verbose --print-certs Builds/my-xonotic-full.apk
```

قارن package/versionName/versionCode/minSdk/ABI/cert والحجم مع الإيصال.
`artifactBytes` هو حجم APK؛ `bytes` في إيصال Unity totalSize مختلف ولا يُعرض كحجم التنزيل.
سجّل commit نظيفًا أو fingerprint وتفصيل dirty tree بوضوح.
انشر APK مع SHA256SUMS وملاحظات عربية وأدلة/قيود، **prerelease وlatest=false**.
يمكن إنشاء draft أولًا، فحص `assets[].state/size/digest` من GitHub API،
ثم نشره والتحقق من الرابط النهائي. draft بلا tag قد يكون `untagged-*`:
ابحث في قائمة releases بـ`tag_name` بدل الاعتماد على endpoint by-tag قبل النشر.
لا ترفق keystore/رخصة/raw logs/ملفات حساب. لا تدفع APK إلى Git.

## 10. اختصارات التحكم وكيف تختبر الهاتف

Baseline dev.6: WASD حركة، mouse نظر، Space قفز، زر mouse الأيسر إطلاق
والأيمن alt، Q التالي/E السابق، 1 Blaster/2 Rifle/3 Rocket.
P أو Escape توقف/استئناف، R إعادة، M رجوع للقائمة أثناء الإيقاف.
على الهاتف: joystick يسار، نظر مستقل يمين، FIRE/ALT/JUMP وWPN± وPAUSE.
اللمس مبني على finger IDs و`Screen.safeArea`؛ `TouchLayout` مصدر الحقيقة
لأماكن الرسم والالتقاط معًا، لا تغيّر Hud وحده فتفصل الصورة عن hit region.

قبل تسمية إصدار «مجرّب على Android»، سجّل اسم الهاتف وAndroid وGPU وبصمة APK:

- [ ] تثبيت وبدء؛ وإن كان تحديثًا فقارن التوقيع واحفظ بيانات المستخدم.
- [ ] حركة+نظر+إطلاق متزامن، القفز، إلغاء touches بعد pause/background/resume.
- [ ] safe area والاتجاه الأفقي على نسب شاشة مختلفة.
- [ ] الخامات والسماء والإضاءة والشفافية وصوت الأسلحة والموسيقى.
- [ ] كل خريطة: spawn صالح، أرض/جدران، jump/teleport/hurt، عناصر ومباراة وإعادة.
- [ ] قياس FPS/frame time، ذاكرة، حرارة وبطارية خلال جلسة مسماة، لا تخمين أداء.
- [ ] لقطة/فيديو من **هذا APK** للمشكلات؛ لا صور upstream أو صور مصطنعة كدليل.

التصوير Linux/Xvfb/llvmpipe السابق انتهى بعد 1,500 ثانية دون PNG صالح.
لا تكرّر انتظارًا مفتوحًا ولا تعد بفيديو من مسار لم يعمل. headless ليس visual QA.

## 11. أعطال ودروس تمنع إعادة الأخطاء

| العرض | التشخيص/الإجراء |
|---|---|
| `Another local invocation holds the lock` | افحص PID في `Artifacts/local-unity.lock` وعمليات Editor دون argv. لا تمسح قفلًا حيًا ولا تشغّل نسخة ثانية |
| timeout خارجي ترك Unity يعمل | تحقق من شجرة العمليات؛ أوقف فقط عمليتك المملوكة بعد التنسيق. لا تقتل Unity لزميل أو تتجاوز رفض النظام |
| APK صغيرة أو المشهد الصناعي | ALL_MAPS لم يُمرر أو scene أعاد BuildSettings؛ افحص receipt+manifest+catalog لا الاسم |
| خريطة بلا spawn | راجع SpawnClasses وteam/race/attacker/defender، لا تُخفِ origin fallback |
| كل frame خطأ Submit/Cancel | StandaloneInputModule يحتاج InputManager axes حتى في قائمة لمس |
| عناصر أصلية تتحول لكرات بعد save | `PersistPickupVisuals` يحفظ transient فقط (`!AssetDatabase.Contains`)، ثم content-test على مشاهد أُعيد فتحها |
| أجزاء متكدسة عند الصفر | inline vertices قد تكون pivot-local؛ طبّق entity origin واختبر bounds مثل afterslime `*7` |
| أسطح lightmapped مثقوبة | `blendfunc filter` أو additive لا يعني alpha-cutout؛ لا تقص ألفا لكل blendfunc |
| ETC يفشل بلا C# exception | NPOT مثل 600×600 يصنع mip 150×150؛ اتركه RGBA32 mipmapped أو أعد التحجيم بقرار موثق |
| references تتكسر بعد reimport | حافظ على GUID واستعمل تحديث asset موجود لا delete/recreate |
| سقوط خلال أرض BSP | لا تعكس جميع meshverts عميانيًا: source winding مع swap Y/Z له اختبار حقيقي؛ patch winding منفصل |
| MD3 يفشل رغم الامتداد | افحص magic؛ بعض `.md3` الأصلية IQM (`INTERQUAKEMODEL`) |
| shader script لا يُقرأ | اسم shader قد يسبق `{` بسطر جديد؛ parser يجب أن يتجاوز newline |
| apksigner لا يجد java | اضبط JAVA_HOME **وPATH** إلى OpenJDK/bin |
| تكرار Git status البطيء | الحزمة كبيرة؛ استخدم فحصًا scoped و`--untracked-files=no` عبر الأداة، لا تكدّس scans يتيمة |
| missing raw/derived resource | راجع restore manifests وأولوية roots وتحويلات dev.6، لا تستبدله بplaceholder دون تصريح |

اتفاق الإحداثيات: `(x,y,z)` الأصلي → `(x,z,y)/32`، واختلاف handedness
يعالج حسب نوع السطح. origin اللاعب ليس القدم؛ `ContentBridge` يطرح `24/32`
على Y. تصادم mesh triangles **ليس** BSP brush collision الأصلي.
قارئ IQM هنا للموارد الرسمية المعروفة، لا بوابة آمنة عامة لملفات مجهولة.

## 12. خطة المتابعة ومعايير قبولها

ابدأ بتقييم أحدث dev.7 قبل كتابة نفس الأنظمة من الصفر. ما يلي **بوابات**
مفتوحة من baseline dev.6، لا حكم على تعديلات غير منشورة:

| الأولوية | العمل ونقطة الربط | القبول المطلوب |
|---|---|---|
| P0 | اختبار APK المنشور على هاتف | معلومات الجهاز والبصمة، صور حقيقية، تقرير crashes/لمس/أداء |
| P1 | UI/HUD ولمس: Player/TouchLayout/Hud/MainMenu | hit regions لا تتداخل، 3 أصابع مستقلة، pause/resume، لا allocations ثقيلة لكل frame |
| P1 | IQM skeletal animation: importer/runtime character | bind pose صحيح، idle/run/jump/death، bounds وnormals وثبات references، اختبار runtime لا إطار ثابت فقط |
| P1 | doors/platforms/rotating/bobbing من ImportedSubmodel | origin/axis/distance/speed/wait صحيحة، collision وركوب المنصة والانسداد والpause/reset، فحوص فيزياء حقيقية |
| P1 | الترسانة والذخيرة المشتركة/التقاط الأسلحة | نماذج view أصلية وخامات/أصوات كاملة لكل سلاح، أولي/ثانوي، ammo وownership ودورة pickup قابلة للاختبار |
| P2 | OBJ وjump-pad materials والمؤثرات | حصر مسارات unresolved ثم فحص scene serialization ومراجعة مرئية، لا تحويل كل تحذير إلى نجاح |
| P2 | الحركة/BSP collision/bots | مسارات مرجعية لكل نوع هندسة ومنحدر وممر، bots لا تتعلق/تسقط؛ أرقام مقارنة لا ادعاء parity |
| P3 | modes ثم networking | مواصفة مستقلة، شروط فوز لكل نمط؛ authoritative server/prediction واختبار جهازين وتأخير قبل claim |
| قبل الإنتاج | التراخيص، source correspondence، LTS وتوقيع إصدار | مراجعة موثقة منفصلة وخطة تحديث/استرجاع؛ لا استنتاج clearance من وجود الموارد |

**تعريف إنجاز أي موجة:**
1. مجال واضح وملفات مملوكة، لا edits متزامنة متعارضة.
2. regression يفشل قبل الإصلاح وينجح بعده حيث أمكن.
3. Python/host/Unity compile ثم Editor/content/Play Mode الملائمة؛ تحقق تقارير حديثة.
4. all-maps بلا filter عند ادعاء الحزمة الكاملة؛ سجل استيراد وكيانات وخامات ناقصة.
5. APK محلي برقم جديد وإيصال وCRC/manifest/signature/hash؛ اختبار الجهاز منفصل وصريح.
6. تحديث VERSION/CHANGELOG/هذا الدليل ووثيقة الإصدار وأدلة منقحة،
   commit/PR ثم Release إن طُلب. اترك للـagent التالي آخر نجاح وآخر فشل والخطوة التالية.

## 13. ترتيب المراجع والحفاظ على التاريخ

- هذا الملف هو نقطة الدخول التشغيلية؛ التفاصيل المصدرية أحدث من التخمين.
- [UNITY-DEV6](docs/UNITY-DEV6.md) وتقاريره هي مرجع APK المنشور.
- [LOCAL_BUILD](docs/LOCAL_BUILD.md)، [RELEASING](docs/RELEASING.md) للتوسّع.
- [FULL-GAME-GATES](docs/FULL-GAME-GATES.md) يحتوي جدول dev.4 تاريخيًا؛
  اقرأ تحديث dev.6 في بدايته قبل نقل ادعاء غياب محتوى.
- [TESTING](docs/TESTING.md) و[ARCHITECTURE](docs/ARCHITECTURE.md)
  يتضمنان تأسيسًا أقدم؛ وصف «APK لم يُبن» أو «art غائب» لا يُنقل كحالة اليوم.
- [CHANGELOG](CHANGELOG.md)، [dev.5](docs/UNITY-DEV5.md)،
  [dev.4](docs/UNITY-DEV4.md)، [dev.3](docs/UNITY-CONTINUATION.md) لسجل التطور.
- `docs/UNITY-DEV5-WIP.md` وصف عمل غير مُثبت بالنشر وقت كتابته، وليس دليل وجوده في APK.
- [التسليم السابق محفوظ كاملًا](docs/history/AGENTS-PRE-HANDOFF-2026-09-21.md)
  للرجوع، لا للعمل بتعليمات فروعه القديمة على أنها حالية.

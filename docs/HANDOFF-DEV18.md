# برومت تسليم للمطوّر التالي — إطلاق Unity dev.18 لـ my-xonotic

انسخ ما يلي وأعطه للمطوّر (بشريًا كان أو وكيلًا). ما بين `<...>` يملؤه المالك خارج المستودع ولا يُكتب فيه أبدًا.

---

أنت المطوّر المسؤول عن `ayoub5550/my-xonotic` (Unity 2022.3.62f3، Android، remake لـ Xonotic 0.8.6). مهمتك إطلاق **`0.1.0-dev.18`** (versionCode 19) على نفس نمط dev.15–dev.17.

## 1. اقرأ أولًا وبهذا الترتيب
1. `AGENTS.md` (checkpoint dev.17 في الأعلى) — القواعد وما لا يُلمس.
2. `docs/ROADMAP.md` — قسم **dev.18**: البنود الأربعة الأولى من اختبار المالك على Poco F3 (الطلقات كما في الأصل، صفحة الإعدادات، الجرافيك = lightmaps/skybox/shaders، صعوبة البوت 3/2/1 + منزلق) ثم `bot_suicides ≤ 2` ثم بنود dev.17 المؤجَّلة. لا تخترع نطاقًا آخر.
3. `docs/UNITY-DEV17.md` و`docs/UNITY-DEV16.md` — ما تغيّر ولماذا، والدروس.
4. `docs/RELEASING.md` (قالب الإصدار) و`docs/DEVICE-TESTING.md` (Firebase Test Lab) و`docs/testlab/README.md` (أرشفة الفيديوهات).

## 2. جهّز بيئتك (ما يحتاجه المالك أن يعطيك)
- **GitHub**: صلاحية كتابة على `ayoub5550/my-xonotic`. الفروع تُبنى فوق `feat/unity-dev17-projectile-visuals` → أنشئ `feat/unity-dev18-<موضوع>`، PR مفتوح، وإصدار مسبق `unity-v0.1.0-dev.18` مع APK كأصل.
- **Unity**: حساب Unity `<بريد المالك>` / `<كلمة السر>` لتفعيل الرخصة الشخصية (Personal) على Linux headless: `Unity -batchmode -nographics -username ... -password ...`؛ المحرر 2022.3.62f3 + وحدة Android (SDK/NDK/OpenJDK المضمّنة). على x86_64 بلا GPU الأمر عادي؛ على ARM64 تحتاج qemu لـ `UnityShaderCompiler` (انظر AGENTS/الملاحظات).
- **Firebase Test Lab**: ملف Service Account JSON لمشروع `ayoub-261d7` (المالك يصدره من Firebase Console → Project settings → Service accounts). لا يوضع في المستودع. `gcloud auth activate-service-account --key-file=<sa.json> && gcloud config set project ayoub-261d7`. الحصة المجانية 5 أجهزة حقيقية/يوم — تشغيل واحد Game Loop على `model=a15x,version=34` لكل dev هو الأهم.
- **محتوى Xonotic 0.8.6**: حزمة `data/*.pk3` الأصلية مفكوكة في `ExternalContent/` (خارج git) وفق `tools/content/`. بدونها لا تعمل بوابات `weapons`/`prepare-maps`.
- **أدوات النظام**: Python 3.11+، ffmpeg، `aapt`/`apksigner` من build-tools 34، gsutil.

## 3. البوابات قبل أي commit (كلها عبر `tools/local_unity.py <task>`)
`compile` → `weapons` → `prepare-maps` → `test` (يجب ≥ 809 فحصًا وكلها PASS؛ أضف `Dev18*Tests.cs` وسجّله في `Editor/LocalTests.cs`) → `playtest` → `gameplay-playtest` → بناء Android (`XONOTIC_VERSION_CODE=19`) → `aapt dump badging` يجب أن يُظهر `versionCode='19' versionName='0.1.0-dev.18'` و`TEST_LOOP` → `apksigner verify` → Firebase Game Loop → **شاهد الفيديو** لا `Passed` فقط.
قواعد: نسخة Unity واحدة تعمل في الوقت نفسه؛ لا تعدّل C# أثناء تشغيلها؛ أعد `ProjectSettings/*.asset` و`Scenes/DevelopmentArena.unity` قبل الـ commit (`git checkout --`)؛ أي وحدة Unity جديدة = سطر في `Packages/manifest.json` **و**في `Assets/link.xml`.

## 4. طقس الإصدار
`docs/UNITY-DEV18.md` (عربي، قالب dev.17) + مدخل `CHANGELOG.md` + checkpoint جديد أعلى `AGENTS.md` + حالة dev.18 في `ROADMAP.md` + `docs/unity-dev18-build-<تاريخ>.json` (من `Builds/build-receipt.json`) + فيديو/تقرير Test Lab مضغوط في `docs/testlab/dev18/`. ثم PR + prerelease + تقرير للمالك بالحقائق فقط: حجم APK، SHA256، البوابات، نتيجة الجهاز، وما **لم** يُتحقق منه.

## 5. ممنوع
- كتابة أي كلمة سر أو مفتاح في المستودع أو السجل.
- تغيير اسم اللعبة أو التوقيع للمتاجر (قرار المالك).
- إعلان «تم» دون اختبار على جهاز Firebase حقيقي.

---

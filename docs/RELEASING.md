# سياسة الإصدارات

## أنواع الإصدار

- `*-dev.*`: نقطة تطوير. يجوز أن تكون **مصدرًا فقط**، بشرط أن يقول العنوان
  والملاحظات بوضوح: لا APK ولا ادعاء لعبة مكتملة.
- APK development: يتطلب بناء محلي ناجح ومعلومات التوقيع والحزمة وchecksum،
  مع فصل «بُني» عن «جُرّب على جهاز».
- إصدار للاعبين: لا يتم قبل اكتمال اختبار الجهاز والتراخيص والموارد والمنطق المطلوب.

## خطوات الإصدار

1. حدّث `VERSION` و`CHANGELOG.md` و`AGENTS.md`.
2. نفّذ الاختبارات المستقلة وتحقق من manifests والـGUIDs.
3. افحص staged files: لا كلمات مرور أو logs حساب أو signing keys أو موارد مجهولة.
4. commit على فرع معزول، مزامنة upstream، push، ومراجعة PR.
5. عند وجود رخصة صالحة: Unity compile → Editor tests → Play Mode → Android محلي.
6. سجّل commit المصدر ونسخة Unity والأدوات ونتيجة كل بوابة.
7. افحص APK باستخدام `aapt` و`apksigner` وSHA256 ثم جهاز فعلي. تحفظ binaries
   كمرفقات إصدار، لا داخل Git. debug signature ليست توقيع الإنتاج.
8. لا تستخدم `latest` أو عبارة «اللعبة كاملة» لنقطة مصدر أو prototype.

## قالب الملاحظات

```text
Version:
Source commit:
Type: source-only / development APK / device-tested release
What changed:
Included resource scope and licences:
Passed checks:
Not run / blockers:
Known gaps:
APK SHA256, package, ABI and signing certificate (only when APK exists):
Named test device and results (only when actually tested):
```

لا تُرفق سجلات تسجيل الدخول أو الرخصة الخام بالإصدارات العامة.

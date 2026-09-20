# البناء المحلي

## المتطلبات

- Unity **2022.3.62f3**، مع رخصة صالحة مفعّلة محليًا.
- Android Build Support وAndroid SDK/NDK وOpenJDK من الإصدار نفسه.
- Python **3.10+** للأدوات، وMono لاختبارات C# المستقلة.
- Linux هو بيئة التطوير التي فُحصت هنا. أوامر Unity عبر Python قابلة للتهيئة
  على أنظمة أخرى، لكننا لم نتحقق منها على Windows/macOS.

أدوات Android التي ثُبّتت في جلسة التأسيس: NDK r23b، OpenJDK 11.0.14.1،
build-tools 34.0.0، platform-tools 32.0.0 وplatform 36. تحقق من قبول تراخيص SDK
محليًا ومن نجاح البناء؛ وجود الملفات وحده غير كافٍ.

## التفعيل

الطريقة المفضلة هي تسجيل الدخول وتفعيل Unity Personal عبر **Unity Hub**.
محاولة هذه الجلسة أعادت `401` وانتهت قبل استيراد المشروع؛ لم تنتج APK.
لا تضع بريدك أو كلمة مرورك أو ملف الرخصة في Git، ولا تُرفق سجلات التفعيل الخام.

الأداة تقبل اختياريًا `UNITY_USER` و`UNITY_PASS` من البيئة للتفعيل القديم،
لكنها لا تطبع الأمر. هذا الأسلوب قد يُظهر الوسائط في قائمة العمليات المحلية؛
لا تستخدمه على جهاز مشترك، وامسح المتغيرات بعد الاستخدام. لا يحتوي المشروع
على بيانات حساب أو خطوة تحايل على ترخيص Unity.

## التشغيل بالواجهة

1. أضف جذر المشروع إلى Hub وافتحه بالنسخة المثبتة.
2. انتظر انتهاء ترجمة الكود؛ افحص Console.
3. `My Xonotic → 1 - Configure local project`.
4. `My Xonotic → 2 - Create development arena scene`.
5. اضغط Play. يجب أن تظهر عبارة `DEVELOPMENT SLICE — NOT FULL XONOTIC`.
6. شغّل اختبارات Editor وPlay Mode قبل بناء Android.

## التشغيل من الطرفية

```bash
export UNITY_EDITOR="/path/to/Unity"
python3 tools/local_unity.py compile
python3 tools/local_unity.py scene
python3 tools/content/pk3_tool.py fixture
python3 tools/local_unity.py test
python3 tools/local_unity.py playtest --graphics
python3 tools/local_unity.py android --timeout 3600
```

`--graphics` يحتاج شاشة محلية أو Xvfb؛ بدونه تُستخدم `-nographics`.
الاختبار بلا رسومات لا يثبت صحة العرض. `playtest` لا يستخدم `-quit` لأن الاختبار
يجب أن ينتظر مرور إطارات ومحاكاة فعلية.

الأداة تمنع تشغيل مهمتين متزامنتين على المشروع بقفل محلي، وتوقف مجموعة العمليات
عند انتهاء المهلة. لا تحذف قفلًا قبل التأكد من انتهاء العملية التي أنشأته.
مخرجات البناء والسجلات محلية ومستبعدة من Git.

## إعداد Android الحالي

- Package ID: `com.ayoub.myxonotic`.
- ARM64، IL2CPP، landscape، OpenGLES3، min API 26، target API 36.
- Development Build وتوقيع debug محلي؛ لا يوجد keystore إنتاجي.
- المخرج المتوقع عند **نجاح** البناء: `Builds/my-xonotic-development.apk`
  و`Builds/build-receipt.json`. هذه أسماء مخرجات، وليست ملفات أنتجتها هذه الجلسة.
- البناء الافتراضي يحتوي الساحة الأصلية فقط، ولا يضم `ThirdParty/Xonotic`.
- `XONOTIC_INCLUDE_EXTERNAL=1` مرفوض عمدًا إلى أن يكتمل مسار الدمج ومراجعة التوزيع.

## استيراد خريطة موجودة في المستودع

```bash
export XONOTIC_BSP="$PWD/ThirdParty/Xonotic/maps-pk3/maps/boil.bsp"
python3 tools/local_unity.py import
```

هذا يُنشئ **هندسة خريطة مع قواعد تطوير**؛ لا يطبق مواد Xonotic أو الضوء الأصلي
أو jump pads أو teleporters أو بقية منطق الخريطة. لا تستخدم نجاح هذا الأمر
لتسمية الخريطة مكتملة.

## ملاحظات Linux المقيد

في بعض sandboxes قد يفشل UnityShaderCompiler بسبب دعم FS/GS، أو FMOD بسبب
طلبات realtime scheduling. مشروع `my-librequake` وثّق حلولًا محلية خاصة لتلك البيئة
(qemu للـShaderCompiler وshim لجدولة FMOD). ليست متطلبات للمستخدم العادي،
ولا تُنسخ ملفات الرخصة/الثنائيات أو المسارات الخاصة إلى هذا المشروع.
`--unity` يقبل wrapper محليًا عند الحاجة.

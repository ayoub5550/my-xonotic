# سجل Firebase Test Lab — فيديوهات ولقطات لكل dev

كل إصدار يُختبر على هواتف Firebase Test Lab الحقيقية (`docs/DEVICE-TESTING.md`). هنا الأثر الخام: فيديو الجهاز، تقرير Game Loop، ولقطة مجمّعة (إطار كل 12 ثانية).

| dev | الجهاز | النوع | الفيديو | الملاحظة |
|---|---|---|---|---|
| dev.15 | Galaxy S24 (SC-51E) / Android 16 | Robo | `dev15/s24-robo.mp4` | خطأ `CapsuleCollider` (strip engine code) |
| dev.15 | Galaxy A15 5G (a15x) / Android 14 | Robo | `dev15/a15x-robo.mp4` | نفس الخطأ، 30–43 FPS في القائمة |
| dev.16 | Galaxy S24 / Android 16 | Robo | `dev16/s24-robo.mp4` + `s24-robo-screenshots/` | Passed، 0 أخطاء Unity |
| dev.16 | Galaxy A15 / Android 14 | Game Loop | `dev16/a15x-gameloop.mp4` + `a15x-results_scenario_1.json` | Passed لكن الطيار الآلي علق، `bot_frags=-10` |
| dev.17 | Galaxy A15 / Android 14 | Game Loop | `dev17/a15x-gameloop.mp4` + `a15x-results_scenario_1.json` | Passed، الطيار يتحرك، البوتات تصيب اللاعب، `bot_suicides=9` |

القاعدة: بعد كل تشغيل Test Lab، انسخ `video.mp4` و`results_scenario_N.json` إلى `docs/testlab/dev<N>/`، وولّد اللقطة المجمّعة:
`ffmpeg -i video.mp4 -vf "fps=1/12,scale=480:-1,tile=4x3" -frames:v 1 <name>-frames.png`.
لقطات Robo (~150 PNG لكل جهاز) لا تُرفع كاملة — عيّنة كل 10 لقطات فقط. الفيديوهات هنا نسخ مضغوطة (960px، x264 crf 28، بلا صوت) كي يبقى المستودع صغيرًا؛ الأصل بجودة الجهاز الكاملة مرفق كأصل (asset) في إصدار GitHub المقابل (`unity-v0.1.0-dev.16` و`dev.17`).

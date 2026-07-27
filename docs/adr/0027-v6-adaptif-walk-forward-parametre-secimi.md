# ADR-0027: v6 Adaptif Walk-Forward Parametre Seçimi

Durum: Kabul edildi
Tarih: 2026-07-27

## Bağlam

v6 sabit 30 bps bandı ATR tabanlı hale getirdi, fakat tek bir ATR period ve
multiplier kombinasyonunu bütün piyasa rejimlerine sabitlemek overfitting riskini
ortadan kaldırmıyordu. Parametre güncellemesinin OOS sonucunu görerek yapılması
ise doğrudan look-ahead ve seçim yanlılığı üretir.

## Karar

`WalkForwardBacktestOrchestrator` v6'ya özel ayrı bir adaptif akış sunar.
Immutable ve bounded aday grid'i train geçmişiyle warm-up edilir, yalnız
validation işlemleriyle Profit Factor üzerinden skorlanır. Seçim stream'i OOS
başlangıcında kesilir. Kazanan tanım, seçim tamamlandıktan sonra açılan taze
dataset oturumlarıyla yalnız bir sonraki OOS penceresinde çalıştırılır.

Sıfır tamamlanmış validation trade'i olan adaylar uygun değildir. Eşit skorlar
net getiri, drawdown ve daha sade parametrelerle deterministik çözülür. Hiç uygun
aday yoksa OOS çalıştırılmaz. Legacy `RunAsync` ve v1-v5 davranışı değiştirilmez.

## Sonuçlar

- OOS verisi parametre skoruna veya seçimine giremez.
- Her pencere bağımsız, tekrar üretilebilir seçim kanıtı taşır.
- Dataset açma maliyeti aday sayısıyla doğrusal artar; grid 64 adayla sınırlıdır.
- Selection raporu henüz SQL persistence şemasına eklenmemiştir; bu ayrı bir
  migration kararı gerektirir.
- Mekanizma acceptance kapılarını gevşetmez ve kârlılık garantisi vermez.

## Alternatifler

- OOS üzerinde en iyi parametreyi seçmek: veri sızıntısı nedeniyle reddedildi.
- Tüm geçmiş için tek global optimum: rejim değişimini ve temporal drift'i
  yakalamadığı için reddedildi.
- Sınırsız/paralel brute-force grid: kaynak tüketimi ve determinism riski
  nedeniyle reddedildi.

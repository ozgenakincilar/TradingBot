# v6 Dinamik Benchmark Paritesi ve Acceptance CLI

Durum: Kabul edildi

Tarih: 2026-07-27

## Like-for-like execution

Strateji simülatörü ve buy-and-hold benchmark artık aynı
`DynamicTwapExecutionModel` çekirdeğini kullanır. Ortak çekirdek aşağıdaki
kuralları tek uygulamada tutar:

- Son tamamlanmış mumun `(High-Low)/Close` volatilitesi.
- Kümülatif child miktarına göre doğrusal olmayan spread ve slippage.
- Mum hacminin en fazla `%5`i kadar toplam katılım.
- Tick/lot/minimum-notional quantization.
- `2..64` arası bounded child-order sayısı ve deterministik zaman dağılımı.
- Aynı `PaperExecutionEngine`, komisyon ve fill normalizasyonu.
- Quote bütçesinde yalnız decimal bölme kaynaklı bir-trilyonda-bir seviyesindeki
  farkı clamp eden ortak dust politikası.

Benchmark girişi ilk değerlendirme mumunun açılışında, yalnız hemen önceki
tamamlanmış mumun range/hacim bilgisiyle başlar. Katılım sınırı nedeniyle kalan
alış bütçesi sonraki mumlara taşınır. Terminal satış, son OOS mumu kapandıktan
sonra bilinen son OHLCV ve kapanış fiyatıyla bounded child-order'lara ayrılır;
pozisyon `%5` kapasite içinde tamamen kapanamıyorsa rapor fail-closed olur.

## Kilitli v6 araştırma sözleşmesi

`validate-atr-hysteresis-v6` komutu:

- Tam instrument kurallarını zorunlu tutar.
- v5 baseline ile adaptif v6 candidate'ı aynı dinamik policy altında çalıştırır.
- Dinamik policy'yi `2/100` spread bps, `1/150` slippage bps, `1/2`
  volatilite çarpanı, `5/20` katılım cezası ve `4` TWAP child olarak kilitler.
- ATR grid'ini period `7/14/21` × multiplier `0,1/0,2/0,3` olmak üzere dokuz
  kombinasyona kilitler.
- En az beş pencere, 30 günlük validation, 30 günlük OOS ve
  `2026-07-28` veya sonrası history başlangıcı ister.

## Acceptance kapıları

Sekiz kapının tamamı birlikte geçmelidir:

1. En az 30 tamamlanmış v6 işlemi.
2. Profit Factor en az `1,10` ve v5'ten yüksek.
3. Pozitif bileşik net getiri.
4. Dinamik maliyetli benchmark'a karşı negatif olmayan excess.
5. En kötü pencere drawdown'u en fazla `%5`.
6. Kârlı pencere oranı en az `%60`.
7. Execution maliyeti pozitif gross-before-cost kârdan düşük.
8. Hiçbir candidate penceresinde pending execution veya açık miktar yok.

## Process exit sözleşmesi

- `0`: komut ve acceptance başarılı.
- `1`: argüman, domain, veri, tarih, benchmark parity veya dış I/O hatası.
- `2`: operatör iptali.
- `3`: rapor başarıyla üretildi fakat `Acceptance.IsAccepted == false`.

Exit code `3` exception veya altyapı arızası için kullanılmaz.

Bu kilidin açılması kârlılık kanıtı değildir. Forward veri henüz oluşmamıştır;
komut tarih ve pencere kapılarından dolayı bugün çalıştırıldığında acceptance
iddiası üretemez.

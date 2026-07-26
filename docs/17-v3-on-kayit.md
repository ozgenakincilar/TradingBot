# v3 düşük-turnover ve kâr-koruma hipotezi ön kaydı

**Durum:** Kilitli kurallarla değerlendirildi; aday reddedildi

**Tarih:** 2026-07-26

## Araştırma sorusu

2024 v2 attribution çalışması, 146 işlemin 110'unun zararlı olduğunu, tahmini `87,0932 USDT` execution maliyetinin küçük brüt avantajı tükettiğini ve 78 işlemin olumlu excursion sonrasında kârı geri verdiğini gösterdi. v3 hipotezi iki ayrı sorunu hedefler:

1. Hızlı yeniden girişleri azaltarak turnover ve iki yönlü maliyeti düşürmek.
2. Anlamlı olumlu excursion oluştuktan sonra kazancın tamamını EMA gecikmesine geri vermemek.

## Sonuç görülmeden kilitlenen kurallar

- v2'deki signal EMA20 ve `30 bps` simetrik hysteresis aynen korunur.
- Bir `ExitToFlat` kararından sonra dört tamamlanmış `15m` candle boyunca yeni giriş engellenir. Bu süre bir `1H` trend candle'a karşılık gelir.
- Entry referansı, deterministik ve execution adaptöründen bağımsız olan entry-signal candle close değeridir.
- Pozisyon entry referansının `100 bps` üzerine çıktıktan sonra profit protection aktive olur.
- Aktivasyon sonrasındaki en yüksek kapalı signal candle close değerinden `50 bps` geri çekilme `profit-protection-exit` üretir.
- Trend filtresi kaybı profit protection'dan önce değerlendirilir; güvenlik çıkışı geciktirilmez.
- Mum içi high/low veya sonraki candle verisi karar motoruna girmez; yalnız kapalı candle close kullanılır.
- Spot long/flat, kaldıraçsız `%10` allocation ve mevcut maliyet politikası değişmez.

`100 bps` aktivasyon, kabul edilmiş `60 bps` iki yönlü execution maliyetinin üzerinde `40 bps` brüt alan ister. `50 bps` trailing mesafesi aktivasyonun yarısıdır; sabit take-profit değildir ve yukarı yönlü hareketi sınırlandırmaz. Dört-candle cooldown, ayrı bir optimize edilmiş sayı değil mevcut `15m/1H` timeframe zarfının exact oranıdır.

## Ayrı development validation kapıları

v3, hipotezi doğuran 2024 datasetinde kabul edilmeyecektir. İlk değerlendirme daha önce strateji seçimi için kullanılmamış ayrı bir canonical dataset üzerinde yalnız train/validation partition'larında v2 ile karşılaştırılır. Ayrılmış OOS strategy stream'ine verilmez.

Tüm kapılar birlikte geçmelidir:

- v3 tamamlanan trade sayısı v2'ye göre en az `%20` azalmalı,
- toplam fee + spread + slippage v2'ye göre en az `%20` azalmalı,
- kâra geçip zararla/başa baş kapanan trade oranı v2'ye göre en az `%30` azalmalı,
- v3 compounded validation net return pozitif olmalı,
- aynı pencerelerde maliyetli buy-and-hold benchmark excess negatif olmamalı,
- worst-window maximum drawdown `%5` değerini aşmamalı,
- validation pencerelerinin en az `%60`'ı pozitif net kapanmalı.

Kapılardan biri geçmezse v3 reddedilir. Eşikler sonuç görüldükten sonra gevşetilmez; yeni OOS açılmaz ve paper/testnet/live profile'a terfi yapılmaz.

## Kimlik ve tekrar üretilebilirlik

- Strateji version: `3`
- Strategy configuration schema: `profit-protection-v1`
- Random seed, canonical dataset SHA-256, split, execution policy ve v2/v3 manifest hash'leri raporda yer alır.
- v1/v2 configuration ve report hash'leri geriye dönük olarak değiştirilemez.

## Değerlendirme sonucu

Ön kayıt değiştirilmeden 2023 ayrı development train/validation verisinde çalıştırıldı. Yedi kapının yalnız drawdown kapısı geçti; v3 reddedildi ve ayrılmış OOS açılmadı. Hash'ler ve ayrıntılı sonuçlar [2023 v3 validation kanıtında](18-2023-v3-validation-kaniti.md) kayıtlıdır.

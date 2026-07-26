# ADR-0022: Cooldown ve trailing profit-protection v3 araştırma adayı

**Durum:** Kabul edildi

**Tarih:** 2026-07-26

## Bağlam

Reddedilmiş v2 adayının 2024 validation attribution sonucu, zararların yüksek turnover/execution maliyeti ve olumlu excursion'ın geri verilmesi etrafında yoğunlaştığını gösterdi. Aynı EMA veya hysteresis parametresini sonuçlara göre taramak overfitting riskini artırır. Yeni hipotezin davranışı ve kabul kapıları ayrı veriye bakılmadan önce kilitlenmelidir.

## Karar

- v3, v2'nin EMA20/EMA200, FOMO guard ve 30 bps hysteresis kurallarını korur.
- Çıkıştan sonra dört signal candle re-entry cooldown uygulanır.
- Entry-signal close değerinin 100 bps üstünde trailing profit-protection aktive edilir.
- Aktivasyondan sonra en yüksek kapalı candle close değerinden 50 bps düşüş pozisyonu flat'e taşır.
- Trend-filter exit daha yüksek önceliklidir; trailing kuralı koruyucu risk kontrolü yerine geçmez.
- Strateji state'i entry reference, peak close ve cooldown sayacını bounded scalar değerler olarak taşır.
- Yeni alanlar yalnız v3 configuration hash zarfına girer. v1/v2 sözleşme ve hash yolları aynen korunur.
- Ayrı development validation kabul kapıları [v3 ön kayıt belgesinde](../17-v3-on-kayit.md) sonuç görülmeden kilitlenmiştir.

## Sonuçlar

- v3 deterministik fakat stateful bir long/flat karar motorudur.
- Cooldown hızlı re-entry sayısını, trailing kural ise kâr geri verme oranını azaltmayı hedefler; başarı iddiası validation sonucuna bağlıdır.
- Closed-candle close semantiği live ve backtest için aynıdır; candle içi sıra varsayılmaz.
- v3 kodunun varlığı OOS, paper, testnet veya live kabulü değildir.

## Alternatifler

- 2024 verisinde parameter grid search yapmak hipotez verisine aşırı uyum riski nedeniyle reddedildi.
- Sabit take-profit, büyük kazananların yukarı yönünü sınırlandırdığı için reddedildi.
- Candle high üzerinden trailing yapmak candle içi olay sırasını bilmeden iyimser fill varsayımı yaratacağı için reddedildi.
- Lokal hard stop'u bu araştırma kuralına eklemek server-side koruyucu stop gereksiniminin yerine geçemeyeceği için reddedildi.

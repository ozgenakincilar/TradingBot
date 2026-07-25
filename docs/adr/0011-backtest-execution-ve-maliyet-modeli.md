# ADR-0011: Backtest execution ve maliyet modeli

**Durum:** Kabul edildi  
**Tarih:** 2026-07-25

## Bağlam

ADR-0010 deterministik karar replay'ini kurdu fakat fill veya PnL üretmedi. Sinyal candle kapanış fiyatından anında ve maliyetsiz fill varsaymak look-ahead ve aşırı iyimserlik yaratır. İlk araştırma raporu canlı order book arşivi bulunmadan da tekrarlanabilir olmalı, fakat gerçekçilik sınırını saklamamalıdır.

## Karar

- Karar aynı signal candle içinde fill edilemez; hedef ilk kez bir sonraki `15m` candle açılışında değerlendirilebilir.
- Fill zamanı `nextOpen + configured minimum latency`, fiyatı next-open midpoint etrafında sentetik spread ve yönsel slippage uygulanmış market fiyatıdır.
- Alış ve satışta quote-asset komisyonu net nakit, maliyet ve PnL'den düşülür.
- Aynı `PaperExecutionEngine` ve `PaperExecutionPolicy`, paper ile backtest finansal matematiğinin ayrışmasını azaltmak için yeniden kullanılır.
- Fill likiditesi mevcut candle'ın gelecekte bilinecek toplam hacminden değil, karar anında kapanmış önceki candle'ın base volume değerinden türetilir ve maximum participation ile sınırlandırılır.
- Likidite yetmezse target sonraki candle'a taşınabilir. Karşıt karar açık hedefi iptal edebilir/değiştirebilir.
- Pozisyon maliyeti, realized PnL ve iki taraflı fee hesabı mevcut `SpotPosition` domain modeliyle yürütülür.
- Rapor gross/net return, realized PnL, fee/spread/slippage maliyeti, net-liquidation value, drawdown, win rate, profit factor, expectancy ve ortalama holding time taşır.
- Açık pozisyon veri sonunda zorla kapatılmaz; open quantity, net-liquidation estimate ve pending execution açıkça raporlanır.

## Sonuçlar

- Flat piyasadaki round trip maliyetlerden sonra zarar yazar.
- Aynı karar/candle/policy akışı aynı raporu üretir.
- Kullanılabilir nakitten fazla alış yapılamaz; leverage veya negatif Spot pozisyon üretilemez.
- Next-open proxy, intrabar order book ve queue position bilmediği için production kârlılık kanıtı değildir.
- Walk-forward, out-of-sample, gerçek order-book replay, tick/lot rounding ve benchmark karşılaştırması hâlâ gereklidir.

## Alternatifler

- Signal close'da anında fill, look-ahead ve gecikme körlüğü nedeniyle reddedildi.
- Mevcut candle'ın toplam hacmini open fill'de kullanmak future-data sızıntısı olduğu için reddedildi.
- Veri sonunda zorunlu pozisyon kapatma, olmayan bir fill yaratacağı için reddedildi.

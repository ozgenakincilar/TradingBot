# ADR-0023: ADX trend-kalite filtresi v4 araştırma adayı

**Durum:** Kabul edildi

**Tarih:** 2026-07-27

## Bağlam

v2 ve v3 validation/attribution kanıtları, EMA entry sonrasında sürdürülemeyen hareketlerin ve tekrarlanan round-trip maliyetlerinin ana kayıp kaynağı olduğunu gösterdi. v3 exit odaklı trailing değişikliği bazı kazançları korusa da toplam trade ve maliyeti artırdı. Yeni hipotez exit parametresini tekrar taramadan giriş kalitesini hedeflemelidir.

`instructions.md` kural 80, trend stratejisinin yatay rejimi ayırmasını ister. Mevcut `1H close > EMA200` yönü gösterir fakat trend gücünü ayrı ölçmez.

## Karar

- v4, v2'nin davranışından çatallanır; v3 cooldown/trailing kuralları taşınmaz.
- Mevcut `1H close > EMA200` long yön filtresine ek olarak yalnız entry anında `1H ADX(14) >= 25` gerekir.
- ADX exact Wilder smoothing, kapalı candle ve checked decimal matematikle hesaplanır. Exact formül [v4 ön kayıt belgesinde](../19-v4-adx-rejim-on-kaydi.md) kilitlenmiştir.
- ADX long pozisyonda exit sinyali değildir. v2 trend-loss ve hysteresis exit sırası korunur.
- ADX düşük entry reason code'u `trend-strength-blocked` olur.
- v4 strategy configuration schema `adx-regime-v1` olur. v1-v3 hash yolları değiştirilmez.
- İlk validation 2022 historical development train/validation verisinde v2'ye karşı yapılır; holdout açılmaz ve sonuç forward OOS sayılmaz.
- Sekiz acceptance kapısı sonuç görülmeden [ön kayda](../19-v4-adx-rejim-on-kaydi.md) alınmıştır.

## Sonuçlar

- Tek yeni indikatör yalnız entry kalitesini etkiler ve v2 exit davranışını izole eder.
- ADX hesaplama stateless ve bounded trend window üzerinde tekrar üretilebilir kalır.
- Daha az işlem beklenir; ancak başarı yalnız ön kayıtlı validation kapılarıyla kabul edilebilir.
- v4 kodu ileride eklense bile paper/testnet/live izni oluşturmaz.

## Alternatifler

- EMA200 slope filtresi, slope periyodu/eşiği için repository dışı güçlü bir standart sağlamadığı ve aynı lagging EMA bilgisini tekrar kullandığı için reddedildi.
- Bollinger Bandwidth, volatility genişlemesini ölçüp trend yönü/gücünü tek başına ayırmadığı için ilk hipotez olarak reddedildi.
- RSI, MACD ve Stochastic gibi çoklu onay zinciri, çelişen indikatörler ve hareketsizlik riskini artırdığı için reddedildi.
- ADX düşüşünde exit, v3'te gözlenen erken exit/re-entry maliyet döngüsünü yeniden yaratabileceği için reddedildi.
- 2023/2024 üzerinde eşik taraması yapmak aynı gözlenmiş veriye overfit olacağı için reddedildi.

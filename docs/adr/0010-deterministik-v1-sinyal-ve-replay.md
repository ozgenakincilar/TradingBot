# ADR-0010: Deterministik v1 sinyal ve streaming replay

**Durum:** Kabul edildi  
**Tarih:** 2026-07-25

## Bağlam

ADR-0009 instrument, timeframe, exposure ve warm-up zarfını belirledi; exact giriş/çıkış formülünü bilerek açık bıraktı. Aynı kapalı candle setinin restart ve backtest sırasında aynı kararı üretmesi, gelecekteki trend candle'ın sinyale sızmaması ve henüz kanıtlanmamış formülün emir üretmemesi gerekir.

## Karar

- `btc-usdt-long-flat-baseline/v1` sinyal EMA periyodu `20`, trend EMA periyodu `200` olacaktır.
- Flat durumda giriş adayı yalnız son kapalı `15m` candle EMA20'yi aşağıdan yukarı keserken ve ilgili son kapalı `1H` candle EMA200'ün üzerindeyken oluşur.
- Giriş candle'ının pozitif gövde hareketi `%2`yi aşarsa FOMO guard kararı `Hold` yapar.
- Long durumda `1H close <= EMA200` veya `15m` EMA20 aşağı kesişimi `ExitToFlat` üretir; aksi halde `Hold` üretilir.
- EMA'lar `decimal`, son tam pencere ve first-close seed politikasıyla hesaplanır.
- Replay iki sıralı async candle akışını kapanış zamanına göre birleştirir; eşit kapanışta trend candle sinyal değerlendirmesinden önce alınır. Sinyal kapanışından sonraki trend verisi kullanılamaz.
- Replay yalnız karar ve sanal `Flat/Long` state üretir. Fill, ücret, spread, slippage, latency, PnL, position sizing veya emir üretmez.
- Aylık `%10` stretch hedefi formül, state veya parametre girdisi değildir.

## Sonuçlar

- Aynı sıralı veri ve v1 sözleşmesi aynı karar dizisini üretir.
- Gap, out-of-order veya identity mismatch tüm replay'i fail-closed sonlandırır.
- Bu baseline ancak ayrı train/validation/out-of-sample ve gerçekçi fill raporu kabul edilirse paper intent hattına bağlanabilir.
- EMA20 veya `%2` eşiğindeki değişiklik yeni strategy version gerektirir.

## Alternatifler

- RSI/MACD/Stochastic çoklu onayı, ilk baseline'ı gereksiz karmaşıklaştırdığı için reddedildi.
- Mum kapanışında anında fill ve PnL varsayımı look-ahead/gerçekçilik riski nedeniyle bu dilime alınmadı.
- Aylık hedefe göre sinyal eşiği veya risk artırımı yasak olduğu için reddedildi.

# ADR-0009: İlk Strateji Zarfı

**Durum:** Kabul edildi
**Tarih:** 2026-07-25

## Bağlam

İlk strateji kodlanmadan önce instrument, exposure yönü, timeframe, warm-up ve sürüm sınırları açık olmalıdır. Aylık net `%10` stretch hedefinin sinyal veya risk girdisine dönüşmesi yasaktır. Backtest ile kanıtlanmamış giriş/çıkış formülünün paper ya da live execution üretmesi de kabul edilemez.

## Karar

- İlk strateji instrument'ı `OKX:BTC-USDT` olacaktır.
- Exposure yalnız `LongFlat` olacaktır: sistem `Hold`, `EnterLong` veya `ExitToFlat` kararı verebilir; short üretemez.
- Sinyal değerlendirmesi yalnız tamamen kapanmış `15m` candle üzerinde yapılacaktır.
- Makro trend filtresi yalnız tamamen kapanmış `1H` candle ve `EMA(200)` kullanacaktır.
- `15m` ve `1H` serilerinin her biri en az 200 contiguous kapalı candle ile warm-up olmadan karar üretemez.
- Strateji kimliği `btc-usdt-long-flat-baseline`, ilk sürümü `1` olacaktır. Parametre veya karar formülü değişikliği yeni sürüm gerektirir.
- `1H` trend candle'ı ilgili `15m` sinyal candle'ının kapanışından sonra kapanmış olamaz; future-data/look-ahead fail-closed reddedilir.
- Aylık `%10` yalnız raporlama stretch hedefidir; strategy input, position sizing girdisi veya risk limiti artırma gerekçesi değildir.
- Bu ADR strateji zarfını kabul eder. Kesin giriş/çıkış formülü walk-forward ve out-of-sample kanıtı taşıyan ayrı bir karar olmadan execution'a bağlanmayacaktır.

## Sonuçlar

Olumlu:

- Kaldıraçsız Spot ve short yasağı strateji tip sistemine taşınır.
- Backtest, paper ve gelecekteki live çalışma aynı strategy ID/version ile karşılaştırılabilir.
- Closed-candle ve multi-timeframe look-ahead sınırı domain invariant'ı olur.
- Warm-up gereksinimi indikatör periyodundan kısa yapılandırılamaz.

Bedeller:

- Multi-timeframe candle readiness ve senkronizasyon gerekir.
- `1H EMA(200)` yaklaşık 8,3 günlük kapalı geçmiş ister.
- Kesin entry/exit formülü seçilene kadar strateji ekonomik emir üretemez.
- Tek instrument başlangıcı çeşitlendirme sağlamaz; exposure limitleri korunmalıdır.

## Alternatifler

- Tek `15m` timeframe: Makro trend filtresi olmadığı için reddedildi.
- Daha kısa EMA: Rejim filtresini daha hassas fakat daha gürültülü yapacağı için ilk baseline'da reddedildi.
- Short/Futures: ADR-0007 ve kullanıcı tercihi gereği kapsam dışıdır.
- `%10` hedefe göre dinamik risk: Hedef kovalamayı ve drawdown büyümesini teşvik ettiği için yasaktır.

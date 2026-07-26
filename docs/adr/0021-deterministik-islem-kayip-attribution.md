# ADR-0021: Deterministik işlem kaybı attribution raporu

**Durum:** Kabul edildi

**Tarih:** 2026-07-26

## Bağlam

Walk-forward raporu toplam getiri, drawdown, işlem sayısı ve execution maliyetini gösteriyordu; fakat zararların sinyal çıkışı, maliyet veya olumlu hareketin geri verilmesiyle ilişkisini işlem seviyesinde ayırmıyordu. Bu eksik gözlem, yeni strateji hipotezini kanıta dayalı kurmayı zorlaştırıyordu.

Mevcut execution raporu ve manifest hash'leri kilitli araştırma kanıtlarının parçasıdır. Attribution eklemek bu sözleşmeleri değiştirmemeli veya daha önce açılmamış OOS verisini strategy stream'ine vermemelidir.

## Karar

- Mevcut `BacktestExecutionReport` değiştirilmez; diagnostics açıkça çağrılan ayrı bir sözleşme ve SHA-256 kimliği üretir.
- Tamamlanan her trade için entry/exit reason code, fill VWAP, net PnL, fee, spread, slippage, MFE, MAE ve holding süresi tutulur.
- MFE/MAE yalnız pozisyonun açık olduğu tamamlanmış mumlardan hesaplanır. Next-open exit fill sonrasında aynı mumun high/low değeri kullanılmaz.
- Diagnostics koleksiyonu varsayılan 100.000 tamamlanmış trade ile bounded'dır; limit aşımı kısmi rapor üretmeden fail-closed sonuçlanır.
- Normal `RunAsync` diagnostics koleksiyonu ayırmaz ve eski rapor davranışını korur.
- `diagnose-hysteresis-v2` yalnız train/validation aralığını kullanır; OOS'u açmaz, acceptance kararı veya live yetkisi üretmez.
- Dış rapor, pencere manifest hash'leri ve diagnostics hash'leri üzerinden ayrı run/report SHA-256 üretir.

## Sonuçlar

- Kayıplar exit reason ve execution maliyeti temelinde tekrar üretilebilir biçimde incelenebilir.
- Kâra geçip zararla kapanan işlemler, MFE/MAE üzerinden ayrı ölçülür.
- Locked legacy raporları ve config hash'leri değişmeden kalır.
- Excursion metriği candle içi olay sırasını bilmez; stop/limit fill replay veya gerçek queue simülasyonu değildir.

## Alternatifler

- Alanları mevcut execution raporuna eklemek legacy hash uyumluluğunu bozacağı için reddedildi.
- Tüm karar ve candle akışını RAM'e almak bounded-memory kuralına aykırı olduğu için reddedildi.
- Çıkış mumunun high/low değerini kullanmak fill sonrasında oluşabilecek gelecek bilgiyi attribution'a katacağı için reddedildi.
- Attribution sonucundan doğrudan parametre optimize etmek overfitting ve OOS sızıntısı riski nedeniyle reddedildi.

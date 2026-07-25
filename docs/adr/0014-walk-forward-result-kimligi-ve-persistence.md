# ADR-0014: Walk-forward sonuç kimliği ve persistence

**Durum:** Kabul edildi
**Tarih:** 2026-07-25

## Bağlam

Walk-forward pencereleri tek başına araştırma kanıtı değildir. Hangi schedule, dataset, strateji, execution varsayımı ve OOS sonucu kullanıldığının değiştirilemez kimliklerle bağlanması gerekir. Aynı run girdilerinin farklı sonuç üretmesi determinism ihlalidir. Binlerce pencereyi tek bir büyük JSON hücresine yazmak sorgulanabilirliği ve veri sınırlarını zayıflatır.

## Karar

- `ScheduleSha256`, training modu/süreleri ile tüm sıralı split sınırlarını kapsar.
- `RunSha256`, schedule hash ve sıralı final-OOS manifest hash'lerini kapsar; araştırma girdisinin kimliğidir.
- `ReportSha256`, run hash ile her pencerenin tam execution metriklerini kapsar; araştırma çıktısının kimliğidir.
- Birleşik rapor yalnız schedule ile aynı indeks/split'e sahip, `FinalOutOfSampleEvaluation` amaçlı ve sadece OOS partition'ı açılmış sonuçları kabul eder.
- Pencere raporlarının initial balance, net/gross return, fee, spread, slippage, drawdown, win rate, profit factor ve expectancy ilişkileri yeniden doğrulanır.
- Birleşik metrikler kârlı pencere sayısı, toplam trade/fee, mean/median/worst/best net return, mean maximum drawdown ve bağımsız pencere getirilerinin varsayımsal compound değeridir.
- SQL Server'da üst kayıt `research.WalkForwardRuns`, pencere detayları `research.WalkForwardWindowResults` tablolarında normalize saklanır.
- Üst run ve tüm window satırları kısa Serializable transaction içinde atomik yazılır.
- Aynı `RunSha256` ve aynı `ReportSha256` tekrarında işlem idempotenttir. Aynı run için farklı report hash fail-closed determinism ihlalidir.
- Sonuç tabloları execution kaynağı değildir; analitik/research read modelidir.

## Sonuçlar

- Aynı schedule, manifestler ve metrikler aynı üç hash'i üretir.
- Sonuç değişirse run kimliği korunur, report kimliği değişir ve mevcut run üzerine sessizce yazılamaz.
- Pencere metrikleri SQL üzerinden ayrı sorgulanabilir; kontrolsüz tek JSON payload oluşmaz.
- Compound metrik gerçek sermayenin pencereler arasında taşındığı anlamına gelmez ve canlı getiri garantisi değildir.
- Tarihsel datasetleri her pencerede otomatik çalıştıran orchestration ve gerçek OOS kanıt üretimi sonraki dilimdir.

## Alternatifler

- Yalnız insan-okunur rapor dosyası, idempotency ve sorgulanabilir kanıt sağlamadığı için reddedildi.
- Tüm pencereleri tek `nvarchar(max)` JSON içinde tutmak, boyut ve analitik sorgu maliyeti nedeniyle reddedildi.
- Aynı run kimliğinde son sonucu overwrite etmek, nondeterminism kanıtını yok ettiği için reddedildi.

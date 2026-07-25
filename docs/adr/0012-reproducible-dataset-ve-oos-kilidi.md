# ADR-0012: Reproducible dataset ve out-of-sample kilidi

**Durum:** Kabul edildi  
**Tarih:** 2026-07-25

## Bağlam

Backtest sonucu veri dosyası, strategy/execution config veya split değiştiğinde sessizce farklılaşmamalıdır. Büyük CSV dosyalarını belleğe toplamak `instructions.md` kural 29'u ihlal eder. Parametre seçiminin out-of-sample veriyi görmesi de overfitting ve selection bias üretir.

## Karar

- Canonical candle dosyası UTF-8 CSV ve schema `closed-candle-csv-v1` olacaktır.
- Header tam olarak `open_time_utc,open,high,low,close,base_volume`; timestamp UTC round-trip (`O`), sayılar invariant decimal olacaktır.
- Dosya 64 KiB FileStream buffer ve satır-bazlı async reader ile okunur; `ReadAllLines` veya tüm dataset allocation'ı yasaktır.
- Açık dosyanın raw byte içeriği streaming SHA-256 ile fingerprint edilir, stream başa sarılır ve aynı salt-okunur handle üzerinden candle üretilir.
- CSV yalnız bir kez enumerate edilebilir. Tam EOF görülmeden row count/first/last summary ve run manifest üretilemez.
- Her satır OHLCV, UTC boundary, closed-candle, instrument/timeframe ve önceki satırla continuity kurallarından geçer.
- Split sabit UTC zaman aralıklarıdır: train, validation ve out-of-sample birbirini kesmeyen `[start, end)` aralıklarıdır.
- Parameter-selection planı yalnız train veya train+validation okuyabilir. Out-of-sample bu akışta fiziksel olarak yield edilmez.
- Final evaluation yalnız out-of-sample partition'ını tek başına açabilir.
- Run manifest strategy/config, execution varsayımları, dataset source/schema/hash/count/range, split, purpose, partitions ve random seed için canonical SHA-256 kimlikleri taşır.
- Split sınırları hem signal hem trend timeframe UTC boundary'sine oturmalı ve iki dataset tüm split aralığını kapsamalıdır.

## Sonuçlar

- Aynı veri, config, split ve seed aynı manifest hash'ini üretir.
- Seed değişikliği data/config hash'ini değil manifest kimliğini değiştirir.
- Eksik okuma, gap, malformed row, coverage eksikliği veya OOS politika ihlali fail-closed olur.
- Walk-forward pencere üretimi ve manifest/result persistence sonraki dilimlerdir; OOS başarısı henüz production kanıtı değildir.

## Alternatifler

- `File.ReadAllLines`, büyük datasetlerde kontrolsüz RAM/LOH riski nedeniyle reddedildi.
- Yüzdeye göre sonradan rastgele split, zaman serisi sızıntısı nedeniyle reddedildi.
- Parameter tuning sırasında OOS metriğini göstermek selection bias nedeniyle yasaklandı.

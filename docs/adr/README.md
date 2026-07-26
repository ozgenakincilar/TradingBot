# Architecture Decision Records

ADR’ler önemli ve geri dönüş maliyetli teknik kararların bağlamını korur.

| ADR | Karar | Durum |
|---|---|---|
| [ADR-0001](0001-moduler-monolit-clean-architecture-ddd.md) | Modüler monolit + Clean Architecture + DDD | Kabul edildi |
| [ADR-0002](0002-dotnet-10.md) | .NET 10 platformu | Kabul edildi |
| [ADR-0003](0003-paper-first.md) | Paper-first ve live deny-by-default | Kabul edildi |
| [ADR-0004](0004-microsoft-sql-server.md) | Microsoft SQL Server persistence | Kabul edildi |
| [ADR-0005](0005-acid-cap-ve-tutarlilik.md) | ACID transaction sınırları ve CAP tutarlılık tercihi | Kabul edildi |
| [ADR-0006](0006-trunk-based-git-stratejisi.md) | Trunk-based Git ve Pull Request stratejisi | Kabul edildi |
| [ADR-0007](0007-kaldiracsiz-spot-only.md) | Kaldıraçsız Spot-only ürün sınırı | Kabul edildi |
| [ADR-0008](0008-okx-tr-spot-ilk-borsa.md) | İlk borsa olarak OKX TR Spot V5 API | Kabul edildi |
| [ADR-0009](0009-ilk-strateji-zarfi.md) | BTC-USDT long/flat, 15m sinyal ve 1H EMA200 strateji zarfı | Kabul edildi |
| [ADR-0010](0010-deterministik-v1-sinyal-ve-replay.md) | EMA20 kesişimi, EMA200 trend, FOMO guard ve streaming decision replay | Kabul edildi |
| [ADR-0011](0011-backtest-execution-ve-maliyet-modeli.md) | Next-bar fill, spread/slippage/fee/latency ve PnL raporu | Kabul edildi |
| [ADR-0012](0012-reproducible-dataset-ve-oos-kilidi.md) | Streaming CSV, SHA-256 run manifest ve out-of-sample kilidi | Kabul edildi |
| [ADR-0013](0013-walk-forward-pencere-politikasi.md) | Rolling/expanding ve çakışmasız OOS walk-forward pencereleri | Kabul edildi |
| [ADR-0014](0014-walk-forward-result-kimligi-ve-persistence.md) | Schedule/run/report hash ve normalize walk-forward sonuç persistence | Kabul edildi |
| [ADR-0015](0015-walk-forward-oos-orkestrasyonu.md) | Streaming walk-forward orchestration ve OOS state izolasyonu | Kabul edildi |
| [ADR-0016](0016-atomik-tarihsel-dataset-export.md) | OKX geçmişinden sayfalı ve atomik canonical CSV dataset export | Kabul edildi |
| [ADR-0017](0017-maliyetli-buy-and-hold-benchmark.md) | Aynı OOS pencere ve maliyetlerle buy-and-hold benchmark | Kabul edildi |
| [ADR-0018](0018-cost-derived-ema-hysteresis-v2.md) | Round-trip maliyetinden türetilen 30 bps EMA hysteresis v2 araştırma adayı | Kabul edildi |
| [ADR-0019](0019-backtest-instrument-quantization.md) | Backtest tick/lot rounding ve minimum emir kuralları | Kabul edildi |
| [ADR-0020](0020-bounded-cumulative-order-book-depth.md) | Beş seviyeli cumulative depth ve paper market-impact fill modeli | Kabul edildi |

Yeni ADR biçimi:

```text
# ADR-NNNN: Başlık
Durum: Önerildi | Kabul edildi | Değiştirildi
Tarih: YYYY-MM-DD
Bağlam
Karar
Sonuçlar
Alternatifler
```

Kabul edilmiş ADR değiştirilmez; yeni ADR eskisini “Değiştirildi” olarak işaretler.

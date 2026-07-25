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

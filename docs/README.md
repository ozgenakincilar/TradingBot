# TradingBot Dokümantasyon İndeksi

Bu klasör, uygulama kodundan önce kabul edilmesi gereken ürün, mimari ve operasyon kararlarını içerir. Çelişki halinde `instructions.md` içindeki güvenlik kuralları önceliklidir.

| Belge | Amaç |
|---|---|
| [01-kapsam-ve-gereksinimler.md](01-kapsam-ve-gereksinimler.md) | Kapsam, varsayımlar, fonksiyonel ve kalite gereksinimleri |
| [02-mimari.md](02-mimari.md) | Clean Architecture, DDD, modüller ve bağımlılık kuralları |
| [03-domain-modeli.md](03-domain-modeli.md) | Bounded context, aggregate, entity, value object ve invariants |
| [04-teknik-gereksinimler.md](04-teknik-gereksinimler.md) | Platform, kod standartları, veri, ağ ve yapılandırma gereksinimleri |
| [05-entegrasyonlar.md](05-entegrasyonlar.md) | Borsa portları, adaptör sözleşmeleri ve dayanıklılık politikaları |
| [06-guvenlik-ve-risk.md](06-guvenlik-ve-risk.md) | Tehdit modeli, sır yönetimi ve finansal risk kontrolleri |
| [07-test-stratejisi.md](07-test-stratejisi.md) | Test piramidi, deterministik simülasyon ve kabul kapıları |
| [08-operasyon-ve-gozlemlenebilirlik.md](08-operasyon-ve-gozlemlenebilirlik.md) | Deployment, telemetry, alarm ve olay müdahalesi |
| [09-diyagramlar.md](09-diyagramlar.md) | Sistem, emir, veri ve deployment diyagramları |
| [10-yol-haritasi.md](10-yol-haritasi.md) | Aşamalar, teslimatlar ve tamamlanma ölçütleri |
| [11-git-stratejisi.md](11-git-stratejisi.md) | Branch, commit, PR, merge ve release çalışma modeli |
| [adr/README.md](adr/README.md) | Architecture Decision Record indeksi |

## Belge statüleri

- **Taslak:** Tartışmaya açık.
- **Kabul edildi:** Uygulama bu karara uymalıdır.
- **Değiştirildi:** Yeni bir ADR tarafından geçersiz kılınmıştır.

Belgeler kodla aynı pull request içinde güncellenir. Mimari sınırı değiştiren her karar için ADR oluşturulur.

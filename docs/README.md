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
| [12-performans-ve-risk-hedefleri.md](12-performans-ve-risk-hedefleri.md) | Getiri hedefleri, drawdown sınırları ve hedef yönetişimi |
| [13-instructions-uyumluluk-matrisi.md](13-instructions-uyumluluk-matrisi.md) | `instructions.md` içindeki 100 zorunlu kuralın statüsü ve kanıtı |
| [14-2025-oos-baseline-kaniti.md](14-2025-oos-baseline-kaniti.md) | Gerçek 2025 BTC-USDT verisindeki reddedilmiş v1 OOS sonucu ve artifact hash'leri |
| [15-2024-v2-validation-kaniti.md](15-2024-v2-validation-kaniti.md) | Ayrı 2024 geliştirme verisindeki reddedilmiş v2 validation sonucu ve acceptance kanıtı |
| [16-2024-v2-kayip-attribution-kaniti.md](16-2024-v2-kayip-attribution-kaniti.md) | Reddedilmiş v2 adayının işlem bazlı MFE/MAE, exit reason ve execution maliyeti kanıtı |
| [17-v3-on-kayit.md](17-v3-on-kayit.md) | Düşük-turnover ve trailing profit-protection v3 hipotezi ile sonuç öncesi acceptance kapıları |
| [adr/README.md](adr/README.md) | Architecture Decision Record indeksi |

## Belge statüleri

- **Taslak:** Tartışmaya açık.
- **Kabul edildi:** Uygulama bu karara uymalıdır.
- **Değiştirildi:** Yeni bir ADR tarafından geçersiz kılınmıştır.

Belgeler kodla aynı pull request içinde güncellenir. Mimari sınırı değiştiren her karar için ADR oluşturulur.

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
| [18-2023-v3-validation-kaniti.md](18-2023-v3-validation-kaniti.md) | Ayrı 2023 development verisinde reddedilen v3 validation sonucu ve exit attribution |
| [19-v4-adx-rejim-on-kaydi.md](19-v4-adx-rejim-on-kaydi.md) | ADX(14) trend-kalite v4 tasarımı, exact matematik ve sonuç öncesi validation kapıları |
| [20-2022-v4-validation-kaniti.md](20-2022-v4-validation-kaniti.md) | Ayrı 2022 historical development verisinde reddedilen v4 ADX validation sonucu |
| [21-2022-v4-kayip-attribution-kaniti.md](21-2022-v4-kayip-attribution-kaniti.md) | Reddedilmiş v4 işlemlerinin exit reason, MFE/MAE ve execution maliyeti ayrımı |
| [22-v5-dmi-yon-on-kaydi.md](22-v5-dmi-yon-on-kaydi.md) | v5 için entry-only `+DI > -DI` yön hipotezi, exact matematik ve veri öncesi kapılar |
| [23-2021-v5-validation-kaniti.md](23-2021-v5-validation-kaniti.md) | Ayrı 2021 historical development verisinde reddedilen v5 DMI yön validation sonucu |
| [24-2021-v5-kayip-attribution-kaniti.md](24-2021-v5-kayip-attribution-kaniti.md) | Reddedilmiş v5 işlemlerinin exit reason, MFE/MAE ve execution maliyeti ayrımı |
| [25-v6-atr-hysteresis-on-kaydi.md](25-v6-atr-hysteresis-on-kaydi.md) | v6 için `ATR(14) × 0,2` EMA bandı, nedensellik ve forward acceptance ön kaydı |
| [26-v6-adaptif-walk-forward-secim-sozlesmesi.md](26-v6-adaptif-walk-forward-secim-sozlesmesi.md) | v6 ATR grid'inin validation-only seçimi ve bakir OOS uygulama sözleşmesi |
| [27-v6-dinamik-benchmark-paritesi-ve-acceptance-cli.md](27-v6-dinamik-benchmark-paritesi-ve-acceptance-cli.md) | Ortak dinamik TWAP benchmark paritesi, kilitli v6 CLI ve exit-code sözleşmesi |
| [adr/README.md](adr/README.md) | Architecture Decision Record indeksi |

## Belge statüleri

- **Taslak:** Tartışmaya açık.
- **Kabul edildi:** Uygulama bu karara uymalıdır.
- **Değiştirildi:** Yeni bir ADR tarafından geçersiz kılınmıştır.

Belgeler kodla aynı pull request içinde güncellenir. Mimari sınırı değiştiren her karar için ADR oluşturulur.

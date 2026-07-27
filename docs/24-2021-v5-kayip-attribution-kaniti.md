# 2021 v5 kayıp attribution kanıtı

**Durum:** Tamamlandı; yalnız tanısal historical development kanıtı

**Tarih:** 2026-07-27

**Veri sınırı:** v5 validation ile aynı 2021 train/validation stream'i; holdout açılmadı

## Amaç

[Reddedilen v5 sonucunun](23-2021-v5-validation-kaniti.md) kalan yön hatasından mı, execution maliyetinden mi, yoksa olumlu hareketin EMA cross-down çıkışına kadar geri verilmesinden mi oluştuğunu işlem seviyesinde ayırmak. Bu çalışma parametre taraması, yeni strateji adayı veya kabul kararı değildir.

## Tekrar üretim ve kimlik

`validate-dmi-direction-v5` komutu [validation kanıtındaki](23-2021-v5-validation-kaniti.md) dataset, zaman, schedule, execution policy ve seed ile tekrar çalıştırılır. Çıktının `Candidate` alanı v5 diagnostics artifact'ıdır.

- Şema: `strategy-loss-diagnostics-v1`
- Run SHA-256: `C9F979FB66B02393DC80DF708B7908392B7A8E38D1BF469BE4368B03E667429E`
- Report SHA-256: `5DD0D486BA9A68BA08C7B11B8B0C810415F091677179EDEE81EEC52F34C061E1`
- Validation process exit code: `3`; bu beklenen aday ret kodudur

## Toplu sonuç

| Ölçüm | Sonuç |
|---|---:|
| Tamamlanan trade | 80 |
| Kazanan trade | 13 |
| Zararlı/net sıfır trade | 67 |
| Toplam net PnL | `-46,0080` |
| Tahmini execution maliyeti | `47,6139` |
| Maliyet öncesi yaklaşık sonuç | `+1,6059` |
| Pozitif MFE görüp net zarar eden trade | 47 (`%58,75`) |

Maliyet öncesi değer, net PnL'ye raporlanan fee/spread/slippage tahminlerinin geri eklenmesiyle üretilen tanısal karşı-olgudur; maliyetsiz fill varsayımı veya gerçek bir trade sonucu değildir. Yaklaşık brüt edge toplam sermayenin `%0,16`'sı düzeyindeyken tahmini maliyet bunun yaklaşık 29,65 katıdır. Bu nedenle edge ekonomik olarak uygulanabilir veya maliyet hatasına dayanıklı değildir.

## Exit reason attribution

| Exit reason | Trade | Kazanan | Net PnL | Maliyet | Ort. MFE | Ort. MAE | Kârı geri veren |
|---|---:|---:|---:|---:|---:|---:|---:|
| `signal-ema-hysteresis-cross-down` | 79 | 13 | `-45,0346` | `47,0184` | `%1,2558` | `%1,0641` | 47 |
| `trend-filter-exit` | 1 | 0 | `-0,9734` | `0,5955` | `%0` | `%0,7468` | 0 |

İşlemlerin `%98,75`i ve negatif net PnL büyüklüğünün `%97,8843`ü EMA hysteresis cross-down grubunda toplanır. Bu grubun maliyet öncesi yaklaşık sonucu `+1,9837` olsa da 79 round-trip için `47,0184` tahmini maliyet taşır. Beş pencerenin tamamının negatif olması, sonucun tek bir kötü döneme bağlı olmadığını gösterir.

## Çıkarım sınırı

- Strict `+DI > -DI` giriş doğrulaması yanlış yönlü girişleri azalttı ancak kalan sinyal sıklığı uygulanabilir edge üretmedi.
- Sorun artık yalnız yön seçimi değildir; küçük brüt hareket, 15m EMA çevresindeki tekrar giriş/çıkış maliyeti tarafından tüketilir.
- 47 favorable-giveback işlemi exit araştırması için sinyal verse de v3 trailing yaklaşımı daha önce turnover'ı artırmıştır. Aynı trailing parametrelerini yeniden taramak kanıt sayılmaz.
- Sonraki hipotez aynı yıllarda eşik optimizasyonu yapmamalı; işlem frekansını yapısal olarak azaltırken işlem başına beklenen hareketi maliyet tabanından anlamlı ölçüde büyütmelidir.
- 2021–2025 yeniden parametre seçimi için kullanılmaz. Yeni historical development dönemi kalmadığından sonraki adayın nihai kabulü 2026-07-27 sonrasında oluşan gerçek forward veriye dayanmalıdır.

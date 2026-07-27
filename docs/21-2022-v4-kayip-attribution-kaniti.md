# 2022 v4 kayıp attribution kanıtı

**Durum:** Tamamlandı; yalnız tanısal historical development kanıtı

**Tarih:** 2026-07-27

**Veri sınırı:** v4 validation ile aynı 2022 train/validation stream'i; holdout açılmadı

## Amaç

[Reddedilen v4 sonucunun](20-2022-v4-validation-kaniti.md) yalnız maliyet yüzünden mi, yanlış exit yüzünden mi, yoksa giriş sonrası yönlü hareket eksikliği yüzünden mi oluştuğunu işlem seviyesinde ayırmak. Bu çalışma parametre taraması veya yeni aday kabulü değildir.

## Tekrar üretim

`validate-adx-regime-v4` komutuyla aynı dataset, zaman, schedule, execution policy ve seed kullanılır; yalnız komut adı `diagnose-adx-regime-v4` olur.

- Şema: `strategy-loss-diagnostics-v1`
- Run SHA-256: `A77BF784AAD2E76EA2EC4A43FAF1787FF697842B33184C6521BB6BDAB8D84CEF`
- Report SHA-256: `718E1EE0E9C402C33B106230E28F629246FEC86C69DD3C6635B0D45D4C5EFD46`
- Beklenen process exit code: `0`

## Toplu sonuç

| Ölçüm | Sonuç |
|---|---:|
| Tamamlanan trade | 72 |
| Zararlı/net sıfır trade | 60 |
| Toplam net PnL | `-56,1860` |
| Tahmini execution maliyeti | `42,7761` |
| Pozitif MFE görüp net zarar eden trade | 37 |

Execution maliyeti tamamen çıkarılsa dahi yaklaşık brüt sonuç `-13,4099` olur. Bu karşı-olgusal değer gerçek fill modeli değildir fakat zararın yalnız komisyon/spread/slippage ile açıklanamayacağını gösterir.

## Exit reason attribution

| Exit reason | Trade | Kazanan | Net PnL | Maliyet | Ort. MFE | Ort. MAE | Kârı geri veren |
|---|---:|---:|---:|---:|---:|---:|---:|
| `signal-ema-hysteresis-cross-down` | 68 | 12 | `-52,0398` | `40,3993` | `%1,0858` | `%1,0756` | 36 |
| `trend-filter-exit` | 4 | 0 | `-4,1462` | `2,3767` | `%0,0810` | `%0,8201` | 1 |

İşlemlerin `%94,44`'ü ve negatif net PnL büyüklüğünün `%92,62`'si EMA hysteresis cross-down çıkışlarında yoğunlaşır. Buna rağmen bu grubun execution öncesi yaklaşık sonucu da negatiftir (`-11,6405`). Yalnız exit'i geciktirmek veya maliyeti azaltmak pozitif expectancy kanıtı değildir.

## Çıkarım sınırı

- ADX gücü tek başına long yönlü follow-through sağlamadı.
- `1H close > EMA200`, güçlü trendin kısa vadede yukarı yönlü olduğunu garanti etmez.
- 37 favorable-giveback işlemi exit araştırması için sinyal verir; fakat aynı grupta toplam brüt edge negatif olduğu için yalnız trailing ayarı v3 hatasını tekrarlayabilir.
- Yeni hipotez, güçlü trendin yönünü entry anında bağımsız doğrulamalı ve exit değişikliğini aynı sürüme karıştırmamalıdır.
- 2022/2023/2024/2025 yeniden parametre seçimi için kullanılmaz. Yeni historical validation ayrı ve daha önce kullanılmamış veri gerektirir; gerçek kabul yine tasarım tarihinden sonra oluşan forward OOS ister.

# ADR-0006: Trunk-Based Git ve Pull Request Stratejisi

**Durum:** Kabul edildi  
**Tarih:** 2026-07-25

## Bağlam

TradingBot finansal doğruluk ve güvenlik açısından kritik değişiklikler içerir. Değişikliklerin küçük, izlenebilir, otomatik doğrulanmış ve gerektiğinde geri alınabilir olması gerekir. Uzun ömürlü branch'ler entegrasyon gecikmesi ve büyük merge riski oluşturur.

## Karar

- `main` tek uzun ömürlü branch olacaktır.
- Geliştirme kısa ömürlü `feature`, `fix`, `refactor`, `test`, `docs` veya `chore` branch'lerinde yapılacaktır.
- Her mantıksal ve doğrulanmış adım Conventional Commits biçiminde commit edilecektir.
- `main` değişiklikleri zorunlu Pull Request ve CI kalite kapılarından geçecektir.
- Varsayılan birleştirme yöntemi Squash and Merge olacaktır.
- Release'ler Semantic Versioning uyumlu immutable tag'lerle işaretlenecektir.
- Execution, Risk, migration ve güvenlik değişiklikleri bağımsız inceleme gerektirecektir.

Ayrıntılı operasyon kuralları [Git ve GitHub Stratejisi](../11-git-stratejisi.md) belgesindedir.

## Sonuçlar

Olumlu:

- `main` geçmişi sade ve release'e uygun kalır.
- Küçük PR'lar inceleme kalitesini ve geri alınabilirliği artırır.
- CI başarısızlığı veya kritik inceleme notu merge'i engeller.
- Modüller arasındaki entegrasyon gecikmesi azalır.

Bedeller:

- Branch'ler sık sık `main` ile güncel tutulmalıdır.
- Büyük işler dikey dilimlere ayrılmalıdır.
- GitHub ruleset, CI ve CODEOWNERS yapılandırması gerekir.
- Squash sonrasında feature branch içindeki ara commit'ler `main` geçmişinde korunmaz.

## Alternatifler

- GitFlow: Uzun ömürlü `develop` ve release branch'lerinin ek merge/entegrasyon maliyeti nedeniyle reddedildi.
- Doğrudan main push: İnceleme ve kalite kapılarını atladığı için reddedildi.
- Merge commit ağırlıklı akış: Main geçmişini gereksiz dallandırdığı için varsayılan seçilmedi.
- Rebase-only: Commit düzeyindeki düzeni korusa da PR başına atomik geri alma hedefi için Squash tercih edildi.

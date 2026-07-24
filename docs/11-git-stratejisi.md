# Git ve GitHub Stratejisi

**Durum:** Kabul edildi  
**Sahiplik:** Geliştirme ekibi

## 1. Amaç

Kaynak kod değişikliklerinin küçük, denetlenebilir, test edilmiş ve gerektiğinde güvenli biçimde geri alınabilir kalmasını sağlamaktır. Finansal doğruluk, güvenlik ve migration değişiklikleri özellikle sıkı inceleme gerektirir.

## 2. Çalışma modeli

Proje **Trunk-Based Development** kullanır:

- `main` tek uzun ömürlü branch'tir.
- `develop`, kalıcı release branch'i veya kişisel uzun ömürlü branch kullanılmaz.
- Her iş `main` üzerinden açılan kısa ömürlü bir branch'te geliştirilir.
- Branch mümkünse 1–3 gün içinde Pull Request ile birleştirilir.
- `main` her zaman derlenebilir, testleri geçen ve dağıtıma aday durumda tutulur.
- Doğrudan `main` push yasaktır.

```mermaid
gitGraph
    commit id: "main"
    branch feature/risk-engine
    checkout feature/risk-engine
    commit id: "domain"
    commit id: "tests"
    checkout main
    merge feature/risk-engine id: "squash PR"
    branch fix/order-reconcile
    checkout fix/order-reconcile
    commit id: "fix + regression test"
    checkout main
    merge fix/order-reconcile id: "squash PR"
```

## 3. Branch adlandırma

```text
feature/<kisa-konu>
fix/<kisa-konu>
hotfix/<kisa-konu>
refactor/<kisa-konu>
test/<kisa-konu>
docs/<kisa-konu>
chore/<kisa-konu>
```

Örnekler:

```text
feature/risk-profile
feature/sql-server-persistence
fix/duplicate-order
docs/git-strategy
chore/ci-pipeline
```

Branch adları küçük harf, ASCII ve tire kullanır. Issue sistemi etkinleştirildiğinde numara eklenebilir: `feature/42-risk-profile`.

## 4. Commit politikası

Her **mantıksal, doğrulanmış ve geri alınabilir adım** ayrı commit olur. Her dosya kaydı veya mekanik değişiklik için commit oluşturulmaz.

Conventional Commits biçimi kullanılır:

```text
<type>(optional-scope): <imperative summary>
```

Desteklenen temel tipler:

| Tip | Kullanım |
|---|---|
| `feat` | Yeni kullanıcı/domain yeteneği |
| `fix` | Hata düzeltmesi |
| `test` | Test ekleme veya düzeltme |
| `docs` | Yalnızca dokümantasyon |
| `refactor` | Davranışı değiştirmeyen yapı değişikliği |
| `perf` | Ölçülmüş performans iyileştirmesi |
| `chore` | Tooling, paket veya bakım işi |
| `ci` | CI/CD değişikliği |
| `build` | Build sistemi veya bağımlılık değişikliği |

Örnekler:

```text
feat(risk): add maximum order exposure rule
fix(execution): prevent duplicate order after timeout
test(domain): cover cancellation and fill race
docs: document ACID transaction boundaries
ci: add release build and test workflow
```

Kurallar:

- Mesajlar İngilizce, emir kipinde ve kısa yazılır.
- `WIP`, `update`, `changes` gibi belirsiz mesajlar kullanılmaz.
- Kod ve onu doğrulayan test mümkünse aynı commit'te bulunur.
- Commit öncesi ilgili build/test komutları başarılı olmalıdır.
- Secret, gerçek connection string, API key, log ve build çıktısı commit edilmez.
- Başkasının commit geçmişi force-push ile değiştirilmez.
- Breaking change commit body/footer içinde `BREAKING CHANGE:` ile belirtilir.

## 5. Pull Request akışı

```text
Issue veya tanımlı iş
  → main'den kısa ömürlü branch
  → küçük ve anlamlı commit'ler
  → yerel build/test
  → Draft Pull Request
  → CI ve inceleme
  → Squash and Merge
  → branch silme
```

PR açıklaması en az şunları içerir:

- Problem ve kapsam.
- Uygulanan çözüm.
- Risk ve geri dönüş planı.
- Çalıştırılan testler ve sonuçları.
- Veri tabanı/migration etkisi.
- Güvenlik veya live-trading etkisi.
- İlgili issue ve ADR bağlantıları.

PR mümkün olduğunca tek bir iş sonucuna odaklanır. Büyük değişiklikler dikey ve çalışabilir dilimlere ayrılır.

## 6. Zorunlu kalite kapıları

PR birleştirilmeden önce:

- Release build: 0 warning, 0 error.
- Unit ve ilgili integration/contract testleri başarılı.
- Format ve analyzer kontrolü başarılı.
- Secret scan kritik bulgu içermiyor.
- NuGet vulnerability taraması kritik/yüksek açık içermiyor veya onaylı istisna var.
- Domain/mimari değişikliği ilgili doküman ve ADR ile uyumlu.
- SQL migration script'i incelenmiş ve geri dönüş/restore planı belirtilmiş.
- Execution, Risk veya Live davranış değişikliği en az bir bağımsız onay almış.

## 7. Merge politikası

Varsayılan yöntem **Squash and Merge**'dür:

- Feature branch commit'leri PR incelemesinde görünür.
- `main` üzerinde her PR tek, anlamlı commit olur.
- Merge commit varsayılan olarak kullanılmaz.
- Rebase merge varsayılan olarak kullanılmaz.
- Başarılı merge sonrasında remote branch silinir.
- Squash commit mesajı Conventional Commits biçimine getirilir.

## 8. Main branch koruması

GitHub repository oluşturulduğunda aşağıdaki ruleset uygulanır:

- Pull Request zorunlu.
- En az bir approval zorunlu.
- İstenen değişiklikler ve konuşmalar çözülmeden merge yasak.
- Zorunlu CI status check'leri başarılı olmalı.
- Branch merge öncesi güncel olmalı veya merge queue kullanılmalı.
- Force push ve branch deletion yasak.
- Admin bypass mümkünse kapalı ve audit'li.
- CODEOWNERS eşleşen kritik dosyalarda owner onayı zorunlu.

Kritik sahiplik alanları:

```text
src/TradingBot.Domain/Risk/
src/TradingBot.Domain/Orders/
src/TradingBot.Infrastructure/Persistence/Migrations/
src/TradingBot.Infrastructure/Exchanges/
.github/workflows/
```

## 9. Release ve sürümleme

Semantic Versioning kullanılır:

```text
v0.1.0  Paper trading çekirdeği
v0.2.0  Market data
v0.3.0  Backtest
v0.4.0  Testnet execution
v1.0.0  Kontrollü production sürümü
```

- Release yalnızca `main` üzerindeki doğrulanmış commit'ten üretilir.
- Tag biçimi `vMAJOR.MINOR.PATCH` olur.
- Production tag'leri mümkünse imzalı ve immutable tutulur.
- Artifact, commit SHA ve yapılandırma sürümü izlenebilir olmalıdır.
- Tag silme veya yeniden hedefleme yasaktır.

## 10. Hotfix akışı

1. `main` üzerinden `hotfix/<konu>` açılır.
2. Hata önce regression test ile yeniden üretilir.
3. En küçük güvenli düzeltme uygulanır.
4. Normal PR ve zorunlu CI kuralları korunur.
5. Gerekirse patch sürümü yayınlanır.

Acil durum kalite kapılarını kaldırmaz. Live trading'i durdurmak deployment gerektirmeyen kill switch/runbook üzerinden yapılır.

## 11. Veritabanı ve güvenlik değişiklikleri

- Migration ile uygulama kodu aynı PR içinde uyumlu olmalıdır.
- Destructive migration açıkça işaretlenir; backup ve rollback/forward-fix planı olmadan merge edilmez.
- Production connection string veya secret hiçbir branch'e yazılmaz.
- API key sızıntısında commit silmeye güvenilmez; key derhal revoke/rotate edilir.
- Security fix PR'ları hassas ayrıntıları public issue/commit mesajında açıklamaz.

## 12. Yerel çalışma komutları

```powershell
git switch main
git pull --ff-only
git switch -c feature/risk-profile

dotnet build TradingBot.slnx --configuration Release
dotnet test TradingBot.slnx --configuration Release --no-build

git add <ilgili-dosyalar>
git commit -m "feat(risk): add risk profile aggregate"
git push -u origin feature/risk-profile
```

`git reset --hard`, kontrolsüz force push ve paylaşılan branch geçmişini yeniden yazmak normal çalışma akışında kullanılmaz.

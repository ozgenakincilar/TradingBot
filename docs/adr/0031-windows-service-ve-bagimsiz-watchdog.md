# ADR-0031: Windows Service ve Bağımsız Watchdog

Durum: Kabul edildi

Tarih: 2026-07-27

## Bağlam

Forward evidence Host'u console tabanlı soak sürecinde doğrulandı; ancak kullanıcı
oturumunun kapanması, process crash'i veya event loop/worker kilitlenmesi için
işletim sistemi sahipliğinde otomatik recovery yoktu. Process içi health durumu,
aynı process'in kilitlendiği durumda bağımsız kanıt sayılamaz.

## Karar

`TradingBot.Host`, resmi .NET Windows Service lifetime'ına bağlanacaktır. SCM
delayed automatic başlangıç ve üç kademeli restart recovery uygulayacaktır.
Bağımsız scheduled-task watchdog her 60 saniyede local liveness, forward health
ve heartbeat metriğini denetleyecek; üç ardışık hata ve restart cooldown sonrası
SCM üzerinden idempotent restart yapacaktır. Host ayrıca service adına bağlı
global Windows kernel lease'i alacaktır. Deployment port çakışmasını ve aynı
adın yönetilen install root dışında kullanılmasını fail-closed reddedecektir.

## Sonuçlar

- Kullanıcı oturumundan bağımsız otomatik başlangıç ve crash recovery sağlanır.
- Watchdog process içi worker kilitlenmesini dışarıdan algılar.
- Exchange readiness kaybı restart fırtınası üretmez; yalnız liveness ve forward
  heartbeat recovery tetikler.
- Service sanal hesabı dosya ve SQL için en az yetkiyle sınırlandırılır.
- Global kernel lease, evidence file lease ve SCM benzersizliği birlikte yerel
  single-instance savunması sağlar.
- Multi-host failover ve distributed consensus hâlâ kapsam dışıdır.

## Alternatifler

- Yalnız SCM recovery: yaşayan fakat kilitlenmiş process'i algılamadığı için
  reddedildi.
- Watchdog'u Host içinde çalıştırmak: aynı failure domain'ini paylaştığı için
  reddedildi.
- Azure/container orchestration: mevcut yerel forward kanıt dönemi için gereksiz
  operasyonel değişken oluşturduğu için ertelendi.

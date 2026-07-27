# Forward Evidence Operasyonel Doğrulama

Durum: Kabul edildi

Tarih: 2026-07-27

## Kanıt yüzeyleri

- `TradingBotForwardEvidenceTest` adlı ayrı SQL Server veritabanında migration
  uygulanır. Test, iki append-only tabloda hem `UPDATE` hem `DELETE` dener;
  trigger hatası beklenir ve test satırlarının transaction dışına çıkmadığı
  ayrı bağlantıyla doğrulanır.
- Gerçek artifact store ve kilitli v6 evaluator kullanan sentetik prova yedi tam
  30 günlük bölüm üretir. İlk altı bölüm evaluation üretmez. Yedinci bölüm aynı
  girdide aynı run, report ve report-file SHA-256 kimliklerini üretir.
- Yedinci bölümden tek bir `15m` candle çıkarıldığında staging dizini temizlenir,
  manifest yayınlanmaz ve evaluator çağrılmaz.
- `smoke-okx-candles` yalnız OKX TR public endpoint'inden son iki kapanmış `15m`
  ve `1H` candle'ını ister. Ana evidence dizinine ve SQL'e yazmaz; payload veya
  fiyat loglamadan istek, rate-limit yanıtı ve süre özetini döndürür.
- Process-local telemetry durumu yalnız atomik primitive alanlar kullanır. Son
  başarılı çevrim, pencere sayıları, son mühürlenen indeks, disk alanı, retry
  sinyali ve SQL hata sayısı `/health/forward-evidence` ile
  `/metrics/forward-evidence` uçlarından okunabilir.

## Gerçek SQL testi

Test yalnız environment variable açıkça verildiğinde çalışır ve yanlış catalog
adıyla fail-fast olur:

```powershell
$env:TRADINGBOT_FORWARD_EVIDENCE_TEST_DB_CONNECTION="Data Source=localhost\MSSQLSERVER01;Initial Catalog=TradingBotForwardEvidenceTest;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
dotnet test tests/TradingBot.Infrastructure.Tests/TradingBot.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~ForwardEvidenceAppendOnlySqlIntegrationTests
```

## OKX diagnostic smoke

```powershell
dotnet run --project src/TradingBot.Research/TradingBot.Research.csproj -c Release -- smoke-okx-candles --instrument BTC-USDT --timeout-seconds 15
```

Komut API anahtarı, hesap veya para kullanmaz. Başarı `0`, timeout/payload/HTTP
hatası `1`, kullanıcı iptali `2` döndürür. Acceptance `exit 3` davranışıyla hiçbir
bağlantısı yoktur.

## Tek-yazar sınırı

Worker evidence root altında `.writer.lock` dosyasını `FileShare.None` ile açık
tutar. Aynı paylaşılan volume üzerinde ikinci writer fail-fast olur. Dağıtımda
replica sayısı yine `1` olmalıdır; farklı ve paylaşılmayan diskler arasında bu
kilit distributed consensus sağlamaz.

## Değişmeyen bilimsel sözleşme

Bu paket v6 strateji tanımını, ATR grid'ini, execution policy'yi, random seed'i
ve sekiz acceptance kapısını değiştirmez. Sentetik test yalnız operasyonel
determinizm kanıtıdır; kârlılık veya gerçek forward OOS sonucu değildir.

## Windows servis loglaması

Host Windows üzerinde console veya yönlendirilmiş stdout/stderr ile çalıştırılırken
`EventLogLoggerProvider` devre dışı bırakılır. Böylece Event Log kaynağı yazma
izninin bulunmaması, OKX veya SQL kaynaklı asıl başlangıç hatasını ikincil bir
`AggregateException` ile maskelemez. Operasyonel loglar supervisor tarafından
stdout/stderr üzerinden toplanmalıdır.

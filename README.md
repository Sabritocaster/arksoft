# Enterprise Vault → storionX Migration Demo

Enterprise Vault arşivlerini mock storionX API'ye taşıyan küçük bir C#/.NET 8 uygulamasıdır.

Uygulama şunları gösterir:

- Mailbox, journal ve FSA arşivlerini keşfetme
- Kullanıcı, compliance ve dosya arşivi eşleme
- Orphan arşivleri bekletme
- SIS parçalarını cache ile birleştirme ve SHA-256 doğrulama
- Retention, metadata ve legal hold bilgisini koruma
- Paralel ingestion, rate limit ve retry
- Idempotency, checkpoint/resume ve JSON audit raporu
- Dry-run, filtreleme ve kaynak-hedef reconciliation

Tasarım kararları için [DESIGN.md](./DESIGN.md) dosyasına bakılabilir.

## Gereksinimler

- .NET 8 SDK
- İsteğe bağlı: `curl` ve `jq`

```bash
cd backend
dotnet restore Arksoft.EvMigration.sln
dotnet build Arksoft.EvMigration.sln
```

## Hızlı başlangıç

Önce örnek EV verisini üretin:

```bash
dotnet run --project src/EvMigration.Cli -- generate
```

Birinci terminalde mock storionX API'yi başlatın:

```bash
dotnet run --project src/StorionX.MockApi --urls http://127.0.0.1:5099
```

İkinci terminalde discovery ve migration çalıştırın:

```bash
dotnet run --project src/EvMigration.Cli -- discover

dotnet run --project src/EvMigration.Cli -- migrate \
  --checkpoint output/demo-checkpoint.json \
  --report output/migration-report.json
```

Aynı migration komutu tekrar çalıştırıldığında tamamlanmış item'lar checkpoint'ten okunur ve API'ye yeniden gönderilmez.

Son olarak kaynak ile hedefi karşılaştırın:

```bash
dotnet run --project src/EvMigration.Cli -- reconcile \
  --report output/reconciliation-report.json
```

## Web demo

Repo, aynı migration motorunu kullanan Next.js/Tailwind dashboard ve küçük bir
Demo API içerir. Web arayüzünde discovery, dry-run, migration, checkpoint,
idempotency, hedef dedup ve reconciliation akışları çalıştırılabilir.

Yerel geliştirme için üç terminal kullanın:

```bash
# Terminal 1: mock hedef
cd backend
dotnet run --project src/StorionX.MockApi --urls http://127.0.0.1:5099

# Terminal 2: demo orchestration API
cd backend
dotnet run --project src/EvMigration.DemoApi --urls http://127.0.0.1:5100

# Terminal 3: frontend
cd frontend
npm install
NEXT_PUBLIC_API_BASE_URL=http://127.0.0.1:5100 npm run dev
```

Arayüz `http://localhost:3000` adresinde açılır. Demo API ilk başlangıçta
örnek EV verisini `backend/demo-data/source` altında otomatik üretir. **Demo'yu sıfırla**
aksiyonu mock hedef state'ini, rate-limit durumunu, checkpoint'i ve raporları
temizler.

### Docker ile tek komut

Yerel HTTP demo:

```bash
# Repo kökünde
cp .env.example .env
docker compose up -d --build
```

Varsayılan örnek ayarlarla dashboard `http://localhost:8088` adresindedir.

VPS üzerinde `.env` içindeki `DEMO_DOMAIN` değerini DNS kaydı sunucuya yönlenen
bir hostname olarak ayarlayın:

```dotenv
DEMO_DOMAIN=migration-demo.example.com
HTTP_PORT=80
HTTPS_PORT=443
```

Caddy bu durumda HTTPS sertifikasını otomatik yönetir. İnternete açık bir demo
için `deploy/Caddyfile.auth.example` dosyasındaki Basic Auth örneği kullanılmalıdır.
Yalnızca gateway portları host'a açılır; Demo API ve mock storionX internal Docker
ağında kalır.

## CLI komutları

Aşağıdaki komutlar `backend/` klasöründe çalıştırılır.

### Veri üretme

```bash
dotnet run --project src/EvMigration.Cli -- generate \
  --output samples/generated
```

Üretilen katalog `samples/generated/ev-data.json`, SIS blob'ları ise `samples/generated/blobs/` altında bulunur.

### Discovery

```bash
dotnet run --project src/EvMigration.Cli -- discover
```

Örnek mapping sonucu:

```text
A1 → sx-mailbox-ayse
A2 → sx-mailbox-mehmet
A3 → pending_mapping
J1 → sx-compliance
F1 → sx-files-finance
```

### Tek item rehydration

```bash
dotnet run --project src/EvMigration.Cli -- rehydrate --item I100
```

### Dry-run ve filtreler

Dry-run API'ye istek göndermez, SIS blob'u okumaz ve checkpoint oluşturmaz.

```bash
dotnet run --project src/EvMigration.Cli -- migrate \
  --dry-run \
  --archive A1 \
  --folder Inbox \
  --from 2021-01-01T00:00:00Z \
  --to 2021-12-31T23:59:59Z
```

Desteklenen filtreler:

| Seçenek | Açıklama |
| --- | --- |
| `--from` | Başlangıç tarihi, dahil |
| `--to` | Bitiş tarihi, dahil |
| `--archive` | Tek bir kaynak archive ID |
| `--folder` | Klasör yolu veya alt klasörleri |
| `--workers` | Paralel worker sayısı, varsayılan `4` |

### Checkpoint kullanmadan idempotency kontrolü

API çalışırken aşağıdaki komut iki kez çalıştırılabilir:

```bash
dotnet run --project src/EvMigration.Cli -- migrate \
  --checkpoint none \
  --report output/idempotency-report.json
```

İlk çalışmada item'lar `created`, ikinci çalışmada `already_existing` olarak sayılır. Hedef item sayısı değişmez.

## Örnek sonuç

İlk migration:

```text
scanned_item_count:          7
eligible_item_count:         6
pending_mapping_item_count:  1
uploaded_item_count:         6
failed_item_count:           0
migrated_bytes:              804
```

Aynı checkpoint ile ikinci çalışma:

```text
attempted_item_count:           0
checkpoint_skipped_item_count:  6
physical_sis_reads:             0
```

Reconciliation:

```text
expected_item_count:  6
target_item_count:    6
matched_item_count:   6
source_logical_bytes: 804
target_logical_bytes: 804
is_reconciled:        true
```

Mock hedefte örnek veri 804 byte iken SIS dedup sonrasında 391 byte olarak saklanır.

## Hata davranışı

- `429`: `Retry-After` dikkate alınır.
- `429`, `503`, timeout: exponential backoff ve jitter ile tekrar denenir.
- Kalıcı `4xx`, bozuk blob veya hash uyuşmazlığı: item başarısız yazılır, diğer item'lar devam eder.
- Başarılı item: atomik checkpoint'e yazılır.
- Mapping bulunamayan archive: API'ye gönderilmez ve raporlanır.
- Legal hold: hedefe taşınır ve kaynak temizliğine dahil edilmez.

## Testler

```bash
dotnet test Arksoft.EvMigration.sln
```

Testler özellikle generator determinism, mailbox/journal/FSA mapping, SIS cache ve rehydration, retry, idempotency, paralellik, checkpoint/resume, dry-run ve reconciliation davranışlarını kapsar.

## Varsayımlar ve sınırlar

- Mock storionX veriyi bellekte tutar; API yeniden başlatılırsa hedef state sıfırlanır.
- İçerikler küçük olduğu için API'ye base64 olarak gönderilir.
- EV kaynağı migration sırasında değişmeyen, salt okunur bir snapshot kabul edilir.
- Gerçek EV erişimi yerine JSON katalog ve blob dosyaları kullanılır.
- Kaynak shortcut/placeholder temizliği uygulama tarafından yapılmaz.

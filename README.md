# randevu-node

Kendi bilgisayarınızdaki [LM Studio](https://lmstudio.ai) modelini **Randebul** platformuna bağlayan küçük program. Bilgisayar
platforma **dışa doğru** bağlanır; platform işi ona gönderir, cevabı alır. Bilgisayarınızda hiçbir port açılmaz, sabit IP ya da
modem ayarı gerekmez, bağlantı TLS ile platformun sertifikasından geçer. Birden çok bilgisayar bağlarsanız platform işi, istenen
model yüklü ve en az meşgul olana verir.

Program yalnızca şunları yapar: platforma bağlanır, 15 saniyede bir LM Studio'da **yüklü** modelleri bildirir, gelen işi
`localhost`'taki LM Studio'ya sorar, cevabı geri yollar. Hiçbir metni saklamaz; günlüğe yalnız sonuç, süre ve token sayısı yazar.

## 1. LM Studio'yu hazırlayın

1. LM Studio'yu kurun, kullanacağınız modeli indirip **yükleyin** (düşünme/reasoning modu kapalı bir model daha iyi sonuç verir).
2. Sunucuyu başlatın: Developer → **Start Server** (varsayılan `http://127.0.0.1:1234`). Bilgisayar açılınca çalışsın
   istiyorsanız "run the LLM server on login" seçeneğini açın ya da `lms daemon up` kullanın.
3. Developer → Server Settings: **Authentication açık** (bir API token üretin, aşağıda kullanacaksınız), **prompt logging kapalı**.
4. Disk şifrelemesi (FileVault / BitLocker) açık, işletim sistemi güncel olsun.

## 2. Belirteç alın

Platform yöneticisi konsolda **Yapay zekâ → Düğümler → Düğüm ekle** ile bilgisayarınız için bir belirteç üretir; belirteç bir kez
gösterilir. Kaybolursa yöneticiden yenisini isteyin (eskisi iptal edilir).

## 3. Kurun

### macOS (Homebrew)

```bash
brew install codehepta/randebul/randevu-node
```

### macOS / Linux (kurulum betiği)

```bash
curl -fsSL https://raw.githubusercontent.com/codehepta/randebul-node/main/install.sh | sh
```

Betik son sürümü `~/.local/bin/randevu-node` olarak indirir.

### Windows

[Releases](https://github.com/codehepta/randebul-node/releases) sayfasından `randevu-node-win-x64.zip` dosyasını indirip açın.

### Kaynaktan

.NET 10 SDK ile: `dotnet publish -c Release -r osx-arm64 -o out` (diğer hedefler: `osx-x64`, `win-x64`, `linux-x64`, `linux-arm64`).

## 4. Çalıştırın

```bash
randevu-node --server https://randebul.com --token <belirteç> --lmstudio http://127.0.0.1:1234 --lmstudio-token <LM Studio token>
```

Aynı bilgiler ortam değişkeniyle de verilebilir: `RANDEVU_NODE_SERVER`, `RANDEVU_NODE_TOKEN`, `RANDEVU_NODE_LMSTUDIO`,
`RANDEVU_NODE_LMSTUDIO_TOKEN` (belirteci komut satırı yerine ortam değişkeniyle vermek işlem listesinde görünmesini önler).

Çıktı: `connected`, ardından her iş için `job ok 3400 ms 2500/300 tokens` gibi bir satır. Platform belirteci iptal ederse program
`token revoked` yazıp 3 koduyla kapanır. Konsoldaki Düğümler kartında bilgisayarınız "çevrimiçi", yüklü modelleri ve iş sayısıyla
görünür.

### Açılışta otomatik başlatma (macOS)

`~/Library/LaunchAgents/com.randebul.node.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>com.randebul.node</string>
  <key>ProgramArguments</key><array><string>/opt/homebrew/bin/randevu-node</string></array>
  <key>EnvironmentVariables</key><dict>
    <key>RANDEVU_NODE_SERVER</key><string>https://randebul.com</string>
    <key>RANDEVU_NODE_TOKEN</key><string>…</string>
    <key>RANDEVU_NODE_LMSTUDIO_TOKEN</key><string>…</string>
  </dict>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
  <key>StandardOutPath</key><string>/tmp/randevu-node.log</string>
  <key>StandardErrorPath</key><string>/tmp/randevu-node.log</string>
</dict></plist>
```

`launchctl load ~/Library/LaunchAgents/com.randebul.node.plist`. Windows'ta Görev Zamanlayıcı (oturum açılınca, hata olursa
yeniden başlat), Linux'ta `Restart=on-failure` ile bir kullanıcı systemd birimi aynı işi görür.

## Sık sorulanlar

- **Model listesi boş görünüyor.** LM Studio'da model *indirilmiş* ama *yüklü* değil; yükleyin. Program yalnız yüklü sohbet
  modellerini bildirir.
- **İşler "timeout" oluyor.** Platformdaki Düğümler kartında bütçeyi büyütün (yerel 30B sınıfı bir model için 8–10 s) ya da daha
  küçük/düşünmeyen bir model yükleyin.
- **İşler "invalidoutput" oluyor.** Model istenen JSON'ı üretemiyor; düşünme modu kapalı bir model deneyin. Program şemayı
  istemde tarif eder ve düşünen modeller için 1500 token pay bırakır, ama her model uymaz.
- **Güvenlik.** Program dışarıya bağlantı açar, içeriye bir şey açmaz; belirteç yalnız bu bilgisayarı tanımlar ve yönetici
  tarafından her an iptal edilebilir. Gelen metinler platformda kimliklerinden arındırılmıştır.

## Lisans

MIT — bkz. `LICENSE`.

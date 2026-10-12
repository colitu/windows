# Colitu Windows çekirdek dosyaları

`scripts/build-installer.ps1` bu klasördeki dosyaları kurulum paketine koyar.

| Dosya | Kaynak | Git'te |
| --- | --- | --- |
| `xray.exe` | [XTLS/Xray-core](https://github.com/XTLS/Xray-core/releases) `Xray-windows-64.zip` — sürüm ve SHA-256 `scripts/cores.json` içinde (şu an **Xray 26.10.10**) | Hayır (`*.exe` yok sayılır) |
| `wintun.dll` | [wintun.net](https://www.wintun.net/) amd64 | Evet |
| `geoip.dat`, `geosite.dat` | Xray yönlendirme verileri | Evet |

`xray.exe` dosyasını `pwsh scripts/fetch-cores.ps1` ile indirin: betik yalnız resmi GitHub
sürümünü alır, arşivin SHA-256 değerini sabitlenen değerle ve sürümün `.dgst` dosyasıyla,
çıkan dosyanınkini de sabitlenen değerle karşılaştırır. `build-installer.ps1`, hash tutmazsa
paket üretmez.

Xray-core MPL-2.0 (`LICENSE`), Wintun kendi lisansı (`LICENSE-wintun.txt`) ile dağıtılır.

# SwearGuard

Küfür koruması. Küfürlü mesaj sohbette görünmez; 1. ve 2. küfürde uyarı, 3. küfürde susturma. Türkçe ve İngilizce geniş liste, harf oyunlarını (o r o s p u, tekrar eden harfler, tek harf hatası) yakalar, masum benzer kelimeleri (sık, şikayet...) ayırır. Admin ve moderatörler muaf.

> Profanity filter (Turkish + English) with warnings and an automatic mute on the third strike.

[YCC TEAM](https://yccteam.com.tr) CS2 sunucusu için sıfırdan yazıldı ve sunucuda her gün çalışıyor. Başka eklentilerden kod alınmadı.

## Gereken

- CS2 sunucusu, [Metamod:Source](https://www.sourcemm.net/) ve [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) (1.0.371 ve üstü)

## Kurulum

1. Sunucuda [Metamod:Source](https://www.sourcemm.net/downloads.php?branch=dev) ve [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases) (with-runtime sürümü) kurulu olmalı.
2. [.NET 10 SDK](https://dotnet.microsoft.com/download) ile derle: `dotnet build -c Release`
3. `bin/Release/net10.0/` içindeki her şeyi sunucuda `addons/counterstrikesharp/plugins/SwearGuard/` klasörüne kopyala.
4. Sunucuyu başlat. Ayar dosyası ilk açılışta `addons/counterstrikesharp/configs/plugins/SwearGuard/SwearGuard.json` olarak kendiliğinden oluşur; düzenleyip sunucuyu yeniden başlat.

## Komutlar

Sohbette `!` ile, konsolda `css_` ile yazılır.

| Komut | Ne yapar |
|---|---|
| `css_kufurcezasifirla` | Uyarı sayaçlarını sıfırlar (sadece sunucu konsolu) |

## Notlar

Ceza komutu ayardan değişir; varsayılanı YccAdmin'in `css_silence` komutudur. Kelime listelerine ayar dosyasından ekleme yapılabilir.

## Lisans

MIT. Kullanabilir, değiştirebilir, paylaşabilirsin; lisans dosyasını koruman yeterli.

Sunucu: **yccteam.com.tr** · Site: https://yccteam.com.tr

namespace v2rayN.Services;

/// <summary>Strings of the 3.x interface refresh (connection panel, leak test, pings, usage history, devices, support), merged into <see cref="Strings"/>.</summary>
public sealed partial class Loc
{
    // key → [ru, tr, en, zh, fa]
    private static readonly Dictionary<string, string[]> RefreshStrings = new()
    {
        ["conn.title"] = ["Подключение", "Bağlantı bilgisi", "Connection", "连接信息", "اطلاعات اتصال"],
        ["conn.country"] = ["Страна", "Ülke", "Country", "国家/地区", "کشور"],
        ["conn.protocol"] = ["Протокол", "Protokol", "Protocol", "协议", "پروتکل"],
        ["conn.ping"] = ["Пинг", "Ping", "Ping", "延迟", "پینگ"],
        ["conn.graph"] = ["Скорость за последнюю минуту", "Son bir dakikanın hızı", "Speed over the last minute", "最近一分钟的速度", "سرعت در یک دقیقهٔ اخیر"],
        ["conn.down"] = ["↓ {v}", "↓ {v}", "↓ {v}", "↓ {v}", "↓ {v}"],
        ["conn.up"] = ["↑ {v}", "↑ {v}", "↑ {v}", "↑ {v}", "↑ {v}"],
        ["conn.speedTunnel"] = ["Трафик через туннель.", "Tünelden geçen trafik.", "Traffic through the tunnel.", "经过隧道的流量。", "ترافیک عبوری از تونل."],
        ["conn.speedPc"] = ["Режим прокси: трафик всего компьютера.", "Proxy modu: bilgisayarın tüm trafiği.", "Proxy mode: the whole PC's traffic.", "代理模式：整台电脑的流量。", "حالت پراکسی: ترافیک کل رایانه."],
        ["conn.checking"] = ["Проверяем…", "Kontrol ediliyor…", "Checking…", "检查中…", "در حال بررسی…"],
        ["conn.unknown"] = ["Не удалось узнать", "Öğrenilemedi", "Could not check", "无法获取", "بررسی نشد"],
        ["conn.leakHint"] = ["Проверка IP и DNS через туннель.", "Tünel içinden IP ve DNS kontrolü.", "Checks IP and DNS through the tunnel.", "通过隧道检查 IP 和 DNS。", "بررسی IP و DNS از داخل تونل."],
        ["conn.leakRun"] = ["Тест утечек", "Sızıntı testi", "Leak test", "泄漏测试", "تست نشت"],
        ["conn.leak.ipOk"] = ["IP: виден адрес сервера ({country}).", "IP: sunucunun adresi görünüyor ({country}).", "IP: sites see the server's address ({country}).", "IP：网站看到的是服务器地址（{country}）。", "IP: سایت‌ها نشانی سرور را می‌بینند ({country})."],
        ["conn.leak.ipWarn"] = ["IP: выход в {country}, а не в стране сервера. Переподключитесь.", "IP: çıkış {country}, sunucunun ülkesi değil. Yeniden bağlanın.", "IP: the exit is in {country}, not the server's country. Reconnect.", "IP：出口在{country}，不是服务器所在国家。请重新连接。", "IP: خروجی در {country} است، نه کشور سرور. دوباره وصل شوید."],
        ["conn.leak.dnsOk"] = ["DNS: запросы уходят через сервер ({ip}, {country}).", "DNS: sorgular sunucu üzerinden gidiyor ({ip}, {country}).", "DNS: lookups go through the server ({ip}, {country}).", "DNS：查询经由服务器发出（{ip}，{country}）。", "DNS: درخواست‌ها از طریق سرور می‌روند ({ip}، {country})."],
        ["conn.leak.dnsWarn"] = ["DNS: резолвер в другой стране ({ip}, {country}), запросы могут идти мимо туннеля. Включите режим TUN.", "DNS: çözücü başka bir ülkede ({ip}, {country}); sorgular tünelin dışından gidiyor olabilir. TUN modunu açın.", "DNS: the resolver is in another country ({ip}, {country}); lookups may leave outside the tunnel. Turn on TUN mode.", "DNS：解析器在另一个国家（{ip}，{country}），查询可能绕过隧道。请开启 TUN 模式。", "DNS: سرور DNS در کشور دیگری است ({ip}، {country})؛ ممکن است درخواست‌ها خارج از تونل بروند. حالت TUN را روشن کنید."],
        ["conn.leak.dnsUnknown"] = ["DNS: резолвер {ip}, страну узнать не удалось.", "DNS: çözücü {ip}, ülkesi öğrenilemedi.", "DNS: resolver {ip}, its country could not be checked.", "DNS：解析器 {ip}，无法确定其国家。", "DNS: سرور {ip}، کشور آن مشخص نشد."],
        ["conn.leak.failed"] = ["Проверка не удалась. Попробуйте ещё раз.", "Test tamamlanamadı. Tekrar deneyin.", "The test did not finish. Try again.", "测试未完成，请重试。", "تست کامل نشد. دوباره تلاش کنید."],
        ["locations.pingAll"] = ["Пинг всех", "Hepsine ping", "Ping all", "全部测速", "پینگ همه"],
        ["locations.pingAllHint"] = ["Измерить пинг всех локаций заново", "Tüm konumların ping'ini yeniden ölç", "Measure every location's ping again", "重新测量所有位置的延迟", "اندازه‌گیری دوبارهٔ پینگ همهٔ مکان‌ها"],
        ["locations.pingRow"] = ["Измерить пинг: {name}", "Ping ölç: {name}", "Measure ping: {name}", "测速：{name}", "اندازه‌گیری پینگ: {name}"],
        ["usage.title"] = ["Трафик за 30 дней", "Son 30 günün trafiği", "Traffic, last 30 days", "最近 30 天流量", "ترافیک ۳۰ روز اخیر"],
        ["usage.today"] = ["Сегодня", "Bugün", "Today", "今天", "امروز"],
        ["usage.peak"] = ["Максимум: {day} · {total}", "En yoğun gün: {day} · {total}", "Busiest day: {day} · {total}", "最高：{day} · {total}", "بیشترین: {day} · {total}"],
        ["usage.day"] = ["{day}: {total} (↓ {down} · ↑ {up})", "{day}: {total} (↓ {down} · ↑ {up})", "{day}: {total} (↓ {down} · ↑ {up})", "{day}：{total}（↓ {down} · ↑ {up}）", "{day}: {total} (↓ {down} · ↑ {up})"],
        ["account.cleanup"] = ["Удалить старые устройства", "Eski cihazları temizle", "Remove old devices", "清理旧设备", "حذف دستگاه‌های قدیمی"],
        ["account.cleanupCount"] = ["Удалить старые устройства ({n})", "Eski cihazları temizle ({n})", "Remove old devices ({n})", "清理旧设备（{n}）", "حذف دستگاه‌های قدیمی ({n})"],
        ["account.cleanupHint"] = ["Устройства, которые не подключались больше 30 дней. Этот компьютер не затрагивается.", "30 günden uzun süredir görülmeyen cihazlar. Bu bilgisayar etkilenmez.", "Devices not seen for more than 30 days. This PC is never removed.", "超过 30 天未出现的设备。不会移除这台电脑。", "دستگاه‌هایی که بیش از ۳۰ روز دیده نشده‌اند. این رایانه حذف نمی‌شود."],
        ["account.cleanupConfirm"] = ["Удалить устройства, не подключавшиеся больше 30 дней ({n})?\n\n{names}\n\nНа них потребуется войти снова. Этот компьютер останется.", "30 günden uzun süredir görülmeyen {n} cihaz kaldırılsın mı?\n\n{names}\n\nBu cihazlarda yeniden giriş yapmak gerekecek. Bu bilgisayar kalır.", "Remove {n} devices not seen for more than 30 days?\n\n{names}\n\nThey will need to sign in again. This PC stays.", "移除 {n} 台超过 30 天未出现的设备？\n\n{names}\n\n这些设备需要重新登录。这台电脑会保留。", "{n} دستگاهی که بیش از ۳۰ روز دیده نشده‌اند حذف شوند؟\n\n{names}\n\nباید دوباره وارد شوند. این رایانه می‌ماند."],
        ["account.cleanupNone"] = ["Нет устройств старше 30 дней.", "30 günden eski cihaz yok.", "No devices older than 30 days.", "没有超过 30 天的设备。", "دستگاهی قدیمی‌تر از ۳۰ روز نیست."],
        ["account.cleanupDone"] = ["Удалено устройств: {n}", "{n} cihaz kaldırıldı", "{n} devices removed", "已移除 {n} 台设备", "{n} دستگاه حذف شد"],
        ["account.rename"] = ["Переименовать", "Yeniden adlandır", "Rename", "重命名", "تغییر نام"],
        ["account.renameLabel"] = ["Название устройства", "Cihaz adı", "Device name", "设备名称", "نام دستگاه"],
        ["account.renameSave"] = ["Сохранить", "Kaydet", "Save", "保存", "ذخیره"],
        ["account.renamed"] = ["Название изменено", "Cihaz adı değişti", "Device renamed", "已重命名", "نام دستگاه تغییر کرد"],
        ["support.typing"] = ["Поддержка печатает…", "Destek yazıyor…", "Support is typing…", "客服正在输入…", "پشتیبانی در حال نوشتن است…"],
        ["support.dropHint"] = ["Перетащите сюда скриншот или файл · до 10 МБ", "Görsel veya dosyayı buraya sürükleyin · en fazla 10 MB", "Drop a screenshot or file here · up to 10 MB", "将截图或文件拖到这里 · 最大 10 MB", "عکس یا فایل را اینجا رها کنید · تا ۱۰ مگابایت"],
        ["support.read"] = ["Прочитано", "Okundu", "Read", "已读", "خوانده شد"],
        ["support.sent"] = ["Отправлено", "Gönderildi", "Sent", "已发送", "ارسال شد"],
        ["support.replyFrom"] = ["Поддержка: {text}", "Destek: {text}", "Support: {text}", "客服：{text}", "پشتیبانی: {text}"],
        ["tray.favorites"] = ["Избранное", "Favoriler", "Favourites", "收藏", "علاقه‌مندی‌ها"],
        ["tray.recent"] = ["Недавние", "Son kullanılanlar", "Recent", "最近使用", "اخیر"],
    };
}

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace v2rayN.Services;

/// <summary>
/// App strings in Russian, Turkish, English, Simplified Chinese and Persian, matching the website's wording
/// (web/portal/lib/i18n). XAML binds through <see cref="T"/>; switching the
/// language refreshes every bound text without reopening windows.
/// </summary>
public sealed partial class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    public static readonly string[] Languages = ["ru", "tr", "en", "zh", "fa"];

    private Loc()
    {
        Language = DefaultLanguage();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public string Language { get; private set; }

    public CultureInfo Culture => Language switch
    {
        "ru" => CultureInfo.GetCultureInfo("ru-RU"),
        "tr" => CultureInfo.GetCultureInfo("tr-TR"),
        "zh" => CultureInfo.GetCultureInfo("zh-CN"),
        "fa" => PersianCulture.Value,
        _ => CultureInfo.GetCultureInfo("en-US")
    };

    /// <summary>Persian is right-to-left; the app flips its windows to RightToLeft for it.</summary>
    public bool IsRightToLeft => Language == "fa";

    public FlowDirection FlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>fa-IR with Gregorian dates (like the website); Persian calendar when the OS does not offer it.</summary>
    private static readonly Lazy<CultureInfo> PersianCulture = new(() =>
    {
        var culture = new CultureInfo("fa-IR", false);
        try
        {
            culture.DateTimeFormat.Calendar = new GregorianCalendar();
        }
        catch (ArgumentException)
        {
        }
        return culture;
    });

    public string this[string key] => Get(key);

    public void SetLanguage(string? language)
    {
        var next = Normalize(language);
        if (next == Language) return;
        Language = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        Changed?.Invoke();
    }

    public static string Normalize(string? language)
    {
        var value = (language ?? "").Trim().ToLowerInvariant();
        return Languages.Contains(value) ? value : DefaultLanguage();
    }

    /// <summary>Russian unless Windows itself runs in Turkish, English, Chinese or Persian, like the website.</summary>
    public static string DefaultLanguage()
    {
        var system = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return system switch
        {
            "tr" => "tr",
            "en" => "en",
            "zh" => "zh",
            "fa" => "fa",
            _ => "ru"
        };
    }

    public string Get(string key)
    {
        if (Strings.TryGetValue(key, out var values))
        {
            var index = Array.IndexOf(Languages, Language);
            return values[index < 0 ? 0 : index];
        }
        return key;
    }

    public string Format(string key, params (string Name, object Value)[] args)
    {
        var text = Get(key);
        foreach (var (name, value) in args)
        {
            text = text.Replace("{" + name + "}", Convert.ToString(value, Culture));
        }
        return text;
    }

    /// <summary>Counted noun with the right plural form ("3 устройства", "5 дней").</summary>
    public string Count(string noun, long n)
    {
        var form = Language switch
        {
            "ru" => RussianForm(n),
            "en" => n == 1 ? "one" : "many",
            _ => "one"
        };
        return Format($"{noun}.{form}", ("n", n.ToString("N0", Culture)));
    }

    private static string RussianForm(long n)
    {
        var mod10 = Math.Abs(n) % 10;
        var mod100 = Math.Abs(n) % 100;
        if (mod10 == 1 && mod100 != 11) return "one";
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return "few";
        return "many";
    }

    public static bool Has(string key) => Strings.ContainsKey(key);

    public static IReadOnlyCollection<string> Keys => Strings.Keys;

    public static string[] ValuesOf(string key) => Strings[key];

    // key → [ru, tr, en, zh, fa]
    private static readonly Dictionary<string, string[]> Strings = new()
    {
        // Shell
        ["nav.home"] = ["Главная", "Ana sayfa", "Home", "首页", "خانه"],
        ["nav.locations"] = ["Локации", "Konumlar", "Locations", "位置", "مکان‌ها"],
        ["nav.plan"] = ["Тариф", "Paket", "Plan", "套餐", "طرح"],
        ["nav.account"] = ["Аккаунт", "Hesap", "Account", "账户", "حساب"],
        ["nav.settings"] = ["Настройки", "Ayarlar", "Settings", "设置", "تنظیمات"],
        ["nav.liveSupport"] = ["Поддержка", "Canlı destek", "Live support", "真人客服", "پشتیبانی زنده"],
        ["window.minimize"] = ["Свернуть", "Küçült", "Minimize", "最小化", "کوچک‌کردن"],
        ["window.close"] = ["Свернуть в трей", "Tepsiye küçült", "Hide to tray", "隐藏到托盘", "پنهان‌کردن در سینی"],
        ["loading.session"] = ["Восстанавливаем сеанс…", "Oturumunuz açılıyor…", "Restoring your session…", "正在恢复你的会话…", "در حال بازیابی نشست شما…"],
        ["update.available"] = ["Обновление {version}", "Güncelleme {version}", "Update {version}", "更新 {version}", "به‌روزرسانی {version}"],
        ["update.downloading"] = ["Загрузка {percent}%", "İndiriliyor %{percent}", "Downloading {percent}%", "正在下载 {percent}%", "در حال دانلود {percent}%"],
        ["update.title"] = ["Доступна версия Colitu {version}", "Colitu {version} hazır", "Colitu {version} is available", "Colitu {version} 现已推出", "Colitu {version} در دسترس است"],
        ["update.ask"] = ["Обновить сейчас? В старой версии подключение и вход в аккаунт могут работать с ошибками.", "Şimdi güncellemek ister misiniz? Eski sürümde bağlantı ve hesap işlemlerinde sorunlar yaşanabilir.", "Update now? The old version may have problems connecting or signing in.", "现在更新吗？旧版本在连接或登录时可能出现问题。", "اکنون به‌روزرسانی شود؟ نسخهٔ قدیمی ممکن است در اتصال یا ورود مشکل داشته باشد."],
        ["update.force"] = ["Эта версия больше не поддерживается. Обновите приложение, чтобы продолжить.", "Bu sürüm artık desteklenmiyor. Devam etmek için uygulamayı güncelleyin.", "This version is no longer supported. Update the app to continue.", "此版本已不再受支持。请更新应用以继续使用。", "این نسخه دیگر پشتیبانی نمی‌شود. برای ادامه، برنامه را به‌روزرسانی کنید."],
        ["update.now"] = ["Обновить сейчас", "Şimdi güncelle", "Update now", "立即更新", "همین حالا به‌روزرسانی کن"],
        ["update.later"] = ["Позже", "Daha sonra", "Later", "稍后", "بعداً"],
        ["update.restart"] = ["Colitu закроется, установит обновление и откроется снова.", "Colitu kapanıp güncellemeyi kuracak ve yeniden açılacak.", "Colitu will close, install the update and open again.", "Colitu 将关闭、安装更新并重新打开。", "Colitu بسته می‌شود، به‌روزرسانی را نصب می‌کند و دوباره باز می‌شود."],
        ["update.installing"] = ["Устанавливаем обновление…", "Güncelleme kuruluyor…", "Installing update…", "正在安装更新…", "در حال نصب به‌روزرسانی…"],
        ["update.failed"] = ["Не удалось установить обновление. Попробуйте позже.", "Güncelleme kurulamadı. Daha sonra tekrar deneyin.", "The update could not be installed. Please try again later.", "无法安装更新，请稍后重试。", "نصب به‌روزرسانی انجام نشد. لطفاً بعداً دوباره تلاش کنید."],

        // Connection status
        ["status.protected"] = ["Защищено", "Korunuyor", "Protected", "已受保护", "محافظت‌شده"],
        ["status.unprotected"] = ["Не защищено", "Korunmuyor", "Not protected", "未受保护", "محافظت‌نشده"],
        ["status.connecting"] = ["Подключение…", "Bağlanıyor…", "Connecting…", "正在连接…", "در حال اتصال…"],
        ["status.reconnecting"] = ["Переподключение…", "Yeniden bağlanıyor…", "Reconnecting…", "正在重新连接…", "در حال اتصال مجدد…"],
        ["status.disconnecting"] = ["Отключение…", "Bağlantı kesiliyor…", "Disconnecting…", "正在断开…", "در حال قطع اتصال…"],
        ["status.blocked"] = ["Интернет заблокирован", "İnternet engelli", "Internet blocked", "网络已阻断", "اینترنت مسدود است"],

        // Home
        ["home.kicker"] = ["COLITU VPN · WINDOWS", "COLITU VPN · WINDOWS", "COLITU VPN · WINDOWS", "COLITU VPN · WINDOWS", "COLITU VPN · WINDOWS"],
        ["home.title.off"] = ["Соединение не защищено", "Bağlantınız korunmuyor", "Your connection isn’t protected", "你的连接未受保护", "اتصال شما محافظت نشده است"],
        ["home.title.on"] = ["Вы под защитой", "Korunuyorsunuz", "You’re protected", "你已受到保护", "شما محافظت می‌شوید"],
        ["home.title.connecting"] = ["Защищаем соединение", "Bağlantınız güvenceye alınıyor", "Securing your connection", "正在保护你的连接", "در حال ایمن‌سازی اتصال شما"],
        ["home.title.noplan"] = ["Выберите тариф, чтобы начать", "Başlamak için bir paket seçin", "Choose a plan to get started", "选择套餐即可开始", "برای شروع، یک طرح انتخاب کنید"],
        ["home.sub.off"] = ["Нажмите кнопку — трафик будет зашифрован, а IP-адрес скрыт.", "Düğmeye basın; trafiğiniz şifrelenir, IP adresiniz gizlenir.", "Press the button to encrypt your traffic and hide your IP address.", "点击按钮即可加密流量并隐藏你的 IP 地址。", "دکمه را بزنید تا ترافیک شما رمزگذاری و نشانی IP شما پنهان شود."],
        ["home.sub.on"] = ["Трафик зашифрован и идёт через {server}.", "Trafiğiniz şifreli ve {server} üzerinden geçiyor.", "Your traffic is encrypted and routed through {server}.", "你的流量已加密，并经由 {server} 传输。", "ترافیک شما رمزگذاری شده و از طریق {server} عبور می‌کند."],
        ["home.sub.connecting"] = ["Готовим зашифрованный туннель…", "Şifreli tünel hazırlanıyor…", "Setting up the encrypted tunnel…", "正在建立加密隧道…", "در حال برپایی تونل رمزگذاری‌شده…"],
        ["home.sub.noplan"] = ["Все серверы, безлимитный трафик и защита от утечек — в одном тарифе.", "Tüm sunucular, sınırsız trafik ve sızıntı koruması tek pakette.", "Every server, unlimited traffic and leak protection in one plan.", "一个套餐即可使用所有服务器、不限流量并获得防泄漏保护。", "همهٔ سرورها، ترافیک نامحدود و محافظت در برابر نشت، همه در یک طرح."],
        ["home.title.blocked"] = ["Интернет заблокирован", "İnternet engellendi", "Internet blocked", "网络已阻断", "اینترنت مسدود است"],
        ["home.sub.blocked"] = ["VPN прервался, и kill switch закрыл интернет, чтобы ничего не утекло. Переподключаемся автоматически — или отключите защиту.", "VPN koptu; kill switch hiçbir şey sızmasın diye interneti kapattı. Otomatik olarak yeniden bağlanıyoruz; isterseniz korumayı kapatın.", "The VPN dropped, so the kill switch closed the internet to keep anything from leaking. We’re reconnecting automatically, or you can turn protection off.", "VPN 已断开，断网保护已切断网络以防止信息泄漏。我们正在自动重新连接，你也可以关闭保护。", "VPN قطع شد و Kill Switch برای جلوگیری از نشت اطلاعات، اینترنت را بست. به‌طور خودکار دوباره وصل می‌شویم، یا می‌توانید محافظت را خاموش کنید."],
        ["home.unblock"] = ["Отключить защиту и открыть интернет", "Korumayı kapat ve interneti aç", "Turn protection off and restore internet", "关闭保护并恢复网络", "خاموش‌کردن محافظت و بازگرداندن اینترنت"],
        ["home.connect"] = ["Подключить", "Bağlan", "Connect", "连接", "اتصال"],
        ["home.disconnect"] = ["Отключить", "Bağlantıyı kes", "Disconnect", "断开", "قطع اتصال"],
        ["home.cancel"] = ["Отменить", "İptal", "Cancel", "取消", "لغو"],
        ["home.tap"] = ["Нажмите, чтобы подключиться", "Bağlanmak için tıklayın", "Click to connect", "点击连接", "برای اتصال کلیک کنید"],
        ["home.tapOff"] = ["Нажмите, чтобы отключить", "Kesmek için tıklayın", "Click to disconnect", "点击断开", "برای قطع اتصال کلیک کنید"],
        ["home.tapCancel"] = ["Нажмите, чтобы отменить", "İptal etmek için tıklayın", "Click to cancel", "点击取消", "برای لغو کلیک کنید"],
        ["home.protocol"] = ["Протокол", "Protokol", "Protocol", "协议", "پروتکل"],
        ["home.protocol.hint"] = ["Нажмите, чтобы выбрать способ подключения", "Bağlantı yöntemini seçmek için tıklayın", "Click to choose the connection method", "点击选择连接方式", "برای انتخاب روش اتصال کلیک کنید"],
        ["transport.auto"] = ["Автоматически", "Otomatik", "Automatic", "自动", "خودکار"],
        // Colitu names of the transports; the technical names stay out of the UI.
        ["transport.hysteria2"] = ["Быстрый", "Hızlı", "Fast", "极速", "سریع"],
        ["transport.vless-reality"] = ["Скрытный", "Gizli", "Stealth", "隐匿", "پنهان"],
        ["transport.vless-xhttp"] = ["Устойчивый", "Dayanıklı", "Resilient", "稳健", "پایدار"],
        ["transport.trojan"] = ["Классический", "Klasik", "Classic", "经典", "کلاسیک"],
        ["transport.shadowsocks"] = ["Лёгкий", "Hafif", "Light", "轻量", "سبک"],
        ["transport.other"] = ["Резервный", "Yedek", "Backup", "备用", "پشتیبان"],
        ["home.changeServer"] = ["Сменить сервер", "Sunucuyu değiştir", "Change server", "更换服务器", "تغییر سرور"],
        ["home.session"] = ["Время подключения", "Bağlantı süresi", "Connection time", "连接时长", "مدت اتصال"],
        ["home.location"] = ["ЛОКАЦИЯ", "KONUM", "LOCATION", "位置", "مکان"],
        ["home.change"] = ["Изменить", "Değiştir", "Change", "更改", "تغییر"],
        ["home.plan"] = ["ТАРИФ", "PAKET", "PLAN", "套餐", "طرح"],
        ["home.quick"] = ["БЫСТРЫЕ НАСТРОЙКИ", "HIZLI AYARLAR", "QUICK SETTINGS", "快捷设置", "تنظیمات سریع"],
        ["home.traffic"] = ["Трафик за период", "Bu dönemki trafik", "Traffic this period", "本周期流量", "ترافیک این دوره"],
        ["home.trafficOf"] = ["{used} из {limit}", "{used} / {limit}", "{used} of {limit}", "{used} / {limit}", "{used} از {limit}"],
        ["home.unlimited"] = ["Безлимитный трафик", "Sınırsız trafik", "Unlimited traffic", "不限流量", "ترافیک نامحدود"],
        ["home.devices"] = ["Устройства", "Cihazlar", "Devices", "设备", "دستگاه‌ها"],
        ["home.offline"] = ["Нет связи с сервером Colitu. Повторяем попытку…", "Colitu sunucusuna ulaşılamıyor. Yeniden deneniyor…", "Can’t reach Colitu right now. Retrying…", "暂时无法连接 Colitu，正在重试…", "اکنون به Colitu دسترسی نداریم. در حال تلاش مجدد…"],

        ["server.auto"] = ["Лучший сервер", "En iyi sunucu", "Best server", "最佳服务器", "بهترین سرور"],
        ["server.autoHint"] = ["Colitu сам выберет самый свободный и стабильный", "Colitu en boş ve kararlı sunucuyu kendisi seçer", "Colitu picks the least busy, most stable one", "Colitu 会选择最空闲、最稳定的服务器", "Colitu کم‌بارترین و پایدارترین سرور را انتخاب می‌کند"],
        ["server.none"] = ["Серверы появятся здесь", "Sunucular burada görünecek", "Servers will appear here", "服务器将显示在这里", "سرورها اینجا نمایش داده می‌شوند"],
        ["server.load.low"] = ["Низкая нагрузка", "Düşük yük", "Low load", "负载低", "بار کم"],
        ["server.load.medium"] = ["Средняя нагрузка", "Orta yük", "Medium load", "负载中等", "بار متوسط"],
        ["server.load.high"] = ["Высокая нагрузка", "Yüksek yük", "High load", "负载高", "بار زیاد"],
        ["server.offline"] = ["Недоступен", "Çevrimdışı", "Offline", "离线", "آفلاین"],
        ["server.connected"] = ["Подключено", "Bağlı", "Connected", "已连接", "متصل"],
        ["server.selected"] = ["Выбрано", "Seçili", "Selected", "已选择", "انتخاب‌شده"],

        // Locations
        ["locations.kicker"] = ["ЛОКАЦИИ", "KONUMLAR", "LOCATIONS", "位置", "مکان‌ها"],
        ["locations.title"] = ["Выберите локацию", "Bir konum seçin", "Choose a location", "选择位置", "یک مکان انتخاب کنید"],
        ["locations.sub"] = ["Серверов онлайн: {n}. При смене локации подключение переключится автоматически.", "Çevrimiçi sunucu: {n}. Konumu değiştirdiğinizde bağlantı otomatik olarak geçer.", "{n} servers online. Switching location moves your connection automatically.", "{n} 台服务器在线。切换位置时，连接会自动迁移。", "{n} سرور آنلاین است. با تغییر مکان، اتصال شما خودکار منتقل می‌شود."],
        ["locations.search"] = ["Поиск страны или города", "Ülke veya şehir ara", "Search country or city", "搜索国家或城市", "جستجوی کشور یا شهر"],
        ["locations.empty"] = ["Ничего не найдено", "Sonuç bulunamadı", "Nothing found", "未找到结果", "چیزی پیدا نشد"],
        ["locations.sortTitle"] = ["Сортировка", "Sıralama", "Sort by", "排序方式", "مرتب‌سازی بر اساس"],
        ["locations.sort.name"] = ["По названию", "Ada göre", "By name", "按名称", "نام"],
        ["locations.sort.ping"] = ["По пингу", "Ping’e göre", "By ping", "按延迟", "پینگ"],
        ["locations.sort.load"] = ["По нагрузке", "Yüke göre", "By load", "按负载", "بار"],
        ["locations.switching"] = ["Переключаемся на {server}…", "{server} konumuna geçiliyor…", "Switching to {server}…", "正在切换到 {server}…", "در حال تغییر به {server}…"],
        ["locations.switched"] = ["Подключено: {server}", "Bağlandı: {server}", "Connected to {server}", "已连接到 {server}", "به {server} وصل شدید"],

        // Plan status
        ["plan.none"] = ["Нет активного тарифа", "Aktif paket yok", "No active plan", "没有有效套餐", "طرح فعالی وجود ندارد"],
        ["plan.noneHint"] = ["Выберите тариф, чтобы подключиться.", "Bağlanmak için bir paket seçin.", "Choose a plan to connect.", "请选择套餐后再连接。", "برای اتصال، یک طرح انتخاب کنید."],
        ["plan.status.active"] = ["АКТИВЕН", "AKTİF", "ACTIVE", "有效", "فعال"],
        ["plan.status.trialing"] = ["ПРОБНЫЙ", "DENEME", "TRIAL", "试用", "آزمایشی"],
        ["plan.status.expired"] = ["ИСТЁК", "SÜRESİ DOLDU", "EXPIRED", "已到期", "منقضی‌شده"],
        ["plan.status.inactive"] = ["НЕ АКТИВЕН", "PASİF", "INACTIVE", "未启用", "غیرفعال"],
        ["plan.until"] = ["До {date}", "{date} tarihine kadar", "Until {date}", "有效期至 {date}", "تا {date}"],
        ["plan.left"] = ["осталось {left}", "{left} kaldı", "{left} left", "剩余 {left}", "{left} باقی مانده"],
        ["plan.choose"] = ["Выбрать тариф", "Paket seç", "Choose a plan", "选择套餐", "انتخاب طرح"],
        ["plan.extend"] = ["Продлить", "Süreyi uzat", "Extend", "续订", "تمدید"],
        ["plan.freeName"] = ["Бесплатный тариф", "Ücretsiz plan", "Free plan", "免费套餐", "طرح رایگان"],
        ["plan.freeHint"] = ["1 ГБ каждый месяц", "Her ay 1 GB", "1 GB every month", "每月 1 GB", "هر ماه ۱ گیگابایت"],
        ["plan.upgrade"] = ["Перейти на безлимит", "Sınırsız trafiğe geç", "Go unlimited", "升级为不限流量", "ارتقا به نامحدود"],
        ["upsell.cta"] = ["Получить Premium", "Premium’a geç", "Get Premium", "获取 Premium", "دریافت Premium"],
        ["upsell.free"] = ["Бесплатный тариф · осталось {left} из {limit} · {devices}", "Ücretsiz plan · {limit} içinden {left} kaldı · {devices}", "Free plan · {left} of {limit} left · {devices}", "免费套餐 · {limit} 中剩余 {left} · {devices}", "طرح رایگان · {left} از {limit} باقی مانده · {devices}"],
        ["upsell.low.title"] = ["Использовано {percent}% бесплатного трафика за этот месяц", "Bu ayki ücretsiz trafiğin %{percent} kadarı kullanıldı", "{percent}% of this month’s free traffic used", "本月免费流量已用 {percent}%", "{percent}٪ از ترافیک رایگان این ماه مصرف شده است"],
        ["upsell.leftUntil"] = ["Осталось {left} до {date}.", "{date} tarihine kadar {left} kaldı.", "{left} left until {date}.", "{date} 前还剩 {left}。", "تا {date} {left} باقی مانده است."],
        ["upsell.left"] = ["Осталось {left} в этом месяце.", "Bu ay {left} kaldı.", "{left} left this month.", "本月还剩 {left}。", "این ماه {left} باقی مانده است."],
        ["upsell.unlimited"] = ["В Premium трафик не ограничен.", "Premium’da trafik sınırı yok.", "Premium has no traffic limit.", "Premium 不限流量。", "Premium محدودیت ترافیک ندارد."],
        ["upsell.out.title"] = ["Бесплатный трафик на этот месяц закончился", "Bu ayın ücretsiz trafiği bitti", "This month’s free traffic is used up", "本月免费流量已用完", "ترافیک رایگان این ماه تمام شده است"],
        ["upsell.out.renews"] = ["Бесплатный трафик снова появится {date}.", "Ücretsiz trafik {date} tarihinde yenilenir.", "Free traffic comes back on {date}.", "免费流量将于 {date} 恢复。", "ترافیک رایگان در {date} دوباره در دسترس است."],
        ["upsell.out.renewsSoon"] = ["Бесплатный трафик снова появится в начале следующего месяца.", "Ücretsiz trafik gelecek ayın başında yenilenir.", "Free traffic comes back at the start of next month.", "免费流量将于下月初恢复。", "ترافیک رایگان در آغاز ماه بعد دوباره در دسترس است."],
        ["upsell.out.premium"] = ["С Premium можно подключиться сразу, без ограничения трафика.", "Premium ile hemen, trafik sınırı olmadan bağlanabilirsiniz.", "With Premium you can connect right away, with no traffic limit.", "开通 Premium 即可立即连接，不限流量。", "با Premium همین حالا و بدون محدودیت ترافیک وصل شوید."],
        ["upsell.ended.title"] = ["Дни Premium закончились", "Premium süreniz bitti", "Your Premium days are over", "你的 Premium 已到期", "دورهٔ Premium شما تمام شد"],
        ["upsell.ended.body"] = ["Теперь у вас бесплатный тариф: {limit} в месяц, {devices}. С Premium трафик снова без ограничений.", "Artık ücretsiz plandasınız: ayda {limit}, {devices}. Premium ile trafik yeniden sınırsız.", "You’re on the free plan now: {limit} a month, {devices}. Premium brings back unlimited traffic.", "你现在使用免费套餐：每月 {limit}，{devices}。开通 Premium 即可恢复不限流量。", "اکنون در طرح رایگان هستید: ماهانه {limit}، {devices}. با Premium ترافیک دوباره نامحدود می‌شود."],
        ["upsell.later"] = ["Не сейчас", "Şimdi değil", "Not now", "暂不", "الان نه"],
        ["premium.ends"] = ["Premium закончится через {days}.", "Premium sürenizin bitmesine {days} kaldı.", "Your Premium ends in {days}.", "你的 Premium 将在 {days} 后结束。", "Premium شما {days} دیگر تمام می‌شود."],
        ["paused.premiumHint"] = ["Платные тарифы позволяют больше устройств.", "Ücretli paketler daha fazla cihaza izin verir.", "Paid plans allow more devices.", "付费套餐可使用更多设备。", "طرح‌های پولی دستگاه‌های بیشتری را مجاز می‌دانند."],
        ["plan.status.quota"] = ["ЛИМИТ ИСЧЕРПАН", "KOTA DOLDU", "LIMIT REACHED", "已达上限", "سقف پر شد"],
        ["plan.trialName"] = ["Пробный период", "Deneme süresi", "Free trial", "免费试用", "دورهٔ آزمایشی رایگان"],
        ["day.one"] = ["{n} день", "{n} gün", "{n} day", "{n} 天", "{n} روز"],
        ["day.few"] = ["{n} дня", "{n} gün", "{n} days", "{n} 天", "{n} روز"],
        ["day.many"] = ["{n} дней", "{n} gün", "{n} days", "{n} 天", "{n} روز"],
        ["hour.one"] = ["{n} час", "{n} saat", "{n} hour", "{n} 小时", "{n} ساعت"],
        ["hour.few"] = ["{n} часа", "{n} saat", "{n} hours", "{n} 小时", "{n} ساعت"],
        ["hour.many"] = ["{n} часов", "{n} saat", "{n} hours", "{n} 小时", "{n} ساعت"],
        ["device.one"] = ["{n} устройство", "{n} cihaz", "{n} device", "{n} 台设备", "{n} دستگاه"],
        ["device.few"] = ["{n} устройства", "{n} cihaz", "{n} devices", "{n} 台设备", "{n} دستگاه"],
        ["device.many"] = ["{n} устройств", "{n} cihaz", "{n} devices", "{n} 台设备", "{n} دستگاه"],
        ["location.one"] = ["{n} локация", "{n} konum", "{n} location", "{n} 个位置", "{n} مکان"],
        ["location.few"] = ["{n} локации", "{n} konum", "{n} locations", "{n} 个位置", "{n} مکان"],
        ["location.many"] = ["{n} локаций", "{n} konum", "{n} locations", "{n} 个位置", "{n} مکان"],

        // Pricing (same wording as colitu.com/pricing)
        ["pricing.kicker"] = ["ТАРИФЫ", "PAKETLER", "PLANS", "套餐", "طرح‌ها"],
        ["pricing.title"] = ["Все серверы в любом тарифе.", "Her pakette tüm sunucular.", "Every server on every plan.", "每个套餐都包含全部服务器。", "همهٔ سرورها در هر طرح."],
        ["pricing.sub"] = ["Выберите тариф и оплатите прямо здесь: картой, СБП или криптовалютой в рублях либо через Alipay / WeChat Pay в юанях.", "Paketinizi seçin ve buradan ödeyin: ruble ile kart, SBP veya kripto; yuan ile Alipay / WeChat Pay.", "Pick a plan and pay right here: card, SBP or crypto in roubles, or Alipay / WeChat Pay in yuan.", "在这里选择套餐并直接付款：卢布可用银行卡、SBP 或加密货币，人民币可用支付宝 / 微信支付。", "طرح خود را انتخاب کنید و همین‌جا بپردازید: کارت، SBP یا رمزارز به روبل، یا Alipay / WeChat Pay به یوان."],
        ["plan.manageTitle"] = ["Подписка управляется на colitu.com", "Abonelik colitu.com üzerinden yönetilir", "Your subscription is managed on colitu.com", "你的订阅在 colitu.com 管理", "اشتراک شما در colitu.com مدیریت می‌شود"],
        ["plan.manageBody"] = ["Тариф, продление и устройства — в личном кабинете. Войдите с той же почтой, что и в приложении.", "Paket, süre uzatma ve cihazlar hesabınızdan yönetilir. Uygulamadaki e-posta adresinizle giriş yapın.", "Plans, renewals and devices are handled in your account. Sign in with the same e-mail as in the app.", "套餐、续订和设备都在你的账户中管理。请使用与应用中相同的邮箱登录。", "طرح‌ها، تمدیدها و دستگاه‌ها در حساب شما مدیریت می‌شوند. با همان ایمیلی که در برنامه استفاده کرده‌اید وارد شوید."],
        ["plan.manageButton"] = ["Открыть app.colitu.com", "app.colitu.com’u aç", "Open app.colitu.com", "打开 app.colitu.com", "باز کردن app.colitu.com"],
        ["plan.refresh"] = ["Обновить статус", "Durumu yenile", "Refresh status", "刷新状态", "تازه‌سازی وضعیت"],
        ["plan.refreshHint"] = ["После изменений в кабинете вернитесь сюда и обновите статус.", "Hesabınızda değişiklik yaptıktan sonra buraya dönüp durumu yenileyin.", "After changing something in your account, come back and refresh.", "在账户中做了更改后，请回到这里刷新。", "پس از تغییر در حساب خود، برگردید و تازه‌سازی کنید."],
        ["plan.refreshed"] = ["Статус обновлён", "Durum güncellendi", "Status updated", "状态已更新", "وضعیت به‌روز شد"],
        ["pricing.current"] = ["Текущий тариф", "Mevcut paket", "Current plan", "当前套餐", "طرح فعلی"],
        ["pricing.months"] = ["{n} МЕС.", "{n} AY", "{n} MONTHS", "{n} 个月", "{n} ماه"],
        ["pricing.oneMonth"] = ["1 МЕС.", "1 AY", "1 MONTH", "1 个月", "۱ ماه"],
        ["pricing.twoYears"] = ["2 ГОДА", "2 YIL", "2 YEARS", "2 年", "۲ سال"],
        ["pricing.off"] = ["Скидка {n}%", "%{n} İNDİRİM", "{n}% off", "立减 {n}%", "{n}% تخفیف"],
        ["pricing.best"] = ["САМОЕ ВЫГОДНОЕ ПРЕДЛОЖЕНИЕ", "EN AVANTAJLI · EN ÇOK TASARRUF", "BEST VALUE · BIGGEST SAVINGS", "超值之选 · 最省钱", "بهترین ارزش · بیشترین صرفه‌جویی"],
        ["pricing.perDevice"] = ["в месяц за устройство", "aylık, cihaz başına", "per device a month", "每台设备每月", "برای هر دستگاه در ماه"],
        ["pricing.saving"] = ["Экономия {amount} на устройство", "Cihaz başına {amount} tasarruf", "Save {amount} per device", "每台设备省 {amount}", "صرفه‌جویی {amount} برای هر دستگاه"],
        ["pricing.devices"] = ["Количество устройств", "Cihaz sayısı", "Number of devices", "设备数量", "تعداد دستگاه‌ها"],
        ["pricing.method"] = ["Способ оплаты", "Ödeme yöntemi", "Payment method", "付款方式", "روش پرداخت"],
        ["pricing.noFee"] = ["Без комиссии", "Komisyonsuz", "No fee", "无手续费", "بدون کارمزد"],
        ["pricing.fee"] = ["Комиссия {n}%", "Komisyon %{n}", "{n}% fee", "手续费 {n}%", "کارمزد {n}%"],
        ["pricing.summary"] = ["ИТОГ", "ÖZET", "SUMMARY", "摘要", "خلاصه"],
        ["pricing.regular"] = ["Обычная цена", "Normal fiyat", "Regular price", "原价", "قیمت عادی"],
        ["pricing.discount"] = ["Скидка ({n}%)", "İndirim (%{n})", "Discount ({n}%)", "折扣（{n}%）", "تخفیف ({n}%)"],
        ["pricing.commission"] = ["Комиссия", "Komisyon", "Payment fee", "支付手续费", "کارمزد پرداخت"],
        ["pricing.total"] = ["ИТОГО", "TOPLAM", "TOTAL", "合计", "مجموع"],
        ["pricing.pay"] = ["Перейти к безопасной оплате", "Güvenli ödemeye geç", "Continue to secure payment", "前往安全付款", "ادامه به پرداخت امن"],
        ["pricing.secure"] = ["Оплата проходит на защищённой странице платёжного сервиса. Доступ откроется сразу после оплаты.", "Ödeme, ödeme sağlayıcısının güvenli sayfasında yapılır. Erişiminiz ödemeden hemen sonra açılır.", "You pay on the payment provider’s secure page. Access opens as soon as the payment clears.", "你将在支付服务商的安全页面完成付款。付款到账后会立即开通。", "پرداخت را در صفحهٔ امن درگاه پرداخت انجام می‌دهید. به‌محض تأیید پرداخت، دسترسی شما فعال می‌شود."],
        ["pricing.loadFailed"] = ["Не удалось загрузить тарифы.", "Paketler yüklenemedi.", "Plans could not be loaded.", "无法加载套餐。", "بارگذاری طرح‌ها ممکن نشد."],
        ["pricing.retry"] = ["Повторить", "Tekrar dene", "Try again", "重试", "تلاش دوباره"],
        ["pay.checking"] = ["Проверяем оплату…", "Ödeme kontrol ediliyor...", "Checking payment…", "正在确认付款…", "در حال بررسی پرداخت…"],
        ["pay.checkingHint"] = ["Завершите оплату в браузере. Эта страница обновится автоматически.", "Ödemeyi tarayıcıda tamamlayın. Bu sayfa otomatik olarak güncellenir.", "Finish paying in your browser. This page updates by itself.", "请在浏览器中完成付款，此页面会自动更新。", "پرداخت را در مرورگر خود کامل کنید. این صفحه خودکار به‌روز می‌شود."],
        ["pay.reopen"] = ["Открыть страницу оплаты", "Ödeme sayfasını aç", "Open payment page", "打开付款页面", "باز کردن صفحهٔ پرداخت"],
        ["pay.cancel"] = ["Отменить", "Vazgeç", "Cancel", "取消", "لغو"],
        ["pay.success"] = ["Оплата прошла", "Ödeme alındı", "Payment received", "付款已收到", "پرداخت دریافت شد"],
        ["pay.successHint"] = ["Тариф активен. Можно подключаться.", "Paketiniz aktif. Hemen bağlanabilirsiniz.", "Your plan is active. You can connect now.", "你的套餐已生效，现在可以连接了。", "طرح شما فعال است. اکنون می‌توانید وصل شوید."],
        ["pay.failed"] = ["Оплата не прошла", "Ödeme tamamlanmadı", "Payment didn’t go through", "付款未成功", "پرداخت انجام نشد"],
        ["pay.failedHint"] = ["Деньги не списаны. Попробуйте ещё раз или выберите другой способ.", "Ücret alınmadı. Tekrar deneyin veya başka bir yöntem seçin.", "You were not charged. Try again or pick another method.", "你没有被扣款。请重试或选择其他付款方式。", "از شما پولی کسر نشد. دوباره تلاش کنید یا روش دیگری انتخاب کنید."],
        ["pay.back"] = ["Вернуться к тарифам", "Paketlere dön", "Back to plans", "返回套餐", "بازگشت به طرح‌ها"],
        ["pay.connect"] = ["Подключиться", "Bağlan", "Connect now", "立即连接", "همین حالا وصل شو"],
        ["pricing.choosePlan"] = ["Выберите тариф", "Paketinizi seçin", "Choose your plan", "选择套餐", "طرح خود را انتخاب کنید"],
        ["pricing.monthly"] = ["Помесячно", "Aylık", "Monthly", "按月", "ماهانه"],
        ["pricing.yearly"] = ["На год", "Yıllık", "Yearly", "按年", "سالانه"],
        ["pricing.perMonth"] = ["/ мес.", "/ ay", "/ month", "/ 月", "/ ماه"],
        ["pricing.billedYearly"] = ["{amount} за год", "Yıllık {amount}", "{amount} billed yearly", "按年支付 {amount}", "{amount} به‌صورت سالانه"],
        ["pricing.package"] = ["Тариф", "Paket", "Plan", "套餐", "طرح"],
        ["tier.personal"] = ["Личный", "Kişisel", "Personal", "个人版", "شخصی"],
        ["tier.plus"] = ["Plus", "Plus", "Plus", "Plus", "Plus"],
        ["tier.family"] = ["Семейный", "Aile", "Family", "家庭", "خانوادگی"],
        ["method.card"] = ["Банковская карта", "Banka kartı", "Bank card", "银行卡", "کارت بانکی"],
        ["method.sbp"] = ["СБП", "SBP", "SBP", "SBP", "SBP"],
        ["method.crypto"] = ["Криптовалюта", "Kripto para", "Crypto", "加密货币", "رمزارز"],
        ["method.kyren"] = ["Alipay / WeChat Pay", "Alipay / WeChat Pay", "Alipay / WeChat Pay", "支付宝 / 微信支付", "Alipay / WeChat Pay"],
        ["currency.rub"] = ["В рублях (₽)", "Ruble ile (₽)", "In roubles (₽)", "以卢布支付（₽）", "به روبل (₽)"],
        ["currency.cny"] = ["В юанях (¥)", "Yuan ile (¥)", "In yuan (¥)", "以人民币支付（¥）", "به یوان (¥)"],

        // Account
        ["account.kicker"] = ["АККАУНТ", "HESAP", "ACCOUNT", "账户", "حساب"],
        ["account.title"] = ["Ваш аккаунт", "Hesabınız", "Your account", "你的账户", "حساب شما"],
        ["account.email"] = ["Электронная почта", "E-posta", "Email", "邮箱", "ایمیل"],
        ["account.plan"] = ["Тариф", "Paket", "Plan", "套餐", "طرح"],
        ["account.validUntil"] = ["Действует до", "Geçerlilik", "Valid until", "有效期至", "معتبر تا"],
        ["account.devices"] = ["УСТРОЙСТВА", "CİHAZLAR", "DEVICES", "设备", "دستگاه‌ها"],
        ["account.devicesTitle"] = ["Подключено {used} из {limit}", "{limit} cihazdan {used} tanesi bağlı", "{used} of {limit} in use", "已使用 {used} / {limit}", "{used} از {limit} در حال استفاده"],
        ["account.thisDevice"] = ["ЭТО УСТРОЙСТВО", "BU CİHAZ", "THIS DEVICE", "本设备", "این دستگاه"],
        ["account.lastSeen"] = ["Активность: {date}", "Son etkinlik: {date}", "Last active {date}", "最近活跃：{date}", "آخرین فعالیت: {date}"],
        ["account.remove"] = ["Отключить устройство", "Cihazı kaldır", "Remove device", "移除设备", "حذف دستگاه"],
        ["account.removeConfirm"] = ["Отключить «{name}»? На нём потребуется войти снова.", "“{name}” kaldırılsın mı? Bu cihazda yeniden giriş yapmak gerekecek.", "Remove “{name}”? It will need to sign in again.", "移除“{name}”？该设备需要重新登录。", "«{name}» حذف شود؟ این دستگاه باید دوباره وارد شود."],
        ["account.manage"] = ["Управлять на colitu.com", "colitu.com’da yönet", "Manage on colitu.com", "在 colitu.com 管理", "مدیریت در colitu.com"],
        ["account.signOut"] = ["Выйти", "Çıkış yap", "Sign out", "退出登录", "خروج"],
        ["account.help"] = ["Нужна помощь?", "Yardım mı lazım?", "Need help?", "需要帮助？", "به کمک نیاز دارید؟"],
        ["account.helpHint"] = ["Напишите нам — отвечаем быстро и по-человечески.", "Bize yazın; hızlı ve insan gibi yanıt veririz.", "Write to us. We answer quickly, and a real person reads it.", "给我们留言。我们会尽快回复，并由真人阅读。", "برای ما بنویسید. سریع پاسخ می‌دهیم و یک انسان واقعی پیام شما را می‌خواند."],
        ["account.support"] = ["Центр поддержки", "Destek merkezi", "Support center", "帮助中心", "مرکز پشتیبانی"],
        ["account.mail"] = ["Написать на почту", "E-posta gönder", "Email support", "邮件支持", "پشتیبانی ایمیلی"],

        // Settings
        ["settings.kicker"] = ["НАСТРОЙКИ", "AYARLAR", "SETTINGS", "设置", "تنظیمات"],
        ["settings.title"] = ["Настройки", "Ayarlar", "Settings", "设置", "تنظیمات"],
        ["settings.language"] = ["Язык", "Dil", "Language", "语言", "زبان"],
        ["settings.connection"] = ["Подключение", "Bağlantı", "Connection", "连接", "اتصال"],
        ["settings.mode"] = ["Режим", "Mod", "Mode", "模式", "حالت"],
        ["settings.mode.proxy"] = ["Прокси", "Proxy", "Proxy", "代理", "پروکسی"],
        ["settings.mode.tun"] = ["Весь трафик (TUN)", "Tüm trafik (TUN)", "All traffic (TUN)", "全部流量 (TUN)", "همهٔ ترافیک (TUN)"],
        ["settings.mode.proxyHint"] = ["Через VPN идут только программы, использующие системный прокси. DNS-запросы, UDP/WebRTC, IPv6 и программы, игнорирующие прокси, не защищены.", "Yalnızca sistem proxy’sini kullanan uygulamalar VPN’den geçer. DNS sorguları, UDP/WebRTC, IPv6 ve proxy’yi yok sayan uygulamalar korunmaz.", "Only apps that use the system proxy go through the VPN. DNS lookups, UDP/WebRTC, IPv6 and apps that ignore the proxy are not protected.", "只有使用系统代理的应用才会经过 VPN。DNS 查询、UDP/WebRTC、IPv6 以及不使用代理的应用不受保护。", "فقط برنامه‌هایی که از پروکسی سیستم استفاده می‌کنند از VPN عبور می‌کنند. جستجوهای DNS، UDP/WebRTC، IPv6 و برنامه‌هایی که پروکسی را نادیده می‌گیرند محافظت نمی‌شوند."],
        ["settings.mode.tunHint"] = ["Весь трафик Windows идёт через защищённый адаптер, включая игры и мессенджеры.", "Oyunlar ve mesajlaşma dahil tüm Windows trafiği güvenli bağdaştırıcıdan geçer.", "All Windows traffic, games and messengers included, goes through the secure adapter.", "所有 Windows 流量（包括游戏和即时通讯）都会经过安全虚拟网卡。", "همهٔ ترافیک ویندوز، از جمله بازی‌ها و پیام‌رسان‌ها، از آداپتور امن عبور می‌کند."],
        ["settings.killSwitch"] = ["Kill switch", "Kill switch", "Kill switch", "断网保护", "Kill Switch"],
        ["settings.killSwitchHint"] = ["Пока VPN включён, трафик мимо VPN блокируется. Если VPN или приложение неожиданно отключатся или подключиться не удаётся, интернет остаётся закрытым, пока вы не повторите попытку или не отключитесь. В режиме прокси программы без системного прокси остаются без интернета.", "VPN açıkken VPN dışı trafik engellenir. VPN ya da uygulama beklenmedik şekilde kapanırsa ya da bağlantı kurulamazsa, yeniden deneyene ya da bağlantıyı kesene kadar internet kapalı kalır. Proxy modunda sistem proxy’sini kullanmayan uygulamalar internete çıkamaz.", "While the VPN is on, traffic outside it is blocked. If the VPN or the app stops unexpectedly, or a connection can’t be made, the internet stays closed until you retry or disconnect. In proxy mode, apps that don’t use the system proxy have no internet.", "VPN 开启期间，不经过 VPN 的流量会被阻断。如果 VPN 或应用意外停止，或无法建立连接，网络会保持关闭，直到你重试或断开连接。在代理模式下，不使用系统代理的应用将无法上网。", "تا زمانی که VPN روشن است، ترافیک خارج از آن مسدود می‌شود. اگر VPN یا برنامه ناگهان متوقف شود یا اتصال برقرار نشود، اینترنت تا زمانی که دوباره تلاش کنید یا اتصال را قطع کنید بسته می‌ماند. در حالت پروکسی، برنامه‌هایی که از پروکسی سیستم استفاده نمی‌کنند اینترنت ندارند."],
        ["settings.dns"] = ["Защита от утечек DNS", "DNS sızıntı koruması", "DNS leak protection", "DNS 泄漏防护", "محافظت در برابر نشت DNS"],
        ["settings.dnsHint"] = ["В режиме TUN все DNS-запросы идут через VPN. В режиме прокси защищены запросы приложений, использующих системный прокси.", "TUN modunda tüm DNS sorguları VPN üzerinden gider. Proxy modunda yalnızca sistem proxy'sini kullanan uygulamaların sorguları korunur.", "In TUN mode every DNS lookup goes through the VPN. In proxy mode only apps that use the system proxy are covered.", "在 TUN 模式下，所有 DNS 查询都会经过 VPN。在代理模式下，仅覆盖使用系统代理的应用。", "در حالت TUN هر جستجوی DNS از VPN عبور می‌کند. در حالت پروکسی فقط برنامه‌هایی که از پروکسی سیستم استفاده می‌کنند پوشش داده می‌شوند."],
        ["settings.autoConnect"] = ["Автоподключение", "Otomatik bağlan", "Auto-connect", "自动连接", "اتصال خودکار"],
        ["settings.autoConnectHint"] = ["Подключаться сразу после запуска приложения.", "Uygulama açılır açılmaz bağlan.", "Connect as soon as the app starts.", "应用启动后立即连接。", "به‌محض اجرای برنامه وصل شود."],
        ["settings.adBlock"] = ["Блокировка рекламы", "Reklam engelleme", "Ad blocking", "广告拦截", "مسدودسازی تبلیغات"],
        ["settings.adBlockHint"] = ["Реклама и трекеры блокируются на DNS-серверах Colitu. Журнал запросов не ведётся. Если какой-то сайт сломается, выключите.", "Reklam ve izleyiciler Colitu DNS sunucularında engellenir. Sorgu kaydı tutulmaz. Bir site bozulursa kapatın.", "Ads and trackers are blocked on Colitu’s DNS servers. No query log is kept. If a site breaks, turn it off.", "广告和跟踪器会在 Colitu 的 DNS 服务器上被拦截，不保留查询日志。如果某个网站无法正常显示，请关闭此功能。", "تبلیغات و ردیاب‌ها روی سرورهای DNS در Colitu مسدود می‌شوند. هیچ گزارش پرس‌وجویی نگهداری نمی‌شود. اگر سایتی درست کار نکرد، آن را خاموش کنید."],
        ["privacy.title"] = ["Режим приватности", "Gizlilik modu", "Privacy mode", "隐私模式", "حالت حریم خصوصی"],
        ["privacy.hint"] = ["Весь трафик через VPN. Если выключено, при сервере за пределами России российские сайты и IP-адреса идут напрямую, вне VPN, и видят ваш реальный IP-адрес.", "Tüm trafik VPN'den geçsin. Kapalıyken, Rusya dışındaki bir sunucuda Rus siteleri ve IP adresleri VPN dışından doğrudan gider ve gerçek IP adresinizi görür.", "Send all traffic through the VPN. When off, Russian sites and IP addresses go directly, outside the VPN, while you use a server outside Russia, and they see your real IP address.", "将所有流量都通过 VPN。关闭后，当你使用俄罗斯境外的服务器时，俄罗斯网站和 IP 地址会直连、不经过 VPN，这些网站会看到你的真实 IP 地址。", "همهٔ ترافیک را از VPN عبور می‌دهد. وقتی خاموش باشد و از سروری خارج از روسیه استفاده کنید، سایت‌ها و نشانی‌های IP روسی مستقیم و بیرون از VPN وصل می‌شوند و نشانی IP واقعی شما را می‌بینند."],
        ["privacy.scope"] = ["Какие сайты и адреса?", "Hangi siteler ve adresler?", "Which sites and addresses?", "涉及哪些网站和地址？", "کدام سایت‌ها و نشانی‌ها؟"],
        ["privacy.chip"] = ["Российские сайты вне VPN", "Rus siteleri VPN dışında", "Russian sites outside VPN", "俄罗斯网站不经 VPN", "سایت‌های روسی بیرون از VPN"],
        ["privacy.notice.title"] = ["Российские сайты идут вне VPN", "Rus siteleri VPN dışından gider", "Russian sites go outside the VPN", "俄罗斯网站将绕过 VPN", "سایت‌های روسی بیرون از VPN وصل می‌شوند"],
        ["privacy.notice.body"] = ["Чтобы российские банки, Госуслуги и другие сервисы, которые не пускают иностранные IP-адреса, продолжали работать, при сервере за пределами России Colitu отправляет российские сайты (список доменов category-ru) и российские IP-адреса напрямую, вне VPN. Эти сайты и ваш интернет-провайдер видят для такого трафика ваш реальный IP-адрес. Весь остальной трафик идёт через VPN. Включите режим приватности, чтобы весь трафик шёл через VPN; это можно изменить в любой момент в настройках.", "Rus bankaları, Gosuslugi ve yabancı IP adreslerini kabul etmeyen diğer servisler çalışmaya devam etsin diye Colitu, Rusya dışındaki bir sunucuda Rus sitelerini (category-ru alan adı listesi) ve Rus IP adreslerini VPN dışından, doğrudan gönderir. Bu trafikte siteler ve internet sağlayıcınız gerçek IP adresinizi görür. Diğer tüm trafik VPN'den geçer. Tüm trafiğin VPN'den geçmesi için gizlilik modunu açın; bunu istediğiniz zaman ayarlardan değiştirebilirsiniz.", "To keep Russian banks, Gosuslugi and other services that refuse foreign IP addresses working, Colitu sends Russian sites (the category-ru domain list) and Russian IP addresses directly, outside the VPN, while you use a server outside Russia. For this traffic those sites and your internet provider see your real IP address. Everything else goes through the VPN. Turn on privacy mode to send all traffic through the VPN; you can change this any time in settings.", "为了让俄罗斯的银行、Gosuslugi 以及其他拒绝境外 IP 地址的服务保持可用，当你使用俄罗斯境外的服务器时，Colitu 会让俄罗斯网站（category-ru 域名列表）和俄罗斯 IP 地址直连、不经过 VPN。对于这部分流量，这些网站和你的网络服务提供商会看到你的真实 IP 地址。其余流量都会经过 VPN。开启隐私模式即可让所有流量经过 VPN；你可以随时在设置中更改。", "برای اینکه بانک‌های روسی، Gosuslugi و سرویس‌های دیگری که نشانی‌های IP خارجی را نمی‌پذیرند همچنان کار کنند، وقتی از سروری خارج از روسیه استفاده می‌کنید، Colitu سایت‌های روسی (فهرست دامنه‌های category-ru) و نشانی‌های IP روسی را مستقیم و بیرون از VPN وصل می‌کند. برای این ترافیک، آن سایت‌ها و ارائه‌دهندهٔ اینترنت شما نشانی IP واقعی شما را می‌بینند. بقیهٔ ترافیک از VPN عبور می‌کند. برای عبور همهٔ ترافیک از VPN، حالت حریم خصوصی را روشن کنید؛ هر زمان می‌توانید در تنظیمات آن را تغییر دهید."],
        ["privacy.notice.keep"] = ["Оставить", "Böyle kalsın", "Keep", "保持不变", "همین‌طور بماند"],
        ["privacy.notice.enable"] = ["Включить режим приватности", "Gizlilik modunu aç", "Turn on privacy mode", "开启隐私模式", "روشن‌کردن حالت حریم خصوصی"],
        ["privacy.details"] = ["Подробнее", "Ayrıntılar", "Details", "详情", "جزئیات"],
        ["settings.app"] = ["Приложение", "Uygulama", "App", "应用", "برنامه"],
        ["settings.startup"] = ["Запускать вместе с Windows", "Windows ile başlat", "Start with Windows", "随 Windows 启动", "اجرا با ویندوز"],
        ["settings.startupHint"] = ["Colitu откроется в трее при входе в Windows.", "Colitu, Windows oturumu açıldığında tepside başlar.", "Colitu starts in the tray when you sign in to Windows.", "登录 Windows 后，Colitu 会在托盘中启动。", "پس از ورود به ویندوز، Colitu در سینی سیستم اجرا می‌شود."],
        ["settings.tray"] = ["Сворачивать в трей при закрытии", "Kapatınca tepsiye küçült", "Keep running in the tray", "保持在托盘中运行", "ادامهٔ اجرا در سینی"],
        ["settings.trayHint"] = ["Защита остаётся включённой, пока окно закрыто.", "Pencere kapalıyken koruma açık kalır.", "Protection stays on while the window is closed.", "关闭窗口后，保护仍保持开启。", "با بسته‌شدن پنجره، محافظت روشن می‌ماند."],
        ["settings.about"] = ["О приложении", "Hakkında", "About", "关于", "درباره"],
        ["settings.version"] = ["Версия {version}", "Sürüm {version}", "Version {version}", "版本 {version}", "نسخهٔ {version}"],
        ["settings.checkUpdates"] = ["Проверить обновления", "Güncellemeleri denetle", "Check for updates", "检查更新", "بررسی به‌روزرسانی"],
        ["settings.upToDate"] = ["У вас последняя версия.", "En güncel sürümü kullanıyorsunuz.", "You’re on the latest version.", "你使用的已是最新版本。", "شما از آخرین نسخه استفاده می‌کنید."],
        ["settings.updateCheckFailed"] = ["Не удалось проверить обновления. Проверьте интернет и попробуйте ещё раз.", "Güncellemeler denetlenemedi. İnternet bağlantınızı kontrol edip tekrar deneyin.", "Couldn’t check for updates. Check your internet connection and try again.", "无法检查更新。请检查网络连接后重试。", "بررسی به‌روزرسانی ممکن نشد. اتصال اینترنت خود را بررسی کنید و دوباره تلاش کنید."],
        ["settings.checking"] = ["Проверяем…", "Denetleniyor…", "Checking…", "正在检查…", "در حال بررسی…"],
        ["settings.privacy"] = ["Конфиденциальность", "Gizlilik", "Privacy", "隐私", "حریم خصوصی"],
        ["settings.terms"] = ["Условия", "Koşullar", "Terms", "条款", "شرایط"],
        ["settings.website"] = ["colitu.com", "colitu.com", "colitu.com", "colitu.com", "colitu.com"],
        ["settings.logs"] = ["Открыть журналы", "Günlükleri aç", "Open logs", "打开日志", "باز کردن گزارش‌ها"],
        ["settings.saved"] = ["Сохранено", "Kaydedildi", "Saved", "已保存", "ذخیره شد"],
        ["settings.reconnectHint"] = ["Изменения применятся при следующем подключении.", "Değişiklik bir sonraki bağlantıda uygulanır.", "Changes apply the next time you connect.", "更改将在下次连接时生效。", "تغییرات در اتصال بعدی اعمال می‌شوند."],
        ["about.openSource"] = ["Открытый исходный код", "Açık kaynak", "Open source", "开源", "متن‌باز"],
        ["about.openSourceHint"] = ["Colitu для Windows распространяется под лицензией GPL-3.0. Код открыт на GitHub: github.com/colitu/windows", "Colitu Windows uygulaması GPL-3.0 lisanslıdır. Kaynak kodu GitHub'da: github.com/colitu/windows", "Colitu for Windows is licensed under GPL-3.0. The source code is on GitHub: github.com/colitu/windows", "Colitu for Windows 采用 GPL-3.0 许可证。源代码在 GitHub：github.com/colitu/windows", "Colitu برای ویندوز با مجوز GPL-3.0 منتشر شده است. کد منبع در GitHub است: github.com/colitu/windows"],
        ["about.viewSource"] = ["Открыть на GitHub", "GitHub'da aç", "View on GitHub", "在 GitHub 上查看", "مشاهده در GitHub"],
        ["about.credits"] = ["Основано на v2rayN (GPL-3.0), Xray-core (MPL-2.0) и sing-box (GPL-3.0).", "v2rayN (GPL-3.0), Xray-core (MPL-2.0) ve sing-box (GPL-3.0) üzerine kuruludur.", "Built on v2rayN (GPL-3.0), Xray-core (MPL-2.0) and sing-box (GPL-3.0).", "基于 v2rayN (GPL-3.0)、Xray-core (MPL-2.0) 和 sing-box (GPL-3.0) 构建。", "ساخته‌شده بر پایهٔ v2rayN (GPL-3.0)، Xray-core (MPL-2.0) و sing-box (GPL-3.0)."],
        ["brand.credit"] = ["© {brand}", "© {brand}", "© {brand}", "© {brand}", "© {brand}"],

        // Auth
        ["auth.kicker"] = ["БЕЗОПАСНО · ПРИВАТНО · БЫСТРО", "GÜVENLİ · ÖZEL · HIZLI", "SECURE · PRIVATE · FAST", "安全 · 私密 · 快速", "امن · خصوصی · سریع"],
        ["auth.heroTitle"] = ["Свободный интернет в одно касание.", "Özgür internet, tek dokunuşla.", "The open internet, one tap away.", "开放的互联网，一键即达。", "اینترنت آزاد، فقط با یک کلیک."],
        ["auth.heroSub"] = ["Серверы в разных странах, шифрование трафика и никаких журналов.", "Farklı ülkelerde sunucular, şifreli trafik ve kayıt tutmama.", "Servers in many countries, encrypted traffic and no activity logs.", "多国服务器、加密流量，且不记录活动日志。", "سرور در کشورهای متعدد، ترافیک رمزگذاری‌شده و بدون ثبت گزارش فعالیت."],
        ["auth.login"] = ["Вход", "Giriş", "Sign in", "登录", "ورود"],
        ["auth.register"] = ["Регистрация", "Kayıt ol", "Sign up", "注册", "ثبت‌نام"],
        ["auth.loginTitle"] = ["С возвращением", "Tekrar hoş geldiniz", "Welcome back", "欢迎回来", "خوش آمدید"],
        ["auth.loginSub"] = ["Войдите в аккаунт Colitu — тот же, что на colitu.com.", "Colitu hesabınızla giriş yapın; colitu.com ile aynı hesap.", "Sign in with your Colitu account, the same one you use on colitu.com.", "使用你的 Colitu 账户登录，与 colitu.com 上的账户相同。", "با حساب Colitu خود وارد شوید؛ همان حسابی که در colitu.com دارید."],
        ["auth.registerTitle"] = ["Создать аккаунт", "Hesap oluştur", "Create account", "创建账户", "ایجاد حساب"],
        ["reset.title"] = ["Восстановление пароля", "Şifrenizi sıfırlayın", "Reset your password", "重置密码", "بازنشانی رمز عبور"],
        ["reset.sub"] = ["Введите почту аккаунта — мы отправим на неё 6-значный код.", "Hesabınızın e-posta adresini girin; 6 haneli bir kod gönderelim.", "Enter your account email and we’ll send you a 6-digit code.", "输入你的账户邮箱，我们会发送一个 6 位验证码。", "ایمیل حساب خود را وارد کنید تا یک کد ۶ رقمی برایتان بفرستیم."],
        ["reset.codeSub"] = ["Введите код из письма и придумайте новый пароль.", "E-postadaki kodu girin ve yeni bir şifre belirleyin.", "Enter the code from the email and choose a new password.", "输入邮件中的验证码，并设置新密码。", "کد ایمیل را وارد کنید و رمز عبور جدیدی انتخاب کنید."],
        ["reset.send"] = ["Отправить код", "Kod gönder", "Send code", "发送验证码", "ارسال کد"],
        ["reset.sent"] = ["Код отправлен на {email}. Проверьте и папку «Спам».", "{email} adresine kod gönderdik. Gereksiz (spam) klasörünü de kontrol edin.", "We sent a code to {email}. Check your spam folder too.", "我们已向 {email} 发送了验证码。请同时查看垃圾邮件文件夹。", "کدی به {email} فرستادیم. پوشهٔ هرزنامه را هم بررسی کنید."],
        ["reset.newPassword"] = ["Новый пароль", "Yeni şifre", "New password", "新密码", "رمز عبور جدید"],
        ["reset.submit"] = ["Сменить пароль и войти", "Şifreyi değiştir ve giriş yap", "Change password and sign in", "修改密码并登录", "تغییر رمز عبور و ورود"],
        ["reset.changeEmail"] = ["Изменить почту", "E-postayı değiştir", "Change email", "更换邮箱", "تغییر ایمیل"],
        ["reset.back"] = ["Вернуться ко входу", "Girişe dön", "Back to sign in", "返回登录", "بازگشت به ورود"],
        ["reset.note"] = ["После смены пароля остальные устройства выйдут из аккаунта.", "Şifre değişince diğer cihazlardaki oturumlar kapanır.", "Changing the password signs you out on your other devices.", "修改密码后，你在其他设备上会被退出登录。", "با تغییر رمز عبور، در دستگاه‌های دیگر از حساب خارج می‌شوید."],
        ["reset.done"] = ["Пароль изменён. Вы вошли в аккаунт.", "Şifreniz değiştirildi, giriş yaptınız.", "Your password was changed and you’re signed in.", "密码已修改，你已登录。", "رمز عبور شما تغییر کرد و وارد شدید."],
        ["auth.registerSub"] = ["Зарегистрируйтесь по почте и пользуйтесь Colitu бесплатно: 1 ГБ каждый месяц.", "E-postanızla kaydolun, Colitu’yu ücretsiz kullanın: her ay 1 GB.", "Sign up with your email and use Colitu for free: 1 GB every month.", "使用邮箱注册，即可免费使用 Colitu：每月 1 GB。", "با ایمیل ثبت‌نام کنید و Colitu را رایگان استفاده کنید: هر ماه ۱ گیگابایت."],
        ["auth.email"] = ["Электронная почта", "E-posta", "Email", "邮箱", "ایمیل"],
        ["auth.emailHint"] = ["you@example.com", "ornek@eposta.com", "you@example.com", "you@example.com", "you@example.com"],
        ["auth.password"] = ["Пароль", "Şifre", "Password", "密码", "رمز عبور"],
        ["auth.passwordHint"] = ["Не менее 10 символов", "En az 10 karakter", "At least 10 characters", "至少 10 个字符", "حداقل ۱۰ نویسه"],
        ["auth.passwordRepeat"] = ["Повторите пароль", "Şifreyi tekrarla", "Repeat password", "重复密码", "تکرار رمز عبور"],
        ["auth.show"] = ["Показать пароль", "Şifreyi göster", "Show password", "显示密码", "نمایش رمز عبور"],
        ["auth.terms"] = ["Я принимаю условия и политику конфиденциальности", "Koşulları ve gizlilik politikasını kabul ediyorum", "I accept the terms and privacy policy", "我接受条款和隐私政策", "شرایط و سیاست حریم خصوصی را می‌پذیرم"],
        ["auth.submitLogin"] = ["Войти", "Giriş yap", "Sign in", "登录", "ورود"],
        ["auth.submitRegister"] = ["Создать аккаунт", "Hesap oluştur", "Create account", "创建账户", "ایجاد حساب"],
        ["auth.forgot"] = ["Забыли пароль?", "Şifremi unuttum", "Forgot password?", "忘记密码？", "رمز عبور را فراموش کرده‌اید؟"],
        ["auth.remember"] = ["Вы останетесь в аккаунте на этом компьютере.", "Bu bilgisayarda oturumunuz açık kalır.", "You’ll stay signed in on this computer.", "你会在这台电脑上保持登录。", "در این رایانه وارد شده باقی می‌مانید."],
        ["auth.err.email"] = ["Введите корректный адрес почты.", "Geçerli bir e-posta adresi girin.", "Enter a valid email address.", "请输入有效的邮箱地址。", "یک نشانی ایمیل معتبر وارد کنید."],
        ["auth.err.password"] = ["Пароль должен быть не короче 10 символов.", "Şifre en az 10 karakter olmalı.", "The password must be at least 10 characters.", "密码至少需要 10 个字符。", "رمز عبور باید حداقل ۱۰ نویسه باشد."],
        ["auth.err.mismatch"] = ["Пароли не совпадают.", "Şifreler eşleşmiyor.", "Passwords don’t match.", "两次输入的密码不一致。", "رمزهای عبور یکسان نیستند."],
        ["auth.err.terms"] = ["Примите условия, чтобы продолжить.", "Devam etmek için koşulları kabul edin.", "Accept the terms to continue.", "请接受条款后继续。", "برای ادامه، شرایط را بپذیرید."],
        ["auth.expired"] = ["Сеанс завершён. Войдите снова.", "Oturumunuz sona erdi. Lütfen tekrar giriş yapın.", "Your session ended. Please sign in again.", "你的会话已结束，请重新登录。", "نشست شما پایان یافت. لطفاً دوباره وارد شوید."],

        // Errors
        ["err.credentials"] = ["Неверная почта или пароль.", "E-posta veya şifre hatalı.", "Email or password is incorrect.", "邮箱或密码不正确。", "ایمیل یا رمز عبور درست نیست."],
        ["err.registration"] = ["Эту почту нельзя зарегистрировать: возможно, аккаунт уже существует.", "Bu e-posta ile kayıt yapılamıyor; hesap zaten olabilir.", "This email can’t be registered. It may already have an account.", "此邮箱无法注册，可能已有账户。", "این ایمیل قابل ثبت‌نام نیست. ممکن است قبلاً حسابی داشته باشد."],
        ["err.rateLimited"] = ["Слишком много попыток. Подождите минуту.", "Çok fazla deneme. Lütfen bir dakika bekleyin.", "Too many attempts. Please wait a minute.", "尝试次数过多，请等待一分钟。", "تلاش‌های بیش از حد. لطفاً یک دقیقه صبر کنید."],
        ["err.deviceLimit"] = ["Достигнут лимит устройств вашего тарифа. Отключите старое устройство на colitu.com/devices или перейдите на платный тариф с большим числом устройств: colitu.com/pricing.", "Paketinizin cihaz sınırına ulaşıldı. colitu.com/devices adresinden eski bir cihazı kaldırın veya daha fazla cihaz için ücretli bir pakete geçin: colitu.com/pricing.", "Your plan’s device limit is reached. Remove an old device at colitu.com/devices, or get a paid plan with more devices at colitu.com/pricing.", "你的套餐设备数量已达上限。请在 colitu.com/devices 移除旧设备，或在 colitu.com/pricing 开通可用更多设备的付费套餐。", "سقف دستگاه‌های طرح شما پر شده است. دستگاه قدیمی را در colitu.com/devices حذف کنید، یا در colitu.com/pricing طرح پولی با دستگاه‌های بیشتر بگیرید."],
        ["err.region"] = ["Регистрация и вход из вашего региона сейчас недоступны. Если вы используете другой VPN или прокси, отключите его и попробуйте снова.", "Bulunduğunuz bölgeden kayıt ve giriş şu anda kullanılamıyor. Başka bir VPN veya proxy açıksa kapatıp tekrar deneyin.", "Sign-up and sign-in aren’t available from your region right now. If another VPN or proxy is on, turn it off and try again.", "目前你所在地区无法注册或登录。如果开启了其他 VPN 或代理，请关闭后重试。", "در حال حاضر ثبت‌نام و ورود از منطقهٔ شما ممکن نیست. اگر VPN یا پروکسی دیگری روشن است، آن را خاموش کنید و دوباره تلاش کنید."],
        ["err.trialUsed"] = ["На этом компьютере пробный период уже использован. Выберите тариф на colitu.com, чтобы продолжить.", "Bu bilgisayarda deneme süresi daha önce kullanıldı. Devam etmek için colitu.com’dan bir paket seçin.", "The free trial was already used on this computer. Choose a plan at colitu.com to continue.", "这台电脑已使用过免费试用。请前往 colitu.com 选择套餐后继续。", "دورهٔ آزمایشی رایگان قبلاً در این رایانه استفاده شده است. برای ادامه، در colitu.com یک طرح انتخاب کنید."],
        ["err.notVerified"] = ["Сначала подтвердите адрес электронной почты.", "Önce e-posta adresinizi doğrulayın.", "Please confirm your email address first.", "请先确认你的邮箱地址。", "ابتدا نشانی ایمیل خود را تأیید کنید."],

        // E-mail verification
        ["verify.kicker"] = ["ПОСЛЕДНИЙ ШАГ", "SON ADIM", "ONE LAST STEP", "最后一步", "آخرین مرحله"],
        ["verify.title"] = ["Подтвердите почту", "E-postanızı doğrulayın", "Confirm your email", "确认你的邮箱", "ایمیل خود را تأیید کنید"],
        ["verify.sub"] = ["Мы отправили 6-значный код на {email}. Введите его, чтобы активировать аккаунт и бесплатный тариф.", "{email} adresine 6 haneli bir kod gönderdik. Hesabınızı ve ücretsiz planınızı açmak için kodu girin.", "We sent a 6-digit code to {email}. Enter it to activate your account and free plan.", "我们已向 {email} 发送了 6 位验证码。输入验证码即可激活账户和免费套餐。", "کدی ۶ رقمی به {email} فرستادیم. آن را وارد کنید تا حساب و طرح رایگان شما فعال شود."],
        ["verify.code"] = ["Код из письма", "E-postadaki kod", "Code from the email", "邮件中的验证码", "کد موجود در ایمیل"],
        ["verify.submit"] = ["Подтвердить", "Doğrula", "Confirm", "确认", "تأیید"],
        ["verify.resend"] = ["Отправить код ещё раз", "Kodu tekrar gönder", "Send the code again", "重新发送验证码", "ارسال دوبارهٔ کد"],
        ["verify.resendIn"] = ["Отправить снова через {n} с", "{n} sn sonra tekrar gönderebilirsiniz", "Send again in {n}s", "{n} 秒后可重新发送", "ارسال دوباره تا {n} ثانیه دیگر"],
        ["verify.sent"] = ["Новый код отправлен. Проверьте и папку «Спам».", "Yeni kod gönderildi. Gereksiz (spam) klasörünü de kontrol edin.", "A new code is on its way. Check your spam folder too.", "新验证码已发出。请同时查看垃圾邮件文件夹。", "کد جدید در راه است. پوشهٔ هرزنامه را هم بررسی کنید."],
        ["verify.other"] = ["Войти в другой аккаунт", "Başka bir hesapla giriş yap", "Use a different account", "使用其他账户", "استفاده از حساب دیگر"],
        ["verify.hint"] = ["Код действует 15 минут. Письмо не пришло? Проверьте «Спам» или отправьте код ещё раз.", "Kod 15 dakika geçerlidir. E-posta gelmediyse spam klasörüne bakın veya kodu yeniden gönderin.", "The code is valid for 15 minutes. No email? Check spam or send the code again.", "验证码 15 分钟内有效。没收到邮件？请查看垃圾邮件，或重新发送验证码。", "کد ۱۵ دقیقه معتبر است. ایمیلی نرسید؟ هرزنامه را بررسی کنید یا کد را دوباره بفرستید."],
        ["verify.web"] = ["Код можно ввести и на colitu.com: приложение продолжит само, как только почта будет подтверждена.", "Kodu colitu.com üzerinden de girebilirsiniz; doğrulandığında uygulama kendiliğinden devam eder.", "You can also enter the code on colitu.com; the app carries on by itself once your email is confirmed.", "你也可以在 colitu.com 上输入验证码；邮箱确认后，应用会自动继续。", "می‌توانید کد را در colitu.com هم وارد کنید؛ پس از تأیید ایمیل، برنامه خودکار ادامه می‌دهد."],
        ["verify.done"] = ["Почта подтверждена. Добро пожаловать в Colitu!", "E-postanız doğrulandı. Colitu’ya hoş geldiniz!", "Email confirmed. Welcome to Colitu!", "邮箱已确认。欢迎使用 Colitu！", "ایمیل تأیید شد. به Colitu خوش آمدید!"],
        ["verify.err.length"] = ["Введите все 6 цифр кода.", "Kodun 6 hanesini de girin.", "Enter all 6 digits of the code.", "请输入验证码的全部 6 位数字。", "هر ۶ رقم کد را وارد کنید."],
        ["verify.err.invalid"] = ["Неверный код. Проверьте письмо и попробуйте снова.", "Kod hatalı. E-postayı kontrol edip tekrar deneyin.", "That code isn’t right. Check the email and try again.", "验证码不正确。请检查邮件后重试。", "این کد درست نیست. ایمیل را بررسی کنید و دوباره تلاش کنید."],
        ["verify.err.expired"] = ["Срок действия кода истёк. Отправьте новый.", "Kodun süresi doldu. Yeni bir kod isteyin.", "The code has expired. Request a new one.", "验证码已过期，请重新获取。", "کد منقضی شده است. کد جدیدی درخواست کنید."],
        ["verify.err.wait"] = ["Новый код можно запросить через минуту.", "Yeni kodu bir dakika sonra isteyebilirsiniz.", "You can request a new code in a minute.", "请一分钟后再请求新的验证码。", "یک دقیقهٔ دیگر می‌توانید کد جدید بخواهید."],
        ["verify.err.mail"] = ["Сейчас не удаётся отправить письмо. Попробуйте чуть позже или напишите в поддержку.", "Şu anda e-posta gönderilemiyor. Biraz sonra tekrar deneyin veya destekle iletişime geçin.", "We can’t send email right now. Try again shortly or contact support.", "我们暂时无法发送邮件。请稍后重试或联系客服。", "اکنون نمی‌توانیم ایمیل بفرستیم. کمی بعد دوباره تلاش کنید یا با پشتیبانی تماس بگیرید."],

        // Server categories
        ["cat.all"] = ["Все", "Tümü", "All", "全部", "همه"],
        ["cat.streaming"] = ["Стриминг", "Streaming", "Streaming", "流媒体", "استریم"],
        ["cat.gaming"] = ["Игры", "Oyun", "Gaming", "游戏", "بازی"],
        ["cat.privacy"] = ["Приватность", "Gizlilik", "Privacy", "隐私", "حریم خصوصی"],
        ["cat.speed"] = ["Скорость", "Hız", "Speed", "速度", "سرعت"],
        ["cat.torrent"] = ["Торренты", "Torrent", "Torrent", "种子下载", "تورنت"],
        ["cat.ai"] = ["ИИ", "Yapay zekâ", "AI", "AI", "هوش مصنوعی"],
        ["cat.adblock"] = ["Блокировка рекламы", "Reklam engelleme", "Ad blocking", "广告拦截", "مسدودسازی تبلیغات"],
        ["service.youtube_adfree"] = ["YouTube без рекламы", "Reklamsız YouTube", "Ad-free YouTube", "无广告 YouTube", "یوتیوب بدون تبلیغ"],
        ["cat.empty"] = ["В этой категории пока нет серверов.", "Bu kategoride henüz sunucu yok.", "No servers in this category yet.", "此分类下暂无服务器。", "هنوز سروری در این دسته نیست."],

        // Live support
        ["nav.support"] = ["Поддержка", "Destek", "Support", "支持", "پشتیبانی"],
        ["support.kicker"] = ["ПОДДЕРЖКА", "DESTEK", "SUPPORT", "支持", "پشتیبانی"],
        ["support.title"] = ["Живая поддержка", "Canlı destek", "Live support", "真人客服", "پشتیبانی زنده"],
        ["support.sub"] = ["Напишите нам прямо из приложения. Отвечает живой человек, обычно в течение часа.", "Bize doğrudan uygulamadan yazın. Gerçek bir kişi, genellikle bir saat içinde yanıt verir.", "Write to us right from the app. A real person answers, usually within an hour.", "直接在应用内给我们留言。真人回复，通常一小时内。", "مستقیم از داخل برنامه برای ما بنویسید. یک انسان واقعی پاسخ می‌دهد، معمولاً ظرف یک ساعت."],
        ["support.new"] = ["Новое обращение", "Yeni talep", "New request", "新建请求", "درخواست جدید"],
        ["support.empty"] = ["Обращений пока нет. Опишите проблему — мы поможем.", "Henüz talebiniz yok. Sorununuzu anlatın, yardımcı olalım.", "No requests yet. Tell us what’s wrong and we’ll help.", "还没有请求。告诉我们遇到了什么问题，我们会帮你解决。", "هنوز درخواستی نیست. بگویید چه مشکلی دارید تا کمک کنیم."],
        ["support.pick"] = ["Выберите обращение слева или создайте новое.", "Soldan bir talep seçin veya yeni bir talep oluşturun.", "Pick a request on the left or start a new one.", "在左侧选择一个请求，或新建一个。", "یک درخواست را از سمت چپ انتخاب کنید یا درخواست جدیدی بسازید."],
        ["support.subject"] = ["Тема", "Konu", "Subject", "主题", "موضوع"],
        ["support.subjectHint"] = ["Например: не подключается к серверу", "Örn: Sunucuya bağlanamıyorum", "For example: can’t connect to a server", "例如：无法连接服务器", "مثلاً: اتصال به سرور برقرار نمی‌شود"],
        ["support.message"] = ["Сообщение", "Mesaj", "Message", "消息", "پیام"],
        ["support.messageHint"] = ["Опишите, что происходит и что вы уже пробовали…", "Ne olduğunu ve neler denediğinizi yazın…", "Describe what’s happening and what you’ve tried…", "描述发生了什么，以及你已尝试过的办法…", "بنویسید چه اتفاقی می‌افتد و چه کارهایی را امتحان کرده‌اید…"],
        ["support.reply"] = ["Напишите ответ…", "Yanıtınızı yazın…", "Write a reply…", "输入回复…", "پاسخ بنویسید…"],
        ["support.send"] = ["Отправить", "Gönder", "Send", "发送", "ارسال"],
        ["support.attach"] = ["Прикрепить файл", "Dosya ekle", "Attach a file", "添加附件", "پیوست فایل"],
        ["support.fileTypes"] = ["Изображения, PDF, текст, архивы", "Görseller, PDF, metin, arşivler", "Images, PDF, text, archives", "图片、PDF、文本、压缩包", "تصویر، PDF، متن، آرشیو"],
        ["support.attachHint"] = ["Скриншоты, PDF, TXT, ZIP · до 10 МБ", "Ekran görüntüsü, PDF, TXT, ZIP · en fazla 10 MB", "Screenshots, PDF, TXT, ZIP · up to 10 MB", "截图、PDF、TXT、ZIP · 最大 10 MB", "عکس صفحه، PDF، TXT، ZIP · تا ۱۰ مگابایت"],
        ["support.diagnostics"] = ["Приложить диагностику", "Tanılama bilgisini ekle", "Include diagnostics", "附带诊断信息", "ارسال اطلاعات عیب‌یابی"],
        ["support.diagnosticsHint"] = ["Версия приложения и Windows, состояние подключения, последние ошибки и журнал. Без истории посещений.", "Uygulama ve Windows sürümü, bağlantı durumu, son hatalar ve günlük kaydı. Gezinme geçmişi gönderilmez.", "App and Windows version, connection state, recent errors and the log. No browsing history.", "应用和 Windows 版本、连接状态、最近的错误和日志。不包含浏览记录。", "نسخهٔ برنامه و ویندوز، وضعیت اتصال، خطاهای اخیر و گزارش. شامل تاریخچهٔ مرور نیست."],
        ["support.create"] = ["Отправить обращение", "Talebi gönder", "Send request", "发送请求", "ارسال درخواست"],
        ["support.cancel"] = ["Отмена", "Vazgeç", "Cancel", "取消", "لغو"],
        ["support.you"] = ["Вы", "Siz", "You", "你", "شما"],
        ["support.team"] = ["Поддержка Colitu", "Colitu Destek", "Colitu Support", "Colitu 客服", "پشتیبانی Colitu"],
        ["support.status.waiting"] = ["ЖДЁТ ОТВЕТА", "YANIT BEKLİYOR", "AWAITING REPLY", "等待回复", "در انتظار پاسخ"],
        ["support.status.open"] = ["В РАБОТЕ", "İNCELENİYOR", "IN PROGRESS", "处理中", "در حال بررسی"],
        ["support.status.resolved"] = ["РЕШЕНО", "ÇÖZÜLDÜ", "RESOLVED", "已解决", "حل‌شده"],
        ["support.status.closed"] = ["ЗАКРЫТО", "KAPANDI", "CLOSED", "已关闭", "بسته‌شده"],
        ["support.closed"] = ["Обращение закрыто. Создайте новое, если нужна помощь.", "Bu talep kapatıldı. Yardım gerekirse yeni bir talep açın.", "This request is closed. Start a new one if you need help.", "此请求已关闭。如需帮助，请新建请求。", "این درخواست بسته شده است. اگر کمک لازم دارید، درخواست جدیدی بسازید."],
        ["support.newReply"] = ["Новый ответ поддержки", "Destekten yeni yanıt", "New reply from support", "客服有新回复", "پاسخ جدید از پشتیبانی"],
        ["support.sent"] = ["Обращение отправлено. Мы ответим здесь и по почте.", "Talebiniz gönderildi. Buradan ve e-postayla yanıt vereceğiz.", "Request sent. We’ll answer here and by email.", "请求已发送。我们会在这里和通过邮件回复你。", "درخواست ارسال شد. اینجا و از طریق ایمیل پاسخ می‌دهیم."],
        ["support.err.subject"] = ["Укажите тему и сообщение.", "Konu ve mesajı yazın.", "Add a subject and a message.", "请填写主题和消息。", "موضوع و پیام را وارد کنید."],
        ["support.err.file"] = ["Файл больше 10 МБ или такой тип не поддерживается.", "Dosya 10 MB’tan büyük veya bu tür desteklenmiyor.", "The file is over 10 MB or the type isn’t supported.", "文件超过 10 MB，或类型不受支持。", "فایل بزرگ‌تر از ۱۰ مگابایت است یا نوع آن پشتیبانی نمی‌شود."],
        ["support.err.files"] = ["Можно прикрепить не больше 5 файлов.", "En fazla 5 dosya ekleyebilirsiniz.", "You can attach up to 5 files.", "最多可添加 5 个附件。", "حداکثر ۵ فایل می‌توانید پیوست کنید."],
        ["support.err.unavailable"] = ["Поддержка в приложении временно недоступна. Напишите на support@colitu.com.", "Uygulama içi destek geçici olarak kapalı. support@colitu.com adresine yazın.", "In-app support is unavailable right now. Write to support@colitu.com.", "应用内支持暂时不可用。请写信至 support@colitu.com。", "پشتیبانی درون‌برنامه‌ای اکنون در دسترس نیست. به support@colitu.com ایمیل بزنید."],
        ["support.help"] = ["Справочный центр", "Yardım merkezi", "Help centre", "帮助中心", "مرکز راهنما"],
        ["bot.button"] = ["Чат с ассистентом", "Asistanla sohbet", "Chat with the assistant", "与助手聊天", "گفتگو با دستیار"],
        ["bot.title"] = ["Ассистент Colitu", "Colitu asistanı", "Colitu assistant", "Colitu 助手", "دستیار Colitu"],
        ["bot.sub"] = ["Отвечает сразу; при необходимости передаёт разговор команде поддержки.", "Hemen yanıtlar; gerekirse görüşmeyi destek ekibine aktarır.", "Answers right away and hands the chat to the support team when needed.", "即时回答，需要时会将对话转交给客服团队。", "فوری پاسخ می‌دهد و در صورت نیاز گفتگو را به تیم پشتیبانی می‌سپارد."],
        ["bot.name"] = ["Ассистент Colitu", "Colitu asistanı", "Colitu assistant", "Colitu 助手", "دستیار Colitu"],
        ["bot.greeting"] = ["Здравствуйте! Я ассистент Colitu. Спросите о подключении, тарифе или устройствах — отвечу сразу. Если понадобится, передам разговор команде поддержки.", "Merhaba! Ben Colitu asistanıyım. Bağlantı, paket ya da cihazlarla ilgili sorunuzu yazın, hemen yanıtlayayım. Gerekirse görüşmeyi destek ekibine aktarırım.", "Hi! I'm the Colitu assistant. Ask about connecting, your plan or your devices and I'll answer right away. If needed, I'll hand the chat to the support team.", "你好！我是 Colitu 助手。关于连接、套餐或设备的问题都可以问我，我会立即回答。必要时，我会把对话转交给客服团队。", "سلام! من دستیار Colitu هستم. دربارهٔ اتصال، طرح یا دستگاه‌هایتان بپرسید تا فوری پاسخ بدهم. در صورت نیاز، گفتگو را به تیم پشتیبانی می‌سپارم."],
        ["bot.placeholder"] = ["Напишите вопрос…", "Sorunuzu yazın…", "Type your question…", "输入你的问题…", "سؤال خود را بنویسید…"],
        ["bot.typing"] = ["Ассистент печатает…", "Asistan yazıyor…", "The assistant is typing…", "助手正在输入…", "دستیار در حال نوشتن است…"],
        ["bot.helpful"] = ["Ответ помог?", "Yanıt işinize yaradı mı?", "Was this helpful?", "这对你有帮助吗？", "آیا مفید بود؟"],
        ["bot.yes"] = ["Да", "Evet", "Yes", "是", "بله"],
        ["bot.no"] = ["Нет", "Hayır", "No", "否", "خیر"],
        ["bot.thanks"] = ["Спасибо за отзыв!", "Geri bildiriminiz için teşekkürler!", "Thanks for the feedback!", "感谢你的反馈！", "از بازخوردتان سپاسگزاریم!"],
        ["bot.human"] = ["Связаться с человеком", "İnsana bağlan", "Talk to a person", "联系真人客服", "صحبت با یک انسان"],
        ["bot.new"] = ["Новый чат", "Yeni sohbet", "New chat", "新对话", "گفتگوی جدید"],
        ["bot.handoverTitle"] = ["Передать команде поддержки", "Destek ekibine aktar", "Hand over to the support team", "转交给客服团队", "سپردن به تیم پشتیبانی"],
        ["bot.handoverLead"] = ["Мы создадим обращение с этой перепиской. Ответ появится в списке обращений слева и придёт на почту.", "Bu görüşmeyle bir destek talebi açacağız. Yanıt soldaki talepler listesinde görünecek ve e-postanıza da gelecek.", "We'll open a support request with this chat. The answer will show in your requests on the left and arrive by e-mail.", "我们会用这段对话创建一个支持请求。回复会显示在左侧的请求列表中，并通过邮件发送给你。", "با این گفتگو یک درخواست پشتیبانی باز می‌کنیم. پاسخ در فهرست درخواست‌های سمت چپ نمایش داده می‌شود و به ایمیل شما هم می‌رسد."],
        ["bot.handoverNote"] = ["Что ещё стоит знать команде? (необязательно)", "Ekibin bilmesi gereken başka bir şey var mı? (isteğe bağlı)", "Anything else the team should know? (optional)", "还有什么想让团队知道的吗？（可选）", "چیز دیگری هست که تیم باید بداند؟ (اختیاری)"],
        ["bot.handover"] = ["Передать", "Aktar", "Hand over", "转交", "سپردن"],
        ["bot.escalated"] = ["Обращение {ticket} создано. Команда ответит в нём; оно уже в списке слева.", "{ticket} numaralı talep açıldı. Ekip yanıtı orada verecek; talep soldaki listede.", "Request {ticket} is open. The team will answer there; it's already in the list on the left.", "请求 {ticket} 已创建。团队会在那里回复；它已出现在左侧列表中。", "درخواست {ticket} باز شد. تیم همان‌جا پاسخ می‌دهد؛ در فهرست سمت چپ هم هست."],
        ["bot.openTicket"] = ["Открыть обращение", "Talebi aç", "Open the request", "打开请求", "باز کردن درخواست"],
        ["bot.err.network"] = ["Не удалось связаться с ассистентом. Проверьте подключение к интернету и попробуйте ещё раз.", "Asistana ulaşılamadı. İnternet bağlantınızı kontrol edip yeniden deneyin.", "Couldn't reach the assistant. Check your internet connection and try again.", "无法连接助手。请检查网络连接后重试。", "دسترسی به دستیار ممکن نشد. اتصال اینترنت خود را بررسی کنید و دوباره تلاش کنید."],
        ["bot.err.limit"] = ["Слишком много сообщений подряд. Подождите минуту и попробуйте снова.", "Çok fazla mesaj gönderildi. Bir dakika bekleyip yeniden deneyin.", "Too many messages in a row. Wait a minute and try again.", "连续发送的消息过多。请等待一分钟后重试。", "پیام‌های پشت‌سرهم زیاد است. یک دقیقه صبر کنید و دوباره تلاش کنید."],
        ["bot.err.long"] = ["Этот чат стал слишком длинным. Передайте его команде поддержки или начните новый.", "Bu sohbet çok uzadı. Destek ekibine aktarın ya da yeni bir sohbet başlatın.", "This chat has got too long. Hand it to the support team or start a new one.", "这段对话太长了。请转交给客服团队，或开始新的对话。", "این گفتگو خیلی طولانی شده است. آن را به تیم پشتیبانی بسپارید یا گفتگوی جدیدی شروع کنید."],
        ["bot.err.off"] = ["Ассистент сейчас недоступен. Вы можете написать команде поддержки через новое обращение.", "Asistan şu an kullanılamıyor. Ekibe yeni bir destek talebiyle yazabilirsiniz.", "The assistant is unavailable right now. You can write to the team with a new request.", "助手暂时不可用。你可以新建请求，直接联系团队。", "دستیار اکنون در دسترس نیست. می‌توانید با درخواست جدید به تیم پیام بدهید."],
        ["bot.err.invalid"] = ["Сообщение слишком длинное (не больше 1500 символов).", "Mesaj çok uzun (en fazla 1500 karakter).", "The message is too long (1500 characters at most).", "消息太长（最多 1500 个字符）。", "پیام خیلی طولانی است (حداکثر ۱۵۰۰ نویسه)."],
        ["bot.err.noteNeeded"] = ["Коротко опишите проблему, чтобы передать её команде.", "Ekibe aktarmak için sorunu kısaca yazın.", "Briefly describe the problem to hand it to the team.", "请简要描述问题，以便转交给团队。", "برای سپردن به تیم، مشکل را کوتاه شرح دهید."],
        ["err.noPlan"] = ["Тариф не активен. Выберите тариф, чтобы подключиться.", "Paketiniz aktif değil. Bağlanmak için bir paket seçin.", "Your plan isn’t active. Choose a plan to connect.", "你的套餐未生效。请选择套餐后再连接。", "طرح شما فعال نیست. برای اتصال، یک طرح انتخاب کنید."],
        ["err.quota"] = ["Лимит трафика на этот период исчерпан.", "Bu dönemin trafik kotası doldu.", "You’ve used this period’s traffic allowance.", "你已用完本周期的流量额度。", "سهمیهٔ ترافیک این دوره را مصرف کرده‌اید."],
        ["err.noServers"] = ["Сейчас нет доступных серверов. Попробуйте чуть позже.", "Şu anda uygun sunucu yok. Biraz sonra tekrar deneyin.", "No server is available right now. Please try again shortly.", "目前没有可用的服务器，请稍后重试。", "اکنون هیچ سروری در دسترس نیست. کمی بعد دوباره تلاش کنید."],
        ["err.network"] = ["Нет связи с сервером Colitu. Проверьте интернет.", "Colitu sunucusuna ulaşılamadı. İnternet bağlantınızı kontrol edin.", "Can’t reach Colitu. Check your internet connection.", "无法连接 Colitu。请检查网络连接。", "دسترسی به Colitu ممکن نیست. اتصال اینترنت خود را بررسی کنید."],
        ["err.refresh"] = ["Не удалось обновить сеанс. Проверьте интернет и попробуйте ещё раз.", "Oturum yenilenemedi. İnternet bağlantınızı kontrol edip tekrar deneyin.", "Couldn’t refresh your session. Check your internet connection and try again.", "无法刷新会话。请检查网络连接后重试。", "تازه‌سازی نشست ممکن نشد. اتصال اینترنت خود را بررسی کنید و دوباره تلاش کنید."],
        ["err.unreachable"] = ["Сервер не отвечает. Попробуйте другую локацию.", "Sunucu yanıt vermiyor. Başka bir konum deneyin.", "The server isn’t responding. Try another location.", "服务器没有响应。请尝试其他位置。", "سرور پاسخ نمی‌دهد. مکان دیگری را امتحان کنید."],
        ["err.admin"] = ["Для VPN нужны права администратора. Перезапустите Colitu от имени администратора.", "VPN için yönetici izni gerekiyor. Colitu’yu yönetici olarak yeniden başlatın.", "The VPN needs administrator rights. Restart Colitu as administrator.", "VPN 需要管理员权限。请以管理员身份重新启动 Colitu。", "VPN به دسترسی مدیر نیاز دارد. Colitu را با دسترسی مدیر دوباره اجرا کنید."],
        ["err.core"] = ["Файлы VPN повреждены. Переустановите Colitu.", "VPN dosyaları eksik. Colitu’yu yeniden kurun.", "VPN files are missing. Please reinstall Colitu.", "缺少 VPN 文件。请重新安装 Colitu。", "فایل‌های VPN یافت نشد. لطفاً Colitu را دوباره نصب کنید."],
        ["err.port"] = ["Локальный порт занят другой программой VPN или прокси. Закройте её и повторите.", "Yerel bağlantı noktası başka bir VPN/proxy programı tarafından kullanılıyor. Kapatıp tekrar deneyin.", "Another VPN or proxy app is using the local port. Close it and try again.", "另一个 VPN 或代理应用正在占用本地端口。请关闭它后重试。", "برنامهٔ VPN یا پروکسی دیگری از درگاه محلی استفاده می‌کند. آن را ببندید و دوباره تلاش کنید."],
        ["err.tun"] = ["Не удалось включить адаптер TUN. Попробуйте режим «Прокси».", "TUN bağdaştırıcısı açılamadı. Proxy modunu deneyin.", "The TUN adapter didn’t start. Try Proxy mode.", "TUN 适配器未能启动。请尝试代理模式。", "آداپتور TUN راه‌اندازی نشد. حالت پروکسی را امتحان کنید."],
        ["err.generic"] = ["Что-то пошло не так. Попробуйте ещё раз.", "Bir şeyler ters gitti. Tekrar deneyin.", "Something went wrong. Please try again.", "出了点问题，请重试。", "مشکلی پیش آمد. لطفاً دوباره تلاش کنید."],
        ["err.payment"] = ["Не удалось создать платёж. Попробуйте другой способ оплаты.", "Ödeme oluşturulamadı. Başka bir ödeme yöntemi deneyin.", "The payment couldn’t be created. Try another payment method.", "无法创建付款。请尝试其他付款方式。", "ایجاد پرداخت ممکن نشد. روش پرداخت دیگری را امتحان کنید."],
        ["err.reconnectFailed"] = ["Соединение потеряно. Нажмите «Подключить», чтобы восстановить.", "Bağlantı koptu. Yeniden bağlanmak için “Bağlan”a basın.", "The connection dropped. Press Connect to restore it.", "连接已断开。点击“连接”即可恢复。", "اتصال قطع شد. برای بازگرداندن آن، «اتصال» را بزنید."],
        ["info.reconnected"] = ["Соединение восстановлено", "Bağlantı yeniden kuruldu", "Connection restored", "连接已恢复", "اتصال دوباره برقرار شد"],
        ["info.disconnected"] = ["VPN отключён", "VPN bağlantısı kesildi", "VPN disconnected", "VPN 已断开", "VPN قطع شد"],
        ["warn.noInternet"] = ["Похоже, пропал интернет. VPN восстановится сам, как только связь вернётся.", "İnternet bağlantınız kesilmiş görünüyor. Bağlantı gelince VPN kendiliğinden devam edecek.", "Your internet connection seems to be down. The VPN will resume on its own once it is back.", "你的网络似乎已断开。网络恢复后，VPN 会自动继续。", "به نظر می‌رسد اینترنت شما قطع است. با بازگشت اینترنت، VPN خودکار ادامه می‌دهد."],
        ["warn.otherVpn"] = ["Похоже, запущен другой VPN (например, Clash). Если соединение не работает, отключите его.", "Başka bir VPN uygulaması (ör. Clash) açık görünüyor. Bağlantı çalışmazsa onu kapatın.", "Another VPN app (for example Clash) seems to be running. If the connection doesn’t work, turn it off.", "似乎有另一个 VPN 应用（例如 Clash）正在运行。如果连接不正常，请将其关闭。", "به نظر می‌رسد برنامهٔ VPN دیگری (مثلاً Clash) در حال اجراست. اگر اتصال کار نکرد، آن را خاموش کنید."],
        ["warn.proxyOtherUser"] = ["Приложение запущено от имени другой учётной записи Windows, поэтому прокси не защитит ваши программы. Используется режим TUN.", "Uygulama başka bir Windows hesabıyla çalışıyor; proxy sizin uygulamalarınızı korumaz. TUN modu kullanılıyor.", "The app runs as another Windows account, so the proxy would not protect your apps. Using TUN mode instead.", "应用正以另一个 Windows 账户运行，代理无法保护你的应用，因此改用 TUN 模式。", "برنامه با حساب ویندوز دیگری اجرا می‌شود و پروکسی از برنامه‌های شما محافظت نمی‌کند. به‌جای آن از حالت TUN استفاده می‌شود."],
        ["info.killSwitch"] = ["Соединение прервалось. Kill switch заблокировал интернет, переподключаемся…", "Bağlantı koptu. Kill switch interneti engelledi, yeniden bağlanılıyor…", "Connection lost. The kill switch blocked the internet while we reconnect…", "连接已断开。重新连接期间，断网保护已阻断网络…", "اتصال قطع شد. تا زمان اتصال مجدد، Kill Switch اینترنت را بسته است…"],
        ["err.killSwitch"] = ["Не удалось включить kill switch. Перезапустите Colitu от имени администратора.", "Kill switch açılamadı. Colitu’yu yönetici olarak yeniden başlatın.", "The kill switch couldn’t be turned on. Restart Colitu as administrator.", "无法开启断网保护。请以管理员身份重新启动 Colitu。", "Kill Switch روشن نشد. Colitu را با دسترسی مدیر دوباره اجرا کنید."],

        // Kill-switch service (2.6.0)
        ["warn.killSwitchFallback"] = ["Служба kill switch Colitu не запущена: защита работает только пока открыто приложение. Переустановите Colitu, чтобы она защищала и при сбое.", "Colitu kill switch hizmeti çalışmıyor: koruma yalnızca uygulama açıkken çalışır. Çökmelerde de korunmak için Colitu’yu yeniden kurun.", "The Colitu kill-switch service isn’t running, so protection only works while the app is open. Reinstall Colitu to stay protected if it crashes.", "Colitu 断网保护服务未运行，因此保护仅在应用打开时有效。请重新安装 Colitu，以便应用崩溃后仍受保护。", "سرویس Kill Switch در Colitu اجرا نمی‌شود، بنابراین محافظت فقط تا زمانی که برنامه باز است کار می‌کند. برای ماندن در محافظت پس از خرابی برنامه، Colitu را دوباره نصب کنید."],
        ["warn.killSwitchProxy"] = ["Kill switch включён в режиме прокси: программы, которые не используют системный прокси, остаются без интернета, пока VPN подключён.", "Kill switch proxy modunda açık: sistem proxy’sini kullanmayan uygulamalar VPN bağlıyken internete çıkamaz.", "The kill switch is on in proxy mode: apps that don’t use the system proxy have no internet while you’re protected.", "断网保护已在代理模式下开启：受保护期间，不使用系统代理的应用将无法上网。", "Kill Switch در حالت پروکسی روشن است: تا وقتی محافظت دارید، برنامه‌هایی که از پروکسی سیستم استفاده نمی‌کنند اینترنت ندارند."],
        ["info.killSwitchHeld"] = ["Colitu закрылся во время подключения, и kill switch не пускал трафик мимо VPN. Переподключаемся…", "Colitu bağlıyken kapandı; kill switch VPN dışı trafiği engelledi. Yeniden bağlanılıyor…", "Colitu closed while connected, so the kill switch kept traffic from leaving outside the VPN. Reconnecting…", "Colitu 在连接状态下关闭了，断网保护阻止了流量绕过 VPN 外泄。正在重新连接…", "Colitu در حال اتصال بسته شد و Kill Switch جلوی خروج ترافیک بیرون از VPN را گرفت. در حال اتصال مجدد…"],
        ["info.killSwitchRecovered"] = ["Colitu закрылся во время подключения, поэтому kill switch блокировал интернет, чтобы ничего не утекло. Интернет снова открыт.", "Colitu bağlıyken kapandığı için kill switch hiçbir şey sızmasın diye interneti engelledi. İnternet yeniden açık.", "Colitu closed while connected, so the kill switch blocked the internet to keep anything from leaking. The internet is open again.", "Colitu 在连接状态下关闭了，断网保护已阻断网络以防止信息泄漏。网络现已恢复。", "Colitu در حال اتصال بسته شد و Kill Switch برای جلوگیری از نشت اطلاعات، اینترنت را بست. اینترنت دوباره باز است."],
        ["home.sub.blockedFailed"] = ["Не удалось подключиться. Kill switch защищает ваш интернет; повторите попытку или отключитесь. Для страницы входа в Wi-Fi отеля или кафе временно отключите защиту.", "Bağlanılamadı. Kill switch internetinizi koruyor; yeniden deneyin ya da bağlantıyı kesin. Otel ya da kafe Wi-Fi’sinin giriş sayfası için korumayı geçici olarak kapatın.", "Couldn’t connect. The kill switch is protecting your internet; try again or disconnect. For a hotel or café Wi-Fi sign-in page, turn protection off for a moment.", "无法连接。断网保护正在保护你的网络；请重试或断开连接。如需登录酒店或咖啡馆的 Wi-Fi 页面，请暂时关闭保护。", "اتصال برقرار نشد. Kill Switch از اینترنت شما محافظت می‌کند؛ دوباره تلاش کنید یا اتصال را قطع کنید. برای صفحهٔ ورود Wi-Fi هتل یا کافه، محافظت را برای لحظه‌ای خاموش کنید."],
        ["home.sub.blockedCrash"] = ["Colitu закрылся во время подключения, и kill switch не пускает трафик мимо VPN. Переподключаемся автоматически — или отключите защиту.", "Colitu bağlıyken kapandı; kill switch VPN dışı trafiği engelliyor. Otomatik olarak yeniden bağlanıyoruz; isterseniz korumayı kapatın.", "Colitu closed while connected, and the kill switch is keeping traffic from leaving outside the VPN. We’re reconnecting automatically, or you can turn protection off.", "Colitu 在连接状态下关闭了，断网保护正在阻止流量绕过 VPN 外泄。我们正在自动重新连接，你也可以关闭保护。", "Colitu در حال اتصال بسته شد و Kill Switch جلوی خروج ترافیک بیرون از VPN را می‌گیرد. به‌طور خودکار دوباره وصل می‌شویم، یا می‌توانید محافظت را خاموش کنید."],
        ["settings.killSwitchLan"] = ["Локальная сеть при kill switch", "Kill switch açıkken yerel ağ", "Local network with the kill switch", "断网保护下的本地网络", "شبکهٔ محلی با Kill Switch"],
        ["settings.killSwitchLanHint"] = ["Принтеры, телевизоры и страница роутера остаются доступны, пока kill switch блокирует интернет.", "Kill switch interneti engellerken yazıcılar, TV’ler ve modem sayfası erişilebilir kalır.", "Printers, TVs and the router page stay reachable while the kill switch blocks the internet.", "断网保护阻断互联网时，打印机、电视和路由器页面仍可访问。", "وقتی Kill Switch اینترنت را می‌بندد، چاپگرها، تلویزیون‌ها و صفحهٔ روتر همچنان در دسترس می‌مانند."],

        // TUN by default and proxy coverage (2.6.0)
        ["tun.prompt.title"] = ["Защищать весь компьютер?", "Tüm bilgisayar korunsun mu?", "Protect the whole computer?", "保护整台电脑？", "از کل رایانه محافظت شود؟"],
        ["tun.prompt.body"] = ["Сейчас Colitu работает в режиме прокси: он защищает только программы, которые используют системный прокси. DNS-запросы, UDP/WebRTC, IPv6 и программы, которые игнорируют прокси, идут мимо VPN. Режим «Весь трафик (TUN)» защищает всё. Переключить?", "Colitu şu an proxy modunda: yalnızca sistem proxy’sini kullanan uygulamaları korur. DNS sorguları, UDP/WebRTC, IPv6 ve proxy’yi yok sayan uygulamalar VPN dışından gider. “Tüm trafik (TUN)” modu her şeyi korur. Geçilsin mi?", "Colitu runs in proxy mode, which only protects apps that use the system proxy. DNS lookups, UDP/WebRTC, IPv6 and apps that ignore the proxy go outside the VPN. “All traffic (TUN)” protects everything. Switch now?", "Colitu 正在代理模式下运行，该模式只保护使用系统代理的应用。DNS 查询、UDP/WebRTC、IPv6 以及不使用代理的应用都会绕过 VPN。“全部流量 (TUN)”可以保护所有内容。现在切换吗？", "Colitu در حالت پروکسی اجرا می‌شود که فقط از برنامه‌هایی که پروکسی سیستم را به کار می‌برند محافظت می‌کند. جستجوهای DNS، UDP/WebRTC، IPv6 و برنامه‌هایی که پروکسی را نادیده می‌گیرند بیرون از VPN می‌روند. «همهٔ ترافیک (TUN)» از همه‌چیز محافظت می‌کند. اکنون تغییر دهیم؟"],
        ["tun.prompt.switch"] = ["Перейти на весь трафик (TUN)", "Tüm trafiğe (TUN) geç", "Switch to all traffic (TUN)", "切换到全部流量 (TUN)", "تغییر به همهٔ ترافیک (TUN)"],
        ["tun.prompt.keep"] = ["Оставить режим прокси", "Proxy modunda kal", "Keep proxy mode", "保持代理模式", "ماندن در حالت پروکسی"],
        ["proxy.coverage"] = ["Режим прокси не защищает DNS-запросы, UDP/WebRTC, IPv6 и программы, которые игнорируют системный прокси.", "Proxy modu DNS sorgularını, UDP/WebRTC’yi, IPv6’yı ve sistem proxy’sini yok sayan uygulamaları korumaz.", "Proxy mode doesn’t protect DNS lookups, UDP/WebRTC, IPv6 or apps that ignore the system proxy.", "代理模式不保护 DNS 查询、UDP/WebRTC、IPv6，也不保护不使用系统代理的应用。", "حالت پروکسی از جستجوهای DNS، UDP/WebRTC، IPv6 و برنامه‌هایی که پروکسی سیستم را نادیده می‌گیرند محافظت نمی‌کند."],
        ["proxy.chip"] = ["Режим прокси: защита неполная", "Proxy modu: koruma kısmi", "Proxy mode: partial protection", "代理模式：部分保护", "حالت پروکسی: محافظت جزئی"],

        // Two-factor sign-in (2.6.0)
        ["mfa.kicker"] = ["ДВУХФАКТОРНАЯ ЗАЩИТА", "İKİ ADIMLI DOĞRULAMA", "TWO-FACTOR AUTHENTICATION", "两步验证", "تأیید دومرحله‌ای"],
        ["mfa.title"] = ["Введите код", "Kodu girin", "Enter your code", "输入验证码", "کد خود را وارد کنید"],
        ["mfa.sub"] = ["Откройте приложение-аутентификатор и введите 6-значный код для {email}.", "Doğrulama uygulamanızı açın ve {email} için 6 haneli kodu girin.", "Open your authenticator app and enter the 6-digit code for {email}.", "打开你的验证器应用，输入 {email} 的 6 位验证码。", "برنامهٔ احراز هویت خود را باز کنید و کد ۶ رقمی {email} را وارد کنید."],
        ["mfa.subRecovery"] = ["Введите один из резервных кодов, сохранённых при включении двухфакторной защиты. Каждый код действует один раз.", "İki adımlı doğrulamayı açarken kaydettiğiniz kurtarma kodlarından birini girin. Her kod bir kez kullanılabilir.", "Enter one of the recovery codes you saved when you turned on two-factor authentication. Each code works once.", "输入你在开启两步验证时保存的一个恢复码。每个恢复码只能使用一次。", "یکی از کدهای بازیابی را که هنگام روشن‌کردن تأیید دومرحله‌ای ذخیره کردید وارد کنید. هر کد فقط یک بار کار می‌کند."],
        ["mfa.code"] = ["Код из приложения", "Uygulamadaki kod", "Code from the app", "应用中的验证码", "کد برنامه"],
        ["mfa.recoveryCode"] = ["Резервный код", "Kurtarma kodu", "Recovery code", "恢复码", "کد بازیابی"],
        ["mfa.useRecovery"] = ["Использовать резервный код", "Kurtarma kodu kullan", "Use a recovery code", "使用恢复码", "استفاده از کد بازیابی"],
        ["mfa.useApp"] = ["Ввести код из приложения", "Uygulamadaki kodu gir", "Use the code from the app", "使用应用中的验证码", "استفاده از کد برنامه"],
        ["mfa.submit"] = ["Подтвердить и войти", "Doğrula ve giriş yap", "Verify and sign in", "验证并登录", "تأیید و ورود"],
        ["mfa.back"] = ["Вернуться ко входу", "Girişe dön", "Back to sign in", "返回登录", "بازگشت به ورود"],
        ["mfa.hint"] = ["Двухфакторная защита включается и отключается на colitu.com.", "İki adımlı doğrulama colitu.com üzerinden açılıp kapatılır.", "Two-factor authentication is turned on and off at colitu.com.", "两步验证需在 colitu.com 开启或关闭。", "تأیید دومرحله‌ای را در colitu.com روشن یا خاموش می‌کنید."],
        ["mfa.err.format"] = ["Введите все 6 цифр кода.", "Kodun 6 hanesini de girin.", "Enter all 6 digits of the code.", "请输入验证码的全部 6 位数字。", "هر ۶ رقم کد را وارد کنید."],
        ["mfa.err.recoveryFormat"] = ["Введите резервный код полностью.", "Kurtarma kodunu eksiksiz girin.", "Enter the whole recovery code.", "请输入完整的恢复码。", "کد بازیابی را کامل وارد کنید."],
        ["mfa.err.invalid"] = ["Неверный код. Проверьте приложение и попробуйте снова.", "Kod hatalı. Uygulamayı kontrol edip tekrar deneyin.", "That code isn’t right. Check the app and try again.", "验证码不正确。请检查应用后重试。", "این کد درست نیست. برنامه را بررسی کنید و دوباره تلاش کنید."],
        ["mfa.err.expired"] = ["Время на ввод кода истекло. Войдите снова.", "Kod girme süresi doldu. Lütfen tekrar giriş yapın.", "The time to enter the code ran out. Please sign in again.", "输入验证码的时间已过。请重新登录。", "زمان وارد کردن کد تمام شد. لطفاً دوباره وارد شوید."],
        ["mfa.err.update"] = ["Для этого аккаунта нужна новая версия Colitu. Обновите приложение.", "Bu hesap için Colitu’nun yeni sürümü gerekiyor. Uygulamayı güncelleyin.", "This account needs a newer version of Colitu. Please update the app.", "此账户需要更新版本的 Colitu。请更新应用。", "این حساب به نسخهٔ جدیدتری از Colitu نیاز دارد. لطفاً برنامه را به‌روزرسانی کنید."],

        // Trial end (2.6.0)
        ["trial.ends"] = ["Пробный период закончится через {days}.", "Deneme sürenizin bitmesine {days} kaldı.", "Your trial ends in {days}.", "你的试用将在 {days} 后结束。", "دورهٔ آزمایشی شما {days} دیگر تمام می‌شود."],
        ["trial.freePlan"] = ["Бесплатный", "Ücretsiz", "free", "免费", "رایگان"],
        ["trial.move"] = ["Затем вы перейдёте на тариф «{plan}»", "Ardından {plan} planına geçeceksiniz", "You’ll move to the {plan} plan", "你将转入 {plan} 套餐", "به طرح {plan} منتقل می‌شوید"],
        ["trial.moveDetails"] = ["Затем вы перейдёте на тариф «{plan}» ({details})", "Ardından {plan} planına geçeceksiniz ({details})", "You’ll move to the {plan} plan ({details})", "你将转入 {plan} 套餐（{details}）", "به طرح {plan} منتقل می‌شوید ({details})"],
        ["trial.gbMonth"] = ["{gb} ГБ в месяц", "ayda {gb} GB", "{gb} GB a month", "每月 {gb} GB", "ماهانه {gb} گیگابایت"],
        ["trial.paused"] = ["устройство, которым вы пользовались последним, останется активным, остальные будут приостановлены (без выхода из аккаунта)", "en son kullandığınız cihaz aktif kalır, diğerleri duraklatılır (oturumları kapanmaz)", "the device you used most recently stays active, the others are paused (not signed out)", "你最近使用最多的设备保持启用，其他设备将被暂停（不会退出登录）", "دستگاهی که اخیراً بیشتر استفاده کرده‌اید فعال می‌ماند و بقیه متوقف می‌شوند (از حساب خارج نمی‌شوند)"],
        ["err.disposableEmail"] = ["Временные адреса электронной почты не принимаются. Используйте постоянный адрес (например, Gmail, Outlook, Яндекс).", "Geçici e-posta adresleri kabul edilmiyor. Lütfen kalıcı bir e-posta adresi kullanın (ör. Gmail, Outlook, Yandex).", "Temporary e-mail addresses are not accepted. Please use a permanent address (e.g. Gmail, Outlook, Yandex).", "不接受临时邮箱地址。请使用长期有效的邮箱（例如 Gmail、Outlook、Yandex）。", "نشانی‌های ایمیل موقت پذیرفته نمی‌شوند. لطفاً از نشانی دائمی (مثلاً Gmail، Outlook، Yandex) استفاده کنید."],
        ["err.passwordBreached"] = ["Этот пароль встречается в известной утечке данных. Выберите другой пароль.", "Bu şifre bilinen bir veri sızıntısında yer alıyor. Lütfen başka bir şifre seçin.", "This password appears in a known data breach. Please choose a different one.", "此密码出现在已知的数据泄露中。请换一个密码。", "این رمز عبور در یک نشت اطلاعاتی شناخته‌شده دیده شده است. لطفاً رمز دیگری انتخاب کنید."],
        ["err.signupIpLimit"] = ["С этой сети недавно создано слишком много аккаунтов. Попробуйте позже.", "Bu ağdan kısa sürede çok fazla hesap oluşturuldu. Lütfen daha sonra tekrar deneyin.", "Too many accounts were created from this network recently. Please try again later.", "最近从此网络创建的账户过多。请稍后重试。", "اخیراً از این شبکه حساب‌های زیادی ساخته شده است. لطفاً بعداً دوباره تلاش کنید."],
        ["err.accountUnderReview"] = ["Ваш аккаунт проверяется нашей командой. Вы сможете войти после одобрения; если есть вопросы, напишите в поддержку.", "Hesabınız ekibimiz tarafından inceleniyor. Onaylandığında giriş yapabilirsiniz; sorunuz varsa destekle iletişime geçin.", "Your account is being reviewed by our team. You can sign in once it is approved; contact support if you have questions.", "你的账户正在由我们的团队审核。审核通过后即可登录；如有疑问，请联系客服。", "حساب شما در حال بررسی توسط تیم ماست. پس از تأیید می‌توانید وارد شوید؛ اگر پرسشی دارید با پشتیبانی تماس بگیرید."],
        ["err.signupBlocked"] = ["Эта регистрация не принята. Если вы считаете это ошибкой, напишите в поддержку.", "Bu kayıt kabul edilmedi. Bir hata olduğunu düşünüyorsanız destekle iletişime geçin.", "This sign-up was not accepted. If you think this is a mistake, contact support.", "此次注册未被接受。如果你认为这是误判，请联系客服。", "این ثبت‌نام پذیرفته نشد. اگر فکر می‌کنید اشتباه شده است، با پشتیبانی تماس بگیرید."],
        ["err.accountBlocked"] = ["Этот аккаунт заблокирован. Если вы считаете это ошибкой, напишите в поддержку.", "Bu hesap engellendi. Bir hata olduğunu düşünüyorsanız destekle iletişime geçin.", "This account has been blocked. If you think this is a mistake, contact support.", "此账户已被封禁。如果你认为这是误判，请联系客服。", "این حساب مسدود شده است. اگر فکر می‌کنید اشتباه شده است، با پشتیبانی تماس بگیرید."],
        ["auth.registerEmailHint"] = ["Временные (одноразовые) адреса электронной почты не принимаются.", "Geçici (tek kullanımlık) e-posta adresleri kabul edilmez.", "Temporary (disposable) e-mail addresses are not accepted.", "不接受临时（一次性）邮箱地址。", "نشانی‌های ایمیل موقت (یک‌بارمصرف) پذیرفته نمی‌شوند."],
        ["mfa.loginEmailTitle"] = ["Это вы входите в аккаунт?", "Bu giriş siz misiniz?", "Is this you signing in?", "是你本人在登录吗？", "این شما هستید که وارد می‌شوید؟"],
        ["mfa.codeEmail"] = ["Код из письма", "E-postadaki kod", "Code from the e-mail", "邮件中的验证码", "کد موجود در ایمیل"],
        ["mfa.loginEmailBody"] = ["Вы входите из необычного места. Введите 6-значный код, который мы отправили на вашу почту.", "Alışılmadık bir konumdan giriş yapıyorsunuz. E-posta adresinize gönderdiğimiz 6 haneli kodu girin.", "You are signing in from an unusual location. Enter the 6-digit code we sent to your e-mail.", "你正在从不常见的位置登录。请输入我们发送到你邮箱的 6 位验证码。", "از یک مکان غیرمعمول وارد می‌شوید. کد ۶ رقمی را که به ایمیلتان فرستادیم وارد کنید."],
        ["mfa.attemptsLeft"] = ["Осталось попыток: {n}.", "Kalan deneme: {n}.", "Attempts left: {n}.", "剩余尝试次数：{n}。", "تعداد تلاش‌های باقی‌مانده: {n}."],
        ["account.paused"] = ["ПРИОСТАНОВЛЕНО", "DURAKLATILDI", "PAUSED", "已暂停", "متوقف‌شده"],
        ["account.activate"] = ["Активировать", "Etkinleştir", "Activate", "启用", "فعال‌سازی"],
        ["account.activated"] = ["Устройство «{name}» снова активно.", "“{name}” yeniden aktif.", "“{name}” is active again.", "“{name}”已重新启用。", "«{name}» دوباره فعال شد."],
        ["account.company"] = ["Компания: {name}", "Şirket: {name}", "Company: {name}", "公司：{name}", "شرکت: {name}"],
        ["account.security"] = ["Двухфакторная защита", "İki adımlı doğrulama", "Two-factor authentication", "两步验证", "تأیید دومرحله‌ای"],
        ["trial.dismiss"] = ["Скрыть до завтра", "Yarına kadar gizle", "Hide until tomorrow", "明天前不再显示", "پنهان تا فردا"],
        ["trial.keep"] = ["Сохранить безлимит", "Sınırsızı koru", "Keep unlimited", "保留不限流量", "حفظ نامحدود"],

        // Paused device (2.6.0)
        ["paused.kicker"] = ["УСТРОЙСТВО ПРИОСТАНОВЛЕНО", "CİHAZ DURAKLATILDI", "DEVICE PAUSED", "设备已暂停", "دستگاه متوقف شد"],
        ["paused.title"] = ["Это устройство приостановлено", "Bu cihaz duraklatıldı", "This device is paused", "此设备已暂停", "این دستگاه متوقف شده است"],
        ["paused.body"] = ["Ваш тариф позволяет {limit}.", "Paketiniz {limit} kullanımına izin veriyor.", "Your plan allows {limit}.", "你的套餐最多允许 {limit}。", "طرح شما حداکثر {limit} را مجاز می‌داند."],
        ["paused.bodyNoLimit"] = ["На вашем тарифе уже используется максимум устройств.", "Paketinizdeki cihaz sınırı dolu.", "Your plan’s devices are all in use.", "你套餐内的设备名额已全部用完。", "همهٔ دستگاه‌های طرح شما در حال استفاده‌اند."],
        ["paused.active"] = ["Активно: {names}.", "Aktif: {names}.", "Active: {names}.", "已启用：{names}。", "فعال: {names}."],
        ["paused.short"] = ["Это устройство приостановлено: на вашем тарифе уже используется максимум устройств.", "Bu cihaz duraklatıldı: paketinizin cihaz sınırı dolu.", "This device is paused: your plan’s devices are all in use.", "此设备已暂停：你套餐内的设备名额已全部用完。", "این دستگاه متوقف شده است: همهٔ دستگاه‌های طرح شما در حال استفاده‌اند."],
        ["paused.useThis"] = ["Использовать это устройство", "Bunun yerine bu cihazı kullan", "Use this device instead", "改用此设备", "استفاده از این دستگاه"],
        ["paused.useThisHint"] = ["Другое устройство будет приостановлено вместо этого.", "Onun yerine diğer cihaz duraklatılır.", "The other device is paused in its place.", "另一台设备将被暂停以腾出名额。", "دستگاه دیگر به‌جای آن متوقف می‌شود."],
        ["paused.premium"] = ["Получить Premium", "Premium’a geç", "Get Premium", "获取 Premium", "دریافت Premium"],
        ["paused.signOut"] = ["Выйти из аккаунта", "Çıkış yap", "Sign out", "退出登录", "خروج"],
        ["paused.noDeviceId"] = ["Это устройство ещё не добавлено в аккаунт. Отключите другое устройство на colitu.com и попробуйте снова.", "Bu cihaz henüz hesaba eklenmedi. colitu.com’da başka bir cihazı kaldırıp tekrar deneyin.", "This device isn’t on the account yet. Remove another device at colitu.com and try again.", "此设备尚未加入账户。请在 colitu.com 移除另一台设备后重试。", "این دستگاه هنوز در حساب نیست. در colitu.com دستگاه دیگری را حذف کنید و دوباره تلاش کنید."],
        ["paused.notice"] = ["Это устройство приостановлено: тариф позволяет меньше устройств. VPN отключён.", "Bu cihaz duraklatıldı: paketiniz daha az cihaza izin veriyor. VPN kapatıldı.", "This device is paused because your plan allows fewer devices. The VPN is off.", "由于你的套餐允许的设备数量较少，此设备已被暂停。VPN 已关闭。", "این دستگاه متوقف شده است چون طرح شما دستگاه کمتری را مجاز می‌داند. VPN خاموش است."],
        ["paused.activated"] = ["Теперь это устройство активно.", "Bu cihaz artık aktif.", "This device is active now.", "此设备现已启用。", "این دستگاه اکنون فعال است."],

        // Split tunnelling (2.6.0)
        ["split.title"] = ["Раздельное туннелирование", "Bölünmüş tünel", "Split tunneling", "分流", "تونل‌زنی تقسیم‌شده"],
        ["split.hint"] = ["Выберите программы, сайты и IP-адреса, которые идут мимо VPN — или только они идут через VPN. Режим приватности работает отдельно.", "VPN dışından gidecek uygulamaları, siteleri ve IP adreslerini seçin; ya da yalnızca bunlar VPN’den geçsin. Gizlilik modu bundan bağımsızdır.", "Choose apps, sites and IP addresses that skip the VPN, or the only ones that use it. Privacy mode works separately.", "选择绕过 VPN 的应用、网站和 IP 地址，或只让它们使用 VPN。隐私模式独立运作。", "برنامه‌ها، سایت‌ها و نشانی‌های IP را انتخاب کنید که از VPN عبور نکنند، یا تنها آن‌ها از VPN استفاده کنند. حالت حریم خصوصی جداگانه کار می‌کند."],
        ["split.mode.off"] = ["Выкл.", "Kapalı", "Off", "关闭", "خاموش"],
        ["split.mode.bypass"] = ["Выбранное мимо VPN", "Seçilenler VPN dışında", "Selected bypass VPN", "所选项绕过 VPN", "موارد انتخاب‌شده بدون VPN"],
        ["split.mode.only"] = ["Только выбранное через VPN", "Yalnızca seçilenler VPN’de", "Only selected use VPN", "仅所选项使用 VPN", "فقط موارد انتخاب‌شده با VPN"],
        ["split.mode.bypassHint"] = ["Выбранные программы и сайты идут мимо VPN, всё остальное — через VPN.", "Seçilen uygulamalar ve siteler VPN dışından, geri kalan her şey VPN’den gider.", "Selected apps and sites bypass the VPN; everything else uses it.", "所选应用和网站绕过 VPN，其余全部使用 VPN。", "برنامه‌ها و سایت‌های انتخاب‌شده از VPN عبور نمی‌کنند؛ بقیه از VPN استفاده می‌کنند."],
        ["split.mode.onlyHint"] = ["Через VPN идут только выбранные программы и сайты, всё остальное — напрямую.", "Yalnızca seçilen uygulamalar ve siteler VPN’den, geri kalan her şey doğrudan gider.", "Only the selected apps and sites use the VPN; everything else goes direct.", "只有所选应用和网站使用 VPN，其余全部直连。", "فقط برنامه‌ها و سایت‌های انتخاب‌شده از VPN استفاده می‌کنند؛ بقیه مستقیم وصل می‌شوند."],
        ["split.apps"] = ["Программы", "Uygulamalar", "Apps", "应用", "برنامه‌ها"],
        ["split.sites"] = ["Сайты", "Siteler", "Sites", "网站", "سایت‌ها"],
        ["split.ips"] = ["IP-адреса и подсети", "IP adresleri ve ağlar", "IP addresses and ranges", "IP 地址和范围", "نشانی‌ها و محدوده‌های IP"],
        ["split.addApp"] = ["Добавить программу…", "Uygulama ekle…", "Add app…", "添加应用…", "افزودن برنامه…"],
        ["split.add"] = ["Добавить", "Ekle", "Add", "添加", "افزودن"],
        ["split.remove"] = ["Убрать", "Kaldır", "Remove", "移除", "حذف"],
        ["split.domainHint"] = ["example.com (вместе с поддоменами)", "example.com (alt alan adlarıyla)", "example.com (subdomains included)", "example.com（包含子域名）", "example.com (با زیردامنه‌ها)"],
        ["split.ipHint"] = ["203.0.113.5 или 198.51.100.0/24", "203.0.113.5 veya 198.51.100.0/24", "203.0.113.5 or 198.51.100.0/24", "203.0.113.5 或 198.51.100.0/24", "203.0.113.5 یا 198.51.100.0/24"],
        ["split.err.domain"] = ["Введите адрес сайта, например example.com.", "Bir site adresi girin, ör. example.com.", "Enter a site address such as example.com.", "请输入网站地址，例如 example.com。", "نشانی سایت را مثل example.com وارد کنید."],
        ["split.err.ip"] = ["Введите IP-адрес или подсеть, например 198.51.100.0/24.", "Bir IP adresi veya ağ girin, ör. 198.51.100.0/24.", "Enter an IP address or range such as 198.51.100.0/24.", "请输入 IP 地址或范围，例如 198.51.100.0/24。", "یک نشانی یا محدودهٔ IP مثل 198.51.100.0/24 وارد کنید."],
        ["split.err.app"] = ["Выберите файл программы (.exe).", "Bir uygulama dosyası (.exe) seçin.", "Choose a program file (.exe).", "请选择程序文件 (.exe)。", "یک فایل برنامه (.exe) انتخاب کنید."],
        ["split.err.limit"] = ["Список заполнен.", "Liste dolu.", "The list is full.", "列表已满。", "فهرست پر است."],
        ["split.proxyNote"] = ["В режиме прокси программы разделить нельзя: прокси не различает программы. Работают сайты и IP-адреса; для программ включите режим «Весь трафик (TUN)».", "Proxy modunda uygulamalar ayrılamaz: proxy uygulamaları ayırt edemez. Siteler ve IP adresleri çalışır; uygulamalar için “Tüm trafik (TUN)” modunu açın.", "Apps can’t be split in proxy mode: a proxy can’t tell apps apart. Sites and IP addresses work; switch to “All traffic (TUN)” for apps.", "代理模式下无法按应用分流：代理无法区分不同应用。网站和 IP 地址可用；如需按应用分流，请切换到“全部流量 (TUN)”。", "در حالت پروکسی نمی‌توان برنامه‌ها را جدا کرد: پروکسی برنامه‌ها را از هم تشخیص نمی‌دهد. سایت‌ها و نشانی‌های IP کار می‌کنند؛ برای برنامه‌ها به «همهٔ ترافیک (TUN)» بروید."],
        ["split.appsNote"] = ["Программы определяются по имени файла: другая программа с таким же именем (например, Update.exe) тоже будет учтена.", "Uygulamalar dosya adıyla eşleştirilir: aynı ada sahip başka bir program (ör. Update.exe) da bu listeye dahil olur.", "Apps are matched by file name: another program with the same name (for example Update.exe) is treated the same way.", "应用按文件名匹配：同名的其他程序（例如 Update.exe）会被同样处理。", "برنامه‌ها بر اساس نام فایل تطبیق داده می‌شوند: برنامهٔ دیگری با همان نام (مثلاً Update.exe) هم همین‌طور رفتار می‌شود."],
        ["split.empty"] = ["Пока ничего не добавлено.", "Henüz bir şey eklenmedi.", "Nothing added yet.", "还没有添加任何内容。", "هنوز چیزی اضافه نشده است."],
        ["split.chip.bypass"] = ["Раздельное туннелирование: мимо VPN — {n}", "Bölünmüş tünel: VPN dışında {n}", "Split tunneling on: {n} outside VPN", "分流已开启：{n} 项不经 VPN", "تونل‌زنی تقسیم‌شده روشن: {n} مورد بیرون از VPN"],
        ["split.chip.only"] = ["Раздельное туннелирование: через VPN только {n}", "Bölünmüş tünel: yalnızca {n} VPN’de", "Split tunneling on: only {n} use VPN", "分流已开启：仅 {n} 项使用 VPN", "تونل‌زنی تقسیم‌شده روشن: فقط {n} مورد با VPN"],
        ["entry.one"] = ["{n} программа или сайт", "{n} uygulama/site", "{n} app or site", "{n} 个应用或网站", "{n} برنامه یا سایت"],
        ["entry.few"] = ["{n} программы или сайта", "{n} uygulama/site", "{n} apps/sites", "{n} 个应用或网站", "{n} برنامه یا سایت"],
        ["entry.many"] = ["{n} программ или сайтов", "{n} uygulama/site", "{n} apps/sites", "{n} 个应用或网站", "{n} برنامه یا سایت"],

        // Multihop routes and rotating IP (2.6.0)
        ["multihop.section"] = ["Мультихоп", "Çoklu atlama", "Multihop", "多跳", "چندگام (Multihop)"],
        ["multihop.sectionHint"] = ["Трафик идёт через два сервера: сначала входной, затем выходной. Работает только по VLESS; пинг показан до входного сервера.", "Trafik iki sunucudan geçer: önce giriş, sonra çıkış. Yalnızca VLESS ile çalışır; ping yalnızca giriş sunucusu içindir.", "Traffic passes two servers: the entry first, then the exit. Works over VLESS only; ping is measured to the entry server.", "流量会经过两台服务器：先入口，再出口。仅适用于 VLESS；延迟按入口服务器测量。", "ترافیک از دو سرور می‌گذرد: اول ورودی، بعد خروجی. فقط روی VLESS کار می‌کند؛ پینگ تا سرور ورودی اندازه‌گیری می‌شود."],
        ["multihop.ping"] = ["Оценка · +1 узел", "Tahmini · +1 atlama", "Estimated · +1 hop", "预估 · 多 1 跳", "تخمینی · ۱ گام بیشتر"],
        ["multihop.sub"] = ["Двойной VPN · {entry} → {exit}", "Çift VPN · {entry} → {exit}", "Double VPN · {entry} → {exit}", "双重 VPN · {entry} → {exit}", "VPN دوگانه · {entry} ← {exit}"],
        ["multihop.home"] = ["Вход {entry} → выход {exit}", "Giriş {entry} → çıkış {exit}", "Entry {entry} → Exit {exit}", "入口 {entry} → 出口 {exit}", "ورودی {entry} ← خروجی {exit}"],
        ["multihop.gone"] = ["Этот маршрут больше недоступен. Список обновлён — выберите другой.", "Bu rota artık kullanılamıyor. Liste yenilendi; başka bir tane seçin.", "This route is no longer available. The list was refreshed; pick another one.", "此线路已不可用。列表已刷新，请选择其他线路。", "این مسیر دیگر در دسترس نیست. فهرست تازه شد؛ مسیر دیگری انتخاب کنید."],
        ["multihop.needsVless"] = ["Мультихоп работает только по VLESS, а этот маршрут не предлагает такой транспорт.", "Çoklu atlama yalnızca VLESS ile çalışır, bu rota böyle bir aktarım sunmuyor.", "Multihop works over VLESS only, and this route offers no VLESS transport.", "多跳仅适用于 VLESS，而此线路没有提供 VLESS 传输。", "Multihop فقط روی VLESS کار می‌کند و این مسیر انتقال VLESS ندارد."],
        ["rotation.title"] = ["Меняющийся IP", "Dönen IP", "Rotating IP", "轮换 IP", "IP چرخشی"],
        ["rotation.hint"] = ["IP-адрес и страна выхода меняются по расписанию без отключения от VPN. Уже открытые соединения остаются на прежнем выходе. Работает только по VLESS.", "Çıkış IP adresiniz ve ülkeniz bağlantı kesilmeden belirli aralıklarla değişir. Açık bağlantılar eski çıkışta kalır. Yalnızca VLESS ile çalışır.", "Your exit IP address and country change on a schedule without disconnecting. Open connections stay on their old exit. Works over VLESS only.", "你的出口 IP 地址和国家会按计划自动更换，无需断开连接。已建立的连接会继续使用原出口。仅适用于 VLESS。", "نشانی IP و کشور خروجی شما طبق برنامه و بدون قطع اتصال عوض می‌شود. اتصال‌های باز روی خروجی قبلی می‌مانند. فقط روی VLESS کار می‌کند."],
        ["rotation.off"] = ["Выкл.", "Kapalı", "Off", "关闭", "خاموش"],
        ["rotation.minutes"] = ["{n} мин", "{n} dk", "{n} min", "{n} 分钟", "{n} دقیقه"],
        ["rotation.countries"] = ["Страны выхода", "Çıkış ülkeleri", "Exit countries", "出口国家", "کشورهای خروجی"],
        ["rotation.countriesHint"] = ["Отметьте минимум две страны. Россия не входит в набор по умолчанию.", "En az iki ülke işaretleyin. Rusya varsayılan sette yer almaz.", "Tick at least two countries. Russia is not in the default set.", "至少勾选两个国家。默认不包含俄罗斯。", "دست‌کم دو کشور را علامت بزنید. روسیه در مجموعهٔ پیش‌فرض نیست."],
        ["rotation.err.few"] = ["Отметьте минимум две страны.", "En az iki ülke işaretleyin.", "Tick at least two countries.", "请至少勾选两个国家。", "دست‌کم دو کشور را علامت بزنید."],
        ["rotation.needsVless"] = ["Смена IP работает только по VLESS, а этот сервер не предлагает такой транспорт.", "IP değişimi yalnızca VLESS ile çalışır, bu sunucu böyle bir aktarım sunmuyor.", "Rotation needs VLESS, and this server offers no VLESS transport.", "轮换需要 VLESS，而此服务器没有提供 VLESS 传输。", "چرخش به VLESS نیاز دارد و این سرور انتقال VLESS ندارد."],
        ["rotation.home"] = ["Выход: {exit} · смена через {time}", "Çıkış: {exit} · değişimine {time}", "Exit: {exit} · changes in {time}", "出口：{exit} · {time}后更换", "خروجی: {exit} · تغییر تا {time} دیگر"],
        ["rotation.homeDue"] = ["Выход: {exit} · меняется сейчас", "Çıkış: {exit} · şimdi değişiyor", "Exit: {exit} · changing now", "出口：{exit} · 正在更换", "خروجی: {exit} · در حال تغییر"],
        ["account.manualConfig"] = ["Ручная настройка", "Manuel yapılandırma", "Manual configuration", "手动配置", "پیکربندی دستی"],

        // Tray
        ["tray.open"] = ["Открыть Colitu", "Colitu’yu aç", "Open Colitu", "打开 Colitu", "باز کردن Colitu"],
        ["tray.connect"] = ["Подключить", "Bağlan", "Connect", "连接", "اتصال"],
        ["tray.disconnect"] = ["Отключить", "Bağlantıyı kes", "Disconnect", "断开", "قطع اتصال"],
        ["tray.exit"] = ["Выйти из Colitu", "Colitu’dan çık", "Quit Colitu", "退出 Colitu", "خروج از Colitu"],
        ["tray.hidden"] = ["Colitu работает в трее. Защита остаётся включённой.", "Colitu tepside çalışıyor. Koruma açık kalır.", "Colitu is still running in the tray. Protection stays on.", "Colitu 仍在托盘中运行，保护保持开启。", "Colitu همچنان در سینی اجرا می‌شود. محافظت روشن می‌ماند."],
        // Adaptive Connect: "best server" moves on when a server carries no traffic on this network.
        ["connect.tryingOther"] = ["Пробуем другой сервер…", "Başka sunucu deneniyor…", "Trying another server…", "正在尝试其他服务器…", "در حال امتحان سرور دیگر…"],
        ["server.autoNow"] = ["Сейчас: {server}", "Şu an: {server}", "Now: {server}", "当前：{server}", "اکنون: {server}"],
        // A server the user picked where every transport failed: one tap to the automatic choice.
        ["connect.tryFastest"] = ["Попробовать самый быстрый сервер", "En hızlı sunucuyu dene", "Try the fastest server", "尝试最快的服务器", "امتحان سریع‌ترین سرور"],
        // Simple / Advanced mode.
        ["mode.simple"] = ["Простой режим", "Basit mod", "Simple mode", "简易模式", "حالت ساده"],
        ["mode.advanced"] = ["Расширенный режим", "Gelişmiş mod", "Advanced mode", "高级模式", "حالت پیشرفته"],
        ["mode.advancedHint"] = ["Раздельное туннелирование, выбор протокола и другое", "Bölünmüş tünel, protokol seçimi ve diğer ayarlar", "Split tunnelling, protocol choice and more", "分流、协议选择等更多功能", "تونل‌زنی تقسیم‌شده، انتخاب پروتکل و موارد بیشتر"],
        ["mode.advancedOn"] = ["Расширенный режим включён", "Gelişmiş mod açıldı", "Advanced mode is on", "高级模式已开启", "حالت پیشرفته روشن است"],
        ["mode.simpleOn"] = ["Простой режим включён", "Basit mod açıldı", "Simple mode is on", "简易模式已开启", "حالت ساده روشن است"],
        ["mode.advancedSettingsOn"] = ["Включены расширенные настройки", "Gelişmiş ayarlar etkin", "Advanced settings on", "高级设置已开启", "تنظیمات پیشرفته روشن است"],
        // Warm spare: a second path inside the core.
        ["settings.warmSpare"] = ["Резервный канал", "Yedek hat", "Warm spare", "热备线路", "مسیر آمادهٔ پشتیبان"],
        ["settings.cslAlpha"] = ["Непрерывная сессия (альфа)", "Kesintisiz oturum (alpha)", "Seamless session (alpha)", "无缝会话（alpha）", "نشست بدون وقفه (آلفا)"],
        ["settings.cslAlphaHint"] = ["Открытые загрузки и звонки по TCP не обрываются, когда меняется канал или сеть. Работает только на тестовых серверах", "Hat veya ağ değişince açık TCP indirmeleri ve görüşmeler kopmaz. Yalnızca test sunucularında çalışır", "Open TCP downloads and calls keep going when the path or the network changes. Works on test servers only", "路径或网络变化时，进行中的 TCP 下载和通话会继续。仅适用于测试服务器", "هنگام تغییر مسیر یا شبکه، دانلودهای TCP و تماس‌های باز ادامه می‌یابند. فقط روی سرورهای آزمایشی کار می‌کند"],
        ["settings.warmSpareHint"] = ["Второй путь готов заранее: если основной пропадёт, трафик перейдёт на него за несколько секунд без переподключения", "İkinci bir yol hazır bekler: ana yol koparsa trafik birkaç saniyede, yeniden bağlanmadan ona geçer", "A second path stands ready: if the main one drops, traffic moves to it within seconds, without reconnecting", "备用线路随时待命：主线路断开时，流量会在几秒内切换过去，无需重新连接", "یک مسیر دوم آماده است: اگر مسیر اصلی قطع شود، ترافیک ظرف چند ثانیه و بدون اتصال مجدد به آن منتقل می‌شود"],
        // In-app notice banner (close button label).
        ["notice.close"] = ["Закрыть уведомление", "Bildirimi kapat", "Dismiss notice", "关闭通知", "بستن اعلان"],
        // Anonymous statistics and in-app surveys
        ["settings.stats"] = ["Анонимная статистика использования", "Anonim kullanım istatistikleri", "Anonymous usage statistics", "匿名使用统计", "آمار استفادهٔ ناشناس"],
        ["settings.statsHint"] = ["Отправляются только общие числа (успех подключений, время, используемые функции); аккаунт, устройство, IP и посещённые сайты не отправляются.", "Yalnızca toplu sayılar (bağlantı başarısı, süreler, kullanılan özellikler) gönderilir; hesap, cihaz, IP veya ziyaret edilen siteler gönderilmez.", "Only totals are sent (connection success, durations, features used); your account, device, IP or the sites you visit are never sent.", "仅发送汇总数据（连接成功率、时长、使用的功能）；绝不会发送你的账户、设备、IP 或你访问的网站。", "فقط مجموع‌ها ارسال می‌شوند (موفقیت اتصال، مدت‌ها، قابلیت‌های استفاده‌شده)؛ حساب، دستگاه، IP یا سایت‌هایی که می‌بینید هرگز ارسال نمی‌شوند."],
        ["settings.surveys"] = ["Показывать опросы", "Anketleri göster", "Show surveys", "显示问卷", "نمایش نظرسنجی‌ها"],
        ["settings.surveysHint"] = ["Короткий опрос о поддержке или продукте — не чаще раза в день, его можно закрыть.", "Destek veya ürün hakkında kısa bir anket; en fazla günde bir kez gösterilir, kapatılabilir.", "A short question about support or the product, at most once a day; you can close it.", "关于支持或产品的简短问题，每天最多一次；你可以关闭。", "یک پرسش کوتاه دربارهٔ پشتیبانی یا محصول، حداکثر روزی یک بار؛ می‌توانید آن را ببندید."],
        ["survey.kicker"] = ["ОПРОС", "ANKET", "SURVEY", "问卷", "نظرسنجی"],
        ["survey.send"] = ["Отправить", "Gönder", "Send", "发送", "ارسال"],
        ["survey.close"] = ["Закрыть опрос", "Anketi kapat", "Close survey", "关闭问卷", "بستن نظرسنجی"],
        ["survey.thanks"] = ["Спасибо за ответ!", "Teşekkürler!", "Thank you!", "谢谢！", "سپاسگزاریم!"],
        ["survey.comment"] = ["Комментарий (необязательно)", "Yorumunuz (isteğe bağlı)", "Comment (optional)", "备注（可选）", "توضیح (اختیاری)"],
        ["survey.q.csat"] = ["Как был решён ваш запрос в поддержку?", "Destek talebiniz nasıl çözüldü?", "How was your support request resolved?", "你的支持请求处理得怎么样？", "درخواست پشتیبانی شما چطور حل شد؟"],
        ["survey.q.nps"] = ["Насколько вероятно, что вы порекомендуете Colitu другу?", "Colitu'yu bir arkadaşınıza önerme olasılığınız nedir?", "How likely are you to recommend Colitu to a friend?", "你有多大可能向朋友推荐 Colitu？", "چقدر احتمال دارد Colitu را به دوستی پیشنهاد کنید؟"],
        ["survey.q.pmf"] = ["Что бы вы почувствовали, если бы больше не могли пользоваться Colitu?", "Colitu'yu artık kullanamasaydınız ne hissederdiniz?", "How would you feel if you could no longer use Colitu?", "如果再也无法使用 Colitu，你会有什么感受？", "اگر دیگر نتوانید از Colitu استفاده کنید چه احساسی دارید؟"],
        ["survey.q.churn"] = ["Почему вы не продлили Premium?", "Premium'u neden yenilemediniz?", "Why didn't you renew Premium?", "你为什么没有续订 Premium？", "چرا Premium را تمدید نکردید؟"],
        ["survey.csat.low"] = ["Очень плохо", "Çok kötü", "Very poor", "非常差", "خیلی بد"],
        ["survey.csat.high"] = ["Отлично", "Çok iyi", "Excellent", "非常好", "عالی"],
        ["survey.nps.low"] = ["Совсем не вероятно", "Hiç olası değil", "Not at all likely", "完全不会", "اصلاً"],
        ["survey.nps.high"] = ["Очень вероятно", "Çok olası", "Extremely likely", "非常可能", "حتماً"],
        ["survey.pmf.very"] = ["Очень расстроился бы", "Çok üzülürdüm", "Very disappointed", "非常失望", "بسیار ناراحت"],
        ["survey.pmf.somewhat"] = ["Немного расстроился бы", "Biraz üzülürdüm", "Somewhat disappointed", "有些失望", "تا حدی ناراحت"],
        ["survey.pmf.not"] = ["Не расстроился бы", "Üzülmezdim", "Not disappointed", "不失望", "ناراحت نمی‌شوم"],
        ["survey.churn.price"] = ["Цена", "Fiyat", "Price", "价格", "قیمت"],
        ["survey.churn.speed"] = ["Скорость", "Hız", "Speed", "速度", "سرعت"],
        ["survey.churn.blocked"] = ["Некоторые сайты или приложения не открывались", "Bazı siteler veya uygulamalar açılmadı", "Some sites or apps did not open", "有些网站或应用打不开", "بعضی سایت‌ها یا برنامه‌ها باز نمی‌شدند"],
        ["survey.churn.not_needed"] = ["Больше не нужен", "Artık gerekmiyor", "I no longer need it", "我不再需要了", "دیگر به آن نیازی ندارم"],
        ["survey.churn.other_vpn"] = ["Использую другой VPN", "Başka VPN kullanıyorum", "I use another VPN", "我在用其他 VPN", "از VPN دیگری استفاده می‌کنم"],
        ["survey.churn.technical"] = ["Технические проблемы", "Teknik sorunlar", "Technical problems", "技术问题", "مشکلات فنی"],
        ["survey.churn.other"] = ["Другое", "Diğer", "Other", "其他", "سایر"],
        // Invite a friend (referral)
        ["auth.invite.toggle"] = ["У меня есть код приглашения", "Davet kodum var", "I have an invite code", "我有邀请码", "کد دعوت دارم"],
        ["auth.invite.label"] = ["Код приглашения (необязательно)", "Davet kodu (isteğe bağlı)", "Invite code (optional)", "邀请码（可选）", "کد دعوت (اختیاری)"],
        ["auth.invite.hint"] = ["Код или ссылка colitu.com/r/КОД", "Kod veya colitu.com/r/KOD bağlantısı", "Code or colitu.com/r/CODE link", "邀请码或 colitu.com/r/CODE 链接", "کد یا پیوند colitu.com/r/CODE"],
        ["auth.err.invite"] = ["Код приглашения состоит из 6–12 букв и цифр.", "Davet kodu 6–12 harf veya rakamdan oluşmalıdır.", "An invite code is 6–12 letters or digits.", "邀请码由 6–12 位字母或数字组成。", "کد دعوت ۶ تا ۱۲ حرف یا رقم است."],
        ["invite.entry"] = ["Пригласить друга", "Arkadaşını davet et", "Invite a friend", "邀请朋友", "دعوت از دوست"],
        ["invite.kicker"] = ["ПРИГЛАШЕНИЕ", "DAVET", "INVITE", "邀请", "دعوت"],
        ["invite.title"] = ["Пригласить друга", "Arkadaşını davet et", "Invite a friend", "邀请朋友", "دعوت از دوست"],
        ["invite.lead"] = ["Получайте {days} Premium за каждое действительное приглашение; приглашённый тоже получает {invitee} Premium.", "Her geçerli davet için {days} Premium kazanın; davet ettiğiniz kişi de {invitee} Premium alır.", "Earn {days} of Premium for every valid invite; your friend gets {invitee} of Premium too.", "每成功邀请一位好友，你可获得 {days} Premium；你的好友也会获得 {invitee} Premium。", "برای هر دعوت معتبر {days} Premium می‌گیرید؛ دوستتان هم {invitee} Premium می‌گیرد."],
        ["invite.loading"] = ["Загружаем приглашение…", "Davet bilgisi yükleniyor…", "Loading your invite…", "正在加载你的邀请…", "در حال بارگذاری دعوت شما…"],
        ["invite.error"] = ["Не удалось загрузить приглашение.", "Davet bilgisi yüklenemedi.", "Could not load your invite.", "无法加载你的邀请。", "بارگذاری دعوت شما ممکن نشد."],
        ["invite.linkTitle"] = ["ВАША ССЫЛКА-ПРИГЛАШЕНИЕ", "DAVET BAĞLANTINIZ", "YOUR INVITE LINK", "你的邀请链接", "پیوند دعوت شما"],
        ["invite.linkLabel"] = ["Ссылка-приглашение", "Davet bağlantısı", "Invite link", "邀请链接", "پیوند دعوت"],
        ["invite.copy"] = ["Копировать", "Kopyala", "Copy", "复制", "کپی"],
        ["invite.copied"] = ["Ссылка скопирована.", "Bağlantı kopyalandı.", "Link copied.", "链接已复制。", "پیوند کپی شد."],
        ["invite.copyFailed"] = ["Не удалось скопировать; выделите ссылку вручную.", "Kopyalanamadı; bağlantıyı elle seçin.", "Could not copy; select the link by hand.", "复制失败，请手动选择链接。", "کپی نشد؛ پیوند را دستی انتخاب کنید."],
        ["invite.share"] = ["Поделиться", "Paylaş", "Share", "分享", "اشتراک‌گذاری"],
        ["invite.shareCopied"] = ["Текст приглашения скопирован — вставьте его в чат.", "Davet metni kopyalandı; sohbete yapıştırabilirsiniz.", "Invite text copied; paste it into a chat.", "邀请文案已复制，请粘贴到聊天中。", "متن دعوت کپی شد؛ آن را در گفتگو بچسبانید."],
        ["invite.shareText"] = ["Пользуюсь Colitu VPN: быстрый и приватный. Зарегистрируйся по этой ссылке и получи {days} Premium:", "Colitu VPN kullanıyorum: hızlı ve gizlilik odaklı. Bu bağlantıyla hesap açarsan {days} Premium kazanırsın:", "I use Colitu VPN: fast and privacy-first. Sign up with this link and get {days} of Premium:", "我在用 Colitu VPN：速度快、注重隐私。通过这个链接注册即可获得 {days} Premium：", "من از Colitu VPN استفاده می‌کنم: سریع و حریم‌خصوصی‌محور. با این لینک ثبت‌نام کن و {days} Premium بگیر:"],
        ["invite.card.body"] = ["Вам и другу — {days} Premium каждому", "Siz de arkadaşınız da {days} Premium kazanırsınız", "You and your friend each get {days} of Premium", "你和好友各得 {days} Premium", "شما و دوستتان هر کدام {days} Premium می‌گیرید"],
        ["invite.card.split"] = ["Другу — {invitee} Premium, вам — {days}", "Arkadaşınıza {invitee} Premium, size {days}", "Your friend gets {invitee} of Premium, you get {days}", "好友得 {invitee} Premium，你得 {days}", "دوستتان {invitee} Premium و شما {days} می‌گیرید"],
        ["invite.telegram"] = ["Telegram", "Telegram", "Telegram", "Telegram", "Telegram"],
        ["invite.whatsapp"] = ["WhatsApp", "WhatsApp", "WhatsApp", "WhatsApp", "WhatsApp"],
        ["invite.codeLine"] = ["Ваш код: {code}. Его также можно ввести при регистрации в приложении.", "Kodunuz: {code}. Uygulamada kayıt olurken de girilebilir.", "Your code: {code}. It can also be entered when signing up in the app.", "你的邀请码：{code}。也可以在应用内注册时输入。", "کد شما: {code}. هنگام ثبت‌نام در برنامه هم می‌توانید آن را وارد کنید."],
        ["invite.stat.invited"] = ["Приглашено", "Davet edilen", "Invited", "已邀请", "دعوت‌شده"],
        ["invite.stat.valid"] = ["Действительные приглашения", "Geçerli davet", "Valid invites", "有效邀请", "دعوت‌های معتبر"],
        ["invite.stat.pending"] = ["{n} в ожидании", "{n} bekliyor", "{n} pending", "{n} 个待确认", "{n} در انتظار"],
        ["invite.stat.earned"] = ["Получено Premium", "Kazanılan Premium", "Premium earned", "已获得 Premium", "Premium کسب‌شده"],
        ["invite.stat.days"] = ["{n} дн.", "{n} gün", "{n} days", "{n} 天", "{n} روز"],
        ["invite.stat.left"] = ["Осталось наград в этом месяце", "Bu ay kalan ödül", "Rewards left this month", "本月剩余奖励", "پاداش‌های باقی‌مانده در این ماه"],
        ["invite.rulesTitle"] = ["ПРАВИЛА", "KURALLAR", "RULES", "规则", "قوانین"],
        ["invite.rule1"] = ["Друг регистрируется по вашей ссылке и подтверждает e-mail: {days} Premium для него.", "Arkadaşınız bağlantınızla hesap açar ve e-postasını doğrular: {days} Premium onun.", "Your friend signs up with your link and verifies the e-mail: {days} of Premium for them.", "你的好友通过你的链接注册并验证邮箱：他们可获得 {days} Premium。", "دوست شما با پیوند شما ثبت‌نام می‌کند و ایمیلش را تأیید می‌کند: {days} Premium برای او."],
        ["invite.rule2"] = ["Он впервые подключается через приложение и сохраняет аккаунт не менее 24 часов: {days} Premium для вас.", "Uygulamayla ilk kez bağlanır ve hesabını en az 24 saat tutar: {days} Premium sizin.", "They connect with the app for the first time and keep the account for at least 24 hours: {days} of Premium for you.", "好友首次通过应用连接，并保留账户至少 24 小时：你可获得 {days} Premium。", "او برای نخستین بار با برنامه وصل می‌شود و حساب را دست‌کم ۲۴ ساعت نگه می‌دارد: {days} Premium برای شما."],
        ["invite.rule3"] = ["В месяц вознаграждается не более {n} приглашений; дни добавляются после текущего срока Premium.", "Ayda en fazla {n} davet ödüllendirilir; süreler mevcut Premium sürenizin sonuna eklenir.", "At most {n} invites are rewarded a month; the days are added after your current Premium.", "每月最多奖励 {n} 次邀请；奖励天数会加在你当前的 Premium 之后。", "در هر ماه حداکثر {n} دعوت پاداش دارد؛ روزها بعد از Premium فعلی شما اضافه می‌شوند."],
        ["invite.rule4"] = ["Аккаунты из той же сети или с того же устройства и временные адреса e-mail не засчитываются.", "Aynı ağdan veya cihazdan açılan hesaplar ve geçici e-posta adresleri sayılmaz.", "Accounts opened from the same network or device and throwaway e-mail addresses do not count.", "从同一网络或设备开通的账户以及一次性邮箱地址不计入。", "حساب‌های ساخته‌شده از یک شبکه یا دستگاه و نشانی‌های ایمیل یک‌بارمصرف حساب نمی‌شوند."],
    };
}

/// <summary>XAML: <c>Text="{s:T home.connect}"</c> — follows language changes.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }

    public T(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// Colitu's own name of a panel transport ("Fast" for Hysteria2 and so on):
/// the technical protocol names stay out of the UI.
/// </summary>
public static class ColituTransportNames
{
    private static readonly HashSet<string> Known = ["hysteria2", "vless-reality", "vless-xhttp", "trojan", "shadowsocks"];

    public static string Of(string? protocol, Loc loc)
    {
        if (string.IsNullOrEmpty(protocol)) return "";
        return loc[Known.Contains(protocol) ? $"transport.{protocol}" : "transport.other"];
    }
}

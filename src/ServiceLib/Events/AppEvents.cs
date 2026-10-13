namespace ServiceLib.Events;

public static class AppEvents
{
    public static readonly EventChannel<RxVoid> ReloadRequested = new();
    public static readonly EventChannel<bool?> ShowHideWindowRequested = new();
    public static readonly EventChannel<RxVoid> AddServerViaScanRequested = new();
    public static readonly EventChannel<RxVoid> AddServerViaClipboardRequested = new();
    public static readonly EventChannel<bool> SubscriptionsUpdateRequested = new();

    public static readonly EventChannel<RxVoid> ProfilesRefreshRequested = new();
    public static readonly EventChannel<RxVoid> SubscriptionsRefreshRequested = new();
    public static readonly EventChannel<RxVoid> ProxiesReloadRequested = new();
    public static readonly EventChannel<ServerSpeedItem> DispatcherStatisticsRequested = new();

    public static readonly EventChannel<string> SendSnackMsgRequested = new();
    public static readonly EventChannel<string> SendMsgViewRequested = new();

    public static readonly EventChannel<RxVoid> AppExitRequested = new();
    public static readonly EventChannel<bool> ShutdownRequested = new();

    public static readonly EventChannel<RxVoid> AdjustMainLvColWidthRequested = new();

    public static readonly EventChannel<string> SetDefaultServerRequested = new();

    public static readonly EventChannel<RxVoid> RoutingsMenuRefreshRequested = new();
    public static readonly EventChannel<RxVoid> TestServerRequested = new();
    public static readonly EventChannel<RxVoid> InboundDisplayRequested = new();
    public static readonly EventChannel<ESysProxyType> SysProxyChangeRequested = new();
}

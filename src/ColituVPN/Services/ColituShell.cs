namespace v2rayN.Services;

/// <summary>
/// Opens links and folders for the user. The app runs elevated, so anything it
/// shell-executes would inherit administrator rights; links are therefore
/// handed to the running Explorer (its desktop shell, or explorer.exe as a
/// fallback), which opens them in the user's normal (non-elevated) session,
/// and only web and mail links are accepted, never programs or files.
/// </summary>
public static class ColituShell
{
    /// <summary>True for absolute https:// links (and mailto: when <paramref name="allowMail"/>).</summary>
    internal static bool IsSafeLink(string? url, bool allowMail = true)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }
        if (allowMail && uri.Scheme == Uri.UriSchemeMailto)
        {
            return true;
        }
        return uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && !uri.IsUnc;
    }

    public static bool OpenUrl(string? url, bool allowMail = true)
    {
        if (!IsSafeLink(url, allowMail))
        {
            Logging.SaveLog($"ColituShell.OpenUrl: refused link with an unsupported scheme or form ({Truncate(url)})");
            return false;
        }
        var link = new Uri(url!.Trim()).AbsoluteUri;
        // "explorer.exe <url>" started from an elevated process can hang without opening
        // anything (seen on the test PC with Firefox as the default browser, 2026-10-22:
        // payment pages never opened). The running desktop shell opens it instead, in the
        // user's normal session and default browser; explorer.exe stays as the fallback.
        if (ShellExecuteAsUser(link))
        {
            return true;
        }
        // Explorer reads commas in its command line as switch separators (/root,<program>), so
        // a link from server-sent text must not carry one; %2C means the same to a web server.
        return StartViaExplorer(link.Replace(",", "%2C", StringComparison.Ordinal));
    }

    /// <summary>
    /// ShellExecute inside the user's own Explorer (the desktop's shell view), so the
    /// link opens non-elevated in the default browser even though the app runs as
    /// administrator (Raymond Chen, "How can I launch an unelevated process from my
    /// elevated process").
    /// </summary>
    private static bool ShellExecuteAsUser(string link)
    {
        try
        {
            var shellWindows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!)!;
            object location = 0; // CSIDL_DESKTOP
            object root = Type.Missing;
            var desktop = shellWindows.FindWindowSW(ref location, ref root, 8 /* SWC_DESKTOP */, out _, 1 /* SWFO_NEEDDISPATCH */);
            var sidTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var iidShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
            var browser = (IShellBrowser)((IServiceProvider)desktop).QueryService(ref sidTopLevelBrowser, ref iidShellBrowser);
            var view = browser.QueryActiveShellView();
            var iidDispatch = new Guid("00020400-0000-0000-C000-000000000046");
            var background = view.GetItemObject(0 /* SVGIO_BACKGROUND */, ref iidDispatch);
            var shell = background.GetType().InvokeMember("Application", System.Reflection.BindingFlags.GetProperty, null, background, null)!;
            shell.GetType().InvokeMember("ShellExecute", System.Reflection.BindingFlags.InvokeMethod, null, shell, [link, "", "", "open", 1]);
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituShell.ShellExecuteAsUser", ex);
            return false;
        }
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        // Only FindWindowSW is called; the slots before it keep the vtable order.
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object location, ref object root, int windowClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid service, ref Guid riid);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow and the IShellBrowser slots before QueryActiveShellView.
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();
        IShellView QueryActiveShellView();
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        // IOleWindow and the IShellView slots before GetItemObject.
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();
        void SelectItem();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetItemObject(uint item, ref Guid riid);
    }

    /// <summary>Shows one of the app's own folders (logs) in Explorer.</summary>
    public static bool OpenFolder(string path)
    {
        var full = Path.GetFullPath(path);
        if (!IsInside(full, Utils.StartupPath()) || !Directory.Exists(full))
        {
            return false;
        }
        return StartViaExplorer(full);
    }

    /// <summary>Shows a downloaded support attachment selected in Explorer (never runs it).</summary>
    public static bool SelectFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (!IsInside(full, ColituSupportService.DownloadRoot) || !File.Exists(full))
        {
            return false;
        }
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            // File names never contain quotes, so the path can be quoted as is.
            using var _ = Process.Start(new ProcessStartInfo(explorer, $"/select,\"{full}\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituShell.SelectFile", ex);
            return false;
        }
    }

    /// <summary>True when <paramref name="full"/> is <paramref name="root"/> or below it (not a sibling like root2).</summary>
    internal static bool IsInside(string full, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(full, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartViaExplorer(string target)
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var startInfo = new ProcessStartInfo(explorer) { UseShellExecute = false };
            startInfo.ArgumentList.Add(target);
            using var _ = Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituShell.StartViaExplorer", ex);
            return false;
        }
    }

    private static string Truncate(string? value) => value == null ? "" : value.Length <= 80 ? value : value[..80];
}

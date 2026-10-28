using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

// Public event signatures from Windows SDK ExDisp.Idl / ExDispId.h.
// Unused notifications intentionally preserve every incoming/outgoing value.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ShellBrowserCommandSink : IShellBrowserCommandEvents
{
    private Action<int, bool>? _command;
    private Action<string?>? _statusText;
    internal ShellBrowserCommandSink(Action<int, bool> command, Action<string?> statusText)
    { _command = command; _statusText = statusText; }
    internal void Disconnect() { _command = null; _statusText = null; }
    public void StatusTextChange(string? Text) => _statusText?.Invoke(Text);
    public void ProgressChange(int Progress, int ProgressMax) {  }
    public void CommandStateChange(int Command, bool Enable) { _command?.Invoke(Command, Enable); }
    public void DownloadBegin() {  }
    public void DownloadComplete() {  }
    public void TitleChange(string Text) {  }
    public void PropertyChange(string szProperty) {  }
    public void BeforeNavigate2(object? pDisp, ref object? URL, ref object? Flags, ref object? TargetFrameName, ref object? PostData, ref object? Headers, ref bool Cancel) {  }
    public void NewWindow2(ref object? ppDisp, ref bool Cancel) {  }
    public void NavigateComplete2(object? pDisp, ref object? URL) {  }
    public void DocumentComplete(object? pDisp, ref object? URL) {  }
    public void OnQuit() {  }
    public void OnVisible(bool Visible) {  }
    public void OnToolBar(bool ToolBar) {  }
    public void OnMenuBar(bool MenuBar) {  }
    public void OnStatusBar(bool StatusBar) {  }
    public void OnFullScreen(bool FullScreen) {  }
    public void OnTheaterMode(bool TheaterMode) {  }
    public void WindowSetResizable(bool Resizable) {  }
    public void WindowSetLeft(int Left) {  }
    public void WindowSetTop(int Top) {  }
    public void WindowSetWidth(int Width) {  }
    public void WindowSetHeight(int Height) {  }
    public void WindowClosing(bool IsChildWindow, ref bool Cancel) {  }
    public void ClientToHostWindow(ref int CX, ref int CY) {  }
    public void SetSecureLockIcon(int SecureLockIcon) {  }
    public void FileDownload(bool ActiveDocument, ref bool Cancel) {  }
    public void NavigateError(object? pDisp, ref object? URL, ref object? Frame, ref object? StatusCode, ref bool Cancel) {  }
    public void PrintTemplateInstantiation(object? pDisp) {  }
    public void PrintTemplateTeardown(object? pDisp) {  }
    public void UpdatePageStatus(object? pDisp, ref object? nPage, ref object? fDone) {  }
    public void PrivacyImpactedStateChange(bool bImpacted) {  }
    public void NewWindow3(ref object? ppDisp, ref bool Cancel, uint dwFlags, string bstrUrlContext, string bstrUrl) {  }
    public void SetPhishingFilterStatus(int PhishingFilterStatus) {  }
    public void WindowStateChanged(uint dwWindowStateFlags, uint dwValidFlagsMask) {  }
    public void NewProcess(int lCauseFlag, object? pWB2, ref bool Cancel) {  }
    public void ThirdPartyUrlBlocked(ref object? URL, uint dwCount) {  }
    public void RedirectXDomainBlocked(object? pDisp, ref object? StartURL, ref object? RedirectURL, ref object? Frame, ref object? StatusCode) {  }
    public void BeforeScriptExecute(object? pDispWindow) {  }
    public void WebWorkerStarted(uint dwUniqueID, string bstrWorkerLabel) {  }
    public void WebWorkerFinsihed(uint dwUniqueID) {  }
}

[ComVisible(true), Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D"),
 InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IShellBrowserCommandEvents
{
    [DispId(102)] void StatusTextChange([MarshalAs(UnmanagedType.BStr)] string? Text);
    [DispId(108)] void ProgressChange(int Progress, int ProgressMax);
    [DispId(105)] void CommandStateChange(int Command, [MarshalAs(UnmanagedType.VariantBool)] bool Enable);
    [DispId(106)] void DownloadBegin();
    [DispId(104)] void DownloadComplete();
    [DispId(113)] void TitleChange([MarshalAs(UnmanagedType.BStr)] string Text);
    [DispId(112)] void PropertyChange([MarshalAs(UnmanagedType.BStr)] string szProperty);
    [DispId(250)] void BeforeNavigate2([MarshalAs(UnmanagedType.IDispatch)] object? pDisp, [In, MarshalAs(UnmanagedType.Struct)] ref object? URL, [In, MarshalAs(UnmanagedType.Struct)] ref object? Flags, [In, MarshalAs(UnmanagedType.Struct)] ref object? TargetFrameName, [In, MarshalAs(UnmanagedType.Struct)] ref object? PostData, [In, MarshalAs(UnmanagedType.Struct)] ref object? Headers, [In, Out, MarshalAs(UnmanagedType.VariantBool)] ref bool Cancel);
    [DispId(251)] void NewWindow2([In, Out, MarshalAs(UnmanagedType.IDispatch)] ref object? ppDisp, [In, Out, MarshalAs(UnmanagedType.VariantBool)] ref bool Cancel);
    [DispId(252)] void NavigateComplete2([MarshalAs(UnmanagedType.IDispatch)] object? pDisp, [In, MarshalAs(UnmanagedType.Struct)] ref object? URL);
    [DispId(259)] void DocumentComplete([MarshalAs(UnmanagedType.IDispatch)] object? pDisp, [In, MarshalAs(UnmanagedType.Struct)] ref object? URL);
    [DispId(253)] void OnQuit();
    [DispId(254)] void OnVisible([MarshalAs(UnmanagedType.VariantBool)] bool Visible);
    [DispId(255)] void OnToolBar([MarshalAs(UnmanagedType.VariantBool)] bool ToolBar);
    [DispId(256)] void OnMenuBar([MarshalAs(UnmanagedType.VariantBool)] bool MenuBar);
    [DispId(257)] void OnStatusBar([MarshalAs(UnmanagedType.VariantBool)] bool StatusBar);
    [DispId(258)] void OnFullScreen([MarshalAs(UnmanagedType.VariantBool)] bool FullScreen);
    [DispId(260)] void OnTheaterMode([MarshalAs(UnmanagedType.VariantBool)] bool TheaterMode);
    [DispId(262)] void WindowSetResizable([MarshalAs(UnmanagedType.VariantBool)] bool Resizable);
    [DispId(264)] void WindowSetLeft(int Left);
    [DispId(265)] void WindowSetTop(int Top);
    [DispId(266)] void WindowSetWidth(int Width);
    [DispId(267)] void WindowSetHeight(int Height);
    [DispId(263)] void WindowClosing([MarshalAs(UnmanagedType.VariantBool)] bool IsChildWindow, [In, Out, MarshalAs(UnmanagedType.VariantBool)] ref bool Cancel);
    [DispId(268)] void ClientToHostWindow([In, Out] ref int CX, [In, Out] ref int CY);
    [DispId(269)] void SetSecureLockIcon(int SecureLockIcon);
    [DispId(270)] void FileDownload([MarshalAs(UnmanagedType.VariantBool)] bool ActiveDocument, [In, Out, MarshalAs(UnmanagedType.VariantBool)] ref bool Cancel);
    [DispId(271)] void NavigateError([MarshalAs(UnmanagedType.IDispatch)] object? pDisp, [In, MarshalAs(UnmanagedType.Struct)] ref object? URL, [In, MarshalAs(UnmanagedType.Struct)] ref object? Frame, [In, MarshalAs(UnmanagedType.Struct)] ref object? StatusCode, [In, Out, MarshalAs(UnmanagedType.VariantBool)] ref bool Cancel);
    [DispId(225)] void PrintTemplateInstantiation([MarshalAs(UnmanagedType.IDispatch)] object? pDisp);
    [DispId(226)] void PrintTemplateTeardown([MarshalAs(UnmanagedType.IDispatch)] object? pDisp);
    [DispId(227)] void UpdatePageStatus([MarshalAs(UnmanagedType.IDispatch)] object? pDisp, [In, MarshalAs(UnmanagedType.Struct)] ref object? nPage, [In, MarshalAs(UnmanagedType.Struct)] ref object? fDone);
    [DispId(272)] void PrivacyImpactedStateChange([MarshalAs(UnmanagedType.VariantBool)] bool bImpacted);
    [DispId(273)] void NewWindow3([In, Out, MarshalAs(UnmanagedType.IDispatch)] ref object? ppDisp, [In, Out, MarshalAs(UnmanagedType.VariantBool)] ref bool Cancel, uint dwFlags, [MarshalAs(UnmanagedType.BStr)] string bstrUrlContext, [MarshalAs(UnmanagedType.BStr)] string bstrUrl);
    [DispId(282)] void SetPhishingFilterStatus(int PhishingFilterStatus);
    [DispId(283)] void WindowStateChanged(uint dwWindowStateFlags, uint dwValidFlagsMask);
    [DispId(284)] void NewProcess(int lCauseFlag, [MarshalAs(UnmanagedType.IDispatch)] object? pWB2, [In, Out, MarshalAs(UnmanagedType.VariantBool)] ref bool Cancel);
    [DispId(285)] void ThirdPartyUrlBlocked([In, MarshalAs(UnmanagedType.Struct)] ref object? URL, uint dwCount);
    [DispId(286)] void RedirectXDomainBlocked([MarshalAs(UnmanagedType.IDispatch)] object? pDisp, [In, MarshalAs(UnmanagedType.Struct)] ref object? StartURL, [In, MarshalAs(UnmanagedType.Struct)] ref object? RedirectURL, [In, MarshalAs(UnmanagedType.Struct)] ref object? Frame, [In, MarshalAs(UnmanagedType.Struct)] ref object? StatusCode);
    [DispId(290)] void BeforeScriptExecute([MarshalAs(UnmanagedType.IDispatch)] object? pDispWindow);
    [DispId(288)] void WebWorkerStarted(uint dwUniqueID, [MarshalAs(UnmanagedType.BStr)] string bstrWorkerLabel);
    [DispId(289)] void WebWorkerFinsihed(uint dwUniqueID);
}

using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ExplorerDirs.NativeProbe;

[ComVisible(true), Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellViewDragSource
{
    [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escapePressed, uint keyState);
    [PreserveSig] int GiveFeedback(uint effect);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class ShellViewDragSource : IShellViewDragSource
{
    private const uint MouseLeftButton = 0x0001;
    private const uint MouseRightButton = 0x0002;
    private const uint MouseMiddleButton = 0x0010;
    private const int DragDropSDrop = 0x00040100;
    private const int DragDropSCancel = 0x00040101;
    private const int DragDropSUseDefaultCursors = 0x00040102;
    private readonly uint _initiatingButton;

    internal ShellViewDragSource(uint initiatingButton = MouseLeftButton)
    {
        if (initiatingButton is not (MouseLeftButton or MouseRightButton or MouseMiddleButton))
            throw new ArgumentOutOfRangeException(nameof(initiatingButton));

        _initiatingButton = initiatingButton;
    }

    public int QueryContinueDrag(bool escapePressed, uint keyState)
    {
        if (escapePressed)
            return DragDropSCancel;

        return (keyState & _initiatingButton) == 0 ? DragDropSDrop : 0;
    }

    public int GiveFeedback(uint effect) => DragDropSUseDefaultCursors;

    internal static int RunOleLoop(ComDataObject dataObject, uint allowedEffects, uint initiatingButton, out uint performedEffect)
    {
        ArgumentNullException.ThrowIfNull(dataObject);
        var source = new ShellViewDragSource(initiatingButton);
        // DoDragDrop owns target discovery and input tracking; callers must run this on an OLE-initialized STA.
        // This probe entry point deliberately does not move the cursor or synthesize input.
        return DoDragDrop(dataObject, source, allowedEffects, out performedEffect);
    }

    internal static void VerifyContract(Action<bool, string> check)
    {
        var source = new ShellViewDragSource();
        var dragSource = Marshal.GetComInterfaceForObject(source, typeof(IShellViewDragSource));
        try
        {
            var continueDrag = Marshal.GetDelegateForFunctionPointer<ContinueDragCallback>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(dragSource), 3 * nint.Size));
            var feedback = Marshal.GetDelegateForFunctionPointer<FeedbackCallback>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(dragSource), 4 * nint.Size));
            check(continueDrag(dragSource, true, MouseLeftButton) == DragDropSCancel,
                "the isolated OLE drag source cancels when Escape is pressed");
            check(continueDrag(dragSource, false, MouseLeftButton) == 0,
                "the isolated OLE drag source continues while the initiating button remains down");
            check(continueDrag(dragSource, false, 0) == DragDropSDrop,
                "the isolated OLE drag source completes when the initiating button is released");
            check(feedback(dragSource, 0) == DragDropSUseDefaultCursors &&
                  feedback(dragSource, 1) == DragDropSUseDefaultCursors &&
                  feedback(dragSource, 2) == DragDropSUseDefaultCursors,
                "the isolated OLE drag source delegates None, Copy, and Move cursor feedback to Windows");
        }
        finally { Marshal.Release(dragSource); }

        var rightButtonSource = new ShellViewDragSource(MouseRightButton);
        var rightDragSource = Marshal.GetComInterfaceForObject(rightButtonSource, typeof(IShellViewDragSource));
        try
        {
            var continueRightDrag = Marshal.GetDelegateForFunctionPointer<ContinueDragCallback>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(rightDragSource), 3 * nint.Size));
            check(continueRightDrag(rightDragSource, false, MouseLeftButton | MouseRightButton) == 0 &&
                  continueRightDrag(rightDragSource, false, MouseLeftButton) == DragDropSDrop,
                "the isolated OLE COM source completes right-button drags only after the initiating button is released");
        }
        finally { Marshal.Release(rightDragSource); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ContinueDragCallback(nint self, [MarshalAs(UnmanagedType.Bool)] bool escapePressed, uint keyState);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FeedbackCallback(nint self, uint effect);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int DoDragDrop(
        [MarshalAs(UnmanagedType.Interface)] ComDataObject dataObject,
        [MarshalAs(UnmanagedType.Interface)] IShellViewDragSource dragSource,
        uint allowedEffects,
        out uint performedEffect);
}

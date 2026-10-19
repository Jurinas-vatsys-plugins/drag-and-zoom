using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using vatsys.Plugin;

namespace vatSys.DragAndZoom
{
    /// <summary>
    /// Uses the right mouse button for ASD panning and adds mouse-wheel zoom.
    /// The ASD is a Direct2D control and its mouse handlers are registered in a
    /// way that does not reliably raise normal WinForms mouse events. Therefore
    /// this plugin observes and translates messages in the WinForms message pump.
    /// </summary>
    [Export(typeof(IPlugin))]
    public sealed class DragAndZoomPlugin : IPlugin, IMessageFilter
    {
        private const int WmMouseMove = 0x0200;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmMButtonDown = 0x0207;
        private const int WmMButtonUp = 0x0208;
        private const int WmMouseWheel = 0x020A;
        private const int MkRButton = 0x0002;
        private const int MkMButton = 0x0010;

        private readonly HashSet<Control> dragging = new HashSet<Control>();
        private readonly Dictionary<IntPtr, Control> controls = new Dictionary<IntPtr, Control>();
        private readonly string logPath;

        public DragAndZoomPlugin()
        {
            logPath = Path.Combine(Path.GetDirectoryName(typeof(DragAndZoomPlugin).Assembly.Location), "DragAndZoom.log");
            Application.AddMessageFilter(this);
            Log("Plugin loaded");
        }

        public string Name
        {
            get { return "Drag-and-zoom (Right-Button Pan + Wheel Zoom)"; }
        }

        public void OnFDRUpdate(vatsys.FDP2.FDR fdr) { }

        public void OnRadarTrackUpdate(vatsys.RDP.RadarTrack radarTrack) { }

        public bool PreFilterMessage(ref Message message)
        {
            try
            {
                Control asd = FindAsd(message.HWnd);
                if (asd == null || asd.IsDisposed)
                    return false;

                switch (message.Msg)
                {
                    case WmRButtonDown:
                        dragging.Add(asd);
                        TranslateButtonMessage(ref message, WmMButtonDown);
                        Log("Right down " + MouseX(message).ToString() + "," + MouseY(message).ToString());
                        break;

                    case WmMouseMove:
                        if (dragging.Contains(asd))
                            TranslateMouseMove(ref message);
                        break;

                    case WmRButtonUp:
                        if (dragging.Remove(asd))
                        {
                            Point finalPoint = new Point(MouseX(message), MouseY(message));
                            TranslateButtonMessage(ref message, WmMButtonUp);
                            RestorePanAfterVatSys(asd, finalPoint);
                            Log("Right up " + finalPoint.X.ToString() + "," + finalPoint.Y.ToString() + "; replay final move");
                        }
                        break;

                    case WmMouseWheel:
                        int delta = SignedHighWord(message.WParam.ToInt64());
                        try
                        {
                            asd.BeginInvoke((MethodInvoker)(() => ZoomWithWheel(asd, delta)));
                        }
                        catch (InvalidOperationException) { }
                        Log("Wheel " + delta.ToString());
                        return true;
                }
            }
            catch (Exception exception)
            {
                Log(exception.ToString());
            }

            // The message is deliberately not consumed: it reaches vatSys after
            // its right-button identity has been changed to middle-button.
            return false;
        }

        private Control FindAsd(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return null;

            Control cached;
            if (controls.TryGetValue(handle, out cached) && !cached.IsDisposed)
                return cached;

            Control control = Control.FromHandle(handle);
            while (control != null)
            {
                if (control.GetType().FullName == "vatsys.ASDControlDX")
                {
                    controls[handle] = control;
                    return control;
                }
                control = control.Parent;
            }
            return null;
        }

        private static void TranslateButtonMessage(ref Message message, int targetMessage)
        {
            message.Msg = targetMessage;
            message.WParam = ReplaceButtonFlags(message.WParam, MkRButton, MkMButton);
        }

        private static void TranslateMouseMove(ref Message message)
        {
            message.WParam = ReplaceButtonFlags(message.WParam, MkRButton, MkMButton);
        }

        private static IntPtr ReplaceButtonFlags(IntPtr value, int from, int to)
        {
            long raw = value.ToInt64();
            int flags = (int)(raw & 0xffffL);
            flags = (flags & ~from) | to;
            return new IntPtr((raw & unchecked((long)0xffffffffffff0000UL)) | (uint)flags);
        }

        private static void RestorePanAfterVatSys(Control asd, Point finalPoint)
        {
            if (asd.IsDisposed)
                return;

            try
            {
                asd.BeginInvoke((MethodInvoker)(() =>
                {
                    // vatSys's own MouseUp resets QuickPan. Re-enable only its
                    // temporary-pan flag and call its own MouseMove routine at
                    // the release point. This reproduces the native calculation
                    // without duplicating its map projection math.
                    FieldInfo quickPan = asd.GetType().GetField(
                        "QuickPan", BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo mouseMove = asd.GetType().GetMethod(
                        "ASD_MouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (quickPan == null || mouseMove == null)
                        return;

                    quickPan.SetValue(asd, true);
                    mouseMove.Invoke(asd, new object[] {
                        asd,
                        new MouseEventArgs(MouseButtons.Middle, 0, finalPoint.X, finalPoint.Y, 0)
                    });
                    quickPan.SetValue(asd, false);
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void ZoomWithWheel(Control asd, int delta)
        {
            if (delta == 0)
                return;

            MethodInfo getRange = asd.GetType().GetMethod(
                "GetRange", BindingFlags.Instance | BindingFlags.Public,
                null, Type.EmptyTypes, null);
            MethodInfo setZoom = asd.GetType().GetMethod(
                "SetZoom", BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(double), typeof(bool), typeof(bool), typeof(bool) }, null);
            if (getRange == null || setZoom == null)
                return;

            int current = Convert.ToInt32(getRange.Invoke(asd, null));
            int next = delta > 0
                ? (int)Math.Round(current * 0.8, MidpointRounding.AwayFromZero)
                : (int)Math.Round(current * 1.25, MidpointRounding.AwayFromZero);

            setZoom.Invoke(asd, new object[] { (double)next, true, true, false });
            int after = Convert.ToInt32(getRange.Invoke(asd, null));
            Log("Range " + current.ToString() + " -> " + after.ToString() + " requested " + next.ToString());
            asd.Invalidate();
        }

        private static int MouseX(Message message)
        {
            return (short)(message.LParam.ToInt64() & 0xffff);
        }

        private static int MouseY(Message message)
        {
            return (short)((message.LParam.ToInt64() >> 16) & 0xffff);
        }

        private static int SignedHighWord(long value)
        {
            int word = (int)((value >> 16) & 0xffff);
            return word >= 0x8000 ? word - 0x10000 : word;
        }

        private void Log(string text)
        {
            try
            {
                File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + text + Environment.NewLine);
            }
            catch { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using vatsys;
using vatsys.Plugin;

namespace vatSys.PersistentPan
{
    /// <summary>
    /// Makes the ASD middle-button pan persistent and adds mouse-wheel zoom.
    /// The ASD is a Direct2D control and its mouse handlers are registered in a
    /// way that does not reliably raise normal WinForms mouse events. Therefore
    /// this plugin observes the WinForms message pump instead.
    /// </summary>
    [Export(typeof(IPlugin))]
    public sealed class PersistentPanPlugin : IPlugin, IMessageFilter
    {
        private const int WmMouseMove = 0x0200;
        private const int WmMButtonDown = 0x0207;
        private const int WmMButtonUp = 0x0208;
        private const int WmMouseWheel = 0x020A;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmXButtonDown = 0x020B;
        private const int WmXButtonUp = 0x020C;
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;

        private readonly HashSet<Control> panning = new HashSet<Control>();
        private readonly HashSet<Control> rightButtonDown = new HashSet<Control>();
        private readonly Dictionary<IntPtr, Control> controls = new Dictionary<IntPtr, Control>();
        private readonly string logPath;
        private readonly string settingsPath;
        private PersistentPanSettings settings;
        private Thread settingsThread;
        private PanSettingsWindow settingsWindow;
        private readonly object settingsWindowLock = new object();
        private ToolStripMenuItem enabledMenuItem;
        private ToolStripMenuItem disabledMenuItem;

        public PersistentPanPlugin()
        {
            logPath = Path.Combine(Path.GetDirectoryName(typeof(PersistentPanPlugin).Assembly.Location), "PersistentPan.log");
            settingsPath = Path.Combine(Path.GetDirectoryName(typeof(PersistentPanPlugin).Assembly.Location), "PersistentPan.settings");
            settings = PersistentPanSettings.Load(settingsPath);
            Application.AddMessageFilter(this);
            Log("Plugin loaded");
            try
            {
                AddMenuItems();
                Log("Menu items registered");
            }
            catch (Exception exception)
            {
                Log("Menu registration failed: " + exception);
            }
        }

        public string Name
        {
            get { return "Persistent Middle-Button Pan + Wheel Zoom"; }
        }

        public void OnFDRUpdate(vatsys.FDP2.FDR fdr) { }

        public void OnRadarTrackUpdate(vatsys.RDP.RadarTrack radarTrack) { }

        public bool PreFilterMessage(ref Message message)
        {
            try
            {
                if (settings.SettingsShortcut != Keys.None &&
                    (message.Msg == WmKeyDown || message.Msg == WmSysKeyDown) &&
                    IsShortcutMessage(message))
                {
                    ShowSettingsWindow();
                    return true;
                }

                if (!settings.Enabled)
                    return false;

                Control asd = FindAsd(message.HWnd);
                if (asd == null || asd.IsDisposed)
                    return false;

                if (settings.DisableNativeZoomBox && settings.PanMouseButton != PanMouseButton.Right)
                {
                    if (message.Msg == WmRButtonDown)
                    {
                        rightButtonDown.Add(asd);
                        return true;
                    }

                    if (message.Msg == WmMouseMove && rightButtonDown.Contains(asd))
                        return true;

                    if (message.Msg == WmRButtonUp && rightButtonDown.Remove(asd))
                        return true;
                }

                switch (message.Msg)
                {
                    case WmLButtonDown:
                    case WmMButtonDown:
                    case WmRButtonDown:
                    case WmXButtonDown:
                        if (!IsConfiguredPanDown(message))
                            break;
                        panning.Add(asd);
                        if (settings.PanMouseButton == PanMouseButton.Middle)
                        {
                            Log("Pan down " + MouseX(message).ToString() + "," + MouseY(message).ToString());
                            break;
                        }
                        BeginConfiguredPan(asd, message);
                        return true;

                    case WmMouseMove:
                        // The native ASD handler performs the actual pan here.
                        // We only need the final coordinates at MouseUp.
                        break;

                    case WmLButtonUp:
                    case WmMButtonUp:
                    case WmRButtonUp:
                    case WmXButtonUp:
                        if (!IsConfiguredPanUp(message))
                            break;
                        if (panning.Remove(asd))
                        {
                            Point finalPoint = new Point(MouseX(message), MouseY(message));
                            if (settings.PanMouseButton == PanMouseButton.Middle)
                            {
                                RestorePanAfterVatSys(asd, finalPoint, MouseButtons.Middle);
                            }
                            else
                            {
                                EndConfiguredPan(asd, finalPoint, settings.PanMouseButton);
                            }
                            Log("Pan up " + finalPoint.X.ToString() + "," + finalPoint.Y.ToString());
                        }
                        return settings.PanMouseButton != PanMouseButton.Middle;

                    case WmMouseWheel:
                        int delta = SignedHighWord(message.WParam.ToInt64());
                        Log("Wheel " + delta.ToString());
                        try
                        {
                            asd.BeginInvoke((MethodInvoker)(() => ZoomWithWheel(asd, delta)));
                        }
                        catch (InvalidOperationException) { }
                        return true;
                }
            }
            catch (Exception exception)
            {
                Log(exception.ToString());
            }

            // Do not consume messages. vatSys still receives the original input.
            return false;
        }

        private bool IsShortcutMessage(Message message)
        {
            Keys pressed = (Keys)((int)message.WParam);
            Keys modifiers = Keys.None;
            if ((Control.ModifierKeys & Keys.Control) != Keys.None)
                modifiers |= Keys.Control;
            if ((Control.ModifierKeys & Keys.Shift) != Keys.None)
                modifiers |= Keys.Shift;
            if ((Control.ModifierKeys & Keys.Alt) != Keys.None)
                modifiers |= Keys.Alt;
            return (pressed | modifiers) == settings.SettingsShortcut;
        }

        private bool IsConfiguredPanDown(Message message)
        {
            return message.Msg == PanButtonDownMessage(settings.PanMouseButton)
                && (settings.PanMouseButton < PanMouseButton.XButton1 || IsXButton(message, settings.PanMouseButton));
        }

        private bool IsConfiguredPanUp(Message message)
        {
            return message.Msg == PanButtonUpMessage(settings.PanMouseButton)
                && (settings.PanMouseButton < PanMouseButton.XButton1 || IsXButton(message, settings.PanMouseButton));
        }

        private static int PanButtonDownMessage(PanMouseButton button)
        {
            switch (button)
            {
                case PanMouseButton.Left: return WmLButtonDown;
                case PanMouseButton.Right: return WmRButtonDown;
                case PanMouseButton.XButton1:
                case PanMouseButton.XButton2: return WmXButtonDown;
                default: return WmMButtonDown;
            }
        }

        private static int PanButtonUpMessage(PanMouseButton button)
        {
            switch (button)
            {
                case PanMouseButton.Left: return WmLButtonUp;
                case PanMouseButton.Right: return WmRButtonUp;
                case PanMouseButton.XButton1:
                case PanMouseButton.XButton2: return WmXButtonUp;
                default: return WmMButtonUp;
            }
        }

        private static bool IsXButton(Message message, PanMouseButton button)
        {
            if (message.Msg != WmXButtonDown && message.Msg != WmXButtonUp)
                return false;
            int highWord = (int)((message.WParam.ToInt64() >> 16) & 0xffff);
            return button == PanMouseButton.XButton1
                ? highWord == 1
                : highWord == 2;
        }

        private void ShowSettingsWindow()
        {
            lock (settingsWindowLock)
            {
                if (settingsWindow != null && !settingsWindow.IsDisposed)
                {
                    try
                    {
                        settingsWindow.BeginInvoke((MethodInvoker)(() =>
                        {
                            settingsWindow.Show();
                            settingsWindow.WindowState = FormWindowState.Normal;
                            settingsWindow.Activate();
                            settingsWindow.BringToFront();
                        }));
                    }
                    catch (InvalidOperationException) { }
                    return;
                }

                if (settingsThread != null && settingsThread.IsAlive)
                    return;

                settingsThread = new Thread(SettingsThreadMain);
                settingsThread.IsBackground = true;
                settingsThread.SetApartmentState(ApartmentState.STA);
                settingsThread.Start();
            }
        }

        private void SettingsThreadMain()
        {
            try
            {
                PanSettingsWindow window = new PanSettingsWindow(settings, settingsPath, Log);
                lock (settingsWindowLock)
                {
                    settingsWindow = window;
                }
                window.FormClosed += (sender, args) =>
                {
                    lock (settingsWindowLock)
                    {
                        settingsWindow = null;
                    }
                };
                Application.Run(window);
            }
            catch (Exception exception)
            {
                Log("Settings thread failed: " + exception);
            }
            finally
            {
                lock (settingsWindowLock)
                {
                    settingsWindow = null;
                    settingsThread = null;
                }
            }
        }

        private void AddMenuItems()
        {
            ToolStripMenuItem menu = new ToolStripMenuItem("Controls");

            ToolStripMenuItem pluginState = new ToolStripMenuItem("Plugin state");
            enabledMenuItem = new ToolStripMenuItem("Active");
            disabledMenuItem = new ToolStripMenuItem("Inactive");
            enabledMenuItem.Click += (sender, args) =>
            {
                settings.Enabled = true;
                SaveSettings();
                UpdateMenuState();
            };
            disabledMenuItem.Click += (sender, args) =>
            {
                settings.Enabled = false;
                SaveSettings();
                UpdateMenuState();
            };

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (sender, args) => MMI.InvokeOnGUI(ShowSettingsWindow);

            pluginState.DropDownItems.Add(enabledMenuItem);
            pluginState.DropDownItems.Add(disabledMenuItem);
            menu.DropDownItems.Add(pluginState);
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(settingsItem);

            MMI.AddCustomMenuItem(new CustomToolStripMenuItem(
                CustomToolStripMenuItemWindowType.Main,
                CustomToolStripMenuItemCategory.Settings,
                menu));
            UpdateMenuState();
        }

        private void SaveSettings()
        {
            try
            {
                settings.Save(settingsPath);
                Log("Settings saved; Enabled=" + settings.Enabled.ToString()
                    + "; DisableNativeZoomBox=" + settings.DisableNativeZoomBox.ToString());
            }
            catch (Exception exception)
            {
                Log(exception.ToString());
                MessageBox.Show(exception.Message, "Could not save Persistent Pan settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateMenuState()
        {
            if (enabledMenuItem != null)
                enabledMenuItem.Checked = settings.Enabled;
            if (disabledMenuItem != null)
                disabledMenuItem.Checked = !settings.Enabled;
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

        private static void BeginConfiguredPan(Control asd, Message message)
        {
            try
            {
                MethodInfo mouseDown = asd.GetType().GetMethod(
                    "ASD_MouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                if (mouseDown == null)
                    return;
                mouseDown.Invoke(asd, new object[] {
                    asd,
                    new MouseEventArgs(MouseButtons.Middle, 1,
                        MouseX(message), MouseY(message), 0)
                });
            }
            catch { }
        }

        private static void EndConfiguredPan(Control asd, Point finalPoint, PanMouseButton button)
        {
            try
            {
                FieldInfo quickPan = asd.GetType().GetField(
                    "QuickPan", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo mouseMove = asd.GetType().GetMethod(
                    "ASD_MouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
                if (quickPan == null || mouseMove == null)
                    return;
                quickPan.SetValue(asd, true);
                mouseMove.Invoke(asd, new object[] {
                    asd,
                    new MouseEventArgs(ToMouseButtons(button), 0,
                        finalPoint.X, finalPoint.Y, 0)
                });
                quickPan.SetValue(asd, false);
                MethodInfo endInteractive = asd.GetType().GetMethod(
                    "EndInteractiveFunction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (endInteractive != null)
                    endInteractive.Invoke(asd, null);
            }
            catch { }
        }

        private static MouseButtons ToMouseButtons(PanMouseButton button)
        {
            switch (button)
            {
                case PanMouseButton.Left: return MouseButtons.Left;
                case PanMouseButton.Right: return MouseButtons.Right;
                case PanMouseButton.XButton1: return MouseButtons.XButton1;
                case PanMouseButton.XButton2: return MouseButtons.XButton2;
                default: return MouseButtons.Middle;
            }
        }

        private static void RestorePanAfterVatSys(Control asd, Point finalPoint, MouseButtons button)
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
                        new MouseEventArgs(button, 0, finalPoint.X, finalPoint.Y, 0)
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

            // SetZoom expects the range value, not RenderParams.Zoom. The third
            // argument tells vatSys to write the calculated zoom into the
            // render parameters. Without it only the internal range field
            // changes while the picture remains at the old scale.
            setZoom.Invoke(asd, new object[] { (double)next, true, true, false });
            int after = Convert.ToInt32(getRange.Invoke(asd, null));
            Log("Range " + current.ToString() + " -> " + after.ToString() + " requested " + next.ToString());

            // SetZoom updates the render parameters; vatSys's render loop will
            // consume them. Request a normal paint without calling Render()
            // directly from the UI callback.
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

    internal sealed class PersistentPanSettings
    {
        public bool Enabled { get; set; } = true;

        public bool DisableNativeZoomBox { get; set; }

        public Keys SettingsShortcut { get; set; }

        public PanMouseButton PanMouseButton { get; set; } = PanMouseButton.Middle;

        public static PersistentPanSettings Load(string path)
        {
            PersistentPanSettings result = new PersistentPanSettings();
            try
            {
                if (!File.Exists(path))
                    return result;

                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    string value = line.Trim();
                    if (value.StartsWith("DisableNativeZoomBox=", StringComparison.OrdinalIgnoreCase))
                    {
                        bool parsed;
                        if (bool.TryParse(value.Substring("DisableNativeZoomBox=".Length).Trim(), out parsed))
                            result.DisableNativeZoomBox = parsed;
                    }
                    else if (value.StartsWith("Enabled=", StringComparison.OrdinalIgnoreCase))
                    {
                        bool parsed;
                        if (bool.TryParse(value.Substring("Enabled=".Length).Trim(), out parsed))
                            result.Enabled = parsed;
                    }
                    else if (value.StartsWith("SettingsShortcut=", StringComparison.OrdinalIgnoreCase))
                    {
                        Keys parsed;
                        if (Enum.TryParse(value.Substring("SettingsShortcut=".Length).Trim(), true, out parsed))
                            result.SettingsShortcut = parsed;
                    }
                    else if (value.StartsWith("PanMouseButton=", StringComparison.OrdinalIgnoreCase))
                    {
                        PanMouseButton parsed;
                        if (Enum.TryParse(value.Substring("PanMouseButton=".Length).Trim(), true, out parsed))
                            result.PanMouseButton = parsed;
                    }
                }
            }
            catch { }
            return result;
        }

        public void Save(string path)
        {
            File.WriteAllLines(path, new[] {
                "# vatSys Persistent Pan settings",
                "Enabled=" + Enabled.ToString(),
                "DisableNativeZoomBox=" + DisableNativeZoomBox.ToString(),
                "SettingsShortcut=" + SettingsShortcut.ToString(),
                "PanMouseButton=" + PanMouseButton.ToString()
            });
        }
    }

    internal sealed class PanSettingsWindow : Form
    {
        private readonly PersistentPanSettings settings;
        private readonly string settingsPath;
        private readonly Action<string> log;
        private readonly CheckBox disableZoomBox;
        private readonly KeyCaptureButton shortcutButton;
        private readonly MouseCaptureButton panButton;

        public PanSettingsWindow(PersistentPanSettings settings, string settingsPath, Action<string> log)
        {
            this.settings = settings;
            this.settingsPath = settingsPath;
            this.log = log;

            Text = "Persistent Pan Settings";
            Name = "PersistentPanSettingsWindow";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 285);
            ShowInTaskbar = false;
            FormClosing += PanSettingsWindow_FormClosing;
            try
            {
                BackColor = vatsys.Colours.GetColour(vatsys.Colours.Identities.WindowBackground);
                ForeColor = vatsys.Colours.GetColour(vatsys.Colours.Identities.GenericText);
                Font = vatsys.MMI.eurofont_winsml;
            }
            catch
            {
                Font = SystemFonts.MessageBoxFont;
            }

            Label description = new Label();
            description.AutoSize = true;
            description.Location = new Point(15, 15);
            description.Font = new Font(Font, FontStyle.Bold);
            description.Text = "Persistent Pan Settings";

            disableZoomBox = new CheckBox();
            disableZoomBox.AutoSize = true;
            disableZoomBox.Location = new Point(18, 55);
            disableZoomBox.Text = "Disable native right-button zoom box";
            disableZoomBox.Checked = settings.DisableNativeZoomBox;
            disableZoomBox.CheckedChanged += (sender, args) => UpdateNativeZoomAvailability();

            Label hint = new Label();
            hint.AutoSize = true;
            hint.Location = new Point(18, 82);
            hint.Text = "When enabled, right-button drag on the ASD is ignored.";

            Label shortcutLabel = new Label();
            shortcutLabel.AutoSize = true;
            shortcutLabel.Location = new Point(18, 120);
            shortcutLabel.Text = "Settings window shortcut:";

            shortcutButton = new KeyCaptureButton(settings.SettingsShortcut);
            shortcutButton.Location = new Point(250, 113);
            shortcutButton.Size = new Size(185, 30);

            Label shortcutHint = new Label();
            shortcutHint.AutoSize = true;
            shortcutHint.Location = new Point(18, 155);
            shortcutHint.Text = "Click the button, then release a key combination. Middle-click clears it.";

            Label panLabel = new Label();
            panLabel.AutoSize = true;
            panLabel.Location = new Point(18, 190);
            panLabel.Text = "Pan drag mouse button:";

            panButton = new MouseCaptureButton(settings.PanMouseButton);
            panButton.Location = new Point(250, 183);
            panButton.Size = new Size(185, 30);
            panButton.MouseButtonChanged += (sender, args) => UpdateNativeZoomAvailability();

            Button save = new Button();
            save.Text = "Save";
            save.DialogResult = DialogResult.OK;
            save.Location = new Point(350, 245);
            save.Click += Save_Click;

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(437, 245);

            Controls.Add(description);
            Controls.Add(disableZoomBox);
            Controls.Add(hint);
            Controls.Add(shortcutLabel);
            Controls.Add(shortcutButton);
            Controls.Add(shortcutHint);
            Controls.Add(panLabel);
            Controls.Add(panButton);
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            UpdateNativeZoomAvailability();
        }

        private void UpdateNativeZoomAvailability()
        {
            bool rightButtonIsPan = panButton != null && panButton.MouseButton == PanMouseButton.Right;
            if (rightButtonIsPan)
            {
                disableZoomBox.Checked = true;
                disableZoomBox.Enabled = false;
                disableZoomBox.Text = "Native right-button zoom box (unavailable while Right Button is Pan)";
            }
            else
            {
                disableZoomBox.Enabled = true;
                disableZoomBox.Text = "Disable native right-button zoom box";
            }
        }

        private void PanSettingsWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            try
            {
                settings.DisableNativeZoomBox = disableZoomBox.Checked;
                settings.SettingsShortcut = shortcutButton.Shortcut;
                settings.PanMouseButton = panButton.MouseButton;
                settings.Save(settingsPath);
                if (log != null)
                    log("Settings saved; DisableNativeZoomBox=" + settings.DisableNativeZoomBox.ToString());
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class KeyCaptureButton : Button
    {
        private Keys keyValue;
        private Keys modifierValue;
        private bool learning;

        public Keys Shortcut { get; private set; }

        public KeyCaptureButton(Keys shortcut)
        {
            Shortcut = shortcut;
            Text = FormatShortcut(shortcut);
            TextAlign = ContentAlignment.MiddleCenter;
            TabStop = true;
            AutoSize = false;
            FlatStyle = FlatStyle.Standard;
            UseVisualStyleBackColor = false;
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.FromArgb(230, 230, 230);
            Font = new Font("Terminus (TTF)", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle rectangle = ClientRectangle;
            rectangle.Width--;
            rectangle.Height--;
            Color fill = Focused ? Color.FromArgb(58, 58, 58) : Color.FromArgb(32, 32, 32);
            Color border = Focused ? Color.FromArgb(230, 230, 230) : Color.FromArgb(120, 120, 120);
            using (SolidBrush brush = new SolidBrush(fill))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            using (Pen pen = new Pen(border))
                e.Graphics.DrawRectangle(pen, rectangle);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            keyValue = Keys.None;
            modifierValue = Keys.None;
            learning = true;
            Text = "Press a key...";
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (learning)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!learning)
                return;

            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                Shortcut = Keys.None;
                keyValue = Keys.None;
                modifierValue = Keys.None;
                learning = false;
            }
            else if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Control)
            {
                modifierValue |= Keys.Control;
            }
            else if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Shift)
            {
                modifierValue |= Keys.Shift;
            }
            else if (e.KeyCode == Keys.Menu || e.KeyCode == Keys.Alt)
            {
                modifierValue |= Keys.Alt;
            }
            else
            {
                keyValue = e.KeyCode;
                modifierValue |= e.Modifiers & Keys.Modifiers;
            }

            Text = keyValue == Keys.None ? "Press a key..." : FormatShortcut(keyValue | modifierValue);
            Invalidate();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (!learning)
                return;

            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                Shortcut = Keys.None;
            }
            else if (keyValue != Keys.None)
            {
                Shortcut = keyValue | modifierValue | (e.Modifiers & Keys.Modifiers);
            }
            else
            {
                return;
            }

            learning = false;
            keyValue = Keys.None;
            modifierValue = Keys.None;
            Text = FormatShortcut(Shortcut);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                Shortcut = Keys.None;
                Text = FormatShortcut(Shortcut);
                learning = false;
                keyValue = Keys.None;
                modifierValue = Keys.None;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        private static string FormatShortcut(Keys shortcut)
        {
            if (shortcut == Keys.None)
                return "None";

            Keys code = shortcut & Keys.KeyCode;
            Keys modifiers = shortcut & Keys.Modifiers;
            string prefix = "";
            if ((modifiers & Keys.Control) != Keys.None)
                prefix += "Ctrl ";
            if ((modifiers & Keys.Shift) != Keys.None)
                prefix += "Shift ";
            if ((modifiers & Keys.Alt) != Keys.None)
                prefix += "Alt ";
            return prefix + code.ToString();
        }
    }

    internal enum PanMouseButton
    {
        Middle,
        Left,
        Right,
        XButton1,
        XButton2
    }

    internal sealed class MouseCaptureButton : Button
    {
        public PanMouseButton MouseButton { get; private set; }
        public event EventHandler MouseButtonChanged;

        public MouseCaptureButton(PanMouseButton button)
        {
            MouseButton = button;
            Text = FormatButton(button);
            TextAlign = ContentAlignment.MiddleCenter;
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.FromArgb(230, 230, 230);
            FlatStyle = FlatStyle.Standard;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Text = "Press mouse button...";
            Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                MouseButton = PanMouseButton.Middle;
                Text = FormatButton(MouseButton);
                MouseButtonChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (e.Button == MouseButtons.Left && Text == "Press mouse button...")
            {
                MouseButton = PanMouseButton.Left;
                Text = FormatButton(MouseButton);
                MouseButtonChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            MouseButton = FromMouseButtons(e.Button);
            Text = FormatButton(MouseButton);
            MouseButtonChanged?.Invoke(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        private static PanMouseButton FromMouseButtons(MouseButtons button)
        {
            switch (button)
            {
                case MouseButtons.Left: return PanMouseButton.Left;
                case MouseButtons.Right: return PanMouseButton.Right;
                case MouseButtons.XButton1: return PanMouseButton.XButton1;
                case MouseButtons.XButton2: return PanMouseButton.XButton2;
                default: return PanMouseButton.Middle;
            }
        }

        private static string FormatButton(PanMouseButton button)
        {
            switch (button)
            {
                case PanMouseButton.Left: return "Left Button";
                case PanMouseButton.Right: return "Right Button";
                case PanMouseButton.XButton1: return "XButton 1";
                case PanMouseButton.XButton2: return "XButton 2";
                default: return "Middle Button";
            }
        }
    }
}

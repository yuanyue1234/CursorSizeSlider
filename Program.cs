using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CursorSizeSlider;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (TryHandleCli(args, out var exitCode))
            {
                return exitCode;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CursorSizeForm());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cursor Size Slider", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool TryHandleCli(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0)
        {
            return false;
        }

        if (args.Length == 1 && args[0].Equals("--get", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(CursorController.ReadSnapshot());
            return true;
        }

        if (args.Length == 2 && args[0].Equals("--set", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args[1], out var size))
            {
                Console.Error.WriteLine("Usage: CursorSizeSlider.exe --set <1-256>");
                exitCode = 2;
                return true;
            }

            CursorController.Apply(size);
            Console.WriteLine(CursorController.ReadSnapshot());
            return true;
        }

        Console.Error.WriteLine("Usage: CursorSizeSlider.exe [--get | --set <1-256>]");
        exitCode = 2;
        return true;
    }
}

internal sealed class CursorSizeForm : Form
{
    private readonly TrackBar sizeTrackBar = new();
    private readonly NumericUpDown sizeBox = new();
    private readonly Label titleLabel = new();
    private readonly Label valueLabel = new();
    private readonly Label registryLabel = new();
    private readonly Label statusLabel = new();
    private readonly CheckBox liveApplyBox = new();
    private readonly Button applyButton = new();
    private readonly Button resetButton = new();
    private readonly Button reloadButton = new();
    private readonly System.Windows.Forms.Timer applyTimer = new();
    private bool syncing;
    private int lastAppliedSize;

    public CursorSizeForm()
    {
        Text = "鼠标大小滑块";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        ClientSize = new Size(560, 330);
        MinimumSize = new Size(520, 330);
        MaximizeBox = false;

        titleLabel.Text = "拖动滑块实时切换鼠标指针大小";
        titleLabel.AutoSize = true;
        titleLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        titleLabel.Location = new Point(24, 22);

        valueLabel.AutoSize = true;
        valueLabel.Location = new Point(24, 70);

        sizeTrackBar.Minimum = CursorController.MinSize;
        sizeTrackBar.Maximum = CursorController.MaxSize;
        sizeTrackBar.TickFrequency = 16;
        sizeTrackBar.SmallChange = 1;
        sizeTrackBar.LargeChange = 16;
        sizeTrackBar.Location = new Point(20, 102);
        sizeTrackBar.Size = new Size(420, 56);
        sizeTrackBar.ValueChanged += (_, _) => OnSizeControlChanged(sizeTrackBar.Value, fromTrackBar: true);

        sizeBox.Minimum = CursorController.MinSize;
        sizeBox.Maximum = CursorController.MaxSize;
        sizeBox.Location = new Point(455, 107);
        sizeBox.Width = 76;
        sizeBox.ValueChanged += (_, _) => OnSizeControlChanged((int)sizeBox.Value, fromTrackBar: false);

        registryLabel.AutoSize = false;
        registryLabel.Location = new Point(24, 165);
        registryLabel.Size = new Size(510, 52);

        liveApplyBox.Text = "滑动时立即应用";
        liveApplyBox.Checked = true;
        liveApplyBox.AutoSize = true;
        liveApplyBox.Location = new Point(24, 224);

        applyButton.Text = "应用当前值";
        applyButton.Size = new Size(116, 34);
        applyButton.Location = new Point(24, 264);
        applyButton.Click += (_, _) => ApplySelectedSize(showAlreadyApplied: true);

        resetButton.Text = "恢复 32";
        resetButton.Size = new Size(100, 34);
        resetButton.Location = new Point(152, 264);
        resetButton.Click += (_, _) => SetSelectedSize(32, apply: true);

        reloadButton.Text = "重新读取";
        reloadButton.Size = new Size(100, 34);
        reloadButton.Location = new Point(264, 264);
        reloadButton.Click += (_, _) => LoadCurrentSize();

        statusLabel.AutoSize = false;
        statusLabel.Location = new Point(376, 259);
        statusLabel.Size = new Size(156, 46);
        statusLabel.TextAlign = ContentAlignment.MiddleRight;

        applyTimer.Interval = 120;
        applyTimer.Tick += (_, _) =>
        {
            applyTimer.Stop();
            ApplySelectedSize(showAlreadyApplied: false);
        };

        Controls.AddRange(new Control[]
        {
            titleLabel,
            valueLabel,
            sizeTrackBar,
            sizeBox,
            registryLabel,
            liveApplyBox,
            applyButton,
            resetButton,
            reloadButton,
            statusLabel
        });

        LoadCurrentSize();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmSettingChange)
        {
            UpdateLabels((int)sizeBox.Value);
        }

        base.WndProc(ref m);
    }

    private void LoadCurrentSize()
    {
        var snapshot = CursorController.ReadSnapshot();
        SetSelectedSize(snapshot.BaseSize, apply: false);
        statusLabel.Text = "已读取当前设置";
    }

    private void OnSizeControlChanged(int size, bool fromTrackBar)
    {
        if (syncing)
        {
            return;
        }

        syncing = true;
        if (fromTrackBar)
        {
            sizeBox.Value = size;
        }
        else
        {
            sizeTrackBar.Value = size;
        }
        syncing = false;

        UpdateLabels(size);
        if (liveApplyBox.Checked)
        {
            applyTimer.Stop();
            applyTimer.Start();
        }
    }

    private void SetSelectedSize(int size, bool apply)
    {
        size = CursorController.Clamp(size);
        syncing = true;
        sizeTrackBar.Value = size;
        sizeBox.Value = size;
        syncing = false;
        UpdateLabels(size);

        if (apply)
        {
            ApplySelectedSize(showAlreadyApplied: true);
        }
    }

    private void UpdateLabels(int size)
    {
        var accessibilityStep = CursorController.ToAccessibilityStep(size);
        valueLabel.Text = $"当前选择：{size} px    设置页档位：{accessibilityStep}";
        registryLabel.Text =
            "写入位置：HKCU\\Control Panel\\Cursors\\CursorBaseSize" + Environment.NewLine +
            "同步位置：HKCU\\SOFTWARE\\Microsoft\\Accessibility\\CursorSize（32 以下会显示为第 1 档）";
    }

    private void ApplySelectedSize(bool showAlreadyApplied)
    {
        var size = (int)sizeBox.Value;
        if (!showAlreadyApplied && size == lastAppliedSize)
        {
            return;
        }

        try
        {
            CursorController.Apply(size);
            lastAppliedSize = size;
            statusLabel.Text = $"已应用 {size}px";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "应用失败";
            MessageBox.Show(ex.Message, "应用鼠标大小失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal readonly record struct CursorSnapshot(int BaseSize, int AccessibilityStep)
{
    public override string ToString()
    {
        return $"CursorBaseSize={BaseSize}; CursorSize={AccessibilityStep}";
    }
}

internal static class CursorController
{
    public const int MinSize = 1;
    public const int MaxSize = 256;
    private const string CursorKeyPath = @"Control Panel\Cursors";
    private const string AccessibilityKeyPath = @"SOFTWARE\Microsoft\Accessibility";
    private const string CursorBaseSizeValue = "CursorBaseSize";
    private const string AccessibilitySizeValue = "CursorSize";

    public static int Clamp(int size)
    {
        return Math.Min(MaxSize, Math.Max(MinSize, size));
    }

    public static void Apply(int requestedSize)
    {
        var size = Clamp(requestedSize);
        var accessibilityStep = ToAccessibilityStep(size);
        WriteRegistry(size, accessibilityStep);

        if (!NativeMethods.SetCursorBaseSize(size))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SystemParametersInfo(0x2029) 调用失败。");
        }

        NativeMethods.BroadcastSettingsChange(CursorKeyPath);
        NativeMethods.BroadcastSettingsChange(AccessibilityKeyPath);
    }

    public static CursorSnapshot ReadSnapshot()
    {
        var baseSize = ReadDword(CursorKeyPath, CursorBaseSizeValue, 32);
        var accessibilityStep = ReadDword(AccessibilityKeyPath, AccessibilitySizeValue, ToAccessibilityStep(baseSize));
        return new CursorSnapshot(Clamp(baseSize), Math.Min(15, Math.Max(1, accessibilityStep)));
    }

    public static int ToAccessibilityStep(int baseSize)
    {
        if (baseSize <= 32)
        {
            return 1;
        }

        if (baseSize >= 256)
        {
            return 15;
        }

        return Math.Min(15, Math.Max(1, (int)Math.Round((baseSize - 32) / 16.0, MidpointRounding.AwayFromZero) + 1));
    }

    private static void WriteRegistry(int baseSize, int accessibilityStep)
    {
        using var cursorKey = Registry.CurrentUser.CreateSubKey(CursorKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 CursorBaseSize 注册表项。");
        cursorKey.SetValue(CursorBaseSizeValue, baseSize, RegistryValueKind.DWord);

        using var accessibilityKey = Registry.CurrentUser.CreateSubKey(AccessibilityKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 CursorSize 注册表项。");
        accessibilityKey.SetValue(AccessibilitySizeValue, accessibilityStep, RegistryValueKind.DWord);
    }

    private static int ReadDword(string keyPath, string valueName, int fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        var value = key?.GetValue(valueName);
        return value switch
        {
            int intValue => intValue,
            long longValue => unchecked((int)longValue),
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => fallback
        };
    }
}

internal static class NativeMethods
{
    public const int WmSettingChange = 0x001A;
    private const uint SpiSetCursorBaseSize = 0x2029;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;
    private static readonly IntPtr HwndBroadcast = new(0xffff);

    public static bool SetCursorBaseSize(int size)
    {
        return SystemParametersInfo(SpiSetCursorBaseSize, 0, new IntPtr(size), SpifUpdateIniFile | SpifSendChange);
    }

    public static void BroadcastSettingsChange(string area)
    {
        _ = SendNotifyMessage(HwndBroadcast, WmSettingChange, IntPtr.Zero, area);
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SendNotifyMessageW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
}

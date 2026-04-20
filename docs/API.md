# Cursor Size API 调用说明

本文档说明 `CursorSizeSlider` 如何在 Windows 上读取和设置鼠标指针大小，以及如何在其他程序中复用相同思路。

## 适用范围

- 系统：Windows
- 权限：普通用户即可
- 作用范围：当前用户，写入 `HKEY_CURRENT_USER`
- 语言示例：C# / Win32 API

## 核心结论

Windows 鼠标指针大小不是只改一个注册表值就能立即生效。可靠做法是：

1. 写入实际像素大小：
   `HKCU\Control Panel\Cursors\CursorBaseSize`
2. 同步 Windows 辅助功能设置页档位：
   `HKCU\SOFTWARE\Microsoft\Accessibility\CursorSize`
3. 调用 Win32 API：
   `SystemParametersInfoW(SPI_SETCURSORBASESIZE, 0, size, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)`
4. 广播设置变化：
   `WM_SETTINGCHANGE`

本项目把这些步骤封装在 `CursorController.Apply(int requestedSize)` 中。

## 命令行 API

打包程序支持两个命令行参数，方便被其他程序调用。

### 读取当前大小

```powershell
CursorSizeSlider.exe --get
```

输出示例：

```text
CursorBaseSize=64; CursorSize=3
```

含义：

- `CursorBaseSize`：实际像素大小。
- `CursorSize`：Windows 设置页使用的 1-15 档。

### 设置鼠标大小

```powershell
CursorSizeSlider.exe --set 64
```

参数范围：

```text
1-256
```

设置成功后会再次输出当前状态：

```text
CursorBaseSize=64; CursorSize=3
```

### 退出码

```text
0  成功
1  程序运行异常
2  命令行参数错误
```

## 注册表项

### CursorBaseSize

路径：

```text
HKEY_CURRENT_USER\Control Panel\Cursors
```

值名：

```text
CursorBaseSize
```

类型：

```text
REG_DWORD
```

含义：实际鼠标指针像素大小。

示例：

```text
32
64
128
256
```

### CursorSize

路径：

```text
HKEY_CURRENT_USER\SOFTWARE\Microsoft\Accessibility
```

值名：

```text
CursorSize
```

类型：

```text
REG_DWORD
```

含义：Windows 设置页鼠标指针大小的 1-15 档。

## 像素到档位的转换

Windows 设置页通常使用 1-15 档，近似对应：

```text
1  -> 32px
2  -> 48px
3  -> 64px
4  -> 80px
5  -> 96px
6  -> 112px
7  -> 128px
8  -> 144px
9  -> 160px
10 -> 176px
11 -> 192px
12 -> 208px
13 -> 224px
14 -> 240px
15 -> 256px
```

项目中的转换函数：

```csharp
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

    return Math.Min(
        15,
        Math.Max(
            1,
            (int)Math.Round((baseSize - 32) / 16.0, MidpointRounding.AwayFromZero) + 1
        )
    );
}
```

## Win32 API

### SystemParametersInfoW

声明：

```csharp
[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool SystemParametersInfo(
    uint uiAction,
    uint uiParam,
    IntPtr pvParam,
    uint fWinIni
);
```

本项目使用：

```csharp
private const uint SpiSetCursorBaseSize = 0x2029;
private const uint SpifUpdateIniFile = 0x01;
private const uint SpifSendChange = 0x02;

SystemParametersInfo(
    SpiSetCursorBaseSize,
    0,
    new IntPtr(size),
    SpifUpdateIniFile | SpifSendChange
);
```

参数说明：

- `0x2029`：设置鼠标指针基础大小，本项目命名为 `SPI_SETCURSORBASESIZE`。
- `uiParam = 0`：该调用不使用此参数。
- `pvParam = new IntPtr(size)`：传入目标像素大小。
- `0x01 | 0x02`：写入用户配置并通知系统设置变化。

注意：`0x2029` 不是常见公开文档中经常出现的常量名，但在实测中可用于更新 Windows 鼠标指针大小。

### SendNotifyMessageW

声明：

```csharp
[DllImport("user32.dll", EntryPoint = "SendNotifyMessageW", SetLastError = true, CharSet = CharSet.Unicode)]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool SendNotifyMessage(
    IntPtr hWnd,
    int msg,
    IntPtr wParam,
    string lParam
);
```

本项目使用：

```csharp
private static readonly IntPtr HwndBroadcast = new(0xffff);
public const int WmSettingChange = 0x001A;

SendNotifyMessage(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Control Panel\\Cursors");
SendNotifyMessage(HwndBroadcast, WmSettingChange, IntPtr.Zero, "SOFTWARE\\Microsoft\\Accessibility");
```

作用：

- 通知系统和其他程序鼠标设置已经变化。
- 避免只写注册表但界面或当前鼠标指针没有立即刷新的问题。

## 完整调用流程

源码位置：`Program.cs`

```csharp
public static void Apply(int requestedSize)
{
    var size = Clamp(requestedSize);
    var accessibilityStep = ToAccessibilityStep(size);
    WriteRegistry(size, accessibilityStep);

    if (!NativeMethods.SetCursorBaseSize(size))
    {
        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            "SystemParametersInfo(0x2029) 调用失败。"
        );
    }

    NativeMethods.BroadcastSettingsChange(CursorKeyPath);
    NativeMethods.BroadcastSettingsChange(AccessibilityKeyPath);
}
```

执行顺序：

1. 限制输入范围到 `1-256`。
2. 根据像素值计算 Windows 设置页档位。
3. 写入两个注册表项。
4. 调用 `SystemParametersInfoW`。
5. 广播设置变化。

## C# 最小示例

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

static void SetCursorSize(int size)
{
    size = Math.Min(256, Math.Max(1, size));
    var step = ToAccessibilityStep(size);

    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors", true))
    {
        key!.SetValue("CursorBaseSize", size, RegistryValueKind.DWord);
    }

    using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Accessibility", true))
    {
        key!.SetValue("CursorSize", step, RegistryValueKind.DWord);
    }

    if (!SystemParametersInfo(0x2029, 0, new IntPtr(size), 0x01 | 0x02))
    {
        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    SendNotifyMessage(new IntPtr(0xffff), 0x001A, IntPtr.Zero, @"Control Panel\Cursors");
    SendNotifyMessage(new IntPtr(0xffff), 0x001A, IntPtr.Zero, @"SOFTWARE\Microsoft\Accessibility");
}

static int ToAccessibilityStep(int baseSize)
{
    if (baseSize <= 32) return 1;
    if (baseSize >= 256) return 15;
    return Math.Min(15, Math.Max(1, (int)Math.Round((baseSize - 32) / 16.0) + 1));
}

[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

[DllImport("user32.dll", EntryPoint = "SendNotifyMessageW", SetLastError = true, CharSet = CharSet.Unicode)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SendNotifyMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
```

## 从 Python 调用已打包程序

如果不想在 Python 中直接写 Win32 API，可以调用本项目的 exe：

```python
import subprocess
from pathlib import Path

tool = Path("publish") / "CursorSizeSlider.exe"

subprocess.run([str(tool), "--set", "64"], check=True)

result = subprocess.run(
    [str(tool), "--get"],
    text=True,
    capture_output=True,
    check=True,
)
print(result.stdout.strip())
```

优点：

- Python 侧不用维护注册表和 Win32 API 细节。
- 可直接复用本项目验证过的调用流程。

## 常见问题

### 是否需要管理员权限？

不需要。项目写入的是 `HKEY_CURRENT_USER`，普通用户权限即可。

### 为什么不能只改注册表？

只改注册表通常不会立刻刷新当前鼠标指针。需要调用 `SystemParametersInfoW` 并广播 `WM_SETTINGCHANGE`。

### 为什么还要写 Accessibility 的 CursorSize？

`CursorBaseSize` 控制实际大小，`Accessibility\CursorSize` 用于同步 Windows 设置页显示的档位。两个值同时写，系统界面和实际效果更一致。

### 小于 32px 有什么区别？

小于 32px 时，设置页仍会显示第 1 档，但 `CursorBaseSize` 可以保存更小的实际值。实际显示效果取决于 Windows 当前版本和指针资源。

### 是否会影响所有用户？

不会。写入的是 `HKCU`，只影响当前 Windows 用户。

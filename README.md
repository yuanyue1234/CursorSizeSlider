# Cursor Size Slider

一个 Windows 鼠标指针大小调整工具。它提供图形界面和命令行两种用法，可以直接修改系统鼠标指针大小。

## 已打包程序

仓库附带已发布好的程序，位置：

```text
publish\CursorSizeSlider.exe
```

这是 .NET 10 Windows 桌面应用的框架依赖发布版本，同目录下的 `.dll`、`.deps.json`、`.runtimeconfig.json` 需要和 exe 放在一起。目标电脑需要安装 .NET 10 Desktop Runtime。

## 功能

- 图形界面滑块调整鼠标指针大小。
- 支持“滑动时立即应用”。
- 支持命令行读取当前大小。
- 支持命令行设置指定像素大小。
- 同步写入 Windows 鼠标大小相关注册表项。
- 调用 Win32 API 让大小立即生效。

## API 原理

核心逻辑见：

- [docs/API.md](docs/API.md)
- `Program.cs` 中的 `CursorController`
- `Program.cs` 中的 `NativeMethods`

程序同时做三件事：

- 写入 `HKCU\Control Panel\Cursors\CursorBaseSize`，保存实际像素大小。
- 写入 `HKCU\SOFTWARE\Microsoft\Accessibility\CursorSize`，同步 Windows 设置页的 1-15 档。
- 调用 `SystemParametersInfoW(0x2029, 0, size, 0x03)`，让系统立即应用新的鼠标指针大小。

## 使用

图形界面：

```powershell
.\publish\CursorSizeSlider.exe
```

命令行读取当前大小：

```powershell
.\publish\CursorSizeSlider.exe --get
```

命令行设置大小：

```powershell
.\publish\CursorSizeSlider.exe --set 32
.\publish\CursorSizeSlider.exe --set 64
.\publish\CursorSizeSlider.exe --set 128
```

恢复常见默认大小：

```powershell
.\publish\CursorSizeSlider.exe --set 32
```

## 参数范围

程序允许传入 `1-256` 的像素值。

Windows 设置页的鼠标大小通常是 1-15 档，对应关系大致为：

```text
1  -> 32px
2  -> 48px
3  -> 64px
4  -> 80px
...
15 -> 256px
```

小于 32 的值会同步为设置页第 1 档，但 `CursorBaseSize` 仍会保存传入的实际像素值。

## 开发

要求：

- Windows
- .NET SDK 10

运行源码：

```powershell
dotnet run -c Release
```

发布：

```powershell
dotnet publish -c Release -o publish
```

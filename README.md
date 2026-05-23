# PrankTools

三个无害的 Windows 桌面整蛊工具，用于假装电脑出问题。

## 工具列表

| 工具 | 效果 | 退出方式 |
|---|---|---|
| **FakeBSOD** | 全屏 Win10 蓝屏 `: (` + 假错误码 `CRITICAL_PROCESS_DIED` | `Ctrl + Shift + F12` |
| **MouseDrift** | 光标缓慢随机漂移，像触控板进水 | `Ctrl + Shift + F12` |
| **FakeUpdate** | 主屏 Win11 风格"正在更新…"动画 + 副屏纯黑 | `Ctrl + Shift + F12` |

## 构建

需要 .NET 9 SDK。

```bash
cd FakeBSOD    && dotnet publish -c Release -o publish
cd MouseDrift  && dotnet publish -c Release -o publish
cd FakeUpdate  && dotnet publish -c Release -o publish
```

可执行文件在各自 `publish/` 目录下。

## 技术细节

- C# WinForms (.NET 9)
- 全屏覆盖多显示器（`Screen.AllScreens` 手动枚举）
- 低层键盘钩子封锁 Alt+F4 / Alt+Tab / Win 键 / Esc
- `HWND_TOPMOST` + 定时器暴力置顶保前台
- 退出快捷键在键盘钩子中直接检测，不依赖窗口焦点

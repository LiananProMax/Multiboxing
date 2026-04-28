# KeyMouseSyncReplica

C# WinForms 复刻版多窗口键鼠同步器。

## 运行

构建输出位于：

`replica\bin\x86\Release\net9.0-windows\win-x86\KeyMouseSyncReplica.exe`

程序已配置为启动时请求管理员权限。大漠后台绑定通常需要和目标窗口同等或更高权限，尤其是游戏客户端；如果不以管理员运行，`BindWindow` 很容易返回 `0`。

输出目录会同时复制：

- `配置.ini`
- `dm3.1233.dll`
- `dm5.1423.dll`
- `dm6.1544.dll`

## 使用流程

1. 以管理员权限启动程序后选择一个 `dm*.dll`。
2. 程序会优先从所选 `dm*.dll` 直加载创建对象；如果直加载失败，才退回系统已注册的大漠 COM。`注册dm` 只作为备用排障按钮。
3. 在右侧 `绑定配置` 中确认 `display=normal`、`mouse=windows`、`keypad=windows`、`mode=0`；旧配置里的 `mouse/keypad=0` 会自动按 `windows` 处理。必要时填写 `public`。
4. 按住左侧十字图标，拖到目标窗口后松开，即可把窗口加入列表；也可以把鼠标移动到目标窗口上后点击 `添加鼠标所在窗口`。
5. 选择列表中的源窗口，点击 `设置为操作窗口`。
6. 勾选键盘/鼠标同步，点击 `开启同步`。

## 说明

这个版本按反编译结果复刻原程序的界面、配置和状态机，并继续调用大漠 COM。为了增强可用性，程序还包含一层 Windows 消息级同步兜底，用于把主操作窗口上的键鼠事件转发到目标窗口。

原程序使用易语言/黑月与 `YunDm.fne` 的免注册调用能力；C# 版会优先用 `LoadLibrary + DllGetClassObject` 模拟免注册创建，再退回 x86 COM 互操作方式。DX 后台模式仍可能需要管理员权限。

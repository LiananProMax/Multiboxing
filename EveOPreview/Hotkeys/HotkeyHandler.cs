#nullable disable
using System;
using System.Windows.Forms;
using System.ComponentModel;
using System.Windows.Interop;

namespace EveOPreview.UI.Hotkeys
{
	class HotkeyHandler : IDisposable
	{
		private static int _currentId;
		private const int MAX_ID = 0xBFFF;

		private readonly int _hotkeyId;
		private readonly IntPtr _hotkeyTarget;
		private readonly ThreadMessageEventHandler _messageHandler;
		private bool _isHooked;

		public HotkeyHandler(IntPtr target, Keys hotkey)
		{
			this._hotkeyId = HotkeyHandler._currentId;
			HotkeyHandler._currentId = (HotkeyHandler._currentId + 1) & HotkeyHandler.MAX_ID;
			this._hotkeyTarget = target;
			this.IsRegistered = false;
			this.KeyCode = hotkey;
			this._messageHandler = this.OnThreadPreprocessMessage;
		}

		public void Dispose()
		{
			this.Unregister();
			GC.SuppressFinalize(this);
		}

		~HotkeyHandler() { this.Unregister(); }

		public bool IsRegistered { get; private set; }
		public Keys KeyCode { get; private set; }
		public event HandledEventHandler Pressed;

		public bool CanRegister()
		{
			if (this.Register()) { this.Unregister(); return true; }
			return false;
		}

		public bool Register()
		{
			if (this.IsRegistered) return false;
			if (this.KeyCode == Keys.None) return false;

			uint key = (uint)this.KeyCode & (~(uint)Keys.Alt) & (~(uint)Keys.Control) & (~(uint)Keys.Shift);
			uint modifiers = (this.KeyCode.HasFlag(Keys.Alt) ? HotkeyHandlerNativeMethods.MOD_ALT : 0)
							 | (this.KeyCode.HasFlag(Keys.Control) ? HotkeyHandlerNativeMethods.MOD_CONTROL : 0)
							 | (this.KeyCode.HasFlag(Keys.Shift) ? HotkeyHandlerNativeMethods.MOD_SHIFT : 0);

			if (!HotkeyHandlerNativeMethods.RegisterHotKey(this._hotkeyTarget, this._hotkeyId, modifiers, key))
			{
				return false;
			}

			// WPF 消息循环下 WinForms 的 IMessageFilter 不会被调用，改用 ComponentDispatcher。
			// 它在线程消息分发前触发，对 RegisterHotKey(NULL) 与窗口句柄两种目标都有效。
			if (!this._isHooked)
			{
				ComponentDispatcher.ThreadPreprocessMessage += this._messageHandler;
				this._isHooked = true;
			}
			this.IsRegistered = true;
			return true;
		}

		public void Unregister()
		{
			if (this._isHooked)
			{
				try { ComponentDispatcher.ThreadPreprocessMessage -= this._messageHandler; } catch { }
				this._isHooked = false;
			}
			if (!this.IsRegistered) return;
			this.IsRegistered = false;
			HotkeyHandlerNativeMethods.UnregisterHotKey(this._hotkeyTarget, this._hotkeyId);
		}

		private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
		{
			if (handled || !this.IsRegistered) return;
			if (msg.message != (int)HotkeyHandlerNativeMethods.WM_HOTKEY) return;
			if (msg.wParam.ToInt32() != this._hotkeyId) return;
			if (this.OnPressed()) handled = true;
		}

		private bool OnPressed()
		{
			HandledEventArgs handledEventArgs = new HandledEventArgs(false);
			this.Pressed?.Invoke(this, handledEventArgs);
			return handledEventArgs.Handled;
		}
	}
}

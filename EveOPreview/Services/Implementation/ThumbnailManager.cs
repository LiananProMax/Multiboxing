#nullable disable
using EveOPreview.Configuration;
using EveOPreview.Mediator.Messages;
using EveOPreview.Services.Interop;
using EveOPreview.UI.Hotkeys;
using EveOPreview.View;
using MediatR;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;

namespace EveOPreview.Services
{
	sealed class ThumbnailManager : IThumbnailManager
	{
		#region Private constants
		private const int WINDOW_POSITION_THRESHOLD_LOW = -10_000;
		private const int WINDOW_POSITION_THRESHOLD_HIGH = 31_000;
		private const int WINDOW_SIZE_THRESHOLD = 10;
		private const int FORCED_REFRESH_CYCLE_THRESHOLD = 2;
		private const int DEFAULT_LOCATION_CHANGE_NOTIFICATION_DELAY = 2;
		private const int DEFAULT_SIZE_CHANGE_SAVE_DELAY = 2;

		// 倍数模式暂时读不到源窗口时的折算基准，与旧配置迁移用的 1080p 一致
		private const int FALLBACK_SOURCE_WIDTH = 1920;
		private const int FALLBACK_SOURCE_HEIGHT = 1080;

		private const string DEFAULT_CLIENT_TITLE = "EVE";
		#endregion

		#region Private fields
		private readonly IMediator _mediator;
		private readonly IProcessMonitor _processMonitor;
		private readonly IWindowManager _windowManager;
		private readonly IThumbnailConfiguration _configuration;
		private readonly DispatcherTimer _thumbnailUpdateTimer;
		private readonly IThumbnailViewFactory _thumbnailViewFactory;
		private readonly ISyncTargetRegistry _syncTargetRegistry;
		private readonly Dictionary<IntPtr, IThumbnailView> _thumbnailViews;
		private readonly Dictionary<IntPtr, Size> _sourceSizes;

		private (IntPtr Handle, string Title) _activeClient;
		private IntPtr _externalApplication;

		private readonly object _locationChangeNotificationSyncRoot;
		private (IntPtr Handle, string Title, string ActiveClient, Point Location, int Delay) _enqueuedLocationChangeNotification;

		// -1 表示没有待保存的尺寸变化。拖拽停手后再等几个刷新周期，避免每次鼠标移动都写盘
		private int _sizeChangeSaveDelay = -1;

		private bool _ignoreViewEvents;
		private bool _isHoverEffectActive;

		private int _refreshCycleCount;
		private int _hideThumbnailsDelay;

		private List<HotkeyHandler> _cycleClientHotkeyHandlers = new List<HotkeyHandler>();
		#endregion

		public ThumbnailManager(IMediator mediator, IThumbnailConfiguration configuration, IProcessMonitor processMonitor, IWindowManager windowManager, IThumbnailViewFactory factory, ISyncTargetRegistry syncTargetRegistry)
		{
			this._mediator = mediator;
			this._processMonitor = processMonitor;
			this._windowManager = windowManager;
			this._configuration = configuration;
			this._thumbnailViewFactory = factory;
			this._syncTargetRegistry = syncTargetRegistry;

			this._activeClient = (IntPtr.Zero, ThumbnailManager.DEFAULT_CLIENT_TITLE);

			this.EnableViewEvents();
			this._isHoverEffectActive = false;

			this._refreshCycleCount = 0;
			this._locationChangeNotificationSyncRoot = new object();
			this._enqueuedLocationChangeNotification = (IntPtr.Zero, null, null, Point.Empty, -1);

			this._thumbnailViews = new Dictionary<IntPtr, IThumbnailView>();
			this._sourceSizes = new Dictionary<IntPtr, Size>();

			//  DispatcherTimer setup
			this._thumbnailUpdateTimer = new DispatcherTimer();
			this._thumbnailUpdateTimer.Tick += ThumbnailUpdateTimerTick;
			this._thumbnailUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, configuration.ThumbnailRefreshPeriod);

			this._hideThumbnailsDelay = this._configuration.HideThumbnailsDelay;

			RegisterCycleClientHotkey(this._configuration.CycleGroup1ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup1ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup1BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup1ClientsOrder);

			RegisterCycleClientHotkey(this._configuration.CycleGroup2ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup2ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup2BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup2ClientsOrder);

			RegisterCycleClientHotkey(this._configuration.CycleGroup3ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup3ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup3BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup3ClientsOrder);

			RegisterCycleClientHotkey(this._configuration.CycleGroup4ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup4ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup4BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup4ClientsOrder);

			RegisterCycleClientHotkey(this._configuration.CycleGroup5ForwardHotkeys?.Select(x => this._configuration.StringToKey(x)), true, this._configuration.CycleGroup5ClientsOrder);
			RegisterCycleClientHotkey(this._configuration.CycleGroup5BackwardHotkeys?.Select(x => this._configuration.StringToKey(x)), false, this._configuration.CycleGroup5ClientsOrder);

			RegisterMinimizeAllClientsHotkey(this._configuration.MinimizeAllClientsHotkeys?.Select(x => this._configuration.StringToKey(x)));
		}

		public IThumbnailView GetClientByTitle(string title)
		{
			return _thumbnailViews.FirstOrDefault(x => x.Value.Title == title).Value;
		}

		public IThumbnailView GetClientByPointer(IntPtr ptr)
		{
			return _thumbnailViews.FirstOrDefault(x => x.Key == ptr).Value;
		}

		public IThumbnailView GetActiveClient()
		{
			return GetClientByPointer(this._activeClient.Handle);
		}

		public void SetActive(KeyValuePair<IntPtr, IThumbnailView> newClient)
		{
			this.GetActiveClient()?.ClearBorder();
#if LINUX
			this._windowManager.ActivateWindow(newClient.Key, newClient.Value.Title);
#else
			this._windowManager.ActivateWindow(newClient.Key, this._configuration.WindowsAnimationStyle);
#endif
			this.SwitchActiveClient(newClient.Key, newClient.Value.Title);

			newClient.Value.SetHighlight();
			newClient.Value.Refresh(true);
		}

		public void MinimizeAllClients()
		{
			foreach (var x in _thumbnailViews.Reverse())
			{
				this._windowManager.MinimizeWindow(x.Value.Id, this._configuration.WindowsAnimationStyle, false);
			}
		}
		public void CycleNextClient(bool isForwards, Dictionary<string, int> cycleOrder)
		{
			IOrderedEnumerable<KeyValuePair<string, int>> clientOrder;
			Dictionary<string, int> _cycleOrder = new Dictionary<string, int>(cycleOrder);

			if ( _cycleOrder.Count == 0 ) 
			{
				int order = 0;
				foreach( var x in _thumbnailViews )
				{
					_cycleOrder.Add(x.Value.Title, order++);
				}
			}

			if (isForwards)
			{
				clientOrder = _cycleOrder.OrderBy(x => x.Value);
			}
			else
			{
				clientOrder = _cycleOrder.OrderByDescending(x => x.Value);
			}

			bool setNextClient = false;
			IThumbnailView lastClient = null;

			foreach (var t in clientOrder)
			{
				if (t.Key == _activeClient.Title && t.Key != DEFAULT_CLIENT_TITLE)
				{
					setNextClient = true;
					lastClient = _thumbnailViews.FirstOrDefault(x => x.Value.Title == t.Key).Value;
					continue;
				}

				// cycle through login screens ?
				if (t.Key == _activeClient.Title && t.Key == DEFAULT_CLIENT_TITLE)
				{
					lastClient = _thumbnailViews.FirstOrDefault(x => x.Value.Title == t.Key && x.Value.Id == _activeClient.Handle).Value;
					if (lastClient == null)
					{
						setNextClient = true;
						continue;
					}
					var possibleClients = (isForwards ? _thumbnailViews.OrderBy(x => x.Value.Id.ToInt64()) : _thumbnailViews.OrderByDescending(x => x.Value.Id.ToInt64())).Where(x => x.Value.Title == t.Key && ! x.Value.IsExcludedFromCycleGroup);
					foreach (var pc in possibleClients)
					{
						if ( pc.Value.Id.Equals(lastClient.Id) )
						{
							setNextClient = true;
							continue;
						}

						if (!setNextClient)
						{
							continue;
						}

						// this is the next client (at login screen)
						SetActive(pc);
						return;
					}

					// rolled off top of list - back to first (if any there!)
					// set next client ?
					continue;
				}

				if (!setNextClient)
				{
					continue;
				}

				if (_thumbnailViews.Any(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup))
				{
					var ptr = t.Key.Equals(DEFAULT_CLIENT_TITLE) ? 
						(isForwards ? _thumbnailViews.OrderBy(x => x.Value.Id.ToInt64()) : _thumbnailViews.OrderByDescending(x => x.Value.Id.ToInt64())).FirstOrDefault(x => x.Value.Title == t.Key && ! x.Value.IsExcludedFromCycleGroup)
						: _thumbnailViews.First(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup);
					SetActive(ptr);
					return;
				}
			}

			// we didn't get a next one. just get the first one from the start.
			foreach (var t in clientOrder)
			{
				if (_thumbnailViews.Any(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup))
				{
					var ptr = t.Key.Equals(DEFAULT_CLIENT_TITLE) ?
						(isForwards ? _thumbnailViews.OrderBy(x => x.Value.Id.ToInt64()) : _thumbnailViews.OrderByDescending(x => x.Value.Id.ToInt64())).FirstOrDefault(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup)
						: _thumbnailViews.First(x => x.Value.Title == t.Key && !x.Value.IsExcludedFromCycleGroup);
					SetActive(ptr);
					_activeClient = (ptr.Key, t.Key);
					return;
				}
			}

			// unable to select anything !
			return;
		}

		public void RegisterCycleClientHotkey(IEnumerable<Keys> keys, bool isForwards, Dictionary<string, int> cycleOrder)
		{
			foreach (var hotkey in keys)
			{
				if (hotkey == Keys.None)
				{
					return;
				}

				var newHandler = new HotkeyHandler(default(IntPtr), hotkey);
				newHandler.Pressed += (object s, HandledEventArgs e) =>
				{
					this.CycleNextClient(isForwards, cycleOrder);
					e.Handled = true;
				};

				newHandler.Register();
				this._cycleClientHotkeyHandlers.Add(newHandler);
			}
		}
		public void RegisterMinimizeAllClientsHotkey(IEnumerable<Keys> keys)
		{
			foreach (var hotkey in keys)
			{
				if (hotkey == Keys.None)
				{
					return;
				}

				var newHandler = new HotkeyHandler(default(IntPtr), hotkey);
				newHandler.Pressed += (object s, HandledEventArgs e) =>
				{
					this.MinimizeAllClients();
					e.Handled = true;
				};

				newHandler.Register();
				this._cycleClientHotkeyHandlers.Add(newHandler);
			}
		}

		public void Start()
		{
			// Stop() 注销过周期切换热键，重新启动时恢复注册
			foreach (var handler in this._cycleClientHotkeyHandlers)
			{
				try { handler.Register(); } catch { }
			}

			this._thumbnailUpdateTimer.Start();

			// Stop() 已清空视图与进程缓存，立即重建而不是等下一个定时周期
			this.UpdateThumbnailsList();
			this.RefreshThumbnails();
		}

		public void Stop()
		{
			this._thumbnailUpdateTimer.Stop();

			// 注销全局热键（保留对象供 Start() 重新注册）
			foreach (var handler in this._cycleClientHotkeyHandlers)
			{
				try { handler.Unregister(); } catch { }
			}

			var views = this._thumbnailViews.Values.ToList();
			var removedTitles = new List<string>();

			// 先清空字典：视图 Close 时可能触发回调，回调里按 Id 查不到视图即可安全跳过
			this._thumbnailViews.Clear();
			this._sourceSizes.Clear();
			this._activeClient = (IntPtr.Zero, ThumbnailManager.DEFAULT_CLIENT_TITLE);
			this._isHoverEffectActive = false;
			this._refreshCycleCount = 0;
			this._sizeChangeSaveDelay = -1;
			lock (this._locationChangeNotificationSyncRoot)
			{
				this._enqueuedLocationChangeNotification = (IntPtr.Zero, null, null, Point.Empty, -1);
			}

			foreach (var view in views)
			{
				if (view.Title != ThumbnailManager.DEFAULT_CLIENT_TITLE)
				{
					removedTitles.Add(view.Title);
				}

				try { view.UnregisterHotkey(); } catch { }

				view.ThumbnailResized = null;
				view.ThumbnailMoved = null;
				view.ThumbnailFocused = null;
				view.ThumbnailLostFocus = null;
				view.ThumbnailActivated = null;
				view.ThumbnailDeactivated = null;
				view.ThumbnailToggleCycleGroup = null;
				view.ThumbnailToggleSync = null;
				view.ThumbnailSetMainSync = null;

				// Close() 会关闭缩略图窗口并（Live 视图）注销 DWM 缩略图；
				// 即使进程随后被系统回收，屏幕上也不会留下冻结的窗口
				try { view.Close(); } catch { }
			}

			// 让下一次 Start() 把仍在运行的客户端当作新增重新创建缩略图，
			// 并同步清掉界面上的客户端列表，避免重复项
			try { this._processMonitor.Reset(); } catch { }

			if (removedTitles.Count > 0)
			{
				try
				{
					this._mediator.Publish(new ThumbnailListUpdated(new List<string>(), removedTitles))
						.GetAwaiter().GetResult();
				}
				catch { }
			}
		}

		private void ThumbnailUpdateTimerTick(object sender, EventArgs e)
		{
			this.UpdateThumbnailsList();
			this.RefreshThumbnails();
		}

		private async void UpdateThumbnailsList()
		{
			this._processMonitor.GetUpdatedProcesses(out ICollection<IProcessInfo> addedProcesses, out ICollection<IProcessInfo> updatedProcesses, out ICollection<IProcessInfo> removedProcesses);

			List<string> viewsAdded = new List<string>();
			List<string> viewsRemoved = new List<string>();

			foreach (IProcessInfo process in addedProcesses)
			{
				Size initialSize = this._configuration.ThumbnailSize;
				if (this._configuration.PerClientThumbnailSize.Any(x => x.Key == process.Title))
				{
					initialSize = this._configuration.PerClientThumbnailSize[process.Title];
				}

				IThumbnailView view = this._thumbnailViewFactory.Create(process.Handle, process.Title, this._configuration.ThumbnailSize);
				view.IsOverlayEnabled = this._configuration.ShowThumbnailOverlays;
				view.IsExcludedFromCycleGroup = false;
				view.SetFrames(this._configuration.ShowThumbnailFrames);
				// Max/Min size limitations should be set AFTER the frames are disabled
				// Otherwise thumbnail window will be unnecessary resized
				this.ApplySizeLimitations(view);
				view.SetTopMost(this._configuration.ShowThumbnailsAlwaysOnTop);

				view.ThumbnailLocation = this.IsManageableThumbnail(view)
											? this._configuration.GetThumbnailLocation(view.Title, this._activeClient.Title, view.ThumbnailLocation)
											: this._configuration.LoginThumbnailLocation;

				this._thumbnailViews.Add(view.Id, view);

				view.ThumbnailResized = this.ThumbnailViewResized;
				view.ThumbnailMoved = this.ThumbnailViewMoved;
				view.ThumbnailFocused = this.ThumbnailViewFocused;
				view.ThumbnailLostFocus = this.ThumbnailViewLostFocus;
				view.ThumbnailActivated = this.ThumbnailActivated;
				view.ThumbnailDeactivated = this.ThumbnailDeactivated;

				view.ThumbnailToggleCycleGroup = this.ThumbnailToggleCycleGroup;
				view.ThumbnailToggleSync = this.ThumbnailToggleSync;
				view.ThumbnailSetMainSync = this.ThumbnailSetMainSync;

				view.RegisterHotkey(this._configuration.GetClientHotkey(view.Title));

				this.ApplyClientLayout(view);
				this.ApplyCaptionBar(view);

				// TODO Add extension filter here later
				if (view.Title != ThumbnailManager.DEFAULT_CLIENT_TITLE)
				{
					viewsAdded.Add(view.Title);
				}
			}

			foreach (IProcessInfo process in updatedProcesses)
			{
				this._thumbnailViews.TryGetValue(process.Handle, out IThumbnailView view);

				if (view == null)
				{
					// Something went terribly wrong
					continue;
				}

				if (process.Title != view.Title) // update thumbnail title
				{
					viewsRemoved.Add(view.Title);
					view.Title = process.Title;
					viewsAdded.Add(view.Title);

					view.RegisterHotkey(this._configuration.GetClientHotkey(process.Title));

					this.ApplyClientLayout(view);
					this.ApplyCaptionBar(view);
				}
			}

			foreach (IProcessInfo process in removedProcesses)
			{
				IThumbnailView view = this._thumbnailViews[process.Handle];

				this._thumbnailViews.Remove(view.Id);
				this._sourceSizes.Remove(view.Id);
				if (view.Title != ThumbnailManager.DEFAULT_CLIENT_TITLE)
				{
					viewsRemoved.Add(view.Title);
				}

				view.UnregisterHotkey();

				view.ThumbnailResized = null;
				view.ThumbnailMoved = null;
				view.ThumbnailFocused = null;
				view.ThumbnailLostFocus = null;
				view.ThumbnailActivated = null;
				view.ThumbnailDeactivated = null;
				view.ThumbnailToggleCycleGroup = null;
				view.ThumbnailToggleSync = null;
				view.ThumbnailSetMainSync = null;

				view.Close();
			}

			if ((viewsAdded.Count > 0) || (viewsRemoved.Count > 0))
			{
				await this._mediator.Publish(new ThumbnailListUpdated(viewsAdded, viewsRemoved));
			}
		}

		private void RefreshThumbnails()
		{
			// TODO Split this method
			IntPtr foregroundWindowHandle = this._windowManager.GetForegroundWindowHandle();

			// The foreground window can be NULL in certain circumstances, such as when a window is losing activation.
			// It is safer to just skip this refresh round than to do something while the system state is undefined
			if (foregroundWindowHandle == IntPtr.Zero)
			{
				return;
			}

			string foregroundWindowTitle = null;

			// Check if the foreground window handle is one of the known handles for client windows or their thumbnails
			bool isClientWindow = this.IsClientWindowActive(foregroundWindowHandle);
			bool isMainWindowActive = this.IsMainWindowActive(foregroundWindowHandle);

			if (foregroundWindowHandle == this._activeClient.Handle)
			{
				foregroundWindowTitle = this._activeClient.Title;
			}
			else if (this._thumbnailViews.TryGetValue(foregroundWindowHandle, out IThumbnailView foregroundView))
			{
				// This code will work only on Alt+Tab switch between clients
				foregroundWindowTitle = foregroundView.Title;
			}
			else if (!isClientWindow)
			{
				this._externalApplication = foregroundWindowHandle;
			}

			// No need to minimize EVE clients when switching out to non-EVE window (like thumbnail)
			if (!string.IsNullOrEmpty(foregroundWindowTitle))
			{
				this.SwitchActiveClient(foregroundWindowHandle, foregroundWindowTitle);
			}

			bool hideAllThumbnails = this._configuration.HideThumbnailsOnLostFocus && !(isClientWindow || isMainWindowActive);

			// Wait for some time before hiding all previews
			if (hideAllThumbnails)
			{
				this._hideThumbnailsDelay--;
				if (this._hideThumbnailsDelay > 0)
				{
					hideAllThumbnails = false; // Postpone the 'hide all' operation
				}
				else
				{
					this._hideThumbnailsDelay = 0; // Stop the counter
				}
			}
			else
			{
				this._hideThumbnailsDelay = this._configuration.HideThumbnailsDelay; // Reset the counter
			}

			this._refreshCycleCount++;

			bool forceRefresh;
			if (this._refreshCycleCount >= ThumbnailManager.FORCED_REFRESH_CYCLE_THRESHOLD)
			{
				this._refreshCycleCount = 0;
				forceRefresh = true;
			}
			else
			{
				forceRefresh = false;
			}

			this.DisableViewEvents();
			try
			{
				// Snap thumbnail
				// No need to update Thumbnails while one of them is highlighted
				if ((!this._isHoverEffectActive) && this.TryDequeueLocationChange(out var locationChange))
				{
					if ((locationChange.ActiveClient == this._activeClient.Title) && this._thumbnailViews.TryGetValue(locationChange.Handle, out var view))
					{
						this.SnapThumbnailView(view);

						this.RaiseThumbnailLocationUpdatedNotification(view.Title);
					}
					else
					{
						this.RaiseThumbnailLocationUpdatedNotification(locationChange.Title);
					}
				}

				// Hide, show, resize and move - update ZoomAnchor setting
				foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
				{
					IThumbnailView view = entry.Value;
					// update ZoomAnchor regardless
					view.ClientZoomAnchor = this._configuration.GetZoomAnchor(view.Title, this._configuration.ThumbnailZoomAnchor);


					if (hideAllThumbnails || this._configuration.IsThumbnailDisabled(view.Title))
					{
						if (view.IsActive)
						{
							view.Hide();
						}
						continue;
					}

					if (this._configuration.HideActiveClientThumbnail && (view.Id == this._activeClient.Handle))
					{
						if (view.IsActive)
						{
							view.Hide();
						}
						continue;
					}

					if (this._configuration.HideLoginClientThumbnail && (view.Title == DEFAULT_CLIENT_TITLE ))
					{
						if (view.IsActive)
						{
							view.Hide();
						}
						continue;
					}

					// No need to update Thumbnails while one of them is highlighted
					if (!this._isHoverEffectActive)
					{
						// 位置只管理已登录的客户端。尺寸对登录窗口也要适配，
						// 否则它们一直停在初始框大小，一次误触发的拖拽就会把全局倍数算到下限
						if (this.IsManageableThumbnail(view))
						{
							view.ThumbnailLocation = this._configuration.GetThumbnailLocation(view.Title, this._activeClient.Title, view.ThumbnailLocation);
						}

						// box 必须取配置尺寸（开启比例时是最大范围），不能用已适配过的当前尺寸，否则会逐步缩小
						Size box = this._configuration.GetThumbnailSize(view.Title, this._activeClient.Title, this._configuration.ThumbnailSize);
						view.ThumbnailSize = this.FitToSourceRatio(view, box);

						view.SetOpacity(this._configuration.ThumbnailOpacity);
						view.SetTopMost(this._configuration.ShowThumbnailsAlwaysOnTop);
					}

					view.IsOverlayEnabled = this._configuration.ShowThumbnailOverlays;

					view.SetHighlight(
						this._configuration.EnableActiveClientHighlight && (view.Id == this._activeClient.Handle), 
						this._configuration.ActiveClientHighlightThickness);
					// 同步边框以同步列表为唯一数据源：列表页手动增删也会在下一轮刷新到边框
					bool isSyncTarget = this._syncTargetRegistry != null && this._syncTargetRegistry.IsSyncTarget(view.Id);
					view.SetSyncHighlight(isSyncTarget, isSyncTarget && this._syncTargetRegistry.IsMainSyncTarget(view.Id));

					if (!view.IsActive)
					{
						view.Show();
					}
					else
					{
						view.Refresh(forceRefresh);
					}
				}

				if (this.TryDequeueSizeChangeSave())
				{
					this.RaiseThumbnailConfigurationSave();
				}
			}
			finally
			{
				this.EnableViewEvents();
			}
		}

		public void UpdateThumbnailsSize()
		{
			this.SetThumbnailsSize(this._configuration.ThumbnailSize);
		}
		public void UpdateCycleGroupIndicator()
		{
			this.SetCycleGroupIndicator(this._configuration.CycleGroupIndicatorAnchor);
		}

		private void SetCycleGroupIndicator(ZoomAnchor anchor)
		{
			this.DisableViewEvents();
			try
			{
				foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
				{
					entry.Value.SetCycleGroupIndicator(entry.Value.IsExcludedFromCycleGroup, anchor);
					entry.Value.Refresh(false);
				}
			}
			finally
			{
				this.EnableViewEvents();
			}
		}

		private void SetThumbnailsSize(Size size)
		{
			this.DisableViewEvents();
			try
			{
				foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
				{
					this.ApplySizeLimitations(entry.Value);
					entry.Value.ThumbnailSize = this.FitToSourceRatio(entry.Value, size);
					entry.Value.Refresh(false);
				}
			}
			finally
			{
				this.EnableViewEvents();
			}
		}

		private void ApplySizeLimitations(IThumbnailView view)
		{
			if (this._configuration.UseThumbnailScale)
			{
				// 倍数模式：像素上限由倍数上限 1.0× 约束，不能再被固定最大尺寸钳住
				view.SetSizeLimitations(new Size(20, 20), Size.Empty);
				return;
			}

			// 开启比例时适配后的尺寸可能小于配置的最小尺寸，放宽下限避免被窗体钳制导致比例失真
			Size minimumSize = this._configuration.KeepThumbnailAspectRatio
				? new Size(20, 20)
				: this._configuration.ThumbnailMinimumSize;
			view.SetSizeLimitations(minimumSize, this._configuration.ThumbnailMaximumSize);
		}

		// 取源窗口客户区尺寸：优先实时读取，取不到（最小化等）用上次缓存的尺寸
		private bool TryGetSourceSize(IThumbnailView view, out Size sourceSize)
		{
			sourceSize = this._windowManager.GetClientAreaSize(view.Id);
			if (!sourceSize.IsEmpty && sourceSize.Width > 0 && sourceSize.Height > 0)
			{
				this._sourceSizes[view.Id] = sourceSize;
				return true;
			}

			return this._sourceSizes.TryGetValue(view.Id, out sourceSize)
				&& sourceSize.Width > 0 && sourceSize.Height > 0;
		}

		private Size FitToSourceRatio(IThumbnailView view, Size box)
		{
			// 倍数模式：以各自源窗口的实际尺寸为基准等比缩放，比例不同的窗口天然不变形。
			// 读不到源尺寸（刚启动且窗口最小化、缓存也空）时按 1080p 折算，
			// 不能退回固定框尺寸，否则和倍数脱节，重启后会突然变大或变小
			if (this._configuration.UseThumbnailScale)
			{
				double scale = EveOPreview.Configuration.Implementation.ThumbnailConfiguration.ApplyRestrictions(this._configuration.ThumbnailScale);
				Size source = this.TryGetSourceSize(view, out Size scaledSource)
					? scaledSource
					: new Size(ThumbnailManager.FALLBACK_SOURCE_WIDTH, ThumbnailManager.FALLBACK_SOURCE_HEIGHT);
				return new Size(
					Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero)),
					Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero)));
			}

			if (!this._configuration.KeepThumbnailAspectRatio || box.Width <= 0 || box.Height <= 0)
			{
				return box;
			}

			// 固定宽高模式：把 box 作为最大范围，按源窗口客户区宽高比等比放进去；取不到比例时用 box 本身
			if (!this.TryGetSourceSize(view, out Size sourceSize))
			{
				return box;
			}

			double ratio = (double)sourceSize.Width / sourceSize.Height;

			int width = Math.Min(box.Width, (int)Math.Round(box.Height * ratio, MidpointRounding.AwayFromZero));
			int height = (int)Math.Round(width / ratio, MidpointRounding.AwayFromZero);
			if (height > box.Height)
			{
				height = box.Height;
				width = (int)Math.Round(height * ratio, MidpointRounding.AwayFromZero);
			}

			return new Size(Math.Max(1, width), Math.Max(1, height));
		}

		private static Size ClampSize(Size size, Size minimumSize, Size maximumSize)
		{
			return new Size(
				Math.Min(Math.Max(size.Width, minimumSize.Width), Math.Max(minimumSize.Width, maximumSize.Width)),
				Math.Min(Math.Max(size.Height, minimumSize.Height), Math.Max(minimumSize.Height, maximumSize.Height)));
		}

		public void UpdateThumbnailFrames()
		{
			this.DisableViewEvents();
			try
			{
				foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
				{
					entry.Value.SetFrames(this._configuration.ShowThumbnailFrames);
					ApplyCaptionBar(entry.Value);
					entry.Value.SetPreventPreviews();
				}
			}
			finally
			{
				this.EnableViewEvents();
			}
		}

		private void EnableViewEvents()
		{
			this._ignoreViewEvents = false;
		}

		private void DisableViewEvents()
		{
			this._ignoreViewEvents = true;
		}

		private void SwitchActiveClient(IntPtr foregroundClientHandle, string foregroundClientTitle)
		{
			// Check if any actions are needed
			if (this._activeClient.Handle == foregroundClientHandle)
			{
				return;
			}

			// Minimize the currently active client if needed
			if (this._configuration.MinimizeInactiveClients && !this._configuration.IsPriorityClient(this._activeClient.Title))
			{
				this._windowManager.MinimizeWindow(this._activeClient.Handle, this._configuration.WindowsAnimationStyle, false);
#if LINUX
   			    this._windowManager.ActivateWindow(foregroundClientHandle, foregroundClientTitle);
#else
				this._windowManager.ActivateWindow(foregroundClientHandle, this._configuration.WindowsAnimationStyle);
#endif
			}

			this._activeClient = (foregroundClientHandle, foregroundClientTitle);
		}

		private void ThumbnailViewFocused(IntPtr id)
		{
			if (this._isHoverEffectActive)
			{
				return;
			}

			this._isHoverEffectActive = true;

			IThumbnailView view = this._thumbnailViews[id];

			// 只有用户开启“预览始终置顶”时才保持置顶；关闭时悬停不改变 Z 序
			view.SetTopMost(this._configuration.ShowThumbnailsAlwaysOnTop);
			view.SetOpacity(1.0);

			if (this._configuration.ThumbnailZoomEnabled && ! view.IsPreventPreviews() )
			{
				this.ThumbnailZoomIn(view);
			}
		}

		private void ThumbnailViewLostFocus(IntPtr id)
		{
			if (!this._isHoverEffectActive)
			{
				return;
			}

			IThumbnailView view = this._thumbnailViews[id];

			if (this._configuration.ThumbnailZoomEnabled)
			{
				this.ThumbnailZoomOut(view);
			}

			view.SetOpacity(this._configuration.ThumbnailOpacity);

			this._isHoverEffectActive = false;
		}

		private void ThumbnailActivated(IntPtr id)
		{
			IThumbnailView view = this._thumbnailViews[id];

			Task.Run(() =>
				{
#if LINUX
					this._windowManager.ActivateWindow(view.Id, view.Title);
#else
					this._windowManager.ActivateWindow(view.Id, this._configuration.WindowsAnimationStyle);
#endif
				})
				.ContinueWith((task) =>
				{
					// This code should be executed on UI thread
					this.SwitchActiveClient(view.Id, view.Title);
					this.UpdateClientLayouts();
					this.RefreshThumbnails();
				}, TaskScheduler.FromCurrentSynchronizationContext());
		}

		private void ThumbnailDeactivated(IntPtr id, bool switchOut)
		{
			if (switchOut)
			{
#if LINUX
				this._windowManager.ActivateWindow(this._externalApplication, null);
#else
				this._windowManager.ActivateWindow(this._externalApplication, this._configuration.WindowsAnimationStyle);
#endif
			}
			else
			{
				if (!this._thumbnailViews.TryGetValue(id, out IThumbnailView view))
				{
					return;
				}

				this._windowManager.MinimizeWindow(view.Id, this._configuration.WindowsAnimationStyle, true);
				this.RefreshThumbnails();
			}
		}

		private void ThumbnailToggleSync(IntPtr id)
		{
			if (this._syncTargetRegistry == null)
			{
				return;
			}

			var view = GetClientByPointer(id);
			if (view == null)
			{
				return;
			}

			// id 就是游戏客户端窗口句柄（view.Id），不是缩略图自身窗口
			this._syncTargetRegistry.Toggle(view.Id, view.Title);
			bool isSyncTarget = this._syncTargetRegistry.IsSyncTarget(view.Id);
			view.SetSyncHighlight(isSyncTarget, isSyncTarget && this._syncTargetRegistry.IsMainSyncTarget(view.Id));
			view.Refresh(true);
		}

		private void ThumbnailSetMainSync(IntPtr id)
		{
			if (this._syncTargetRegistry == null)
			{
				return;
			}

			var view = GetClientByPointer(id);
			if (view == null)
			{
				return;
			}

			// id 就是游戏客户端窗口句柄（view.Id），不是缩略图自身窗口
			this._syncTargetRegistry.SetMain(view.Id, view.Title);

			// 主窗口切换会影响旧主窗口（琥珀金->绿色）和新主窗口（绿色/无框->琥珀金），
			// 因此要刷新全部缩略图的同步边框，不能只刷新被点击的那一个
			foreach (var entry in this._thumbnailViews.Values.ToList())
			{
				bool isSyncTarget = this._syncTargetRegistry.IsSyncTarget(entry.Id);
				entry.SetSyncHighlight(isSyncTarget, isSyncTarget && this._syncTargetRegistry.IsMainSyncTarget(entry.Id));
				entry.Refresh(true);
			}
		}

		private void ThumbnailToggleCycleGroup(IntPtr id)
		{
			var view = GetClientByPointer(id);
			if ( view != null )
			{
				view.IsExcludedFromCycleGroup = !view.IsExcludedFromCycleGroup;
				view.SetCycleGroupIndicator(view.IsExcludedFromCycleGroup, _configuration.CycleGroupIndicatorAnchor);

			}
			this.RefreshThumbnails();
		}


		private async void ThumbnailViewResized(IntPtr id)
		{
			if (this._ignoreViewEvents)
			{
				return;
			}

			IThumbnailView view = this._thumbnailViews[id];

			if (this._configuration.UseThumbnailScale)
			{
				if (!this.TryGetSourceSize(view, out Size sourceSize))
				{
					// 取不到源尺寸时无法反推倍数，也不能落到固定尺寸分支去改 ThumbnailSize
					this.SetThumbnailsSize(this._configuration.ThumbnailSize);
					view.Refresh(false);
					return;
				}

				// 倍数模式：从拖拽后的尺寸反推倍数。取相对变化更大的那一轴，
				// 避免另一轴的取整误差把倍数带偏；倍数不做四舍五入存储，
				// 否则每次几像素的拖拽增量会被吞掉
				Size dragged = view.ThumbnailSize;
				double scaleByWidth = (double)dragged.Width / sourceSize.Width;
				double scaleByHeight = (double)dragged.Height / sourceSize.Height;
				double currentScale = this._configuration.ThumbnailScale;
				double scale = Math.Abs(scaleByWidth - currentScale) >= Math.Abs(scaleByHeight - currentScale)
					? scaleByWidth
					: scaleByHeight;
				scale = EveOPreview.Configuration.Implementation.ThumbnailConfiguration.ApplyRestrictions(scale);
				this._configuration.ThumbnailScale = scale;

				this.SetThumbnailsSize(this._configuration.ThumbnailSize);

				view.Refresh(false);

				this.EnqueueSizeChangeSave();
				await this._mediator.Publish(new ThumbnailActiveSizeUpdated(this._configuration.ThumbnailSize, scale));
				return;
			}

			// 拖拽尺寸作为新的 box（开启比例时是最大范围）：先夹回 min/max 并写入配置，
			// 否则 RefreshThumbnails 会按旧配置尺寸把拖拽结果覆盖掉
			Size size = ThumbnailManager.ClampSize(view.ThumbnailSize, this._configuration.ThumbnailMinimumSize, this._configuration.ThumbnailMaximumSize);
			this._configuration.ThumbnailSize = size;

			this.SetThumbnailsSize(size);

			view.Refresh(false);

			this.EnqueueSizeChangeSave();
			await this._mediator.Publish(new ThumbnailActiveSizeUpdated(size, this._configuration.ThumbnailScale));
		}

		private void ThumbnailViewMoved(IntPtr id)
		{
			if (this._ignoreViewEvents)
			{
				return;
			}

			IThumbnailView view = this._thumbnailViews[id];
			view.Refresh(false);
			this.EnqueueLocationChange(view);
		}

		// Checks whether currently active window belongs to an EVE client or its thumbnail
		private bool IsClientWindowActive(IntPtr windowHandle)
		{
			if (windowHandle == IntPtr.Zero)
			{
				return false;
			}

			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				IThumbnailView view = entry.Value;

				if (view.IsKnownHandle(windowHandle))
				{
					return true;
				}
			}

			return false;
		}

		// Check whether the currently active window belongs to EVE-O-Preview itself
		private bool IsMainWindowActive(IntPtr windowHandle)
		{
			return (this._processMonitor.GetMainProcess().Handle == windowHandle);
		}

		private void ThumbnailZoomIn(IThumbnailView view)
		{
			this.DisableViewEvents();
			try
			{
				view.ZoomIn(ViewZoomAnchorConverter.Convert(view.ClientZoomAnchor), this._configuration.ThumbnailZoomFactor);
				view.Refresh(false);
			}
			finally
			{
				this.EnableViewEvents();
			}
		}

		private void ThumbnailZoomOut(IThumbnailView view)
		{
			this.DisableViewEvents();
			try
			{
				view.ZoomOut();
				view.Refresh(false);
			}
			finally
			{
				this.EnableViewEvents();
			}
		}

		private void SnapThumbnailView(IThumbnailView view)
		{
			// Check if this feature is enabled
			if (!this._configuration.EnableThumbnailSnap)
			{
				return;
			}

			// Only borderless thumbnails can be docked
			if (this._configuration.ShowThumbnailFrames)
			{
				return;
			}

			// 开启比例后各缩略图尺寸可能不同，吸附要用各自的实际尺寸
			int width = view.ThumbnailSize.Width;
			int height = view.ThumbnailSize.Height;

			// TODO Extract method
			int baseX = view.ThumbnailLocation.X;
			int baseY = view.ThumbnailLocation.Y;

			Point[] viewPoints = { new Point(baseX, baseY), new Point(baseX + width, baseY), new Point(baseX, baseY + height), new Point(baseX + width, baseY + height) };

			// TODO Extract constants
			int thresholdX = Math.Max(20, width / 10);
			int thresholdY = Math.Max(20, height / 10);

			foreach (var entry in this._thumbnailViews)
			{
				IThumbnailView testView = entry.Value;

				if (view.Id == testView.Id)
				{
					continue;
				}

				int testX = testView.ThumbnailLocation.X;
				int testY = testView.ThumbnailLocation.Y;
				int testWidth = testView.ThumbnailSize.Width;
				int testHeight = testView.ThumbnailSize.Height;

				Point[] testPoints = { new Point(testX, testY), new Point(testX + testWidth, testY), new Point(testX, testY + testHeight), new Point(testX + testWidth, testY + testHeight) };

				var delta = ThumbnailManager.TestViewPoints(viewPoints, testPoints, thresholdX, thresholdY);

				if ((delta.X == 0) && (delta.Y == 0))
				{
					continue;
				}

				view.ThumbnailLocation = new Point(view.ThumbnailLocation.X + delta.X, view.ThumbnailLocation.Y + delta.Y);
				this._configuration.SetThumbnailLocation(view.Title, this._activeClient.Title, view.ThumbnailLocation);
				break;
			}
		}

		private static (int X, int Y) TestViewPoints(Point[] viewPoints, Point[] testPoints, int thresholdX, int thresholdY)
		{
			// Point combinations that we need to check
			// No need to check all 4x4 combinations
			(int ViewOffset, int TestOffset)[] testOffsets =
								{   ( 0, 3 ), ( 0, 2 ), ( 1, 2 ),
									( 0, 1 ), ( 0, 0 ), ( 1, 0 ),
									( 2, 1 ), ( 2, 0 ), ( 3, 0 )};

			foreach (var testOffset in testOffsets)
			{
				Point viewPoint = viewPoints[testOffset.ViewOffset];
				Point testPoint = testPoints[testOffset.TestOffset];

				int deltaX = testPoint.X - viewPoint.X;
				int deltaY = testPoint.Y - viewPoint.Y;

				if ((Math.Abs(deltaX) <= thresholdX) && (Math.Abs(deltaY) <= thresholdY))
				{
					return (deltaX, deltaY);
				}
			}

			return (0, 0);
		}
		private bool SetWindowStyle(IThumbnailView view, UInt32 styleToChange, bool remove)
		{
			IntPtr handle = view.Id;
			uint style = User32NativeMethods.GetWindowLong(handle, InteropConstants.GWL_STYLE);
			if (((style & styleToChange) == styleToChange) && remove == true)
			{
				style = style & ~styleToChange;
				User32NativeMethods.SetWindowLong(handle, InteropConstants.GWL_STYLE, style);
				return true;
			}
			if (((style & styleToChange) != styleToChange) && remove == false)
			{
				style = style | styleToChange;
				User32NativeMethods.SetWindowLong(handle, InteropConstants.GWL_STYLE, style);
				return true;
			}
			return false;
		}
		private void ApplyCaptionBar(IThumbnailView view)

		{
			if (view.Title == ThumbnailManager.DEFAULT_CLIENT_TITLE) return;
			IntPtr handle = view.Id;

			bool enable = this._configuration.HideCaptionOnClients;
			bool changed = false;
			changed = changed | SetWindowStyle(view, InteropConstants.WS_CAPTION, enable);
			changed = changed | SetWindowStyle(view, InteropConstants.WS_THICKFRAME, enable);
		}
		private void ApplyClientLayout(IThumbnailView view)
		{
			IntPtr clientHandle = view.Id;
			string clientTitle = view.Title;

			if (!this._configuration.EnableClientLayoutTracking)
			{
				return;
			}

			// No need to apply layout for not yet logged-in clients
			if (clientTitle == ThumbnailManager.DEFAULT_CLIENT_TITLE)
			{
				return;
			}

			ClientLayout clientLayout = this._configuration.GetClientLayout(clientTitle);

			if (clientLayout == null)
			{
				return;
			}

			if (clientLayout.IsMaximized)
			{
				this._windowManager.MaximizeWindow(clientHandle);
			}
			else
			{
				this._windowManager.MoveWindow(clientHandle, clientLayout.X, clientLayout.Y, clientLayout.Width, clientLayout.Height);
			}
		}

		private void UpdateClientLayouts()
		{
			if (!this._configuration.EnableClientLayoutTracking)
			{
				return;
			}

			foreach (KeyValuePair<IntPtr, IThumbnailView> entry in this._thumbnailViews)
			{
				IThumbnailView view = entry.Value;

				// No need to save layout for not yet logged-in clients
				if (view.Title == ThumbnailManager.DEFAULT_CLIENT_TITLE)
				{
					continue;
				}

				(int Left, int Top, int Right, int Bottom) position = this._windowManager.GetWindowPosition(view.Id);
				int width = Math.Abs(position.Right - position.Left);
				int height = Math.Abs(position.Bottom - position.Top);

				var isMaximized = this._windowManager.IsWindowMaximized(view.Id);

				if (!(isMaximized || this.IsValidWindowPosition(position.Left, position.Top, width, height)))
				{
					continue;
				}

				this._configuration.SetClientLayout(view.Title, new ClientLayout(position.Left, position.Top, width, height, isMaximized));
			}
		}

		private void EnqueueSizeChangeSave()
		{
			this._sizeChangeSaveDelay = ThumbnailManager.DEFAULT_SIZE_CHANGE_SAVE_DELAY;
		}

		private bool TryDequeueSizeChangeSave()
		{
			if (this._sizeChangeSaveDelay < 0)
			{
				return false;
			}

			this._sizeChangeSaveDelay--;

			if (this._sizeChangeSaveDelay > 0)
			{
				return false;
			}

			this._sizeChangeSaveDelay = -1;
			return true;
		}

		private async void RaiseThumbnailConfigurationSave()
		{
			await this._mediator.Send(new SaveConfiguration());
		}

		private void EnqueueLocationChange(IThumbnailView view)
		{
			string activeClientTitle = this._activeClient.Title;
			// TODO ??
			this._configuration.SetThumbnailLocation(view.Title, activeClientTitle, view.ThumbnailLocation);

			lock (this._locationChangeNotificationSyncRoot)
			{
				if (this._enqueuedLocationChangeNotification.Handle == IntPtr.Zero)
				{
					this._enqueuedLocationChangeNotification = (view.Id, view.Title, activeClientTitle, view.ThumbnailLocation, ThumbnailManager.DEFAULT_LOCATION_CHANGE_NOTIFICATION_DELAY);
					return;
				}

				// Reset the delay and exit
				if ((this._enqueuedLocationChangeNotification.Handle == view.Id) &&
					(this._enqueuedLocationChangeNotification.ActiveClient == activeClientTitle))
				{
					this._enqueuedLocationChangeNotification.Delay = ThumbnailManager.DEFAULT_LOCATION_CHANGE_NOTIFICATION_DELAY;
					return;
				}

				this.RaiseThumbnailLocationUpdatedNotification(this._enqueuedLocationChangeNotification.Title);
				this._enqueuedLocationChangeNotification = (view.Id, view.Title, activeClientTitle, view.ThumbnailLocation, ThumbnailManager.DEFAULT_LOCATION_CHANGE_NOTIFICATION_DELAY);
			}
		}

		private bool TryDequeueLocationChange(out (IntPtr Handle, string Title, string ActiveClient, Point Location) change)
		{
			lock (this._locationChangeNotificationSyncRoot)
			{
				change = (IntPtr.Zero, null, null, Point.Empty);

				if (this._enqueuedLocationChangeNotification.Handle == IntPtr.Zero)
				{
					return false;
				}

				this._enqueuedLocationChangeNotification.Delay--;

				if (this._enqueuedLocationChangeNotification.Delay > 0)
				{
					return false;
				}

				change = (this._enqueuedLocationChangeNotification.Handle, this._enqueuedLocationChangeNotification.Title, this._enqueuedLocationChangeNotification.ActiveClient, this._enqueuedLocationChangeNotification.Location);
				this._enqueuedLocationChangeNotification = (IntPtr.Zero, null, null, Point.Empty, -1);

				return true;
			}
		}

		private async void RaiseThumbnailLocationUpdatedNotification(string title)
		{
			if (string.IsNullOrEmpty(title) || (title == ThumbnailManager.DEFAULT_CLIENT_TITLE))
			{
				return;
			}

			await this._mediator.Send(new SaveConfiguration());
		}

		// We shouldn't manage some thumbnails (like thumbnail of the EVE client sitting on the login screen)
		// TODO Move to a service (?)
		private bool IsManageableThumbnail(IThumbnailView view)
		{
			return view.Title != ThumbnailManager.DEFAULT_CLIENT_TITLE;
		}

		// Quick sanity check that the window is not minimized
		private bool IsValidWindowPosition(int left, int top, int width, int height)
		{
			return (left > ThumbnailManager.WINDOW_POSITION_THRESHOLD_LOW) && (left < ThumbnailManager.WINDOW_POSITION_THRESHOLD_HIGH)
					&& (top > ThumbnailManager.WINDOW_POSITION_THRESHOLD_LOW) && (top < ThumbnailManager.WINDOW_POSITION_THRESHOLD_HIGH)
					&& (width > ThumbnailManager.WINDOW_SIZE_THRESHOLD) && (height > ThumbnailManager.WINDOW_SIZE_THRESHOLD);
		}
	}
}
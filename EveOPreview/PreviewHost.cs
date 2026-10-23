#nullable disable
using System;
using EveOPreview;
using EveOPreview.Configuration;
using EveOPreview.Mediator.Messages;
using EveOPreview.Presenters;
using EveOPreview.Services;
using EveOPreview.View;
using Multiboxing.Views;
using MediatR;

namespace Multiboxing;

/// <summary>
/// EVE-O-Preview 的组合根，替代原 WinForms Program.Main / Application.Run。
/// 在 WPF UI 线程上构造，使 ThumbnailManager 的 DispatcherTimer 正常工作。
/// </summary>
internal sealed class PreviewHost : IDisposable
{
    private readonly LightInjectContainer _container;
    private readonly PreviewViewModel _view;
    private MainFormPresenter _presenter;
    private IMediator _mediator;
    private IConfigurationStorage _configurationStorage;
    private bool _started;
    private bool _disposed;

    public PreviewHost(PreviewViewModel view, EveOPreview.Services.ISyncTargetRegistry syncTargetRegistry)
    {
        _view = view;
        _container = new LightInjectContainer();
        _container.RegisterInstance<EveOPreview.Services.ISyncTargetRegistry>(syncTargetRegistry);

        _container.Register<IWindowManager>();
        _container.Register<IProcessMonitor>();

        _container.Register<IMediator, MediatR.Mediator>();
        _container.RegisterInstance<ServiceFactory>(t => _container.Resolve(t));
        var assembly = typeof(PreviewHost).Assembly;
        _container.Register(typeof(INotificationHandler<>), assembly);
        _container.Register(typeof(IRequestHandler<>), assembly);
        _container.Register(typeof(IRequestHandler<,>), assembly);

        _container.Register<IConfigurationStorage>();
        _container.Register<IAppConfig>();
        _container.Register<IThumbnailConfiguration>();

        _container.Register<IThumbnailManager>();
        _container.Register<IThumbnailViewFactory>();
        _container.Register<IThumbnailDescription>();

        var controller = new ApplicationController(_container);
        controller.RegisterView<StaticThumbnailView, StaticThumbnailView>();
        controller.RegisterView<LiveThumbnailView, LiveThumbnailView>();
        _container.RegisterInstance<IMainFormView>(view);

        _view.ServiceToggleRequested += HandleServiceToggleRequested;
    }

    public bool IsRunning { get; private set; }

    public void Activate()
    {
        if (_started || _disposed) return;
        _started = true;
        _mediator = _container.Resolve<IMediator>();
        _configurationStorage = _container.Resolve<IConfigurationStorage>();
        // Run 会解析 Presenter（其构造函数把回调挂到 View 上），随后调用 View.Show()，
        // Show 内部触发 FormActivated -> Presenter 加载配置并发送 StartService。
        new ApplicationControllerFacade(_container).RunPresenter();
        _presenter = _container.Resolve<MainFormPresenter>();
        IsRunning = true;
        _view.SetPreviewRunning(true);
    }

    private sealed class ApplicationControllerFacade
    {
        private readonly LightInjectContainer _c;
        public ApplicationControllerFacade(LightInjectContainer c) => _c = c;
        public void RunPresenter()
        {
            var controller = (IApplicationController)_c.Resolve(typeof(IApplicationController));
            controller.Run<MainFormPresenter>();
        }
    }

    private void HandleServiceToggleRequested(bool start)
    {
        if (_mediator == null) return;
        try
        {
            if (start)
            {
                _mediator.Send(new StartService()).Wait();
                IsRunning = true;
            }
            else
            {
                _mediator.Send(new StopService()).Wait();
                IsRunning = false;
            }
            _view.SetPreviewRunning(IsRunning);
        }
        catch
        {
            _view.SetPreviewRunning(IsRunning);
        }
    }

    /// <summary>由主窗口关闭流程调用：返回 true 表示允许真正关闭。</summary>
    public bool RequestClose()
    {
        var request = new ViewCloseRequest();
        _view.RaiseFormCloseRequested(request);
        return request.Allow;
    }

    public void ExitApplication()
    {
        _view.RaiseApplicationExitRequested();
    }

    public void SaveAndStop()
    {
        try { _mediator?.Send(new StopService()).Wait(); } catch { }
        try { _configurationStorage?.Save(); } catch { }
        IsRunning = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view.ServiceToggleRequested -= HandleServiceToggleRequested;
        // 兜底清理：停服务会关闭全部缩略图窗口，即便主进程随后未被回收也不残留
        SaveAndStop();
    }
}

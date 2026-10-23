#nullable disable
using System;

namespace EveOPreview.Services
{
	/// <summary>
	/// 桥接 EVE-O-Preview 缩略图与键鼠同步列表。
	/// 缩略图侧只以“游戏客户端窗口句柄”（IThumbnailView.Id）为键询问/切换，
	/// 不接触缩略图自身窗口句柄。
	/// </summary>
	public interface ISyncTargetRegistry
	{
		bool IsSyncTarget(IntPtr clientHandle);

		bool IsMainSyncTarget(IntPtr clientHandle);

		void Toggle(IntPtr clientHandle, string title);

		void SetMain(IntPtr clientHandle, string title);
	}
}

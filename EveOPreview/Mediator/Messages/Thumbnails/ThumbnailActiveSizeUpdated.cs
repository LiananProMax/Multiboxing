#nullable disable
using System.Drawing;

namespace EveOPreview.Mediator.Messages
{
	sealed class ThumbnailActiveSizeUpdated : NotificationBase<Size>
	{
		public ThumbnailActiveSizeUpdated(Size size, double scale)
				: base(size)
		{
			this.Scale = scale;
		}

		/// <summary>
		/// 拖拽缩略图反推出的倍数，用于同步界面上的倍数滑块
		/// </summary>
		public double Scale { get; }
	}
}
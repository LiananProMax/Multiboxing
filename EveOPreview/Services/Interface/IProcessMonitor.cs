#nullable disable
using System.Collections.Generic;

namespace EveOPreview.Services
{
	public interface IProcessMonitor
	{
		IProcessInfo GetMainProcess();
		ICollection<IProcessInfo> GetAllProcesses();
		void GetUpdatedProcesses(out ICollection<IProcessInfo> addedProcesses, out ICollection<IProcessInfo> updatedProcesses, out ICollection<IProcessInfo> removedProcesses);
		/// <summary>忘记所有已见进程，使下一次轮询把仍在运行的客户端重新上报为新增。</summary>
		void Reset();
	}
}
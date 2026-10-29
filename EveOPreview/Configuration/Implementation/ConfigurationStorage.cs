#nullable disable
using System.IO;
using Newtonsoft.Json;

namespace EveOPreview.Configuration.Implementation
{
	class ConfigurationStorage : IConfigurationStorage
	{
		private const string CONFIGURATION_FILE_NAME = "EVE-O-Preview.json";

		private readonly IAppConfig _appConfig;
		private readonly IThumbnailConfiguration _thumbnailConfiguration;

		public ConfigurationStorage(IAppConfig appConfig, IThumbnailConfiguration thumbnailConfiguration)
		{
			this._appConfig = appConfig;
			this._thumbnailConfiguration = thumbnailConfiguration;
		}

		public void Load()
		{
			string filename = this.GetConfigFileName();

			if (!File.Exists(filename))
			{
				return;
			}

			string rawData = File.ReadAllText(filename);

			JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
			{
				ObjectCreationHandling = ObjectCreationHandling.Replace
			};

			// StageHotkeyArraysToAvoidDuplicates(rawData);

			// 旧配置没有倍数字段时，按 1080p 参考窗口从旧的固定高度折算，
			// 让默认开启的倍数模式视觉大小接近原来的配置尺寸，避免升级后缩略图突然变小
			bool hasScale = rawData.Contains("\"ThumbnailScale\"");

			JsonConvert.PopulateObject(rawData, this._thumbnailConfiguration, jsonSerializerSettings);

			if (!hasScale && this._thumbnailConfiguration.ThumbnailSize.Height > 0)
			{
				this._thumbnailConfiguration.ThumbnailScale = ThumbnailConfiguration.ApplyRestrictions(
					this._thumbnailConfiguration.ThumbnailSize.Height / 1080.0);
			}

			// Validate data after loading it
			this._thumbnailConfiguration.ApplyRestrictions();
		}

		public void Save()
		{
			string rawData = JsonConvert.SerializeObject(this._thumbnailConfiguration, Formatting.Indented);
			string filename = this.GetConfigFileName();
			string tempFilename = filename + ".tmp";

			try
			{
				// 先写临时文件再替换，避免进程在写入中途被结束时留下截断的 JSON
				File.WriteAllText(tempFilename, rawData);

				if (File.Exists(filename))
				{
					File.Replace(tempFilename, filename, null);
				}
				else
				{
					File.Move(tempFilename, filename);
				}
			}
			catch (IOException)
			{
				// Ignore error if for some reason the updated config cannot be written down
			}
			catch (UnauthorizedAccessException)
			{
				// Ignore error if for some reason the updated config cannot be written down
			}
		}

		private string GetConfigFileName()
		{
			// 管理员提权启动时工作目录不可靠，默认固定到 exe 所在目录；已有相对自定义路径同样按此解析。
			string name = string.IsNullOrEmpty(this._appConfig.ConfigFileName) ? ConfigurationStorage.CONFIGURATION_FILE_NAME : this._appConfig.ConfigFileName;
			return Path.IsPathRooted(name) ? name : Path.Combine(System.AppContext.BaseDirectory, name);
		}
	}
}
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AromaShooterUdpBridge
{
	public sealed class Settings
	{
		public string Address = "0.0.0.0";
		public int Port = 10000;
		public string Transport = "USB";
		public bool AutoConnect = true;
		public int[] Intensities = { 100, 100, 100, 100, 100, 100 };
		public int Internal = 100;
		public int External;

		public Settings Clone() => new Settings
		{
			Address = Address,
			Port = Port,
			Transport = Transport,
			AutoConnect = AutoConnect,
			Intensities = (int[])Intensities.Clone(),
			Internal = Internal,
			External = External
		};

		public void Validate()
		{
			IPAddress ip;
			if (!IPAddress.TryParse(Address, out ip) || ip.AddressFamily != AddressFamily.InterNetwork || ip.ToString() != Address)
				throw new FormatException("待受アドレスはIPv4のドット区切り表記で指定してください。");
			if (Port < 1 || Port > 65535 || (Transport != "USB" && Transport != "BLE") ||
					Intensities == null || Intensities.Length != 6 || Intensities.Any(x => x < 0 || x > 100) ||
					Internal < 1 || Internal > 100 || External < 0 || External > 100)
				throw new FormatException("設定値が範囲外です。");
		}

		public string ToJson()
		{
			Validate();
			return new JObject
			{
				["schemaVersion"] = 1,
				["udp"] = new JObject { ["listenAddress"] = Address, ["port"] = Port },
				["device"] = new JObject { ["transport"] = Transport, ["autoConnect"] = AutoConnect },
				["shoot"] = new JObject
				{
					["defaultIntensities"] = new JArray(Intensities),
					["internalBoosterIntensity"] = Internal,
					["externalBoosterIntensity"] = External
				}
			}.ToString(Formatting.Indented);
		}

		public static Settings Parse(string json)
		{
			var root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
			Keys(root, "schemaVersion", "udp", "device", "shoot");
			if (Integer(root["schemaVersion"]) != 1) throw new FormatException("未対応のschemaVersionです。");
			var udp = root["udp"] as JObject; Keys(udp, "listenAddress", "port");
			var device = root["device"] as JObject; Keys(device, "transport", "autoConnect");
			var shoot = root["shoot"] as JObject; Keys(shoot, "defaultIntensities", "internalBoosterIntensity", "externalBoosterIntensity");
			if (udp["listenAddress"].Type != JTokenType.String || device["transport"].Type != JTokenType.String ||
					device["autoConnect"].Type != JTokenType.Boolean || shoot["defaultIntensities"].Type != JTokenType.Array)
				throw new FormatException("設定の型が不正です。");
			var result = new Settings
			{
				Address = (string)udp["listenAddress"],
				Port = Integer(udp["port"]),
				Transport = (string)device["transport"],
				AutoConnect = (bool)device["autoConnect"],
				Intensities = ((JArray)shoot["defaultIntensities"]).Select(Integer).ToArray(),
				Internal = Integer(shoot["internalBoosterIntensity"]),
				External = Integer(shoot["externalBoosterIntensity"])
			};
			result.Validate(); return result;
		}

		private static int Integer(JToken token)
		{
			if (token == null || token.Type != JTokenType.Integer) throw new FormatException("整数型の設定が必要です。");
			return checked((int)token);
		}
		private static void Keys(JObject obj, params string[] names)
		{
			if (obj == null || !obj.Properties().Select(p => p.Name).OrderBy(n => n).SequenceEqual(names.OrderBy(n => n)))
				throw new FormatException("設定の項目が不足しているか、未知の項目があります。");
		}
	}

	public sealed class SettingsStore
	{
		public string Path { get; }
		public SettingsStore(string path) { Path = path; }
		public string Fingerprint()
		{
			if (!File.Exists(Path)) return null;
			using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(Path)));
		}
		public Settings Read(out string version)
		{
			byte[] bytes = File.ReadAllBytes(Path);
			var result = Settings.Parse(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
			using (var sha = SHA256.Create()) version = Convert.ToBase64String(sha.ComputeHash(bytes));
			return result;
		}
		// 読み取り側(監視/AV等)の一時的な共有違反でReplaceが失敗するため短くリトライする。
		private void Replace(string temp)
		{
			for (int attempt = 0; ; attempt++)
			{
				try { File.Replace(temp, Path, null); return; }
				catch (IOException) when (attempt < 5) { System.Threading.Thread.Sleep(20); }
			}
		}
		public string Save(Settings settings, string expected, bool overwrite)
		{
			string json = settings.ToJson();
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
			string temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllText(temp, json, new UTF8Encoding(false));
				if (!overwrite && Fingerprint() != expected) throw new IOException("設定が外部変更されています。再読み込み、または明示的な上書き保存を選んでください。");
				if (File.Exists(Path)) Replace(temp); else File.Move(temp, Path);
				using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(new UTF8Encoding(false).GetBytes(json)));
			}
			finally { if (File.Exists(temp)) File.Delete(temp); }
		}
	}
}

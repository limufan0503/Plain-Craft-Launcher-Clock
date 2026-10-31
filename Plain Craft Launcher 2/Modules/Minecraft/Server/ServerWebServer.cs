using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：本地网页管理面板（正版 Clock 功能）。
// 监听 http://127.0.0.1:57263/，浏览器打开即可管理服务器（启动/停止/重启/发命令/看日志）。
public static class ServerWebServer
{
	public const int Port = 57263;

	private static HttpListener? _listener;
	private static CancellationTokenSource? _cts;

	public static bool IsRunning => _listener?.IsListening == true;

	public static void Start()
	{
		if (IsRunning)
			return;
		ServerManager.Initialize();
		_cts = new CancellationTokenSource();
		_listener = new HttpListener();
		_listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
		_listener.Prefixes.Add($"http://localhost:{Port}/");
		try
		{
			_listener.Start();
		}
		catch (Exception ex)
		{
			LogWrapper.Error(ex, "ServerWeb", "网页管理服务启动失败（端口 " + Port + "）");
			_listener = null;
			return;
		}
		LogWrapper.Info("ServerWeb", "网页管理面板已启动：http://127.0.0.1:" + Port + "/");
		_ = Task.Run(() => ListenLoop(_cts.Token));
	}

	public static void Stop()
	{
		try { _cts?.Cancel(); } catch { }
		try { _listener?.Stop(); } catch { }
		_listener = null;
	}

	private static async Task ListenLoop(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _listener?.IsListening == true)
		{
			HttpListenerContext ctx;
			try { ctx = await _listener.GetContextAsync(); }
			catch { break; }
			_ = Task.Run(() => SafeHandle(ctx));
		}
	}

	private static void SafeHandle(HttpListenerContext ctx)
	{
		try { Handle(ctx); }
		catch (Exception ex) { LogWrapper.Error(ex, "ServerWeb", "处理网页请求失败"); }
	}

	private static void Handle(HttpListenerContext ctx)
	{
		var req = ctx.Request;
		var resp = ctx.Response;
		string path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/');

		if (req.HttpMethod == "GET" && (path == "" || path == "/"))
		{
			WriteHtml(resp, Html);
			return;
		}

		if (path == "/api/servers")
		{
			var servers = new List<object>();
			lock (ServerManager.Servers)
				foreach (var s in ServerManager.Servers)
					servers.Add(new
					{
						folder = s.FolderName,
						name = s.Name,
						running = ServerManager.IsRunning(s),
						coreType = s.CoreType,
						mcVersion = s.McVersion
					});
			WriteJson(resp, servers);
			return;
		}

		string folder = req.QueryString["folder"] ?? "";
		var server = FindServer(folder);

		if (path == "/api/log")
		{
			WriteText(resp, server == null ? "" : ServerManager.GetServerLog(server));
			return;
		}

		if (path == "/api/start" && req.HttpMethod == "POST")
		{
			TryAction(resp, server, () => ServerManager.StartServer(server!));
			return;
		}

		if (path == "/api/stop" && req.HttpMethod == "POST")
		{
			TryAction(resp, server, () => ServerManager.StopServer(server!));
			return;
		}

		if (path == "/api/restart" && req.HttpMethod == "POST")
		{
			TryAction(resp, server, () =>
			{
				if (ServerManager.IsRunning(server!))
				{
					ServerManager.SendCommand(server, "stop");
					Thread.Sleep(1500);
					if (ServerManager.IsRunning(server)) ServerManager.StopServer(server);
					Thread.Sleep(500);
				}
				ServerManager.StartServer(server!);
			});
			return;
		}

		if (path == "/api/command" && req.HttpMethod == "POST")
		{
			string cmd = req.QueryString["cmd"] ?? "";
			if (server == null || !ServerManager.IsRunning(server))
			{
				WriteJson(resp, new { ok = false, msg = "服务器未运行" });
				return;
			}
			ServerManager.SendCommand(server, cmd);
			WriteJson(resp, new { ok = true });
			return;
		}

		WriteJson(resp, new { ok = false, msg = "未知请求" });
	}

	private static void TryAction(HttpListenerResponse resp, ServerInfo? server, Action action)
	{
		if (server == null)
		{
			WriteJson(resp, new { ok = false, msg = "服务器不存在" });
			return;
		}
		try
		{
			action();
			WriteJson(resp, new { ok = true });
		}
		catch (Exception ex)
		{
			WriteJson(resp, new { ok = false, msg = ex.Message });
		}
	}

	private static ServerInfo? FindServer(string folder)
	{
		if (string.IsNullOrEmpty(folder))
			return null;
		lock (ServerManager.Servers)
			return ServerManager.Servers.FirstOrDefault(s => string.Equals(s.FolderName, folder, StringComparison.OrdinalIgnoreCase));
	}

	private static void WriteJson(HttpListenerResponse resp, object data)
	{
		var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
		resp.ContentType = "application/json; charset=utf-8";
		resp.ContentLength64 = bytes.Length;
		resp.OutputStream.Write(bytes, 0, bytes.Length);
		resp.OutputStream.Close();
	}

	private static void WriteText(HttpListenerResponse resp, string text)
	{
		var bytes = Encoding.UTF8.GetBytes(text);
		resp.ContentType = "text/plain; charset=utf-8";
		resp.ContentLength64 = bytes.Length;
		resp.OutputStream.Write(bytes, 0, bytes.Length);
		resp.OutputStream.Close();
	}

	private static void WriteHtml(HttpListenerResponse resp, string html)
	{
		var bytes = Encoding.UTF8.GetBytes(html);
		resp.ContentType = "text/html; charset=utf-8";
		resp.ContentLength64 = bytes.Length;
		resp.OutputStream.Write(bytes, 0, bytes.Length);
		resp.OutputStream.Close();
	}

	private const string Html = """
<!DOCTYPE html>
<html lang='zh-CN'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Clock 服务器管理</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:-apple-system,'Segoe UI','Microsoft YaHei',sans-serif;background:#0f1520;color:#e6edf3;min-height:100vh;padding:24px}
h1{font-size:20px;margin-bottom:16px}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:14px;margin-bottom:24px}
.card{background:#1b2331;border:1px solid #2d3a4d;border-radius:12px;padding:16px}
.card .name{font-size:16px;font-weight:600;margin-bottom:4px}
.card .info{font-size:12px;color:#8b98a9;margin-bottom:12px}
.badge{display:inline-block;padding:2px 10px;border-radius:20px;font-size:12px;font-weight:600}
.running{background:#1f6f3a;color:#8ef0a8}
.stopped{background:#2d3a4d;color:#8b98a9}
.btn{display:inline-block;border:none;border-radius:8px;padding:7px 14px;font-size:13px;cursor:pointer;margin-right:8px;color:#fff}
.btn-start{background:#1f6feb}
.btn-stop{background:#d1242f}
.btn-restart{background:#8250df}
.btn:disabled{opacity:.45;cursor:not-allowed}
.console{background:#0a0f18;border:1px solid #2d3a4d;border-radius:12px;padding:14px;font-family:Consolas,monospace;font-size:12px;white-space:pre-wrap;word-break:break-all;height:360px;overflow:auto}
.cmdrow{margin-top:12px;display:flex;gap:8px}
.cmdrow input{flex:1;background:#1b2331;border:1px solid #2d3a4d;border-radius:8px;padding:8px 12px;color:#e6edf3;font-size:13px}
.cmdrow input:focus{outline:none;border-color:#1f6feb}
.cmdrow button{background:#1f6feb;border:none;border-radius:8px;padding:8px 16px;color:#fff;cursor:pointer}
.hint{color:#8b98a9;font-size:12px;margin-bottom:16px}
</style>
</head>
<body>
<h1>Clock 服务器管理</h1>
<p class='hint'>在浏览器中管理本地 Minecraft 服务器 · 地址 http://127.0.0.1:57263</p>
<div class='cards' id='cards'></div>
<div id='detail' style='display:none'>
<div class='console' id='console'>（选择左侧服务器以查看控制台）</div>
<div class='cmdrow'><input id='cmd' placeholder='输入控制台命令，如 stop / list / say …' onkeydown='if(event.key=="Enter")sendCmd()'><button onclick='sendCmd()'>发送</button></div>
</div>
<script>
let current='';
let lastLen=0;
async function load(){
  const r=await fetch('/api/servers');const list=await r.json();
  const cards=document.getElementById('cards');cards.innerHTML='';
  for(const s of list){
    const d=document.createElement('div');d.className='card';
    d.innerHTML=`<div class='name'>${esc(s.name)}</div><div class='info'>${esc(s.coreType)} ${esc(s.mcVersion||'')}</div>`+
      `<span class='badge ${s.running?'running':'stopped'}'>${s.running?'运行中':'已停止'}</span><br><br>`+
      `<button class='btn btn-start' ${s.running?'disabled':''} onclick='act("start","${esc(s.folder)}")'>启动</button>`+
      `<button class='btn btn-stop' ${s.running?'':'disabled'} onclick='act("stop","${esc(s.folder)}")'>停止</button>`+
      `<button class='btn btn-restart' ${s.running?'':'disabled'} onclick='act("restart","${esc(s.folder)}")'>重启</button>`;
    d.addEventListener('click',e=>{if(e.target.tagName!=='BUTTON'){current=s.folder;document.getElementById('detail').style.display='block';pollLog();}});
    cards.appendChild(d);
  }
  if(!current && list.length) current=list[0].folder;
}
async function act(type,folder){await fetch(`/api/${type}?folder=${encodeURIComponent(folder)}`,{method:'POST'});load();}
async function sendCmd(){const c=document.getElementById('cmd').value.trim();if(!c||!current)return;await fetch(`/api/command?folder=${encodeURIComponent(current)}&cmd=${encodeURIComponent(c)}`,{method:'POST'});document.getElementById('cmd').value='';}
async function pollLog(){
  if(!current)return;
  const r=await fetch('/api/log?folder='+encodeURIComponent(current));const t=await r.text();
  const el=document.getElementById('console');el.textContent=t;el.scrollTop=el.scrollHeight;
}
function esc(s){return (s||'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
load();
setInterval(load,3000);
setInterval(pollLog,1500);
</script>
</body>
</html>
""";
}

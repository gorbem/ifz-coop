using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace IfzCoopUpdater
{
    internal static class Program
    {
        public const string Repo = "gorbem/ifz-coop";
        public const string VersionFile = "_ifz_coop_version.txt";

        public static readonly string[] ManifestUrls =
        {
            "https://api.github.com/repos/" + Repo + "/contents/manifest.json",
            "https://raw.githubusercontent.com/" + Repo + "/main/manifest.json",
            "https://cdn.jsdelivr.net/gh/" + Repo + "@main/manifest.json",
            "https://github.com/" + Repo + "/raw/main/manifest.json",
        };

        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

            if (args.Length >= 2 && args[0].Equals("/silent", StringComparison.OrdinalIgnoreCase))
            {
                var log = new StringBuilder();
                int code;
                try
                {
                    var manifest = ManifestLoader.Load(m => log.AppendLine(m));
                    code = Installer.Run(args[1], manifest, m => log.AppendLine(m), null, true);
                }
                catch (Exception e)
                {
                    log.AppendLine("FATAL: " + e);
                    code = 2;
                }
                try { File.WriteAllText(Path.Combine(args[1], "_updater_log.txt"), log.ToString(), Encoding.UTF8); } catch { }
                Environment.Exit(code);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UpdaterForm());
        }
    }

    internal sealed class Manifest
    {
        public string modVersion { get; set; }
        public string gameVersion { get; set; }
        public string modUrl { get; set; }
        public string modSha256 { get; set; }
        public string bepinexVersion { get; set; }
        public string bepinexUrl { get; set; }
        public string bepinexSha256 { get; set; }
    }

    internal static class ManifestLoader
    {
        public static Manifest Load(Action<string> log)
        {
            Exception last = null;
            foreach (var url in Program.ManifestUrls)
            {
                try
                {
                    log?.Invoke("Читаю манифест: " + url);
                    using (var client = Http.CreateClient())
                    using (var resp = client.GetAsync(url).Result)
                    {
                        resp.EnsureSuccessStatusCode();
                        var json = resp.Content.ReadAsStringAsync().Result;
                        if (url.Contains("api.github.com"))
                        {
                            var doc = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                            var b64 = (string)doc["content"];
                            json = Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace("\n", "")));
                        }
                        var m = new JavaScriptSerializer().Deserialize<Manifest>(json);
                        if (m == null || string.IsNullOrEmpty(m.modUrl) || string.IsNullOrEmpty(m.modVersion))
                            throw new Exception("манифест пустой или неполный");
                        log?.Invoke("Манифест: мод " + m.modVersion + ", BepInEx " + m.bepinexVersion);
                        return m;
                    }
                }
                catch (Exception e)
                {
                    last = e;
                    log?.Invoke("  не вышло: " + e.Message);
                }
            }
            throw new Exception("Не удалось получить manifest.json ни с одного адреса.\n" + last?.Message);
        }
    }

    internal static class Http
    {
        public static HttpClient CreateClient()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromMinutes(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("IFZ-Co-op-Updater/1.0");
            return c;
        }

        public static void Download(HttpClient client, string url, string dest, Action<long, long> progress)
        {
            using (var resp = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result)
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1L;
                using (var src = resp.Content.ReadAsStreamAsync().Result)
                using (var dst = File.Create(dest))
                {
                    var buf = new byte[81920];
                    long read = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        read += n;
                        progress?.Invoke(read, total);
                    }
                }
            }
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }
    }

    internal static class Installer
    {
        public const string ExeName = "Infection Free Zone.exe";
        public const string BackupPrefix = "_coop_backup_";

        public static int Run(string dir, Manifest manifest, Action<string> log,
                              Action<int> pct, bool silent)
        {
            Action<string> say = m => log?.Invoke(m);
            Action<int> prog = p => pct?.Invoke(p);

            dir = (dir ?? "").Trim().TrimEnd('\\', '/');
            if (!Directory.Exists(dir)) throw new Exception("Папка не найдена: " + dir);
            if (!File.Exists(Path.Combine(dir, ExeName)))
            {
                var sub = Path.Combine(dir, "Infection Free Zone");
                if (File.Exists(Path.Combine(sub, ExeName))) dir = sub;
                else throw new Exception("В папке нет " + ExeName + ". Выбери папку игры.");
            }

            say("Папка игры: " + dir);
            if (Process.GetProcessesByName("Infection Free Zone").Length > 0)
                throw new Exception("Infection Free Zone запущена. Закрой игру и повтори.");

            var gameVersion = GameVersion(dir);
            say("Версия игры: " + (gameVersion ?? "неизвестна"));
            if (gameVersion != null && manifest.gameVersion != null && gameVersion != manifest.gameVersion)
                say("ВНИМАНИЕ: мод собран под " + manifest.gameVersion + ".");

            var local = LocalVersion(dir);
            say("Установленная версия мода: " + (local ?? "нет") + "  →  доступна: " + manifest.modVersion);

            var tmp = Path.Combine(Path.GetTempPath(), "ifz-coop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                using (var client = Http.CreateClient())
                {
                    var bepZip = Path.Combine(tmp, "bepinex.zip");
                    say("Скачиваю BepInEx " + manifest.bepinexVersion + " (официальный GitHub)...");
                    Http.Download(client, manifest.bepinexUrl, bepZip, (r, t) => Report(prog, r, t, 0, 45));
                    Verify(bepZip, manifest.bepinexSha256, "BepInEx");

                    var modZip = Path.Combine(tmp, "mod.zip");
                    say("Скачиваю мод " + manifest.modVersion + "...");
                    Http.Download(client, manifest.modUrl, modZip, (r, t) => Report(prog, r, t, 45, 95));
                    Verify(modZip, manifest.modSha256, "мод");

                    say("Распаковываю...");
                    var bepDir = Path.Combine(tmp, "bep");
                    var modDir = Path.Combine(tmp, "mod");
                    ZipFile.ExtractToDirectory(bepZip, bepDir);
                    ZipFile.ExtractToDirectory(modZip, modDir);

                    var backup = Path.Combine(dir, BackupPrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    Backup(dir, backup, say);

                    say("Копирую файлы...");
                    CopyTree(bepDir, dir);
                    CopyTree(modDir, dir);

                    File.WriteAllText(Path.Combine(dir, Program.VersionFile),
                        manifest.modVersion + Environment.NewLine, new UTF8Encoding(false));

                    try { Shortcuts(dir); } catch (Exception e) { say("Ярлык не создан: " + e.Message); }

                    prog(100);
                    say("");
                    say("ГОТОВО. Версия " + manifest.modVersion + ".");
                    say("Запуск: ярлык «Infection Free Zone (Co-op)» или «Запуск кооп.bat».");
                    say("Steam должен быть запущен. Не запускай игру кнопкой Play в Steam.");
                    return 0;
                }
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
        }

        private static void Verify(string path, string expected, string what)
        {
            if (string.IsNullOrEmpty(expected)) return;
            var actual = Http.Sha256(path);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new Exception("SHA-256 " + what + " не совпал.\nОжидался: " + expected + "\nПолучен: " + actual);
        }

        private static void Report(Action<int> pct, long read, long total, int from, int to)
        {
            if (pct == null) return;
            if (total <= 0) { pct(from); return; }
            pct(from + (int)((to - from) * read / total));
        }

        public static string GameVersion(string dir)
        {
            try
            {
                var gg = Path.Combine(dir, "Infection Free Zone_Data", "globalgamemanagers");
                if (!File.Exists(gg)) return null;
                var text = Encoding.ASCII.GetString(File.ReadAllBytes(gg));
                var m = System.Text.RegularExpressions.Regex.Match(text, @"\d+\.\d+\.\d+\.\d+");
                return m.Success ? m.Value : null;
            }
            catch { return null; }
        }

        public static string LocalVersion(string dir)
        {
            try
            {
                var f = Path.Combine(dir, Program.VersionFile);
                return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
            }
            catch { return null; }
        }

        private static void Backup(string dir, string backup, Action<string> say)
        {
            var items = new[]
            {
                "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "steam_appid.txt",
                Path.Combine("Infection Free Zone_Data", "Plugins", "x86_64", "steam_api64.dll"),
                Path.Combine("BepInEx", "plugins", "IFZMultiplayer"),
                Path.Combine("BepInEx", "plugins", "EconomyConfigBridge"),
            };
            foreach (var rel in items)
            {
                var src = Path.Combine(dir, rel);
                var dst = Path.Combine(backup, rel);
                if (File.Exists(src))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);
                }
                else if (Directory.Exists(src))
                {
                    CopyTree(src, dst);
                }
            }
            if (Directory.Exists(backup)) say("Резервная копия: " + backup);
        }

        private static void CopyTree(string src, string dst)
        {
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(src.Length).TrimStart('\\');
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private static void Shortcuts(string dir)
        {
            var exe = Path.Combine(dir, ExeName);
            var bat = Path.Combine(dir, "Запуск кооп.bat");
            if (!File.Exists(bat))
                File.WriteAllText(bat, "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"" + ExeName + "\"\r\n", Encoding.ASCII);

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var link = Path.Combine(desktop, "Infection Free Zone (Co-op).lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic sc = shell.CreateShortcut(link);
            sc.TargetPath = exe;
            sc.WorkingDirectory = dir;
            sc.IconLocation = exe + ",0";
            sc.Description = "Infection Free Zone — кооп-сборка";
            sc.Save();
        }
    }

    internal sealed class UpdaterForm : Form
    {
        private readonly TextBox _path = new TextBox();
        private readonly TextBox _log = new TextBox();
        private readonly Button _install = new Button();
        private readonly Button _check = new Button();
        private readonly Button _play = new Button();
        private readonly Label _status = new Label();
        private Manifest _manifest;

        public UpdaterForm()
        {
            Text = "Infection Free Zone — Co-op Updater";
            Width = 820;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var title = new Label
            {
                Text = "IFZ Co-op Updater",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 12)
            };
            var sub = new Label
            {
                Text = "IFZ Multiplayer RC7 (патч 0.26.10.8) + BepInEx 5.4.23.2 · github.com/" + Program.Repo,
                AutoSize = true,
                Location = new Point(18, 44),
                ForeColor = Color.DimGray
            };
            var pathLabel = new Label { Text = "Папка игры:", AutoSize = true, Location = new Point(18, 76) };
            _path.Location = new Point(100, 73);
            _path.Width = 570;
            var browse = new Button { Text = "Обзор...", Location = new Point(680, 71), Width = 110 };
            browse.Click += (s, e) => Browse();

            _check.Text = "Проверить обновления";
            _check.Location = new Point(18, 108);
            _check.Width = 170;
            _check.Click += async (s, e) => await CheckAsync();

            _install.Text = "Установить / Обновить";
            _install.Location = new Point(196, 108);
            _install.Width = 180;
            _install.Click += async (s, e) => await InstallAsync();

            _play.Text = "Играть";
            _play.Location = new Point(384, 108);
            _play.Width = 110;
            _play.Click += (s, e) => Play();

            _status.Location = new Point(18, 148);
            _status.Width = 770;
            _status.Height = 22;
            _status.Text = "Укажи папку игры и нажми «Установить / Обновить».";

            _log.Location = new Point(18, 176);
            _log.Width = 772;
            _log.Height = 290;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Color.White;
            _log.Font = new Font("Consolas", 8.5f);

            Controls.AddRange(new Control[] { title, sub, pathLabel, _path, browse, _check, _install, _play, _status, _log });

            var saved = LoadSettings();
            if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved)) _path.Text = saved;

            Shown += (s, e) => { _ = CheckAsync(); };
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Выбери папку Infection Free Zone";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _path.Text = dlg.SelectedPath;
                    SaveSettings(dlg.SelectedPath);
                }
            }
        }

        private void Append(string s)
        {
            if (!IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Append), s); return; }
            _log.AppendText(s + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private async Task CheckAsync()
        {
            _check.Enabled = false;
            _status.Text = "Проверяю обновления...";
            try
            {
                _manifest = await Task.Run(() => ManifestLoader.Load(Append));
                var local = Installer.LocalVersion(_path.Text.Trim());
                var v = "Установлено: " + (local ?? "нет") + "   ·   В сети: " + _manifest.modVersion;
                if (local == _manifest.modVersion) v += "   (актуально)";
                _status.Text = v;
            }
            catch (Exception e)
            {
                _status.Text = "Не удалось проверить обновления.";
                Append("ОШИБКА: " + e.Message);
            }
            finally { _check.Enabled = true; }
        }

        private async Task InstallAsync()
        {
            var dir = _path.Text.Trim();
            if (dir.Length == 0) { MessageBox.Show(this, "Выбери папку игры."); return; }
            _install.Enabled = false;
            _check.Enabled = false;
            _log.Text = "";
            try
            {
                if (_manifest == null) _manifest = await Task.Run(() => ManifestLoader.Load(Append));
                var code = await Task.Run(() => Installer.Run(dir, _manifest, Append,
                    p => { if (IsHandleCreated) BeginInvoke(new Action(() => _status.Text = "Установка... " + p + "%")); },
                    false));
                if (code == 0) { SaveSettings(dir); _status.Text = "Готово: " + _manifest.modVersion; }
            }
            catch (Exception e)
            {
                Append("ОШИБКА: " + e.Message);
                MessageBox.Show(this, e.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { _install.Enabled = true; _check.Enabled = true; }
        }

        private void Play()
        {
            var dir = _path.Text.Trim();
            var exe = Path.Combine(dir, Installer.ExeName);
            if (!File.Exists(exe)) { MessageBox.Show(this, "Сначала укажи папку игры и установи мод."); return; }
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir });
        }

        private static string SettingsPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ifz-coop");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.txt");
        }

        private static string LoadSettings()
        {
            try { var p = SettingsPath(); return File.Exists(p) ? File.ReadAllText(p).Trim() : null; }
            catch { return null; }
        }

        private static void SaveSettings(string dir)
        {
            try { File.WriteAllText(SettingsPath(), dir); } catch { }
        }
    }
}

// Soundpack Switcher — one-click GTA V soundpack switching.
// Swaps RESIDENT.rpf and WEAPONS_PLAYER.rpf in <GTA V>\x64\audio\sfx.
// Targets .NET Framework 4.x (built into Windows 10/11). Build: see build.sh / build.bat.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Soundpack Switcher")]
[assembly: System.Reflection.AssemblyProduct("Soundpack Switcher")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace SoundpackSwitcher
{
    public class Pack
    {
        public string Name;
        public string Dir;
        public bool IsOriginal;
        public bool Has(string file) { return File.Exists(Path.Combine(Dir, file)); }
        public long Size { get { return Core.Files.Where(Has).Sum(f => new FileInfo(Path.Combine(Dir, f)).Length); } }
    }

    public static class Core
    {
        public static readonly string[] Files = { "RESIDENT.rpf", "WEAPONS_PLAYER.rpf" };
        public const string OriginalName = "Original (backup)";
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soundpack Switcher");
        public static string PacksDir { get { return Path.Combine(DataDir, "Packs"); } }
        public static string OriginalDir { get { return Path.Combine(DataDir, "Original"); } }
        static string SettingsPath { get { return Path.Combine(DataDir, "settings.ini"); } }
        public static string GameDir;
        public static string BackupDir;
        public static bool ShortcutPromptOff;

        static readonly string[] RelCandidates = {
            @"Program Files\Rockstar Games\Grand Theft Auto V Legacy",
            @"Program Files\Rockstar Games\Grand Theft Auto V",
            @"Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Legacy",
            @"Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V",
            @"SteamLibrary\steamapps\common\Grand Theft Auto V Legacy",
            @"SteamLibrary\steamapps\common\Grand Theft Auto V",
            @"Program Files\Epic Games\GTAV",
            @"Epic Games\GTAV",
            @"Games\Grand Theft Auto V Legacy",
            @"Games\Grand Theft Auto V",
            @"Rockstar Games\Grand Theft Auto V Legacy",
            @"Rockstar Games\Grand Theft Auto V",
            @"Program Files\Rockstar Games\Grand Theft Auto V Enhanced",
        };

        public static void Init()
        {
            Directory.CreateDirectory(PacksDir);
            LoadSettings();
            if (!IsValidGameDir(GameDir)) { GameDir = Detect(); if (GameDir != null) SaveSettings(); }
        }

        static void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "GameDir") GameDir = v;
                    else if (k == "BackupDir") BackupDir = v;
                    else if (k == "ShortcutPrompt") ShortcutPromptOff = v == "off";
                }
            }
            catch { }
        }

        public static void SaveSettings()
        {
            try { Directory.CreateDirectory(DataDir); File.WriteAllText(SettingsPath, "GameDir=" + (GameDir ?? "") + "\r\nBackupDir=" + (BackupDir ?? "") + "\r\nShortcutPrompt=" + (ShortcutPromptOff ? "off" : "on") + "\r\n"); } catch { }
        }

        public static bool IsValidGameDir(string d)
        {
            return !string.IsNullOrEmpty(d) && Directory.Exists(d) && File.Exists(Path.Combine(d, "RESIDENT.rpf"));
        }

        /// Accepts the game root, the sfx folder, or anything in between; returns the sfx folder or null.
        public static string NormalizeGameDir(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return null;
            foreach (var c in new[] { picked, Path.Combine(picked, "sfx"), Path.Combine(picked, @"audio\sfx"), Path.Combine(picked, @"x64\audio\sfx") })
                if (IsValidGameDir(c)) return Path.GetFullPath(c);
            return null;
        }

        static string Detect()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try { if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue; } catch { continue; }
                foreach (var rel in RelCandidates)
                {
                    var d = NormalizeGameDir(Path.Combine(drive.RootDirectory.FullName, rel));
                    if (d != null) return d;
                }
            }
            return null;
        }

        public static List<Pack> ListPacks()
        {
            var list = new List<Pack>();
            if (Files.Any(f => File.Exists(Path.Combine(OriginalDir, f))))
                list.Add(new Pack { Name = OriginalName, Dir = OriginalDir, IsOriginal = true });
            if (Directory.Exists(PacksDir))
                foreach (var d in Directory.GetDirectories(PacksDir).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
                {
                    var p = new Pack { Name = Path.GetFileName(d), Dir = d };
                    if (Files.Any(p.Has)) list.Add(p);
                }
            return list;
        }

        public static Pack FindPack(string name)
        {
            return ListPacks().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                || (p.IsOriginal && string.Equals(name, "original", StringComparison.OrdinalIgnoreCase)));
        }

        // ---------- fingerprints (size + hash of first/last 1 MB) ----------
        static readonly Dictionary<string, string> fpCache = new Dictionary<string, string>();
        public static string Fingerprint(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            string key = path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            string fp;
            if (fpCache.TryGetValue(key, out fp)) return fp;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = SHA1.Create())
            {
                const int N = 1 << 20;
                var buf = new byte[N];
                int a = fs.Read(buf, 0, (int)Math.Min(N, fs.Length));
                sha.TransformBlock(buf, 0, a, null, 0);
                if (fs.Length > N)
                {
                    fs.Seek(Math.Max(N, fs.Length - N), SeekOrigin.Begin);
                    int b = fs.Read(buf, 0, N);
                    sha.TransformBlock(buf, 0, b, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                fp = fi.Length + ":" + BitConverter.ToString(sha.Hash).Replace("-", "");
            }
            fpCache[key] = fp;
            return fp;
        }

        /// The pack whose files match what's in the game folder right now (null if none / unknown).
        public static Pack ActivePack(List<Pack> packs)
        {
            if (!IsValidGameDir(GameDir)) return null;
            var game = Files.ToDictionary(f => f, f => SafeFp(Path.Combine(GameDir, f)));
            var orig = Files.ToDictionary(f => f, f => SafeFp(Path.Combine(OriginalDir, f)));
            // Prefer real packs over the backup in case the backup itself was a pack.
            foreach (var p in packs.Where(x => !x.IsOriginal).Concat(packs.Where(x => x.IsOriginal)))
            {
                bool all = true;
                foreach (var f in Files)
                {
                    string want = p.Has(f) ? SafeFp(Path.Combine(p.Dir, f)) : orig[f];
                    if (want == null || want != game[f]) { all = false; break; }
                }
                if (all) return p;
            }
            return null;
        }
        static string SafeFp(string p) { try { return Fingerprint(p); } catch { return null; } }

        // ---------- import ----------
        public static Pack Import(IEnumerable<string> paths, string nameOverride = null)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string nameHint = null, tempDir = null;
            try
            {
                foreach (var raw in paths)
                {
                    var path = raw.Trim('"');
                    if (Directory.Exists(path))
                    {
                        Collect(path, found);
                        if (nameHint == null) nameHint = Path.GetFileName(path.TrimEnd('\\', '/'));
                    }
                    else if (File.Exists(path))
                    {
                        string ext = Path.GetExtension(path).ToLowerInvariant();
                        if (ext == ".zip" || ext == ".oiv")
                        {
                            tempDir = Path.Combine(Path.GetTempPath(), "SoundpackSwitcher_" + Guid.NewGuid().ToString("N"));
                            ZipFile.ExtractToDirectory(path, tempDir);
                            Collect(tempDir, found);
                            if (nameHint == null) nameHint = Path.GetFileNameWithoutExtension(path);
                        }
                        else if (ext == ".rar" || ext == ".7z")
                            throw new Exception("RAR and 7z archives aren't supported. Extract it first, then drop the folder.");
                        else
                        {
                            string fname = Path.GetFileName(path);
                            var match = Files.FirstOrDefault(f => string.Equals(f, fname, StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                            {
                                found[match] = path;
                                if (nameHint == null) nameHint = Path.GetFileName(Path.GetDirectoryName(path));
                            }
                        }
                    }
                }
                if (found.Count == 0)
                    throw new Exception("No RESIDENT.rpf or WEAPONS_PLAYER.rpf found in what you dropped.");

                // Folder names like "sfx" or "audio" say nothing about the pack.
                var generic = new[] { "sfx", "audio", "x64", "desktop", "downloads", "documents" };
                if (string.IsNullOrWhiteSpace(nameHint) || generic.Contains(nameHint.ToLowerInvariant()))
                    nameHint = "Soundpack " + DateTime.Now.ToString("yyyy-MM-dd HH.mm");
                string name = Sanitize(nameOverride ?? nameHint);

                // Same files already in the library? Reuse that pack.
                foreach (var p in ListPacks().Where(x => !x.IsOriginal))
                {
                    if (Files.All(f => found.ContainsKey(f) == p.Has(f)
                        && (!found.ContainsKey(f) || SafeFp(found[f]) == SafeFp(Path.Combine(p.Dir, f)))))
                        return p;
                }

                string dir = Path.Combine(PacksDir, name);
                for (int i = 2; Directory.Exists(dir); i++) dir = Path.Combine(PacksDir, name + " (" + i + ")");
                Directory.CreateDirectory(dir);
                foreach (var kv in found) File.Copy(kv.Value, Path.Combine(dir, kv.Key), true);
                return new Pack { Name = Path.GetFileName(dir), Dir = dir };
            }
            finally
            {
                if (tempDir != null) try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static void Collect(string dir, Dictionary<string, string> found)
        {
            foreach (var f in Files)
            {
                if (found.ContainsKey(f)) continue;
                var hits = Directory.EnumerateFiles(dir, f, SearchOption.AllDirectories)
                    .Where(p => string.Equals(Path.GetFileName(p), f, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.Count(c => c == '\\' || c == '/'))
                    .ToList();
                if (hits.Count > 0) found[f] = hits[0];
            }
        }

        public static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            if (name.Length > 80) name = name.Substring(0, 80);
            if (name.Length == 0 || string.Equals(name, "Original", StringComparison.OrdinalIgnoreCase)) name = "Soundpack";
            return name;
        }

        // ---------- apply ----------
        public static string RunningGame()
        {
            foreach (var p in Process.GetProcesses())
            {
                string n;
                try { n = p.ProcessName; } catch { continue; }
                if (n.Equals("GTA5", StringComparison.OrdinalIgnoreCase) || n.Equals("GTA5_Enhanced", StringComparison.OrdinalIgnoreCase)
                    || (n.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase) && n.EndsWith("GTAProcess", StringComparison.OrdinalIgnoreCase))
                    || n.StartsWith("RAGEMP", StringComparison.OrdinalIgnoreCase) || n.Equals("altv", StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            return null;
        }

        /// Backs up the current game files once, before the first switch.
        public static void EnsureOriginalBackup()
        {
            if (Files.Any(f => File.Exists(Path.Combine(OriginalDir, f)))) return;
            Directory.CreateDirectory(OriginalDir);
            foreach (var f in Files)
            {
                var src = Path.Combine(GameDir, f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(OriginalDir, f), true);
            }
            File.WriteAllText(Path.Combine(OriginalDir, "backed-up.txt"),
                "Backed up from " + GameDir + " on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n");
        }

        public static void Apply(Pack pack)
        {
            if (!IsValidGameDir(GameDir)) throw new Exception("GTA V's audio folder isn't set. Choose it under Game folder.");
            var running = RunningGame();
            if (running != null) throw new Exception("Close the game first (" + running + " is running). It locks the audio files.");
            EnsureOriginalBackup();

            var pairs = new List<string>();
            foreach (var f in Files)
            {
                string src = pack.Has(f) ? Path.Combine(pack.Dir, f) : Path.Combine(OriginalDir, f);
                string dst = Path.Combine(GameDir, f);
                if (!File.Exists(src)) continue;
                if (SafeFp(src) == SafeFp(dst)) continue; // already in place
                pairs.Add(src); pairs.Add(dst);
            }
            if (pairs.Count == 0) return;

            if (CanWrite(GameDir)) CopyPairs(pairs);
            else RunElevatedCopy(pairs);
        }

        public static bool CanWrite(string dir)
        {
            try
            {
                using (new FileStream(Path.Combine(dir, ".soundpackswitcher_write_test"), FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch { return false; }
        }

        public static void CopyPairs(IList<string> pairs)
        {
            for (int i = 0; i + 1 < pairs.Count; i += 2)
            {
                string src = pairs[i], dst = pairs[i + 1], tmp = dst + ".soundpackswitcher.tmp";
                File.Copy(src, tmp, true);
                if (File.Exists(dst))
                {
                    File.SetAttributes(dst, FileAttributes.Normal);
                    File.Delete(dst);
                }
                File.Move(tmp, dst);
            }
        }

        static void RunElevatedCopy(List<string> pairs)
        {
            string errFile = Path.Combine(Path.GetTempPath(), "SoundpackSwitcher_err_" + Guid.NewGuid().ToString("N") + ".txt");
            var args = new StringBuilder("--copy " + Quote(errFile));
            foreach (var p in pairs) args.Append(' ').Append(Quote(p));
            var psi = new ProcessStartInfo(Application.ExecutablePath, args.ToString())
            { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            Process proc;
            try { proc = Process.Start(psi); }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) throw new Exception("Switch cancelled: Windows needs admin permission to write to the game folder.");
                throw;
            }
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                string msg = File.Exists(errFile) ? File.ReadAllText(errFile) : "Copy failed (code " + proc.ExitCode + ").";
                try { File.Delete(errFile); } catch { }
                throw new Exception(msg);
            }
        }

        static string Quote(string s) { return "\"" + s.Replace("\"", "") + "\""; }

        public static string Size(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.00") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            return (b / 1024.0).ToString("0") + " KB";
        }

        // ---------- backup current game files to a zip ----------
        /// Zips the RESIDENT.rpf / WEAPONS_PLAYER.rpf currently in the game folder into destZip.
        /// The zip has the files at its root, so it can be dropped straight back into the app.
        public static void BackupCurrent(string destZip, Action<string> progress)
        {
            if (!IsValidGameDir(GameDir)) throw new Exception("GTA V's audio folder isn't set. Choose it under Game folder.");
            var present = Files.Where(f => File.Exists(Path.Combine(GameDir, f))).ToList();
            if (present.Count == 0) throw new Exception("No soundpack files found in the game folder.");
            string tmp = destZip + ".partial";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (var f in present)
                    {
                        if (progress != null) progress("Zipping " + f + "…");
                        var entry = zip.CreateEntry(f, CompressionLevel.Optimal);
                        entry.LastWriteTime = File.GetLastWriteTime(Path.Combine(GameDir, f));
                        using (var src = new FileStream(Path.Combine(GameDir, f), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var dst = entry.Open())
                            src.CopyTo(dst, 1 << 20);
                    }
                }
                if (File.Exists(destZip)) File.Delete(destZip);
                File.Move(tmp, destZip);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        // ---------- shortcuts ----------
        public enum ShortcutState { Ok, Missing, Stale }

        /// Looks on the user's and the shared desktop for a shortcut that opens this exe with no arguments
        /// (the one you drop soundpacks onto). Stale = a "Soundpack Switcher" shortcut exists but points elsewhere.
        public static ShortcutState MainShortcutState()
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return ShortcutState.Ok; // can't check; don't nag
            object shell = Activator.CreateInstance(shellType);
            string exe = Path.GetFullPath(Application.ExecutablePath);
            bool stale = false;
            foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
            {
                string dir;
                try { dir = Environment.GetFolderPath(folder); } catch { continue; }
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (var lnk in Directory.GetFiles(dir, "*.lnk"))
                {
                    try
                    {
                        object sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                        Type t = sc.GetType();
                        string target = (string)t.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) ?? "";
                        string args = ((string)t.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, sc, null) ?? "").Trim();
                        bool ours = target.Length > 0 && string.Equals(Path.GetFullPath(target), exe, StringComparison.OrdinalIgnoreCase);
                        if (ours && args.Length == 0) return ShortcutState.Ok;
                        if (!ours && string.Equals(Path.GetFileNameWithoutExtension(lnk), "Soundpack Switcher", StringComparison.OrdinalIgnoreCase)) stale = true;
                    }
                    catch { }
                }
            }
            return stale ? ShortcutState.Stale : ShortcutState.Missing;
        }

        public static string CreateShortcut(string title, string args)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desktop, Sanitize(title) + ".lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type t = sc.GetType();
            t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Application.ExecutablePath });
            t.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { args });
            t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
            t.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Application.ExecutablePath + ",0" });
            t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "Soundpack Switcher: " + title });
            t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            return lnk;
        }
    }

    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(20, 22, 27);
        public static readonly Color Panel = Color.FromArgb(30, 33, 40);
        public static readonly Color Line = Color.FromArgb(50, 54, 64);
        public static readonly Color Text = Color.FromArgb(234, 236, 242);
        public static readonly Color Muted = Color.FromArgb(150, 156, 170);
        public static readonly Color Accent = Color.FromArgb(240, 180, 41);
        public static readonly Color Good = Color.FromArgb(92, 201, 130);
        public static readonly Color Bad = Color.FromArgb(255, 122, 110);
        public static Font UI(float size, FontStyle st = FontStyle.Regular) { return new Font("Segoe UI", size, st); }

        public static Button MakeButton(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 8), Cursor = Cursors.Hand,
                BackColor = primary ? Accent : Panel, ForeColor = primary ? Bg : Text, Font = UI(9.5f, primary ? FontStyle.Bold : FontStyle.Regular),
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Line;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 200, 80) : Line;
            return b;
        }
    }

    class MainForm : Form
    {
        ListView list;
        Label status, gameLabel;
        Panel drop;
        Button applyBtn, origBtn, backupBtn, renameBtn, deleteBtn, shortcutBtn;
        bool busy;

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = "Soundpack Switcher";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(560, 420);
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.UI(9.5f);
            AllowDrop = true;
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var head = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), WrapContents = false };
            head.Controls.Add(new Label { Text = "Soundpack Switcher", AutoSize = true, Font = Theme.UI(18f, FontStyle.Bold), ForeColor = Theme.Text, Margin = new Padding(0) });
            head.Controls.Add(new Label { Text = "for GTA V", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.UI(10f), Margin = new Padding(10, 12, 0, 0) });
            root.Controls.Add(head);

            drop = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Cursor = Cursors.Hand };
            drop.Paint += PaintDrop;
            drop.Resize += (s, e) => drop.Invalidate();
            drop.Click += (s, e) => BrowseImport();
            root.Controls.Add(drop);

            list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
                BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                OwnerDraw = true, Font = Theme.UI(10f), Margin = new Padding(0, 0, 0, 12),
            };
            list.Columns.Add("Soundpack", 300);
            list.Columns.Add("Files", 150);
            list.Columns.Add("Size", 90);
            list.Columns.Add("Status", 110);
            list.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Bg)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), Theme.UI(8f, FontStyle.Bold), new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += DrawSubItem;
            list.Resize += (s, e) => FitColumns();
            list.DoubleClick += (s, e) => ApplySelected();
            list.SelectedIndexChanged += (s, e) => UpdateButtons();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ApplySelected(); if (e.KeyCode == Keys.Delete) DeleteSelected(); if (e.KeyCode == Keys.F2) RenameSelected(); };
            // Taller rows
            list.SmallImageList = new ImageList { ImageSize = new Size(1, 32) };
            root.Controls.Add(list);

            var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            applyBtn = Theme.MakeButton("Switch to this pack", true); applyBtn.Click += (s, e) => ApplySelected();
            origBtn = Theme.MakeButton("Restore original"); origBtn.Click += (s, e) => { var p = Core.FindPack("original"); if (p != null) DoApply(p); };
            renameBtn = Theme.MakeButton("Rename"); renameBtn.Click += (s, e) => RenameSelected();
            deleteBtn = Theme.MakeButton("Delete"); deleteBtn.Click += (s, e) => DeleteSelected();
            shortcutBtn = Theme.MakeButton("Desktop shortcut"); shortcutBtn.Click += (s, e) => ShortcutSelected();
            var addBtn = Theme.MakeButton("Add pack…"); addBtn.Click += (s, e) => BrowseImport();
            var folderBtn = Theme.MakeButton("Open packs folder"); folderBtn.Click += (s, e) => Process.Start("explorer.exe", Quote(Core.PacksDir));
            backupBtn = Theme.MakeButton("Backup current…"); backupBtn.Click += (s, e) => BackupCurrent();
            btns.Controls.AddRange(new Control[] { applyBtn, origBtn, backupBtn, addBtn, renameBtn, deleteBtn, shortcutBtn, folderBtn });
            root.Controls.Add(btns);

            var gameRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 4, 0, 0) };
            gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gameRow.Controls.Add(new Label { Text = "Game folder", AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
            gameLabel = new Label { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 9f) };
            gameRow.Controls.Add(gameLabel, 1, 0);
            var change = Theme.MakeButton("Change…"); change.Margin = new Padding(8, 0, 0, 0); change.Click += (s, e) => PickGameDir();
            gameRow.Controls.Add(change, 2, 0);
            root.Controls.Add(gameRow);

            status = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0), MaximumSize = new Size(680, 0) };
            root.Controls.Add(status);

            WireDrop(this); WireDrop(drop); WireDrop(list);

            Shown += (s, e) => { RefreshList(); FirstRun(); };
            ResumeLayout(false);
            PerformLayout();
        }

        static string Quote(string s) { return "\"" + s + "\""; }

        void PaintDrop(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, drop.Width - 3, drop.Height - 3);
            bool over = drop.Tag != null;
            using (var bg = new SolidBrush(over ? Color.FromArgb(48, 42, 24) : Theme.Panel)) g.FillRectangle(bg, r);
            using (var pen = new Pen(over ? Theme.Accent : Theme.Line, 2) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r);
            var big = new Rectangle(r.X, r.Y + 14, r.Width, 28);
            TextRenderer.DrawText(g, over ? "Release to add and switch" : "Drop a soundpack here to switch to it", Theme.UI(12f, FontStyle.Bold), big, over ? Theme.Accent : Theme.Text, TextFormatFlags.HorizontalCenter);
            var small = new Rectangle(r.X, r.Y + 44, r.Width, 36);
            TextRenderer.DrawText(g, "RESIDENT.rpf / WEAPONS_PLAYER.rpf, a folder containing them, or a .zip / .oiv  ·  click to browse", Theme.UI(9f), small, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }

        void DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var pack = (Pack)e.Item.Tag;
            bool active = e.Item.SubItems[3].Text == "Active";
            bool sel = e.Item.Selected;
            using (var b = new SolidBrush(sel ? Color.FromArgb(56, 50, 30) : (e.ItemIndex % 2 == 0 ? Theme.Panel : Color.FromArgb(34, 37, 45))))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (e.ColumnIndex == 0 && active)
                using (var b = new SolidBrush(Theme.Good)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 4, 3, e.Bounds.Height - 8);
            Color fg = e.ColumnIndex == 3 ? (active ? Theme.Good : Theme.Muted) : (e.ColumnIndex == 0 ? Theme.Text : Theme.Muted);
            var font = e.ColumnIndex == 0 ? Theme.UI(10f, pack.IsOriginal ? FontStyle.Italic : FontStyle.Bold) : (e.ColumnIndex == 3 && active ? Theme.UI(9.5f, FontStyle.Bold) : Theme.UI(9.5f));
            var rect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font, rect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
        }

        void FitColumns()
        {
            int w = list.ClientSize.Width - list.Columns[1].Width - list.Columns[2].Width - list.Columns[3].Width;
            if (w > 120) list.Columns[0].Width = w;
        }

        void RefreshList(string selectName = null)
        {
            var packs = Core.ListPacks();
            Pack active = null;
            try { active = Core.ActivePack(packs); } catch { }
            string keep = selectName ?? (list.SelectedItems.Count > 0 ? ((Pack)list.SelectedItems[0].Tag).Name : null);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var p in packs)
            {
                string files = string.Join(" + ", Core.Files.Where(p.Has).Select(f => f.StartsWith("RES") ? "Resident" : "Weapons"));
                var it = new ListViewItem(new[] { p.Name, files, Core.Size(p.Size), active == p ? "Active" : "" }) { Tag = p };
                list.Items.Add(it);
                if (keep != null && p.Name == keep) it.Selected = true;
            }
            if (list.SelectedItems.Count == 0 && list.Items.Count > 0)
            {
                var act = list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag == active);
                (act ?? list.Items[0]).Selected = true;
            }
            list.EndUpdate();
            FitColumns();
            gameLabel.Text = Core.IsValidGameDir(Core.GameDir) ? Core.GameDir : "Not found. Click Change… and pick your GTA V folder.";
            gameLabel.ForeColor = Core.IsValidGameDir(Core.GameDir) ? Theme.Text : Theme.Bad;
            if (packs.Count == 0) SetStatus("No packs yet. Drop a soundpack above. Your current game files are backed up automatically before the first switch.", false);
            UpdateButtons();
        }

        Pack Selected { get { return list.SelectedItems.Count > 0 ? (Pack)list.SelectedItems[0].Tag : null; } }

        void UpdateButtons()
        {
            var p = Selected;
            applyBtn.Enabled = p != null && !busy;
            renameBtn.Enabled = deleteBtn.Enabled = p != null && !p.IsOriginal && !busy;
            shortcutBtn.Enabled = p != null;
            origBtn.Enabled = Core.FindPack("original") != null && !busy;
            backupBtn.Enabled = Core.IsValidGameDir(Core.GameDir) && !busy;
        }

        void SetStatus(string text, bool error)
        {
            status.Text = text; status.ForeColor = error ? Theme.Bad : Theme.Muted;
        }

        void FirstRun()
        {
            if (!Core.IsValidGameDir(Core.GameDir))
            {
                SetStatus("Couldn't find GTA V automatically. Click Change… and pick the game folder (the one with GTA5.exe).", true);
                return;
            }
            CheckShortcut();
        }

        async void CheckShortcut()
        {
            if (Core.ShortcutPromptOff) return;
            Core.ShortcutState state;
            try { state = await Task.Run(() => Core.MainShortcutState()); } catch { return; }
            if (state == Core.ShortcutState.Ok) return;
            bool stale = state == Core.ShortcutState.Stale;
            string msg = stale
                ? "Your \"Soundpack Switcher\" desktop shortcut points to an old location of the app.\n\nUpdate it so dropping soundpacks onto it works again?"
                : "There's no Soundpack Switcher shortcut on your desktop.\n\nAdd one? Drag a soundpack (the .rpf files, a folder or a .zip) onto it and it switches instantly, without opening the app.";
            bool dontAsk;
            if (!AskDialog.Show(this, stale ? "Update desktop shortcut" : "Add desktop shortcut", msg, stale ? "Update shortcut" : "Add shortcut", out dontAsk))
            {
                if (dontAsk) { Core.ShortcutPromptOff = true; Core.SaveSettings(); SetStatus("Okay, won't ask again. Select any pack and use Desktop shortcut… if you change your mind.", false); }
                return;
            }
            try { Core.CreateShortcut("Soundpack Switcher", ""); SetStatus("Desktop shortcut " + (stale ? "updated" : "created") + ". Drop soundpacks onto it to switch.", false); }
            catch (Exception ex) { SetStatus("Couldn't create the shortcut: " + ex.Message, true); }
        }

        void WireDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; drop.Tag = "over"; drop.Invalidate(); } };
            c.DragLeave += (s, e) => { drop.Tag = null; drop.Invalidate(); };
            c.DragDrop += (s, e) =>
            {
                drop.Tag = null; drop.Invalidate();
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) BeginInvoke(new Action(() => ImportAndApply(files)));
            };
        }

        void BrowseImport()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose RESIDENT.rpf and/or WEAPONS_PLAYER.rpf, or a .zip",
                Filter = "Soundpack files (*.rpf;*.zip;*.oiv)|*.rpf;*.zip;*.oiv|All files (*.*)|*.*",
                Multiselect = true,
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) ImportAndApply(dlg.FileNames);
            }
        }

        async void ImportAndApply(string[] paths)
        {
            if (busy) return;
            SetBusy(true, "Adding soundpack…");
            Pack pack = null;
            try
            {
                pack = await Task.Run(() => Core.Import(paths));
                RefreshList(pack.Name);
                SetStatus("Switching to " + pack.Name + "…", false);
                await Task.Run(() => Core.Apply(pack));
                SetStatus("Switched to " + pack.Name + ". Start the game to hear it.", false);
            }
            catch (Exception ex)
            {
                SetStatus((pack != null ? "Added " + pack.Name + " to your library, but couldn't switch: " : "") + ex.Message, true);
            }
            finally { SetBusy(false, null); RefreshList(pack != null ? pack.Name : null); }
        }

        async void BackupCurrent()
        {
            if (busy) return;
            if (!Core.IsValidGameDir(Core.GameDir)) { SetStatus("Set the game folder first (Change…).", true); return; }
            Pack active = null;
            try { active = Core.ActivePack(Core.ListPacks()); } catch { }
            string suggested = active != null && !active.IsOriginal ? active.Name : "My soundpack " + DateTime.Now.ToString("yyyy-MM-dd");
            string dest;
            using (var dlg = new SaveFileDialog
            {
                Title = "Back up the current soundpack: choose a name and where to save it",
                Filter = "Zip archive (*.zip)|*.zip",
                DefaultExt = "zip", AddExtension = true, OverwritePrompt = true,
                FileName = Core.Sanitize(suggested) + ".zip",
                InitialDirectory = Directory.Exists(Core.BackupDir ?? "") ? Core.BackupDir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                dest = dlg.FileName;
            }
            Core.BackupDir = Path.GetDirectoryName(dest); Core.SaveSettings();
            SetBusy(true, "Backing up current soundpack…");
            try
            {
                await Task.Run(() => Core.BackupCurrent(dest, msg => BeginInvoke(new Action(() => SetStatus(msg, false)))));
                SetStatus("Backed up to " + dest + " (" + Core.Size(new FileInfo(dest).Length) + "). Drop that zip back in here any time to switch to it.", false);
                if (MessageBox.Show(this, "Saved " + Path.GetFileName(dest) + ".\n\nOpen the folder?", "Backup complete",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Process.Start("explorer.exe", "/select," + Quote(dest));
            }
            catch (Exception ex) { SetStatus("Backup failed: " + ex.Message, true); }
            finally { SetBusy(false, null); }
        }

        void ApplySelected() { var p = Selected; if (p != null) DoApply(p); }

        async void DoApply(Pack p)
        {
            if (busy) return;
            SetBusy(true, "Switching to " + p.Name + "…");
            try
            {
                await Task.Run(() => Core.Apply(p));
                SetStatus(p.IsOriginal ? "Original sounds restored." : "Switched to " + p.Name + ". Start the game to hear it.", false);
            }
            catch (Exception ex) { SetStatus(ex.Message, true); }
            finally { SetBusy(false, null); RefreshList(p.Name); }
        }

        void SetBusy(bool b, string msg)
        {
            busy = b; UseWaitCursor = b;
            if (msg != null) SetStatus(msg, false);
            UpdateButtons();
        }

        void RenameSelected()
        {
            var p = Selected; if (p == null || p.IsOriginal) return;
            string name = Prompt.Show(this, "Rename soundpack", "New name:", p.Name);
            if (name == null) return;
            name = Core.Sanitize(name);
            if (name == p.Name) return;
            string dst = Path.Combine(Core.PacksDir, name);
            if (Directory.Exists(dst)) { SetStatus("A pack called " + name + " already exists.", true); return; }
            try { Directory.Move(p.Dir, dst); RefreshList(name); SetStatus("Renamed to " + name + ". Desktop shortcuts for the old name need to be recreated.", false); }
            catch (Exception ex) { SetStatus("Couldn't rename: " + ex.Message, true); }
        }

        void DeleteSelected()
        {
            var p = Selected; if (p == null || p.IsOriginal) return;
            if (MessageBox.Show(this, "Delete \"" + p.Name + "\" from your library?\n\nThis doesn't change the game files. If it's the active pack, it stays in the game until you switch.",
                "Delete soundpack", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { Directory.Delete(p.Dir, true); RefreshList(); SetStatus("Deleted " + p.Name + ".", false); }
            catch (Exception ex) { SetStatus("Couldn't delete: " + ex.Message, true); }
        }

        void ShortcutSelected()
        {
            var p = Selected; if (p == null) return;
            try
            {
                string title = p.IsOriginal ? "GTA sounds - Original" : "GTA sounds - " + p.Name;
                Core.CreateShortcut(title, "--apply " + Quote(p.IsOriginal ? "original" : p.Name));
                SetStatus("Created \"" + title + "\" on your desktop. Double-click it to switch to this pack.", false);
            }
            catch (Exception ex) { SetStatus("Couldn't create the shortcut: " + ex.Message, true); }
        }

        void PickGameDir()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pick your GTA V folder (the one with GTA5.exe) or its x64\\audio\\sfx folder.", ShowNewFolderButton = false })
            {
                if (Core.IsValidGameDir(Core.GameDir)) dlg.SelectedPath = Core.GameDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var d = Core.NormalizeGameDir(dlg.SelectedPath);
                if (d == null) { SetStatus("That folder doesn't contain x64\\audio\\sfx\\RESIDENT.rpf. Pick the GTA V install folder.", true); return; }
                Core.GameDir = d; Core.SaveSettings(); RefreshList();
                SetStatus("Game folder set.", false);
            }
        }
    }

    static class AskDialog
    {
        /// Yes/No dialog with a "Don't ask again" checkbox (only meaningful when the answer is No).
        public static bool Show(IWin32Window owner, string title, string message, string yesText, out bool dontAsk)
        {
            using (var f = new Form
            {
                AutoScaleDimensions = new SizeF(96F, 96F),
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(420, 170), BackColor = Theme.Bg, ForeColor = Theme.Text,
                Font = Theme.UI(9.5f), AutoScaleMode = AutoScaleMode.Dpi, ShowInTaskbar = false,
            })
            {
                var l = new Label { Text = message, Left = 16, Top = 14, Width = 388, Height = 82 };
                var cb = new CheckBox { Text = "Don't ask again", Left = 16, Top = 114, AutoSize = true, ForeColor = Theme.Muted };
                var yes = Theme.MakeButton(yesText, true); yes.DialogResult = DialogResult.Yes;
                var no = Theme.MakeButton("Not now"); no.DialogResult = DialogResult.No;
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Left = 160, Top = 104, Width = 252, Height = 40, Margin = new Padding(0) };
                row.Controls.Add(no); row.Controls.Add(yes);
                f.Controls.AddRange(new Control[] { l, row, cb });
                f.AcceptButton = yes; f.CancelButton = no;
                var r = f.ShowDialog(owner);
                dontAsk = cb.Checked;
                return r == DialogResult.Yes;
            }
        }
    }

    static class Prompt
    {
        public static string Show(IWin32Window owner, string title, string label, string value)
        {
            using (var f = new Form
            {
                AutoScaleDimensions = new SizeF(96F, 96F),
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 120), BackColor = Theme.Bg, ForeColor = Theme.Text,
                Font = Theme.UI(9.5f), AutoScaleMode = AutoScaleMode.Dpi, ShowInTaskbar = false,
            })
            {
                var l = new Label { Text = label, Left = 14, Top = 12, AutoSize = true };
                var t = new TextBox { Left = 14, Top = 36, Width = 352, Text = value, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
                var ok = Theme.MakeButton("Save", true); ok.Left = 230; ok.Top = 76; ok.DialogResult = DialogResult.OK;
                var cancel = Theme.MakeButton("Cancel"); cancel.Left = 296; cancel.Top = 76; cancel.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK && t.Text.Trim().Length > 0 ? t.Text.Trim() : null;
            }
        }
    }

    /// Small auto-closing notice for shortcut / drag-onto-shortcut runs.
    class Toast : Form
    {
        public Toast(string title, string body, bool error)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Panel; ForeColor = Theme.Text; Font = Theme.UI(9.5f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(360, 86);
            Load += (s, e) => { var wa = Screen.PrimaryScreen.WorkingArea; Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16); };
            var bar = new Panel { Dock = DockStyle.Left, Width = 5, BackColor = error ? Theme.Bad : Theme.Good };
            var t = new Label { Text = title, Font = Theme.UI(11f, FontStyle.Bold), Left = 18, Top = 12, AutoSize = true, ForeColor = error ? Theme.Bad : Theme.Text };
            var b = new Label { Text = body, Left = 18, Top = 38, Width = 330, Height = 44, ForeColor = Theme.Muted };
            Controls.AddRange(new Control[] { bar, t, b });
            var timer = new Timer { Interval = error ? 7000 : 3500 };
            timer.Tick += (s, e) => Close();
            timer.Start();
            Click += (s, e) => Close(); t.Click += (s, e) => Close(); b.Click += (s, e) => Close();
        }
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // Elevated helper: --copy <errfile> <src> <dst> [<src> <dst>]
            if (args.Length >= 1 && args[0] == "--copy")
            {
                string errFile = args.Length > 1 ? args[1] : null;
                try { Core.CopyPairs(args.Skip(2).ToList()); return 0; }
                catch (Exception ex)
                {
                    try { if (errFile != null) File.WriteAllText(errFile, ex is IOException ? "Couldn't replace the game files. Close the game and try again. (" + ex.Message + ")" : ex.Message); } catch { }
                    return 1;
                }
            }

            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Core.Init(); }
            catch (Exception ex) { MessageBox.Show("Soundpack Switcher couldn't start: " + ex.Message, "Soundpack Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }

            if (args.Length == 0) { Application.Run(new MainForm()); return 0; }

            // Shortcut modes: --apply "<pack name>"  or  files/folders dropped onto the exe or its shortcut.
            string title, body; bool error = false;
            try
            {
                if (!Core.IsValidGameDir(Core.GameDir))
                    throw new Exception("GTA V folder not set. Open Soundpack Switcher normally and click Change….");
                Pack pack;
                if (args[0] == "--apply")
                {
                    string name = args.Length > 1 ? args[1] : "";
                    pack = Core.FindPack(name);
                    if (pack == null) throw new Exception("No soundpack called \"" + name + "\" in your library.");
                }
                else pack = Core.Import(args);
                Core.Apply(pack);
                title = pack.IsOriginal ? "Original sounds restored" : "Switched to " + pack.Name;
                body = pack.IsOriginal ? "GTA V is back to its own sounds." : "Saved in your library. Start the game to hear it.";
            }
            catch (Exception ex)
            {
                title = "Couldn't switch soundpack"; body = ex.Message; error = true;
            }
            Application.Run(new Toast(title, body, error));
            return error ? 1 : 0;
        }
    }
}

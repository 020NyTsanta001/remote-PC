// PCRemote - agent Windows discret (sans fenetre) - C# 5, compile avec csc.exe fourni par Windows
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PCRemote
{
    internal static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }
        [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion u; }
    }

    internal static class InputSim
    {
        const uint INPUT_MOUSE = 0;
        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1;
        const uint KEYEVENTF_KEYUP = 0x2;
        const uint KEYEVENTF_UNICODE = 0x4;

        static readonly int Size = Marshal.SizeOf(typeof(Native.INPUT));
        static readonly Dictionary<string, ushort> Keys = BuildKeys();
        static readonly HashSet<ushort> Ext = new HashSet<ushort>(new ushort[] { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C });

        static Dictionary<string, ushort> BuildKeys()
        {
            Dictionary<string, ushort> d = new Dictionary<string, ushort>();
            d["ENTER"] = 0x0D; d["BACKSPACE"] = 0x08; d["TAB"] = 0x09; d["ESC"] = 0x1B; d["SPACE"] = 0x20;
            d["UP"] = 0x26; d["DOWN"] = 0x28; d["LEFT"] = 0x25; d["RIGHT"] = 0x27;
            d["HOME"] = 0x24; d["END"] = 0x23; d["PGUP"] = 0x21; d["PGDN"] = 0x22;
            d["DEL"] = 0x2E; d["INS"] = 0x2D; d["WIN"] = 0x5B; d["CTRL"] = 0x11; d["ALT"] = 0x12; d["SHIFT"] = 0x10;
            d["PRTSC"] = 0x2C;
            d["VOLUME_MUTE"] = 0xAD; d["VOLUME_DOWN"] = 0xAE; d["VOLUME_UP"] = 0xAF;
            d["MEDIA_NEXT"] = 0xB0; d["MEDIA_PREV"] = 0xB1; d["MEDIA_STOP"] = 0xB2; d["MEDIA_PLAY_PAUSE"] = 0xB3;
            for (int i = 1; i <= 12; i++) d["F" + i] = (ushort)(0x70 + i - 1);
            return d;
        }

        static void Send(Native.INPUT[] arr)
        {
            if (arr.Length > 0) Native.SendInput((uint)arr.Length, arr, Size);
        }

        static Native.INPUT Kb(ushort vk, ushort scan, uint flags)
        {
            Native.INPUT i = new Native.INPUT();
            i.type = INPUT_KEYBOARD;
            i.u.ki.wVk = vk;
            i.u.ki.wScan = scan;
            i.u.ki.dwFlags = flags;
            return i;
        }

        static Native.INPUT Ms(uint flags, int dx, int dy, uint data)
        {
            Native.INPUT i = new Native.INPUT();
            i.type = INPUT_MOUSE;
            i.u.mi.dx = dx;
            i.u.mi.dy = dy;
            i.u.mi.mouseData = data;
            i.u.mi.dwFlags = flags;
            return i;
        }

        static Native.INPUT KeyEv(ushort vk, bool up)
        {
            uint f = up ? KEYEVENTF_KEYUP : 0;
            if (Ext.Contains(vk)) f |= KEYEVENTF_EXTENDEDKEY;
            return Kb(vk, 0, f);
        }

        public static void MoveRel(int dx, int dy)
        {
            Send(new Native.INPUT[] { Ms(0x0001, dx, dy, 0) });
        }

        public static void Button(char b, bool down)
        {
            uint f;
            switch (char.ToUpperInvariant(b))
            {
                case 'L': f = down ? 0x0002u : 0x0004u; break;
                case 'R': f = down ? 0x0008u : 0x0010u; break;
                case 'M': f = down ? 0x0020u : 0x0040u; break;
                default: return;
            }
            Send(new Native.INPUT[] { Ms(f, 0, 0, 0) });
        }

        public static void Wheel(int delta, bool horizontal)
        {
            uint f = horizontal ? 0x1000u : 0x0800u;
            Send(new Native.INPUT[] { Ms(f, 0, 0, unchecked((uint)delta)) });
        }

        public static void TypeText(string s)
        {
            List<Native.INPUT> list = new List<Native.INPUT>();
            foreach (char c in s)
            {
                if (c == '\r') continue;
                if (c == '\n') { list.Add(KeyEv(0x0D, false)); list.Add(KeyEv(0x0D, true)); }
                else if (c == '\t') { list.Add(KeyEv(0x09, false)); list.Add(KeyEv(0x09, true)); }
                else
                {
                    list.Add(Kb(0, c, KEYEVENTF_UNICODE));
                    list.Add(Kb(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
                }
                if (list.Count >= 200) { Send(list.ToArray()); list.Clear(); }
            }
            Send(list.ToArray());
        }

        static bool ResolveKey(string name, out ushort vk)
        {
            if (Keys.TryGetValue(name, out vk)) return true;
            if (name.Length == 1)
            {
                char c = name[0];
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { vk = (ushort)c; return true; }
            }
            vk = 0;
            return false;
        }

        // "CTRL+C", "ALT+TAB", "ENTER", "VOLUME_UP" ...
        public static void PressCombo(string spec, int repeat)
        {
            string[] parts = spec.ToUpperInvariant().Split('+');
            List<ushort> vks = new List<ushort>();
            foreach (string p in parts)
            {
                ushort vk;
                if (!ResolveKey(p.Trim(), out vk)) return;
                vks.Add(vk);
            }
            if (vks.Count == 0) return;
            for (int r = 0; r < repeat; r++)
            {
                List<Native.INPUT> l = new List<Native.INPUT>();
                for (int i = 0; i < vks.Count; i++) l.Add(KeyEv(vks[i], false));
                for (int i = vks.Count - 1; i >= 0; i--) l.Add(KeyEv(vks[i], true));
                Send(l.ToArray());
            }
        }
    }

    internal static class Program
    {
        public const int Port = 48555;
        public static string DataDir;
        static string CfgPath;
        public static byte[] Salt;
        static byte[] Hash;
        public static byte[] Secret;
        static int failCount;
        static DateTime lockUntil = DateTime.MinValue;
        static readonly object failLock = new object();
        static readonly object logLock = new object();
        public static readonly RNGCryptoServiceProvider Rng = new RNGCryptoServiceProvider();

        public static void Log(string m)
        {
            try
            {
                lock (logLock)
                {
                    string p = Path.Combine(DataDir, "log.txt");
                    if (File.Exists(p) && new FileInfo(p).Length > 200000) File.Delete(p);
                    File.AppendAllText(p, DateTime.Now.ToString("s") + " " + m + "\r\n");
                }
            }
            catch (Exception) { }
        }

        static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch (Exception) { }
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCRemote");
            Directory.CreateDirectory(DataDir);
            CfgPath = Path.Combine(DataDir, "config.dat");

            if (args.Length > 0 && args[0] == "--setpassword") return SetPassword();

            bool created;
            Mutex m = new Mutex(true, "Local\\PCRemoteAgent", out created);
            if (!created) return 0;
            if (!LoadConfig()) { Log("config.dat manquant : relance install.bat"); return 2; }
            Serve();
            GC.KeepAlive(m);
            return 0;
        }

        static int SetPassword()
        {
            string pw = Environment.GetEnvironmentVariable("PCR_PW");
            if (pw == null || pw.Length < 4) return 3;
            byte[] salt = new byte[16]; Rng.GetBytes(salt);
            byte[] secret = new byte[32]; Rng.GetBytes(secret);
            byte[] hash = new Rfc2898DeriveBytes(pw, salt, 20000).GetBytes(32);
            File.WriteAllLines(CfgPath, new string[] {
                Convert.ToBase64String(salt), Convert.ToBase64String(hash), Convert.ToBase64String(secret) });
            return 0;
        }

        static bool LoadConfig()
        {
            try
            {
                if (!File.Exists(CfgPath)) return false;
                string[] l = File.ReadAllLines(CfgPath);
                Salt = Convert.FromBase64String(l[0]);
                Hash = Convert.FromBase64String(l[1]);
                Secret = Convert.FromBase64String(l[2]);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool FixedEq(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int r = 0;
            for (int i = 0; i < a.Length; i++) r |= a[i] ^ b[i];
            return r == 0;
        }

        public static bool CheckPassword(string pw, out string err)
        {
            lock (failLock)
            {
                if (DateTime.UtcNow < lockUntil) { err = "locked"; return false; }
                byte[] h = new Rfc2898DeriveBytes(pw, Salt, 20000).GetBytes(32);
                if (FixedEq(h, Hash)) { failCount = 0; err = null; return true; }
                failCount++;
                if (failCount >= 5) { lockUntil = DateTime.UtcNow.AddMinutes(1); failCount = 0; }
                err = "bad password";
                return false;
            }
        }

        static void Serve()
        {
            while (true)
            {
                TcpListener l = null;
                try
                {
                    l = new TcpListener(IPAddress.Any, Port);
                    l.Start();
                    while (true)
                    {
                        TcpClient c = l.AcceptTcpClient();
                        Thread t = new Thread(new ParameterizedThreadStart(ClientMain));
                        t.IsBackground = true;
                        t.Start(c);
                    }
                }
                catch (Exception e)
                {
                    Log("listener: " + e.Message);
                    try { if (l != null) l.Stop(); } catch (Exception) { }
                    Thread.Sleep(3000);
                }
            }
        }

        static void ClientMain(object o)
        {
            Session s = new Session((TcpClient)o);
            s.Run();
        }
    }

    internal class Session
    {
        readonly TcpClient tc;
        NetworkStream ns;
        readonly object wlock = new object();
        volatile bool alive = true;
        volatile bool screenOn = false;
        double accX = 0, accY = 0; // reste de pixels du déplacement relatif

        public Session(TcpClient c) { tc = c; }

        static string Hex(byte[] b)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        byte[] ReadExact(int n)
        {
            byte[] buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = ns.Read(buf, off, n - off);
                if (r <= 0) throw new IOException("closed");
                off += r;
            }
            return buf;
        }

        string ReadText()
        {
            while (true)
            {
                byte[] h = ReadExact(4);
                int n = (h[0] << 24) | (h[1] << 16) | (h[2] << 8) | h[3];
                if (n < 1 || n > 262144) throw new IOException("bad frame");
                byte[] b = ReadExact(n);
                if (b[0] == 0) return Encoding.UTF8.GetString(b, 1, n - 1);
            }
        }

        void SendFrame(byte type, byte[] payload)
        {
            int n = payload.Length + 1;
            byte[] buf = new byte[4 + n];
            buf[0] = (byte)(n >> 24); buf[1] = (byte)(n >> 16); buf[2] = (byte)(n >> 8); buf[3] = (byte)n;
            buf[4] = type;
            Buffer.BlockCopy(payload, 0, buf, 5, payload.Length);
            lock (wlock) { ns.Write(buf, 0, buf.Length); }
        }

        void SendText(string s) { SendFrame(0, Encoding.UTF8.GetBytes(s)); }

        public void Run()
        {
            try
            {
                tc.NoDelay = true;
                ns = tc.GetStream();
                ns.ReadTimeout = 60000;
                ns.WriteTimeout = 15000;

                byte[] nonce = new byte[16];
                Program.Rng.GetBytes(nonce);
                SendText("HELLO|" + Environment.MachineName + "|" + Hex(nonce) + "|2");

                string first = ReadText();
                if (first.StartsWith("AUTH|"))
                {
                    byte[] expect;
                    using (HMACSHA256 h = new HMACSHA256(Program.Secret)) { expect = h.ComputeHash(nonce); }
                    byte[] got = Encoding.ASCII.GetBytes(first.Substring(5).Trim().ToLowerInvariant());
                    if (!Program.FixedEq(Encoding.ASCII.GetBytes(Hex(expect)), got)) { SendText("DENY|auth"); return; }
                }
                else if (first.StartsWith("PAIR|"))
                {
                    string err;
                    if (!Program.CheckPassword(first.Substring(5), out err)) { SendText("DENY|" + err); return; }
                    SendText("PAIRED|" + Convert.ToBase64String(Program.Secret));
                }
                else return;

                SendText("OK|" + Environment.MachineName);
                ns.ReadTimeout = 15000;

                Thread st = new Thread(new ThreadStart(ScreenLoop));
                st.IsBackground = true;
                st.Start();

                while (alive)
                {
                    string line = ReadText();
                    Handle(line);
                }
            }
            catch (Exception) { }
            finally
            {
                alive = false;
                try { tc.Close(); } catch (Exception) { }
            }
        }

        static bool Two(string s, out int a, out int b)
        {
            a = 0; b = 0;
            string[] p = s.Split(' ');
            return p.Length >= 2 && int.TryParse(p[0], out a) && int.TryParse(p[1], out b);
        }

        void Handle(string line)
        {
            int sp = line.IndexOf(' ');
            string cmd = sp < 0 ? line : line.Substring(0, sp);
            string arg = sp < 0 ? "" : line.Substring(sp + 1);
            switch (cmd)
            {
                case "PING":
                    SendText("PONG");
                    break;
                case "M":
                    {
                        int dx, dy;
                        if (Two(arg, out dx, out dy)) InputSim.MoveRel(dx, dy);
                        break;
                    }
                case "A":
                    {
                        int fx, fy;
                        if (Two(arg, out fx, out fy))
                        {
                            int w = Native.GetSystemMetrics(0), h = Native.GetSystemMetrics(1);
                            Native.SetCursorPos(fx * w / 10000, fy * h / 10000);
                        }
                        break;
                    }
                case "N":
                    {
                        // déplacement relatif en 1/10000 de l'écran (pixels exacts, sans accélération Windows)
                        int nx, ny;
                        if (Two(arg, out nx, out ny))
                        {
                            Native.POINT cp;
                            if (Native.GetCursorPos(out cp))
                            {
                                int w = Native.GetSystemMetrics(0), h = Native.GetSystemMetrics(1);
                                accX += nx * (double)w / 10000.0;
                                accY += ny * (double)h / 10000.0;
                                int ix = (int)accX, iy = (int)accY;
                                accX -= ix; accY -= iy;
                                if (ix != 0 || iy != 0)
                                {
                                    int x = Math.Max(0, Math.Min(w - 1, cp.X + ix));
                                    int y = Math.Max(0, Math.Min(h - 1, cp.Y + iy));
                                    Native.SetCursorPos(x, y);
                                }
                            }
                        }
                        break;
                    }
                case "D":
                    if (arg.Length > 0) InputSim.Button(arg[0], true);
                    break;
                case "U":
                    if (arg.Length > 0) InputSim.Button(arg[0], false);
                    break;
                case "C":
                    if (arg.Length > 0) { InputSim.Button(arg[0], true); InputSim.Button(arg[0], false); }
                    break;
                case "W":
                    {
                        int v;
                        if (int.TryParse(arg, out v)) InputSim.Wheel(v, false);
                        break;
                    }
                case "H":
                    {
                        int v;
                        if (int.TryParse(arg, out v)) InputSim.Wheel(v, true);
                        break;
                    }
                case "T":
                    InputSim.TypeText(arg);
                    break;
                case "K":
                    {
                        int rep = 1;
                        int star = arg.IndexOf('*');
                        if (star > 0)
                        {
                            int.TryParse(arg.Substring(star + 1), out rep);
                            arg = arg.Substring(0, star);
                            if (rep < 1) rep = 1;
                            if (rep > 50) rep = 50;
                        }
                        InputSim.PressCombo(arg, rep);
                        break;
                    }
                case "SCREEN":
                    screenOn = arg.StartsWith("1");
                    break;
                case "SYS":
                    Sys(arg);
                    break;
            }
        }

        static void RunProc(string file, string args)
        {
            ProcessStartInfo p = new ProcessStartInfo(file, args);
            p.CreateNoWindow = true;
            p.UseShellExecute = false;
            Process.Start(p);
        }

        static void Sys(string a)
        {
            switch (a)
            {
                case "LOCK": RunProc("rundll32.exe", "user32.dll,LockWorkStation"); break;
                case "SLEEP": RunProc("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"); break;
                case "SHUTDOWN": RunProc("shutdown.exe", "/s /t 5"); break;
                case "RESTART": RunProc("shutdown.exe", "/r /t 5"); break;
                case "CANCEL": RunProc("shutdown.exe", "/a"); break;
            }
        }

        static void DrawCursor(Graphics g, float scale)
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            float x = p.X * scale, y = p.Y * scale;
            PointF[] pts = new PointF[] {
                new PointF(x, y), new PointF(x, y + 17), new PointF(x + 4.5f, y + 13),
                new PointF(x + 8, y + 20), new PointF(x + 10.5f, y + 19), new PointF(x + 7, y + 12),
                new PointF(x + 12.5f, y + 12) };
            g.FillPolygon(Brushes.White, pts);
            g.DrawPolygon(Pens.Black, pts);
        }

        // Envoie des images JPEG reduites uniquement quand le telephone affiche l'onglet "Ecran"
        void ScreenLoop()
        {
            ImageCodecInfo codec = null;
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                if (c.MimeType == "image/jpeg") codec = c;
            EncoderParameters ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 50L);
            Bitmap full = null, small = null;
            int cw = 0, ch = 0;
            try
            {
                while (alive)
                {
                    if (!screenOn)
                    {
                        if (full != null) { full.Dispose(); small.Dispose(); full = null; small = null; }
                        Thread.Sleep(120);
                        continue;
                    }
                    DateTime t0 = DateTime.UtcNow;
                    try
                    {
                        int w = Native.GetSystemMetrics(0), h = Native.GetSystemMetrics(1);
                        if (full == null || w != cw || h != ch)
                        {
                            if (full != null) { full.Dispose(); small.Dispose(); }
                            cw = w; ch = h;
                            full = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                            int sw = Math.Min(w, 1000);
                            int sh = (int)((long)h * sw / w);
                            small = new Bitmap(sw, sh, PixelFormat.Format24bppRgb);
                        }
                        using (Graphics g = Graphics.FromImage(full))
                        {
                            g.CopyFromScreen(0, 0, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                        }
                        using (Graphics g2 = Graphics.FromImage(small))
                        {
                            g2.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g2.DrawImage(full, 0, 0, small.Width, small.Height);
                            DrawCursor(g2, (float)small.Width / w);
                        }
                        using (MemoryStream ms = new MemoryStream())
                        {
                            small.Save(ms, codec, ep);
                            SendFrame(1, ms.ToArray());
                        }
                    }
                    catch (IOException) { alive = false; }
                    catch (ObjectDisposedException) { alive = false; }
                    catch (Exception) { Thread.Sleep(500); }
                    int el = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                    if (el < 110) Thread.Sleep(110 - el);
                }
            }
            finally
            {
                if (full != null) full.Dispose();
                if (small != null) small.Dispose();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace Telecommande
{
    /// <summary>Minimal protobuf, as the Android TV Remote v2 protocol needs (length-prefixed messages).</summary>
    class PbWriter
    {
        readonly MemoryStream ms = new MemoryStream();

        static void Varint(Stream s, ulong v)
        {
            while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
            s.WriteByte((byte)v);
        }

        public PbWriter V(int field, long value) { Varint(ms, (ulong)field << 3); Varint(ms, (ulong)value); return this; }

        public PbWriter Bytes(int field, byte[] b)
        {
            Varint(ms, ((ulong)field << 3) | 2);
            Varint(ms, (ulong)b.Length);
            ms.Write(b, 0, b.Length);
            return this;
        }

        public PbWriter Str(int field, string s) { return Bytes(field, Encoding.UTF8.GetBytes(s)); }
        public PbWriter Msg(int field, PbWriter inner) { return Bytes(field, inner.ToArray()); }
        public byte[] ToArray() { return ms.ToArray(); }

        public static void Send(Stream s, PbWriter m)
        {
            byte[] body = m.ToArray();
            var frame = new MemoryStream();
            Varint(frame, (ulong)body.Length);
            frame.Write(body, 0, body.Length);
            byte[] all = frame.ToArray();
            s.Write(all, 0, all.Length);
            s.Flush();
        }
    }

    class PbMsg
    {
        readonly List<Tuple<int, long, byte[]>> fields = new List<Tuple<int, long, byte[]>>();

        public PbMsg(byte[] b)
        {
            int pos = 0;
            while (pos < b.Length)
            {
                ulong key = Read(b, ref pos);
                int num = (int)(key >> 3), type = (int)(key & 7);
                if (type == 0) fields.Add(Tuple.Create(num, (long)Read(b, ref pos), (byte[])null));
                else if (type == 2)
                {
                    int len = (int)Read(b, ref pos);
                    var d = new byte[len];
                    Array.Copy(b, pos, d, 0, len);
                    pos += len;
                    fields.Add(Tuple.Create(num, 0L, d));
                }
                else if (type == 1) pos += 8;
                else if (type == 5) pos += 4;
                else throw new IOException("protobuf");
            }
        }

        static ulong Read(byte[] b, ref int pos)
        {
            ulong r = 0;
            for (int shift = 0; ; shift += 7)
            {
                byte x = b[pos++];
                r |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) return r;
            }
        }

        public bool Has(int n) { return fields.Any(f => f.Item1 == n); }
        public long V(int n, long def = 0) { var f = fields.FirstOrDefault(x => x.Item1 == n); return f == null ? def : f.Item2; }

        public string Str(int n)
        {
            var f = fields.FirstOrDefault(x => x.Item1 == n);
            return f == null || f.Item3 == null ? "" : Encoding.UTF8.GetString(f.Item3);
        }

        public PbMsg Msg(int n)
        {
            var f = fields.FirstOrDefault(x => x.Item1 == n);
            return new PbMsg(f == null || f.Item3 == null ? new byte[0] : f.Item3);
        }

        public static PbMsg Receive(Stream s)
        {
            ulong len = 0;
            for (int shift = 0; ; shift += 7)
            {
                int x = s.ReadByte();
                if (x < 0) throw new IOException("connexion fermée");
                len |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
                if (shift > 28) throw new IOException("longueur invalide");
            }
            if (len > 1 << 20) throw new IOException("message trop gros");
            var body = new byte[len];
            int off = 0;
            while (off < body.Length)
            {
                int n = s.Read(body, off, body.Length - off);
                if (n <= 0) throw new IOException("connexion fermée");
                off += n;
            }
            return new PbMsg(body);
        }
    }

    /// <summary>This PC's certificate for the TV: unique name, CA + SAN extensions (the TV refuses others).</summary>
    static class AtvIdentity
    {
        const string Pw = "telecommande";
        static X509Certificate2 cert;

        public static X509Certificate2 Cert
        {
            get
            {
                if (cert != null) return cert;
                string f = Path.Combine(Store.Dir, "atv.pfx");
                if (!File.Exists(f))
                {
                    var rnd = new byte[3];
                    using (var r = RandomNumberGenerator.Create()) r.GetBytes(rnd);
                    string cn = "Telecommande-PC-" + BitConverter.ToString(rnd).Replace("-", "").ToLowerInvariant();
                    using (RSA rsa = RSA.Create(2048))
                    {
                        var req = new CertificateRequest("CN=" + cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, false));
                        var san = new SubjectAlternativeNameBuilder();
                        san.AddDnsName(cn);
                        req.CertificateExtensions.Add(san.Build());
                        using (var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10)))
                            File.WriteAllBytes(f, c.Export(X509ContentType.Pfx, Pw));
                    }
                }
                // Re-import with a persisted key: Windows' TLS (SChannel) cannot use ephemeral keys.
                cert = new X509Certificate2(f, Pw, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
                return cert;
            }
        }

        public static SslStream Connect(string host, int port, out X509Certificate2 server)
        {
            var tcp = new TcpClient();
            var ar = tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(5000)) { tcp.Close(); throw new IOException("la TV ne répond pas (" + host + ")"); }
            tcp.EndConnect(ar);
            var ssl = new SslStream(tcp.GetStream(), false, (s, c, ch, e) => true, (s, t, lc, rc, ai) => Cert);
            ssl.AuthenticateAsClient(host, new X509CertificateCollection { Cert }, SslProtocols.Tls12, false);
            server = new X509Certificate2(ssl.RemoteCertificate);
            return ssl;
        }

        public static string Fingerprint(X509Certificate2 c)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(c.RawData)).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>Pairing ("Polo") on port 6467: the TV shows a 6-character code.</summary>
    class AtvPairing : IDisposable
    {
        readonly SslStream ssl;
        readonly X509Certificate2 server;

        public AtvPairing(string host)
        {
            ssl = AtvIdentity.Connect(host, 6467, out server);
            ssl.ReadTimeout = 15000;
            Send(Outer().Msg(10, new PbWriter().Str(1, "atvremote").Str(2, "Nexio (" + Store.Name + ")")));
            Expect(11, "la TV n'a pas accepté la demande");
            Send(Outer().Msg(20, new PbWriter().Msg(1, Enc()).V(3, 1)));
            Expect(20, "options refusées");
            Send(Outer().Msg(30, new PbWriter().Msg(1, Enc()).V(2, 1)));
            Expect(31, "configuration refusée");
            ssl.ReadTimeout = Timeout.Infinite;
        }

        static PbWriter Outer() { return new PbWriter().V(1, 2).V(2, 200); }
        static PbWriter Enc() { return new PbWriter().V(1, 3).V(2, 6); }
        void Send(PbWriter m) { PbWriter.Send(ssl, m); }

        void Expect(int field, string error)
        {
            var m = PbMsg.Receive(ssl);
            if (m.V(2) != 200) throw new IOException(error + " (statut " + m.V(2) + ")");
            if (!m.Has(field)) throw new IOException(error);
        }

        /// <summary>Sends the code; returns the TV certificate fingerprint to remember.</summary>
        public string Finish(string code)
        {
            code = code.Trim().ToUpperInvariant();
            if (code.Length != 6 || code.Any(ch => "0123456789ABCDEF".IndexOf(ch) < 0))
                throw new IOException("Le code a 6 caractères (0-9 et A-F).");
            var c = AtvIdentity.Cert.GetRSAPublicKey().ExportParameters(false);
            var s = server.GetRSAPublicKey().ExportParameters(false);
            byte[] secret;
            using (var sha = SHA256.Create())
            {
                var all = new MemoryStream();
                foreach (var part in new[] { Trim(c.Modulus), Trim(c.Exponent), Trim(s.Modulus), Trim(s.Exponent), Hex(code.Substring(2)) })
                    all.Write(part, 0, part.Length);
                secret = sha.ComputeHash(all.ToArray());
            }
            if (secret[0] != Convert.ToByte(code.Substring(0, 2), 16)) throw new IOException("Code incorrect. Vérifie les caractères sur la TV.");
            ssl.ReadTimeout = 15000;
            Send(Outer().Msg(40, new PbWriter().Bytes(1, secret)));
            Expect(41, "code refusé par la TV");
            return AtvIdentity.Fingerprint(server);
        }

        static byte[] Trim(byte[] b)
        {
            int i = 0;
            while (i < b.Length - 1 && b[i] == 0) i++;
            return b.Skip(i).ToArray();
        }

        static byte[] Hex(string s)
        {
            var r = new byte[s.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(s.Substring(2 * i, 2), 16);
            return r;
        }

        public void Dispose() { try { ssl.Dispose(); } catch { } }
    }

    /// <summary>Remote-control connection on port 6466. Events arrive on a background thread.</summary>
    class AtvRemote
    {
        const int F_PING = 1, F_KEY = 2, F_IME = 4, F_POWER = 32, F_VOLUME = 64, F_APP_LINK = 512;
        const int Wanted = F_PING | F_KEY | F_IME | F_POWER | F_VOLUME | F_APP_LINK;

        public event Action<string> Connected;     // "NVIDIA SHIELD Android TV"
        public event Action<string> AppChanged;    // package in front
        public event Action<bool> PowerChanged;
        public event Action<string> Disconnected;  // reason

        readonly TvEntry tv;
        readonly object writeLock = new object();
        SslStream ssl;
        volatile bool closed;
        int active = Wanted, imeCounter, fieldCounter;

        public AtvRemote(TvEntry tv) { this.tv = tv; }

        public bool IsOpen { get { return ssl != null && !closed; } }

        public void Connect() { new Thread(Run) { IsBackground = true, Name = "atv" }.Start(); }

        public void Close()
        {
            closed = true;
            var s = ssl;
            if (s != null) ThreadPool.QueueUserWorkItem(_ => { try { s.Dispose(); } catch { } });
        }

        public void Key(int code) { Send(new PbWriter().Msg(10, new PbWriter().V(1, code).V(2, 3))); }

        /// <summary>Inserts text at the cursor of the TV's focused field (the TV appends).</summary>
        public void Insert(string text)
        {
            int n = text.Length;
            Send(new PbWriter().Msg(21, new PbWriter().V(1, imeCounter).V(2, fieldCounter)
                .Msg(3, new PbWriter().V(1, 1).Msg(2, new PbWriter().V(1, n).V(2, n).Str(3, text)))));
        }

        void Send(PbWriter m)
        {
            var s = ssl;
            if (s == null || closed) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (writeLock) PbWriter.Send(s, m); }
                catch (Exception e) { Fail(e.Message); }
            });
        }

        void Run()
        {
            try
            {
                X509Certificate2 server;
                var s = AtvIdentity.Connect(tv.Host, 6466, out server);
                if (tv.Fingerprint != null && AtvIdentity.Fingerprint(server) != tv.Fingerprint)
                    throw new IOException("Ce n'est plus la même TV. Refais l'appairage.");
                s.ReadTimeout = 20000;
                ssl = s;
                while (!closed) Handle(PbMsg.Receive(s));
            }
            catch (Exception e)
            {
                string m = e.Message;
                if (e is AuthenticationException || m.Contains("certificate") || m.Contains("transport")) m = "La TV ne reconnaît pas ce PC : refais l'appairage.";
                Fail(m);
            }
        }

        void Handle(PbMsg m)
        {
            if (m.Has(1))
            {
                var cfg = m.Msg(1);
                active = Wanted & (int)cfg.V(1);
                Send(new PbWriter().Msg(1, new PbWriter().V(1, active)
                    .Msg(2, new PbWriter().V(3, 1).Str(4, "1").Str(5, "atvremote").Str(6, "1.0.0"))));
                var info = cfg.Msg(2);
                var c = Connected;
                if (c != null) c((info.Str(2) + " " + info.Str(1)).Trim());
            }
            else if (m.Has(2)) Send(new PbWriter().Msg(2, new PbWriter().V(1, active)));
            else if (m.Has(8)) Send(new PbWriter().Msg(9, new PbWriter().V(1, m.Msg(8).V(1))));
            else if (m.Has(40)) { var p = PowerChanged; if (p != null) p(m.Msg(40).V(1) != 0); }
            else if (m.Has(20))
            {
                var inj = m.Msg(20);
                imeCounter = (int)inj.Msg(1).V(1);
                fieldCounter = (int)inj.Msg(2).V(1);
                string pkg = inj.Msg(1).Str(12);
                var a = AppChanged;
                if (pkg.Length > 0 && a != null) a(pkg);
            }
            else if (m.Has(22)) fieldCounter = (int)m.Msg(22).Msg(2).V(1);
            else if (m.Has(21))
            {
                var e = m.Msg(21);
                if (imeCounter == 0) imeCounter = (int)e.V(1);
                if (fieldCounter == 0) fieldCounter = (int)e.V(2);
            }
        }

        void Fail(string reason)
        {
            bool was = closed;
            closed = true;
            try { if (ssl != null) ssl.Dispose(); } catch { }
            var d = Disconnected;
            if (!was && d != null) d(reason);
        }

        // Android key codes
        public const int HOME = 3, BACK = 4, UP = 19, DOWN = 20, LEFT = 21, RIGHT = 22, CENTER = 23, VOL_UP = 24, VOL_DOWN = 25,
            POWER = 26, ENTER = 66, DEL = 67, MENU = 82, PLAY_PAUSE = 85, REWIND = 89, FORWARD = 90, MUTE = 164;
    }
}

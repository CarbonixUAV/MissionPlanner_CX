using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Carbonix.Planning
{
    /// <summary>
    /// Random-access byte source for a remote Cloud-Optimized GeoTIFF over public HTTPS.
    /// Reads are served from fixed-size blocks cached in memory and on disk, so only the
    /// header plus the tiles a mission actually touches are ever fetched, and they persist
    /// across restarts. The object's ETag is checked once on construction; a changed ETag
    /// wipes the on-disk cache. If the server can't be reached, a previously cached copy is
    /// used (offline fallback).
    /// </summary>
    internal sealed class CogBlockCache
    {
        private const int BlockSize = 1 << 20;   // 1 MiB

        private readonly string _url;
        private readonly string _cacheDir;
        private readonly object _gate = new object();
        private readonly Dictionary<long, byte[]> _mem = new Dictionary<long, byte[]>();

        public long Length { get; private set; }

        public CogBlockCache(string url, string cacheRoot)
        {
            _url = url;
            _cacheDir = Path.Combine(cacheRoot, Hash(url));
            Directory.CreateDirectory(_cacheDir);
            Initialise();
        }

        private void Initialise()
        {
            string etag = null;
            long length = -1;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(_url);
                req.Method = "HEAD";
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    length = resp.ContentLength;
                    etag = resp.Headers["ETag"];
                }
            }
            catch
            {
                // Offline / HEAD blocked — fall back to cached metadata below.
            }

            var metaPath = Path.Combine(_cacheDir, "meta.txt");
            string cachedEtag = null;
            long cachedLen = -1;
            if (File.Exists(metaPath))
            {
                var parts = File.ReadAllText(metaPath).Split('\n');
                if (parts.Length >= 2)
                {
                    cachedEtag = parts[0];
                    long.TryParse(parts[1], out cachedLen);
                }
            }

            if (length >= 0)
            {
                // Online: trust the server. Drop cached blocks if the object changed.
                if (cachedEtag != (etag ?? ""))
                    foreach (var f in Directory.GetFiles(_cacheDir, "blk_*.bin"))
                        File.Delete(f);

                Length = length;
                File.WriteAllText(metaPath, (etag ?? "") + "\n" + length);
            }
            else if (cachedLen >= 0)
            {
                Length = cachedLen;   // offline: use last cached copy
            }
            else
            {
                throw new IOException("Cannot reach remote surface and no cache present: " + _url);
            }
        }

        /// <summary>Copy up to <paramref name="count"/> bytes from <paramref name="position"/>
        /// into <paramref name="dst"/>; returns the number copied (fewer only at EOF).</summary>
        public int ReadInto(long position, byte[] dst, int dstOffset, int count)
        {
            if (position >= Length) return 0;
            count = (int)Math.Min(count, Length - position);

            int done = 0;
            while (done < count)
            {
                long abs = position + done;
                long blockIndex = abs / BlockSize;
                int within = (int)(abs - blockIndex * BlockSize);
                byte[] block = GetBlock(blockIndex);
                int n = Math.Min(block.Length - within, count - done);
                Buffer.BlockCopy(block, within, dst, dstOffset + done, n);
                done += n;
            }
            return done;
        }

        private byte[] GetBlock(long blockIndex)
        {
            lock (_gate)
            {
                if (_mem.TryGetValue(blockIndex, out var cached))
                    return cached;

                var path = Path.Combine(_cacheDir, "blk_" + blockIndex + ".bin");
                if (File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    _mem[blockIndex] = bytes;
                    return bytes;
                }

                long start = blockIndex * BlockSize;
                long end = Math.Min(start + BlockSize, Length) - 1;   // inclusive
                var fetched = Fetch(start, end);
                File.WriteAllBytes(path, fetched);
                _mem[blockIndex] = fetched;
                return fetched;
            }
        }

        private byte[] Fetch(long start, long end)
        {
            var req = (HttpWebRequest)WebRequest.Create(_url);
            req.AddRange(start, end);
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                // A server that ignored Range would send 200 (the whole object) — refuse it
                // rather than buffer gigabytes.
                if (resp.StatusCode != HttpStatusCode.PartialContent)
                    throw new IOException("remote surface did not honour Range request: " + _url);

                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        private static string Hash(string s)
        {
            using (var sha = SHA256.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(b[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>
    /// Loads newspaper illustrations from disk into <see cref="Texture2D"/>s, cached by path so a
    /// broadsheet that repaints every frame decodes each PNG exactly once.
    ///
    /// <para>This is the render-side seam for resolved image assets. The fixture points it at the
    /// bundled sample PNGs; the Pollinations pipeline (WorldNews#23) will point it at its on-disk
    /// cache. Either way the path is a plain file on disk and the loader is identical — so nailing
    /// the layout against the samples nails it against real generated assets too.</para>
    ///
    /// <para>A missing or unreadable file caches a null so we neither retry the decode every frame
    /// nor throw into the UI loop; the renderer treats that exactly like a text-only story.</para>
    /// </summary>
    public static class NewspaperImageCache
    {
        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>();

        /// <summary>
        /// Return the texture for <paramref name="path"/>, decoding and caching it on first use.
        /// Returns null (and caches null) when the path is empty, absent, or fails to decode, so
        /// callers can branch on <c>!= null</c> without a try/catch of their own.
        /// </summary>
        public static Texture2D Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (Cache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            Texture2D tex = null;
            try
            {
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    // mipChain: false — these are drawn 1:1 in UI space, mipmaps only cost memory.
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(bytes)) // resizes to the PNG's real dimensions on success
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }
                }
                else
                {
                    RimSynapse.SynapseLogger.Warn("worldnews",
                        $"[RimSynapse-WorldNews] Newspaper image not found, rendering text-only: {path}");
                }
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Warn("worldnews",
                    $"[RimSynapse-WorldNews] Failed to load newspaper image '{path}': {ex.Message}");
                tex = null;
            }

            Cache[path] = tex;
            return tex;
        }
    }
}

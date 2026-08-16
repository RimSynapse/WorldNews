using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Verse;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>
    /// Resolves a story's <see cref="NewspaperStory.ImagePrompt"/> into a cached illustration file
    /// (WorldNews#23). Prompts are turned into pictures by pollinations.ai, downloaded once, and
    /// cached on disk keyed by a hash of the prompt — so re-opening an issue, or reloading, reuses
    /// the same file instead of re-fetching.
    ///
    /// <para><b>Gated and graceful.</b> Nothing is fetched unless the player has enabled illustrations
    /// and answered the consent window (WorldNews#24). A disabled feature, a missing network, a slow
    /// or failed request — every one degrades to text-only; the renderer already draws a story with no
    /// resolved image as text, so a picture never blocks reading the paper.</para>
    ///
    /// <para>Fetches run off the main thread; the resolved path is written back to the story on the
    /// main thread via <see cref="SynapseGameComponent.Enqueue"/>, so an open broadsheet picks the
    /// image up on its next repaint.</para>
    /// </summary>
    public static class NewspaperImagePipeline
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        // Guards against firing a second fetch for a target already in flight (keyed by cache path).
        private static readonly HashSet<string> InFlight = new HashSet<string>();

        private const string ArtStyle =
            "vintage newspaper engraving, black and white ink illustration, cross-hatching, halftone print";

        /// <summary>Where downloaded illustrations are cached.</summary>
        public static string CacheDir =>
            Path.Combine(GenFilePaths.ConfigFolderPath, "RimSynapseAssets", "Newspapers");

        private static bool Enabled
        {
            get
            {
                var s = RimSynapseWorldNewsMod.Settings;
                return s != null && s.enableNewspaperImages && s.imageConsentDecided;
            }
        }

        /// <summary>
        /// For every illustrated-but-unresolved story in the issue: serve it from the disk cache if the
        /// file exists, otherwise (only when enabled) kick off a background fetch. Safe to call on every
        /// dialog open — cache hits are instant and fetches dedup by target file.
        /// </summary>
        public static void Resolve(NewspaperIssue issue)
        {
            if (issue?.Stories == null) return;
            foreach (NewspaperStory story in issue.Stories)
            {
                if (story == null || !story.WantsImage || story.HasImage) continue;

                string cache = CachePathFor(story.ImagePrompt);
                if (File.Exists(cache)) { story.ResolvedImagePath = cache; continue; } // cache hit
                if (!Enabled) continue;                                                // consent/toggle gate
                Fetch(story, cache);
            }
        }

        /// <summary>
        /// True when publishing should wait: images are enabled and at least one wanted illustration is
        /// neither already resolved nor already on disk, so a live fetch is required.
        /// </summary>
        public static bool WillFetch(NewspaperIssue issue)
        {
            if (issue?.Stories == null || !Enabled) return false;
            foreach (NewspaperStory s in issue.Stories)
            {
                if (s == null || !s.WantsImage || s.HasImage) continue;
                if (!File.Exists(CachePathFor(s.ImagePrompt))) return true;
            }
            return false;
        }

        /// <summary>
        /// Resolve every wanted illustration — cache hits instantly, the rest fetched — and invoke
        /// <paramref name="onDone"/> on the main thread once they have all landed or given up (a 4xx
        /// decline or timeout gives up and leaves that story text-only). Used to hold publication until
        /// the pictures are in hand (WorldNews#23).
        /// </summary>
        public static void ResolveAndAwait(NewspaperIssue issue, Action onDone)
        {
            if (issue?.Stories == null) { onDone?.Invoke(); return; }

            var toFetch = new List<KeyValuePair<NewspaperStory, string>>();
            foreach (NewspaperStory s in issue.Stories)
            {
                if (s == null || !s.WantsImage || s.HasImage) continue;
                string cache = CachePathFor(s.ImagePrompt);
                if (File.Exists(cache)) { s.ResolvedImagePath = cache; continue; }
                if (!Enabled) continue;
                toFetch.Add(new KeyValuePair<NewspaperStory, string>(s, cache));
            }

            if (toFetch.Count == 0) { onDone?.Invoke(); return; }

            int[] remaining = { toFetch.Count };
            foreach (KeyValuePair<NewspaperStory, string> kv in toFetch)
            {
                Fetch(kv.Key, kv.Value, () =>
                {
                    if (Interlocked.Decrement(ref remaining[0]) == 0)
                        SynapseGameComponent.Enqueue(() => onDone?.Invoke());
                });
            }
        }

        /// <summary>Force a fetch for a prompt regardless of the consent gate — debug validation only.</summary>
        public static void DebugForceFetch(string prompt)
        {
            string cache = CachePathFor(prompt);
            if (File.Exists(cache))
            {
                RimSynapse.SynapseLogger.Message($"[RimSynapse-WorldNews] Image already cached: {cache}");
                return;
            }
            Fetch(new NewspaperStory { Title = "debug", ImagePrompt = prompt }, cache);
        }

        private static void Fetch(NewspaperStory story, string cachePath, Action onComplete = null)
        {
            lock (InFlight)
            {
                if (!InFlight.Add(cachePath)) { onComplete?.Invoke(); return; } // already downloading this one
            }

            string styled = story.ImagePrompt + ", " + ArtStyle;
            string url = "https://image.pollinations.ai/prompt/" + Uri.EscapeDataString(styled)
                + "?width=768&height=512&nologo=true";

            Task.Run(async () =>
            {
                try
                {
                    using (HttpResponseMessage resp = await Http.GetAsync(url).ConfigureAwait(false))
                    {
                        if (resp.IsSuccessStatusCode)
                        {
                            byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            Directory.CreateDirectory(CacheDir);

                            // Temp file then move, so a half-downloaded file is never seen as a cache hit.
                            string tmp = cachePath + ".tmp";
                            File.WriteAllBytes(tmp, bytes);
                            if (File.Exists(cachePath)) File.Delete(cachePath);
                            File.Move(tmp, cachePath);

                            SynapseGameComponent.Enqueue(() => story.ResolvedImagePath = cachePath);
                            RimSynapse.SynapseLogger.Message(
                                $"[RimSynapse-WorldNews] Newspaper image ready ({bytes.Length / 1024} KB): {Path.GetFileName(cachePath)}");
                        }
                        else
                        {
                            // Service declined (e.g. 4xx) — give up on this picture and publish text-only
                            // rather than holding the paper indefinitely.
                            RimSynapse.SynapseLogger.Warn("worldnews",
                                $"[RimSynapse-WorldNews] Newspaper image declined ({(int)resp.StatusCode}), text-only: {story.ImagePrompt}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    RimSynapse.SynapseLogger.Warn("worldnews",
                        $"[RimSynapse-WorldNews] Newspaper image fetch failed (text-only): {ex.Message}");
                }
                finally
                {
                    lock (InFlight) { InFlight.Remove(cachePath); }
                    onComplete?.Invoke();
                }
            });
        }

        private static string CachePathFor(string prompt) =>
            Path.Combine(CacheDir, Hash(prompt) + ".jpg");

        // Stable across sessions (unlike string.GetHashCode), so the disk cache survives reloads.
        private static string Hash(string s)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] b = md5.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
                var sb = new StringBuilder(b.Length * 2);
                foreach (byte x in b) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}

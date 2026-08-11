using System;
using System.IO;
using System.Text;
using Verse;
using RimSynapse.WorldNews.Models;

namespace RimSynapse.WorldNews.Newspaper
{
    /// <summary>
    /// The second newspaper renderer (WorldNews#22): turns a <see cref="NewspaperIssue"/> into a
    /// single self-contained HTML file the player can open in a browser and share. Illustrations are
    /// embedded as base64 <c>data:</c> URIs, so the one file carries its own pictures — no sidecar
    /// assets, no external requests.
    ///
    /// <para><b>On-demand only.</b> Nothing calls this automatically. Export runs when the player
    /// clicks Export on the broadsheet (or via the debug action); a published issue never writes a
    /// file on its own.</para>
    /// </summary>
    public static class NewspaperHtmlExporter
    {
        /// <summary>Folder exports land in: &lt;save-data&gt;/RimSynapse/Newspapers.</summary>
        public static string ExportDir =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, "RimSynapse", "Newspapers");

        /// <summary>
        /// Write <paramref name="issue"/> to a self-contained HTML file and return its full path, or
        /// null on failure (with <paramref name="error"/> set). Never throws into the caller.
        /// </summary>
        public static string Export(NewspaperIssue issue, out string error)
        {
            error = null;
            if (issue == null) { error = "no issue"; return null; }
            try
            {
                Directory.CreateDirectory(ExportDir);
                string path = Path.Combine(ExportDir, FileName(issue));
                File.WriteAllText(path, BuildHtml(issue), Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                RimSynapse.SynapseLogger.Warn("worldnews",
                    $"[RimSynapse-WorldNews] HTML export failed: {ex}");
                return null;
            }
        }

        private static string FileName(NewspaperIssue issue)
        {
            string baseName = Sanitize(issue.PaperName);
            if (string.IsNullOrEmpty(baseName)) baseName = "newspaper";
            string num = Sanitize(issue.IssueNumber);
            long stamp = Find.TickManager?.TicksAbs ?? 0L; // in-game uniqueness, no wall clock needed
            return string.IsNullOrEmpty(num)
                ? $"{baseName}_{stamp}.html"
                : $"{baseName}_{num}_{stamp}.html";
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_' || c == '.') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        // ---- HTML assembly ------------------------------------------------------------------------

        public static string BuildHtml(NewspaperIssue issue)
        {
            var sb = new StringBuilder(8192);
            sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append("<title>").Append(Esc(issue.PaperName ?? "Newspaper")).Append("</title>");
            sb.Append("<style>").Append(Css).Append("</style></head><body><div class=\"paper\">");

            // Masthead.
            sb.Append("<header class=\"masthead\">");
            sb.Append("<h1>").Append(Esc(issue.PaperName)).Append("</h1>");
            sb.Append("<div class=\"info\"><span>")
              .Append(Esc(Join("   ", issue.Volume, issue.IssueNumber))).Append("</span><span>")
              .Append(Esc(issue.Date)).Append("</span><span>")
              .Append(Esc(issue.Price)).Append("</span></div>");
            if (!string.IsNullOrEmpty(issue.Strapline))
                sb.Append("<div class=\"strapline\">").Append(Esc(issue.Strapline)).Append("</div>");
            sb.Append("</header>");

            if (!string.IsNullOrEmpty(issue.Headline))
                sb.Append("<div class=\"banner\">").Append(Esc(issue.Headline)).Append("</div>");

            sb.Append("<div class=\"content\">");

            // Lead (main column).
            sb.Append("<main class=\"lead\">");
            if (issue.Stories != null && issue.Stories.Count > 0)
            {
                NewspaperStory lead = issue.Stories[0];
                sb.Append("<h2>").Append(Esc(lead.Title)).Append("</h2>");
                if (!string.IsNullOrEmpty(lead.Byline))
                    sb.Append("<p class=\"byline\">").Append(Esc(lead.Byline)).Append("</p>");
                AppendFigure(sb, lead);
                sb.Append("<div class=\"body\">").Append(Paragraphs(lead.Content)).Append("</div>");
            }
            sb.Append("</main>");

            // Right rail.
            sb.Append("<aside class=\"rail\">");
            if (issue.Stories != null)
            {
                for (int i = 1; i < issue.Stories.Count; i++)
                {
                    NewspaperStory s = issue.Stories[i];
                    sb.Append("<section class=\"secondary\"><h3>").Append(Esc(s.Title)).Append("</h3>");
                    if (!string.IsNullOrEmpty(s.Byline))
                        sb.Append("<p class=\"byline\">").Append(Esc(s.Byline)).Append("</p>");
                    AppendFigure(sb, s);
                    sb.Append("<div class=\"body\">").Append(Paragraphs(s.Content)).Append("</div></section>");
                }
            }

            if (issue.Sidebars != null && issue.Sidebars.Count > 0)
            {
                sb.Append("<section class=\"inbrief\"><div class=\"box-title\">IN BRIEF</div>");
                foreach (NewspaperSidebar sb2 in issue.Sidebars)
                {
                    sb.Append("<h4>").Append(Esc(sb2.Title)).Append("</h4>");
                    sb.Append("<p>").Append(Esc(sb2.Content)).Append("</p>");
                }
                sb.Append("</section>");
            }

            if (issue.Ads != null)
            {
                foreach (NewspaperAd ad in issue.Ads)
                {
                    sb.Append("<section class=\"ad\"><div class=\"adv\">").Append(Esc(ad.Advertiser))
                      .Append("</div><div class=\"copy\">").Append(Esc(ad.Copy)).Append("</div></section>");
                }
            }
            sb.Append("</aside>");

            sb.Append("</div>"); // .content

            sb.Append("<footer><span>").Append(Esc(issue.SectionFooter)).Append("</span><span>")
              .Append(Esc(issue.PaperName)).Append("</span></footer>");

            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        private static void AppendFigure(StringBuilder sb, NewspaperStory story)
        {
            if (story == null || !story.WantsImage) return;
            string uri = DataUri(story.ResolvedImagePath);
            if (uri == null) return; // unresolved asset → text-only, exactly like the in-game renderer
            sb.Append("<figure><img alt=\"\" src=\"").Append(uri).Append("\">");
            if (!string.IsNullOrEmpty(story.ImageCaption))
                sb.Append("<figcaption>").Append(Esc(story.ImageCaption)).Append("</figcaption>");
            sb.Append("</figure>");
        }

        private static string DataUri(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
            }
            catch { /* fall through to text-only */ }
            return null;
        }

        private static string Paragraphs(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            var sb = new StringBuilder();
            foreach (string line in content.Replace("\r", "").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                sb.Append("<p>").Append(Esc(t)).Append("</p>");
            }
            return sb.ToString();
        }

        private static string Join(string sep, params string[] values)
        {
            var sb = new StringBuilder();
            foreach (string v in values)
            {
                if (string.IsNullOrEmpty(v)) continue;
                if (sb.Length > 0) sb.Append(sep);
                sb.Append(v);
            }
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        private const string Css = @"
:root{ --paper:#e6ddc7; --ink:#211b14; --muted:#5a4f3f; --rule:#211b14; }
*{ box-sizing:border-box; }
body{ margin:0; padding:24px; background:#cfc4a8; color:var(--ink);
  font-family:Georgia,'Times New Roman',serif; }
.paper{ max-width:1000px; margin:0 auto; background:var(--paper); padding:32px 36px;
  box-shadow:0 8px 30px rgba(0,0,0,.35); }
.masthead h1{ text-align:center; font-size:52px; letter-spacing:1px; margin:0 0 6px;
  border-bottom:3px double var(--rule); padding-bottom:8px; }
.masthead .info{ display:flex; justify-content:space-between; font-size:12px;
  text-transform:uppercase; letter-spacing:.5px; color:var(--muted);
  border-bottom:1px solid var(--rule); padding:4px 0; }
.masthead .strapline{ text-align:center; font-style:italic; color:var(--muted);
  font-size:13px; padding-top:6px; }
.banner{ font-size:30px; font-weight:bold; text-transform:uppercase; letter-spacing:.5px;
  margin:14px 0; padding-bottom:12px; border-bottom:3px double var(--rule); }
.content{ display:grid; grid-template-columns:2fr 1fr; gap:28px; }
.lead h2{ font-size:26px; margin:0 0 2px; }
.rail .secondary h3{ font-size:19px; margin:0 0 2px; }
.byline{ font-style:italic; color:var(--muted); font-size:13px; margin:0 0 8px; }
figure{ margin:0 0 6px; }
figure img{ width:100%; height:auto; display:block; border:1px solid var(--ink); }
figcaption{ font-style:italic; color:var(--muted); font-size:12px; text-align:center;
  padding-top:4px; }
.body{ text-align:justify; }
.body p{ margin:0 0 10px; line-height:1.5; }
.lead>.body{ column-count:2; column-gap:24px; }
.rail{ border-left:1px solid var(--rule); padding-left:24px; }
.rail .secondary{ border-bottom:1px solid var(--muted); padding-bottom:12px; margin-bottom:14px; }
.rail .secondary .body{ font-size:14px; }
.inbrief{ border:1px solid var(--ink); padding:12px; margin-bottom:16px; }
.inbrief .box-title{ text-align:center; text-transform:uppercase; letter-spacing:1px;
  font-size:13px; border-bottom:1px solid var(--muted); padding-bottom:6px; margin-bottom:8px; }
.inbrief h4{ margin:8px 0 2px; font-size:14px; }
.inbrief p{ margin:0; font-size:13px; color:var(--muted); }
.ad{ border:1px solid var(--ink); padding:14px; margin-bottom:14px; text-align:center; }
.ad .adv{ font-size:17px; font-weight:bold; }
.ad .copy{ font-size:13px; font-style:italic; color:var(--muted); padding-top:4px; }
footer{ display:flex; justify-content:space-between; font-size:12px; color:var(--muted);
  text-transform:uppercase; letter-spacing:.5px; border-top:1px solid var(--rule);
  margin-top:20px; padding-top:8px; }
@media (max-width:720px){ .content{ grid-template-columns:1fr; }
  .rail{ border-left:none; padding-left:0; } .lead>.body{ column-count:1; } }
";
    }
}

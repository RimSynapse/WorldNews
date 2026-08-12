using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using RimSynapse.WorldNews.Models;
using RimSynapse.WorldNews.Newspaper;

namespace RimSynapse.WorldNews.UI
{
    /// <summary>
    /// The in-game IMGUI broadsheet renderer (WorldNews#22). Reads the whole <see cref="NewspaperIssue"/>
    /// model — masthead, banner headline, a lead story with an illustration, secondary stories, side
    /// items and advertisements — and lays them out as a front page on a two-column grid.
    ///
    /// <para>Rendering is a two-pass affair per column: a measure pass (<c>draw:false</c>) sums the
    /// content height so the scroll view knows its extent, then a draw pass paints it. The two passes
    /// call the exact same block helpers, so a scrollbar can never disagree with what it scrolls.</para>
    /// </summary>
    public class Dialog_Newspaper : Window
    {
        private readonly NewspaperIssue issue;

        // One scroll position for the whole page: the lead column and the side rail are locked to a
        // single scroll view, so they move together as one broadsheet page.
        private Vector2 pageScroll;

        /// <summary>
        /// Debug-only: on the first layout, jam the page scroll to its foot so a headless screenshot
        /// can see the side items and advertisements without a mouse. Set via the "scrolled to foot"
        /// debug action; harmless in normal use (left false). Pixel-driving the scrollbar is unreliable
        /// here — the game runs on a virtual desktop — so this is how those blocks get verified.
        /// </summary>
        public bool debugStartScrolledToBottom;
        private bool bottomApplied;

        // The path of the most recent on-demand HTML export, shown in the toolbar. Null until the
        // player clicks Export — the paper never writes a file on its own.
        private string lastExportPath;

        // Newsprint palette — cream stock, brown-black ink, a muted grey for bylines and captions.
        private static readonly Color PaperColor = new Color(0.902f, 0.867f, 0.792f);
        private static readonly Color InkColor = new Color(0.129f, 0.106f, 0.078f);
        private static readonly Color MutedInk = new Color(0.353f, 0.310f, 0.259f);

        public override Vector2 InitialSize => new Vector2(900f, 680f);

        public Dialog_Newspaper(NewspaperIssue issue)
        {
            this.issue = issue;
            this.forcePause = true;
            this.doCloseX = true;
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = true;

            // Serve illustrations from the disk cache, or (if enabled) start fetching them; resolved
            // paths land on the story objects and show on the next repaint. No-op for text-only issues.
            NewspaperImagePipeline.Resolve(issue);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (issue == null) return;

            // Newsprint stock behind everything, with a hairline frame.
            Widgets.DrawBoxSolid(inRect, PaperColor);
            GUI.color = InkColor;
            Widgets.DrawBox(inRect);
            GUI.color = Color.white;

            Rect page = inRect.ContractedBy(14f);

            // --- Export toolbar (on-demand HTML export; nothing auto-saves) ---
            float y = page.y + RenderToolbar(page) + 6f;

            // --- Masthead + banner (full width, non-scrolling) ---
            y += RenderMasthead(page.x, y, page.width, draw: true);
            y += 6f;
            y += RenderBanner(page.x, y, page.width, draw: true);
            y += 6f;

            // --- Footer reserved at the bottom, columns fill the space between ---
            float footerH = 22f;
            float bottomLimit = page.yMax - footerH - 6f;
            RenderFooter(page.x, page.yMax - footerH, page.width);

            // One linked page: both columns live in a single scroll view, so the lead story and the
            // side rail scroll together rather than as two independent panes. The taller column sets
            // the shared scroll extent.
            float columnsTop = y + 4f;
            float columnsH = bottomLimit - columnsTop;
            if (columnsH < 60f) return; // window shrunk below anything usable

            Rect columnsOut = new Rect(page.x, columnsTop, page.width, columnsH);
            float gutter = 18f;
            float contentW = page.width - 16f;               // leave room for the one scrollbar
            float leadW = Mathf.Floor((contentW - gutter) * 0.62f);
            float railW = contentW - gutter - leadW;
            float railX = leadW + gutter;                    // column origins are view-relative (0-based)

            // Measure pass — height only; x is irrelevant to measured height so pass the draw origins.
            float contentH = Mathf.Max(RenderLead(0f, leadW, false), RenderRightRail(railX, railW, false));

            if (debugStartScrolledToBottom && !bottomApplied)
            {
                // BeginScrollView clamps this to the real max, so an over-large value just pins the foot.
                pageScroll.y = 100000f;
                bottomApplied = true;
            }

            var viewRect = new Rect(0f, 0f, contentW, contentH);
            Widgets.BeginScrollView(columnsOut, ref pageScroll, viewRect);
            RenderLead(0f, leadW, true);
            GUI.color = MutedInk;
            Widgets.DrawLineVertical(leadW + gutter * 0.5f, 0f, contentH);
            GUI.color = Color.white;
            RenderRightRail(railX, railW, true);
            Widgets.EndScrollView();

            // Leave global GUI state as we found it.
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// The on-page export control: an Export button and a status line that, after a save, shows
        /// the file's location (also copied to the clipboard). The top-right corner is left clear for
        /// the window's close box. Returns the strip height consumed.
        /// </summary>
        private float RenderToolbar(Rect page)
        {
            const float h = 24f;
            const float exportW = 120f;
            const float copyW = 96f;
            const float rightReserve = 30f; // keep clear of the window's close-X
            const float pad = 8f;

            if (Widgets.ButtonText(new Rect(page.x, page.y, exportW, h), "Export HTML"))
            {
                DoExport();
            }

            float midX = page.x + exportW + pad;

            if (string.IsNullOrEmpty(lastExportPath))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = MutedInk;
                LabelAnchored(
                    new Rect(midX, page.y, page.width - exportW - pad - rightReserve, h),
                    "Export a self-contained HTML copy you can share — nothing is saved until you click.",
                    TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return h;
            }

            // A "Copy path" button on the right, and a read-only, selectable field showing the full
            // path between it and the Export button. The field lets the player drag-select and Ctrl+C;
            // the button copies the whole path in one click.
            var copyRect = new Rect(page.xMax - rightReserve - copyW, page.y, copyW, h);
            if (Widgets.ButtonText(copyRect, "Copy path"))
            {
                GUIUtility.systemCopyBuffer = lastExportPath;
                Messages.Message("Newspaper file path copied to clipboard.",
                    MessageTypeDefOf.TaskCompletion, historical: false);
            }

            var fieldRect = new Rect(midX, page.y, copyRect.x - midX - pad, h);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            // Read-only: the returned (possibly edited) value is discarded, so the field always shows
            // the real path while still supporting selection and copy.
            GUI.SetNextControlName("WorldNewsExportPath");
            Widgets.TextField(fieldRect, lastExportPath);
            Text.Font = GameFont.Small;
            return h;
        }

        /// <summary>Debug-only: preset the exported-path state so a screenshot can show the populated
        /// toolbar (selectable field + Copy path) without a mouse click on Export.</summary>
        public void DebugSetExportedPath(string path)
        {
            lastExportPath = path;
        }

        /// <summary>
        /// Write the current issue to a self-contained HTML file, copy the path to the clipboard so it
        /// can be pasted, and report where it landed. On-demand only — invoked by the toolbar button.
        /// </summary>
        private void DoExport()
        {
            string path = NewspaperHtmlExporter.Export(issue, out string error);
            if (path != null)
            {
                lastExportPath = path;
                GUIUtility.systemCopyBuffer = path; // "paste the location it saved"
                Messages.Message("Newspaper exported — path copied to clipboard:\n" + path,
                    MessageTypeDefOf.PositiveEvent, historical: false);
            }
            else
            {
                lastExportPath = null;
                Messages.Message("Newspaper HTML export failed: " + error,
                    MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        // ---- Masthead -----------------------------------------------------------------------------

        private float RenderMasthead(float x, float y, float w, bool draw)
        {
            float startY = y;

            y += Para(x, y, w, issue.PaperName, GameFont.Medium, InkColor, TextAnchor.MiddleCenter, draw);
            y += 2f;
            y += Rule(x, y, w, draw, InkColor, thickness: 3f);
            y += 3f;

            // Info line: VOL/NO left, date centre, price right.
            string left = string.Join("   ", NonEmpty(issue.Volume, issue.IssueNumber));
            Text.Font = GameFont.Tiny;
            float infoH = 18f;
            if (draw)
            {
                GUI.color = MutedInk;
                if (!string.IsNullOrEmpty(left)) LabelAnchored(new Rect(x, y, w, infoH), left, TextAnchor.MiddleLeft);
                if (!string.IsNullOrEmpty(issue.Date)) LabelAnchored(new Rect(x, y, w, infoH), issue.Date, TextAnchor.MiddleCenter);
                if (!string.IsNullOrEmpty(issue.Price)) LabelAnchored(new Rect(x, y, w, infoH), issue.Price, TextAnchor.MiddleRight);
                GUI.color = Color.white;
            }
            y += infoH;

            if (!string.IsNullOrEmpty(issue.Strapline))
            {
                y += 2f;
                y += Para(x, y, w, issue.Strapline, GameFont.Tiny, MutedInk, TextAnchor.MiddleCenter, draw);
            }

            y += 3f;
            y += Rule(x, y, w, draw, InkColor, thickness: 3f); // heavy over thin: classic masthead double rule
            y += 2f;
            y += Rule(x, y, w, draw, InkColor, thickness: 1f);

            return y - startY;
        }

        private float RenderBanner(float x, float y, float w, bool draw)
        {
            if (string.IsNullOrEmpty(issue.Headline)) return 0f;
            return Para(x, y, w, issue.Headline, GameFont.Medium, InkColor, TextAnchor.MiddleLeft, draw);
        }

        private void RenderFooter(float x, float y, float w)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = MutedInk;
            Widgets.DrawLineHorizontal(x, y, w);
            var top = new Rect(x, y + 3f, w, 18f);
            if (!string.IsNullOrEmpty(issue.SectionFooter)) LabelAnchored(top, issue.SectionFooter, TextAnchor.MiddleLeft);
            LabelAnchored(top, issue.PaperName, TextAnchor.MiddleRight);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // ---- Columns ------------------------------------------------------------------------------

        private float RenderLead(float x, float w, bool draw)
        {
            var stories = issue.Stories;
            if (stories == null || stories.Count == 0) return 0f;
            NewspaperStory lead = stories[0];
            float y = 0f;

            y += Para(x, y, w, lead.Title, GameFont.Medium, InkColor, TextAnchor.UpperLeft, draw);
            if (!string.IsNullOrEmpty(lead.Byline))
            {
                y += 1f;
                y += Para(x, y, w, lead.Byline, GameFont.Tiny, MutedInk, TextAnchor.UpperLeft, draw);
            }
            y += 3f;
            y += Rule(x, y, w, draw, MutedInk, thickness: 1f);
            y += 6f;

            y += RenderImage(x, y, w, lead, draw, maxHeightFraction: 0.5f);

            y += RenderLeadBody(x, y, w, lead.Content, draw);
            return y;
        }

        /// <summary>
        /// The lead body, with a drop-cap initial on the first paragraph. Paragraphs are split on blank
        /// lines; the first flows around a raised capital, the rest render normally.
        /// </summary>
        private float RenderLeadBody(float x, float y, float w, string content, bool draw)
        {
            if (string.IsNullOrEmpty(content)) return 0f;

            string[] paras = content.Replace("\r", "")
                .Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            float startY = y;
            bool first = true;
            foreach (string raw in paras)
            {
                string p = raw.Replace("\n", " ").Trim();
                if (p.Length == 0) continue;
                if (first)
                {
                    y += DropCapParagraph(x, y, w, p, draw);
                    first = false;
                }
                else
                {
                    y += 6f;
                    y += Para(x, y, w, p, GameFont.Small, InkColor, TextAnchor.UpperLeft, draw);
                }
            }
            return y - startY;
        }

        /// <summary>
        /// Draw one paragraph with a large raised initial. The first character is set in the Medium
        /// font; the opening lines of body text run beside it for the cap's height, then the remainder
        /// flows full width below. Height is identical in the measure and draw passes.
        /// </summary>
        private float DropCapParagraph(float x, float y, float w, string text, bool draw)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            if (text.Length == 1)
                return Para(x, y, w, text, GameFont.Small, InkColor, TextAnchor.UpperLeft, draw);

            string capStr = text.Substring(0, 1);
            string rest = text.Substring(1).TrimStart();

            Text.Font = GameFont.Medium;
            Vector2 capSize = Text.CalcSize(capStr);
            float capW = capSize.x;
            float capH = capSize.y;

            Text.Font = GameFont.Small;
            float lineH = Text.LineHeight;
            float bandH = Mathf.Max(1, Mathf.CeilToInt(capH / lineH)) * lineH;
            const float gap = 6f;
            float besideW = w - capW - gap;
            if (besideW < 40f) besideW = w; // degenerate width guard: skip the wrap band

            // Longest prefix of the remainder that fits the band beside the cap, snapped to a word break.
            int split = PrefixFillingHeight(rest, besideW, bandH);
            if (split > 0 && split < rest.Length)
            {
                int sp = rest.LastIndexOf(' ', Mathf.Min(split, rest.Length - 1));
                if (sp > 0) split = sp;
            }
            string beside = rest.Substring(0, split).TrimEnd();
            string below = rest.Substring(split).TrimStart();

            if (draw)
            {
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = InkColor;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(x, y, capW + 4f, capH), capStr);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(x + capW + gap, y, besideW, bandH), beside);
                GUI.color = Color.white;
            }

            float belowH = 0f;
            if (below.Length > 0)
            {
                Text.Font = GameFont.Small;
                belowH = Text.CalcHeight(below, w);
                if (draw)
                {
                    GUI.color = InkColor;
                    Widgets.Label(new Rect(x, y + bandH, w, belowH), below);
                    GUI.color = Color.white;
                }
            }

            Text.Font = GameFont.Small;
            return bandH + belowH;
        }

        /// <summary>Longest character prefix of <paramref name="text"/> whose wrapped height at
        /// <paramref name="width"/> stays within <paramref name="maxH"/>. Binary search on length.</summary>
        private static int PrefixFillingHeight(string text, float width, float maxH)
        {
            Text.Font = GameFont.Small;
            int lo = 0, hi = text.Length, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                float h = mid == 0 ? 0f : Text.CalcHeight(text.Substring(0, mid), width);
                if (h <= maxH) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best;
        }

        private float RenderRightRail(float x, float w, bool draw)
        {
            float y = 0f;
            var stories = issue.Stories;

            // Secondary stories (everything after the lead).
            if (stories != null)
            {
                for (int i = 1; i < stories.Count; i++)
                {
                    NewspaperStory s = stories[i];
                    y += Para(x, y, w, s.Title, GameFont.Small, InkColor, TextAnchor.UpperLeft, draw);
                    if (!string.IsNullOrEmpty(s.Byline))
                    {
                        y += Para(x, y, w, s.Byline, GameFont.Tiny, MutedInk, TextAnchor.UpperLeft, draw);
                    }
                    y += 3f;
                    y += RenderImage(x, y, w, s, draw, maxHeightFraction: 0.7f);
                    y += Para(x, y, w, s.Content, GameFont.Tiny, InkColor, TextAnchor.UpperLeft, draw);
                    y += 6f;
                    y += Rule(x, y, w, draw, MutedInk, thickness: 1f);
                    y += 8f;
                }
            }

            // Side items, in a boxed "IN BRIEF" panel.
            if (issue.Sidebars != null && issue.Sidebars.Count > 0)
            {
                y += RenderBoxedSection(x, y, w, "IN BRIEF", draw, (ix, iy, iw, idraw) =>
                {
                    float yy = iy;
                    foreach (NewspaperSidebar sb in issue.Sidebars)
                    {
                        yy += Para(ix, yy, iw, sb.Title, GameFont.Tiny, InkColor, TextAnchor.UpperLeft, idraw);
                        yy += Para(ix, yy, iw, sb.Content, GameFont.Tiny, MutedInk, TextAnchor.UpperLeft, idraw);
                        yy += 5f;
                    }
                    return yy - iy;
                });
                y += 8f;
            }

            // Advertisements, each in its own framed box.
            if (issue.Ads != null)
            {
                foreach (NewspaperAd ad in issue.Ads)
                {
                    y += RenderBoxedSection(x, y, w, null, draw, (ix, iy, iw, idraw) =>
                    {
                        float yy = iy;

                        // Bundled black-and-white brand mark, centred above the advertiser (half the ads).
                        Texture2D brand = NewspaperImageCache.Get(HouseAds.BrandImagePath(ad));
                        if (brand != null)
                        {
                            const float mark = 52f;
                            var mr = new Rect(ix + (iw - mark) / 2f, yy, mark, mark);
                            if (idraw) GUI.DrawTexture(mr, brand, ScaleMode.ScaleToFit);
                            yy += mark + 4f;
                        }

                        yy += Para(ix, yy, iw, ad.Advertiser, GameFont.Small, InkColor, TextAnchor.MiddleCenter, idraw);
                        yy += 2f;
                        yy += Para(ix, yy, iw, ad.Copy, GameFont.Tiny, MutedInk, TextAnchor.MiddleCenter, idraw);
                        return yy - iy;
                    });
                    y += 8f;
                }
            }

            return y;
        }

        // ---- Block primitives ---------------------------------------------------------------------

        /// <summary>
        /// Measure-or-draw a wrapped text block. Returns the height it occupies at <paramref name="w"/>.
        /// Font and colour are set for the measure too, because <see cref="Text.CalcHeight"/> depends
        /// on the active font.
        /// </summary>
        private static float Para(float x, float y, float w, string text, GameFont font, Color color,
            TextAnchor anchor, bool draw)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            Text.Font = font;
            float h = Text.CalcHeight(text, w);
            if (draw)
            {
                Text.Anchor = anchor;
                GUI.color = color;
                Widgets.Label(new Rect(x, y, w, h), text);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            Text.Font = GameFont.Small;
            return h;
        }

        private static float Rule(float x, float y, float w, bool draw, Color color, float thickness)
        {
            if (draw)
            {
                GUI.color = color;
                var r = new Rect(x, y, w, thickness);
                GUI.DrawTexture(r, BaseContent.WhiteTex);
                GUI.color = Color.white;
            }
            return thickness;
        }

        /// <summary>
        /// Draw a story's illustration scaled to the column width (aspect preserved), with a thin ink
        /// frame and the caption beneath. Height is capped at <paramref name="maxHeightFraction"/> of
        /// the column width so a tall image can't swallow the whole column. Returns 0 when the story
        /// wants no image or the asset failed to load — the caller then flows as text-only.
        /// </summary>
        private static float RenderImage(float x, float y, float w, NewspaperStory story, bool draw,
            float maxHeightFraction)
        {
            if (!story.WantsImage) return 0f;
            Texture2D tex = NewspaperImageCache.Get(story.ResolvedImagePath);
            if (tex == null) return 0f;

            float imgW = w;
            float imgH = imgW * tex.height / tex.width;
            float maxH = w * (maxHeightFraction <= 0f ? 0.62f : maxHeightFraction);
            if (imgH > maxH)
            {
                imgH = maxH;
                imgW = imgH * tex.width / tex.height;
            }
            float imgX = x + (w - imgW) * 0.5f; // centre if aspect-capped

            float used = 0f;
            var imgRect = new Rect(imgX, y, imgW, imgH);
            if (draw)
            {
                GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit);
                GUI.color = InkColor;
                Widgets.DrawBox(imgRect);
                GUI.color = Color.white;
            }
            used += imgH;

            if (!string.IsNullOrEmpty(story.ImageCaption))
            {
                used += 2f;
                used += Para(x, y + used, w, story.ImageCaption, GameFont.Tiny, MutedInk,
                    TextAnchor.MiddleCenter, draw);
            }
            used += 6f;
            return used;
        }

        /// <summary>
        /// Draw an optional-titled panel with an ink frame around arbitrary inner content. The inner
        /// renderer follows the same (x, y, width, draw) → height contract as everything else.
        /// </summary>
        private static float RenderBoxedSection(float x, float y, float w, string title, bool draw,
            System.Func<float, float, float, bool, float> inner)
        {
            const float pad = 8f;
            float innerX = x + pad;
            float innerW = w - pad * 2f;
            float innerY = pad;

            if (!string.IsNullOrEmpty(title))
            {
                innerY += Para(innerX, y + innerY, innerW, title, GameFont.Tiny, InkColor,
                    TextAnchor.MiddleCenter, draw);
                innerY += Rule(innerX, y + innerY + 1f, innerW, draw, MutedInk, 1f) + 4f;
            }

            innerY += inner(innerX, y + innerY, innerW, draw);
            innerY += pad;

            if (draw)
            {
                GUI.color = MutedInk;
                Widgets.DrawBox(new Rect(x, y, w, innerY));
                GUI.color = Color.white;
            }
            return innerY;
        }

        // ---- Small helpers ------------------------------------------------------------------------

        private static void LabelAnchored(Rect rect, string text, TextAnchor anchor)
        {
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static IEnumerable<string> NonEmpty(params string[] values)
        {
            foreach (string v in values)
            {
                if (!string.IsNullOrEmpty(v)) yield return v;
            }
        }
    }
}

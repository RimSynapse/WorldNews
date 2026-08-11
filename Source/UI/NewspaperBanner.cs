using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSynapse.WorldNews.UI
{
    /// <summary>
    /// The breaking-news panel (WorldNews#19): a scrolling ticker of the events currently held by
    /// Core's deferred-news pipeline, shown only while the colony has a powered comms console. It is a
    /// movable, resizable, collapsible HUD element — drag it by the body, resize from the bottom-right
    /// corner, collapse it to a small chip — with its placement persisted in mod settings. Defaults to
    /// sitting just below the colonist bar. The placeholder visual until a spoken feed (Kokoro) lands.
    /// </summary>
    public static class NewspaperBanner
    {
        /// <summary>Debug: force the comms check to pass so the panel can be seen without building one.</summary>
        public static bool DebugForceComms;

        private const float HeaderH = 26f;
        private const float CollapsedW = 210f;
        private const float TagW = 150f;
        private const float BtnW = 22f;
        private const float ResizeH = 12f;
        private const float DefaultBelowBarY = 84f;
        private const float ScrollSpeed = 72f;
        private const float RebuildInterval = 0.5f;

        private static readonly Color BarBg = new Color(0.42f, 0.06f, 0.06f, 0.92f);
        private static readonly Color TagBg = new Color(0.74f, 0.12f, 0.12f, 1f);
        private static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        private static readonly Color HandleColor = new Color(1f, 1f, 1f, 0.5f);

        private static string cachedTicker = "";
        private static float cachedWidth;
        private static float lastBuild = -999f;

        private static bool dragging, resizing;
        private static Vector2 dragOffset;

        public static void Draw()
        {
            var mgr = SynapseDeferredNewsComponent.Instance;
            if (mgr == null || mgr.Pending.Count == 0) { dragging = resizing = false; return; }
            if (!AnyCommsAvailable()) { dragging = resizing = false; return; }

            var s = RimSynapseWorldNewsMod.Settings;
            if (s == null) return;

            RebuildIfDue(mgr.Pending);

            Rect rect = CurrentRect(s);
            HandleInput(rect, s);              // before draw so the button/handle rects line up
            rect = CurrentRect(s);             // re-read after a possible drag/resize this event

            Text.Font = GameFont.Small;
            Widgets.DrawBoxSolid(rect, BarBg);

            GUI.BeginGroup(rect);

            // Collapse / expand toggle.
            Rect btn = new Rect(2f, (rect.height - 20f) / 2f, BtnW, 20f);
            if (Widgets.ButtonText(btn, s.bannerCollapsed ? "▶" : "▼"))
            {
                s.bannerCollapsed = !s.bannerCollapsed;
                Persist(s);
            }

            // "BREAKING NEWS" tag (also the count when collapsed).
            Rect tag = new Rect(btn.xMax + 4f, 0f, TagW, rect.height);
            Widgets.DrawBoxSolid(tag, TagBg);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(tag, s.bannerCollapsed ? $"BREAKING ({mgr.Pending.Count})" : "BREAKING NEWS");

            if (!s.bannerCollapsed)
            {
                Rect area = new Rect(tag.xMax + 6f, 0f, rect.width - tag.xMax - 10f, rect.height);
                if (area.width > 20f)
                {
                    GUI.BeginGroup(area);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = TextColor;
                    float period = cachedWidth + area.width;
                    float offset = period > 1f ? (Time.realtimeSinceStartup * ScrollSpeed) % period : 0f;
                    Widgets.Label(new Rect(area.width - offset, 0f, cachedWidth + 60f, area.height), cachedTicker);
                    GUI.EndGroup();
                }

                // Resize grip.
                GUI.color = HandleColor;
                for (int i = 1; i <= 3; i++)
                {
                    float o = i * 3f;
                    Widgets.DrawLine(new Vector2(rect.width - o, rect.height - 2f),
                                     new Vector2(rect.width - 2f, rect.height - o), HandleColor, 1f);
                }
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.EndGroup();
        }

        private static void HandleInput(Rect rect, RimSynapseWorldNewsSettings s)
        {
            Event e = Event.current;
            Rect btnScreen = new Rect(rect.x + 2f, rect.y + (rect.height - 20f) / 2f, BtnW, 20f);
            Rect gripScreen = new Rect(rect.xMax - ResizeH, rect.yMax - ResizeH, ResizeH, ResizeH);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!s.bannerCollapsed && gripScreen.Contains(e.mousePosition))
                {
                    resizing = true; e.Use();
                }
                else if (rect.Contains(e.mousePosition) && !btnScreen.Contains(e.mousePosition))
                {
                    dragging = true;
                    dragOffset = e.mousePosition - new Vector2(rect.x, rect.y);
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && dragging)
            {
                s.bannerX = e.mousePosition.x - dragOffset.x;
                s.bannerY = e.mousePosition.y - dragOffset.y;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && resizing)
            {
                s.bannerW = Mathf.Max(240f, e.mousePosition.x - rect.x);
                s.bannerH = Mathf.Clamp(e.mousePosition.y - rect.y, HeaderH, 120f);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && (dragging || resizing))
            {
                dragging = resizing = false;
                Persist(s);
                e.Use();
            }
        }

        private static Rect CurrentRect(RimSynapseWorldNewsSettings s)
        {
            float sw = Verse.UI.screenWidth;
            float sh = Verse.UI.screenHeight;
            float w = s.bannerCollapsed ? CollapsedW : Mathf.Max(240f, s.bannerW);
            float h = s.bannerCollapsed ? HeaderH : Mathf.Max(HeaderH, s.bannerH);
            float x = s.bannerX >= 0f ? s.bannerX : (sw - w) / 2f;
            float y = s.bannerY >= 0f ? s.bannerY : DefaultBelowBarY;
            x = Mathf.Clamp(x, 0f, sw - w);
            y = Mathf.Clamp(y, 0f, sh - h);
            return new Rect(x, y, w, h);
        }

        private static void Persist(RimSynapseWorldNewsSettings s)
        {
            RimSynapseWorldNewsMod.Instance?.WriteSettings();
        }

        private static void RebuildIfDue(IReadOnlyList<DeferredNewsEvent> pending)
        {
            if (Time.realtimeSinceStartup - lastBuild < RebuildInterval && cachedTicker.Length > 0) return;
            lastBuild = Time.realtimeSinceStartup;

            int now = Find.TickManager?.TicksGame ?? 0;
            var sb = new StringBuilder();
            foreach (DeferredNewsEvent ev in pending)
            {
                if (sb.Length > 0) sb.Append("        ◆        ");
                sb.Append(ev.title);
                float d = (ev.releaseTick - now) / (float)GenDate.TicksPerDay;
                if (d > 0.05f) sb.Append($"  (arriving in {d:0.0} days)");
            }
            cachedTicker = sb.ToString();
            Text.Font = GameFont.Small;
            cachedWidth = Text.CalcSize(cachedTicker).x;
        }

        private static bool AnyCommsAvailable()
        {
            if (DebugForceComms) return true;

            List<Map> maps = Find.Maps;
            if (maps == null) return false;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || !map.IsPlayerHome) continue;
                var consoles = map.listerBuildings?.AllBuildingsColonistOfClass<Building_CommsConsole>();
                if (consoles == null) continue;
                foreach (Building_CommsConsole c in consoles)
                {
                    CompPowerTrader power = c.GetComp<CompPowerTrader>();
                    if (power == null || power.PowerOn) return true;
                }
            }
            return false;
        }
    }
}

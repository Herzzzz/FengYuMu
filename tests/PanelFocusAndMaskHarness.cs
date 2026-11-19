using System;
using System.Collections.Generic;
using System.Drawing;
using MapleOverlay;

public static class PanelFocusAndMaskHarness
{
    static int count;

    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        count++;
        Console.WriteLine("PASS: " + name);
    }

    public static int Run()
    {
        count = 0;

        var frame = new Size(1400, 1000);

        var panels = new List<Rectangle> {
            new Rectangle(100, 100, 350, 600),
            new Rectangle(500, 100, 500, 600)
        };

        var tips = new List<Rectangle> {
            new Rectangle(460, 240, 600, 250)
        };

        foreach (string kind in new[] {
            "statistics",
            "character-info",
            "citizenship",
            "inventory",
            "item",
            "settings",
            "world-map",
            "quest",
            "skill"
        })
        {
            Check(
                PanelFocusPriority.Select(
                    new Point(200, 200),
                    frame,
                    panels,
                    null,
                    Rectangle.Empty) == panels[0],
                kind + " uses geometry without a type whitelist");
        }

        Check(
            PanelFocusPriority.Select(
                new Point(470, 300),
                frame,
                panels,
                tips,
                Rectangle.Empty) == tips[0],
            "tooltip on top wins");

        Check(
            PanelFocusPriority.Select(
                new Point(430, 300),
                frame,
                panels,
                tips,
                Rectangle.Empty) == tips[0],
            "tooltip beside its trigger is eligible");

        Check(
            PanelFocusPriority.Select(
                new Point(200, 200),
                frame,
                panels,
                tips,
                Rectangle.Empty) == panels[0],
            "distant tooltip cannot steal focus");

        Check(
            PanelFocusPriority.Select(
                new Point(1200, 800),
                frame,
                panels,
                tips,
                Rectangle.Empty).IsEmpty,
            "bare scene does not invent a panel");

        Check(
            PanelFocusPriority.Select(
                new Point(-1, 200),
                frame,
                panels,
                tips,
                Rectangle.Empty).IsEmpty,
            "off-client pointer rejected");

        Check(
            PanelFocusPriority.Select(
                new Point(200, 200),
                frame,
                panels,
                null,
                new Rectangle(100, 100, 400, 400)).IsEmpty,
            "chat exclusion honored");

        var items = new List<Rectangle> {
            new Rectangle(1100, 10, 50, 20),
            new Rectangle(120, 160, 60, 20),
            new Rectangle(430, 180, 100, 20)
        };

        var ordered = PanelFocusPriority.Order(
            items,
            delegate(Rectangle r) { return r; },
            panels[0]);

        Check(
            ordered[0] == items[1] &&
            ordered[1] == items[2] &&
            ordered[2] == items[0] &&
            items[0].X == 1100,
            "stable order retains outside results");

        Check(
            PanelFocusPriority.Order(
                items,
                delegate(Rectangle r) { return r; },
                Rectangle.Empty)[0] == items[0],
            "empty focus preserves original order");

        int[] pixels = new int[80 * 80];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = unchecked((int)0xff505080);

        var a = new Rectangle(10, 10, 20, 16);
        var b = new Rectangle(10, 22, 20, 16);

        int[] pa = new int[320];
        int[] pb = new int[320];

        for (int i = 0; i < 320; i++)
        {
            pa[i] = unchecked((int)0xff505080);
            pb[i] = pa[i];
        }

        pa[5 * 20 + 4] = unchecked((int)0xffeeeeee);
        pb[5 * 20 + 4] = unchecked((int)0xffeeeeee);

        int[] original = (int[])pixels.Clone();

        var ma = FusionPatchMask.Create(
            pa, pixels, 80, 80, a);

        var mb = FusionPatchMask.Create(
            pb, pixels, 80, 80, b);

        Check(
            pa[0] == 0 &&
            pa[104] == unchecked((int)0xffeeeeee) &&
            ma[104] &&
            !ma[0],
            "only changed pixels stay opaque");

        Check(
            a.IntersectsWith(b) &&
            !FusionPatchMask.Overlaps(ma, a, mb, b),
            "overlapping margins do not block adjacent rows");

        ma[13 * 20 + 4] = true;
        mb[1 * 20 + 4] = true;

        Check(
            FusionPatchMask.Overlaps(ma, a, mb, b),
            "actual write collision still blocked");

        Check(
            !FusionPatchMask.Touches(
                ma, a, new Rectangle(10, 10, 3, 3)),
            "transparent padding does not block a value");

        Check(
            FusionPatchMask.Touches(
                ma, a, new Rectangle(14, 15, 1, 1)),
            "modified value pixel protected");

        Check(
            FusionPatchMask.Overlaps(null, a, mb, b),
            "missing footprint fails closed");

        bool same = true;

        for (int i = 0; i < pixels.Length; i++)
            same &= pixels[i] == original[i];

        Check(same, "source frame immutable");

        Check(
            FusionPatchMask.Create(
                new int[1], pixels, 80, 80, a) == null,
            "malformed patch rejected");

        var empty = new int[320];

        for (int i = 0; i < 320; i++)
            empty[i] = unchecked((int)0xff505080);

        Check(
            FusionPatchMask.Create(
                empty, pixels, 80, 80, a) == null,
            "empty replacement rejected");

        return count;
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    // --- JSON schema (see the "Sheet Authoring Convention" section of ANIMFRAMEWORK_SPEC.md) ---

    internal class SheetDefinition
    {
        public string TexturePath { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public List<SheetRowDefinition> Rows { get; set; }
        public List<SheetRowDefinition> Overlays { get; set; }
    }

    internal class SheetRowDefinition
    {
        public string State { get; set; }
        public int Row { get; set; }
        public int FrameCount { get; set; }
        public float? Fps { get; set; }
        public float? Duration { get; set; }
        public string Loop { get; set; }
        public float? OriginX { get; set; }
        public float? OriginY { get; set; }
    }

    /// <summary>
    /// Loads a bundled sheet-layout JSON file into an <see cref="AnimationSet"/>. Defensive by
    /// design: hand-authored JSON is where mistakes actually happen (see spec's
    /// Fallback/Validation Needs section), so bad rows are logged and skipped rather than
    /// crashing the whole content mod's load. A missing/unreadable texture path is the one
    /// exception left loud — it throws, on purpose, so it's caught immediately at load time
    /// instead of leaving a silently invisible NPC with no clue why.
    /// </summary>
    public static class SheetLayoutLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public static AnimationSet Load(Mod mod, string jsonAssetPath)
        {
            byte[] bytes = mod.GetFileBytes(jsonAssetPath);
            SheetDefinition def = JsonSerializer.Deserialize<SheetDefinition>(bytes, JsonOptions);

            if (def == null || string.IsNullOrEmpty(def.TexturePath))
                throw new InvalidOperationException(
                    $"AnimFramework: '{jsonAssetPath}' is missing or has no texturePath.");

            // Loaded (and kept loaded, ImmediateLoad) up front purely to read its real pixel
            // height for the row-bounds check below; AnimationClip.GetTexture() requests the
            // same path again later for drawing, which tModLoader's asset system dedupes.
            Asset<Texture2D> texture = ModContent.Request<Texture2D>(def.TexturePath, AssetRequestMode.ImmediateLoad);
            int sheetHeight = texture.Value.Height;

            var states = new Dictionary<string, AnimationClip>();
            foreach (SheetRowDefinition row in def.Rows ?? new List<SheetRowDefinition>())
            {
                AnimationClip clip = BuildClip(mod, jsonAssetPath, def, row, sheetHeight);
                if (clip != null)
                    states[row.State] = clip;
            }

            var overlays = new List<AnimationClip>();
            foreach (SheetRowDefinition row in def.Overlays ?? new List<SheetRowDefinition>())
            {
                AnimationClip clip = BuildClip(mod, jsonAssetPath, def, row, sheetHeight);
                if (clip != null)
                    overlays.Add(clip);
            }

            return new AnimationSet(states, overlays);
        }

        private static AnimationClip BuildClip(Mod mod, string jsonAssetPath, SheetDefinition def, SheetRowDefinition row, int sheetHeight)
        {
            if (string.IsNullOrEmpty(row.State) || row.FrameCount <= 0)
            {
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' has a row with no state name or frameCount <= 0 — skipped.");
                return null;
            }

            // The nastiest failure mode per spec: a row past the sheet's actual pixel height
            // doesn't crash, it silently reads garbage/out-of-bounds pixels. Bounds-check
            // explicitly instead of letting that happen quietly.
            int rowBottom = (row.Row + 1) * def.FrameHeight;
            if (rowBottom > sheetHeight)
            {
                mod.Logger.Error($"AnimFramework: '{jsonAssetPath}' row '{row.State}' (row {row.Row}) reads past the sheet's height ({rowBottom}px > {sheetHeight}px) — skipped.");
                return null;
            }

            LoopMode loop;
            if (!Enum.TryParse(row.Loop, true, out loop))
            {
                if (!string.IsNullOrEmpty(row.Loop))
                    mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{row.State}' has unrecognized loop mode '{row.Loop}' — defaulting to Loop.");
                loop = LoopMode.Loop;
            }

            Vector2? origin = (row.OriginX.HasValue && row.OriginY.HasValue)
                ? new Vector2(row.OriginX.Value, row.OriginY.Value)
                : (Vector2?)null;

            return new AnimationClip(
                row.State, def.TexturePath, def.FrameWidth, def.FrameHeight, row.FrameCount,
                row.Row, row.Fps ?? 0f, row.Duration ?? 0f, loop, origin);
        }
    }
}
